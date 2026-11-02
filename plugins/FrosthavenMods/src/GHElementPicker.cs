using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// "Perform any of the following: consume Fire: Attack 3; consume Air: Move 3; ..." (the 1st edition Elementalist's
    /// Ethereal Manifestation and Malleable Evocation bottoms): the user's flow, 2026-11-02 after manual test 44, is
    /// Frosthaven's own element selector: the step's icon is the element picker, it shows the elements available, the
    /// player picks one, confirms, its action is performed, and the step comes again until no element is left. A
    /// Frosthaven Consume step does exactly that when it holds several consumes and ShowPicker is on (UIUseAbilitiesBar:
    /// the "Any" slot limited to the consumes' elements; on confirm the consume of the picked element runs), but a card
    /// file gives a Consume step one consume. The packs write one Consume step per element (gh_port.empty_step_consumes),
    /// each marked with the misc var GHElementPicker and named as its consume (so the card text finds each one); after
    /// the action is read, each marked step of a pack card gets all the group's consumes and the picker. Only pack cards
    /// (IDs 20000-39999), only marked steps: inert without the content packs.
    /// </summary>
    [HarmonyPatch]
    internal static class GHElementPicker
    {
        private static bool _reported;

        [HarmonyTargetMethod]
        private static System.Reflection.MethodBase TargetMethod() => typeof(ScenarioRuleLibrary.YML.AbilityData)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .First(m => m.Name == "GetAction" && m.GetParameters().Any(p => p.ParameterType == typeof(CAction).MakeByRefType()));

        [HarmonyPostfix]
        private static void After(int cardID, ref CAction action)
        {
            try
            {
                if (action?.Abilities == null || cardID < 20000 || cardID >= 40000) return;
                var group = new List<CAbilityConsume>();
                var drop = new List<CAbility>();
                bool either = false;
                void Flush()
                {
                    // "either element, once" (the 2nd edition Spirit Swap top: Air or Earth, one teleport): one step with
                    // every consume and the picker, the group's other steps dropped
                    if (group.Count >= 2 && either)
                    {
                        group[0].Consumes = group.SelectMany(c => c.Consumes ?? new List<AbilityConsume>()).Where(c => c != null).Distinct().ToList();
                        group[0].ShowPicker = true;
                        drop.AddRange(group.Skip(1));
                        if (!_reportedEither) { _reportedEither = true; Plugin.Log.LogInfo($"Gloomhaven element picker (either): card {cardID}, {group.Count} consumes in one step"); }
                    }
                    else if (group.Count >= 2)
                    {
                        var all = group.SelectMany(c => c.Consumes ?? new List<AbilityConsume>()).Where(c => c != null).Distinct().ToList();
                        foreach (CAbilityConsume c in group)
                        {
                            c.Consumes = new List<AbilityConsume>(all);
                            c.ShowPicker = true;
                        }
                        if (!_reported) { _reported = true; Plugin.Log.LogInfo($"Gloomhaven element picker: card {cardID}, {group.Count} steps, {all.Count} elements each"); }
                    }
                    group.Clear();
                }
                foreach (CAbility a in action.Abilities)
                {
                    bool e = a is CAbilityConsume && Marked(a, "GHElementEither");
                    bool m = a is CAbilityConsume && (e || Marked(a, "GHElementPicker"));
                    if (m && group.Count > 0 && e != either) Flush();
                    if (m) { group.Add((CAbilityConsume)a); either = e; }
                    else Flush();
                }
                Flush();
                foreach (CAbility a in drop) action.Abilities.Remove(a);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("GHElementPicker: " + e.Message); }
        }

        private static bool _reportedEither;

        private static bool Marked(CAbility a, string key)
        {
            try { return a.GetMiscVar(key).GetBool(); }
            catch { return false; }
        }
    }
}
