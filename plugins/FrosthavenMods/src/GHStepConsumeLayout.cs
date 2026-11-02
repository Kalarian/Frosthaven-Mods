using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A card layout's "Consume: Name: X" for a Consume step. The packs turn Gloomhaven's "consume X: do Y" on an empty
    /// step into Frosthaven's Consume step (gh_port.empty_step_consumes: the empty step was never offered its consume, the
    /// user's manual test 43, Boiling Arc bottom, softlocked), named as the consume was. Frosthaven's layout looks a
    /// consume up only among the half's card-level consumes, so those 59 cards failed to load ("Unable to find Consume
    /// data for entry BCFire", the rule library error screen, 2026-11-02); Frosthaven's own Consume-step cards draw their
    /// consume row by hand instead. Before a pack card's layout is read, the Consume steps' own consume data joins the
    /// list it searches, so Gloomhaven's layout draws the row as for any consume. Only pack cards (IDs 20000-39999 in the
    /// file name): inert without the content packs.
    /// </summary>
    [HarmonyPatch]
    internal static class GHStepConsumeLayout
    {
        private static readonly Regex PackCard = new Regex(@"AbilityCards/(\d+)_", RegexOptions.Compiled);
        private static bool _reported;

        // CardLayout(Sequence data, List<CAbility> linkedAbilities, List<AbilityConsume> consumes, ...): the constructor
        // found by its consumes parameter (its Sequence type lives in an assembly the plugin doesn't reference)
        [HarmonyTargetMethod]
        private static System.Reflection.MethodBase TargetMethod() => typeof(CardLayout).GetConstructors()
            .First(c => { var ps = c.GetParameters(); return ps.Length >= 3 && ps[1].ParameterType == typeof(List<CAbility>) && ps[2].ParameterType == typeof(List<AbilityConsume>); });

        [HarmonyPrefix]
        private static void Before(List<CAbility> linkedAbilities, ref List<AbilityConsume> consumes, string filename)
        {
            try
            {
                if (linkedAbilities == null || filename == null) return;
                Match m = PackCard.Match(filename.Replace('\\', '/'));
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out int id) || id < 20000 || id >= 40000) return;
                // the Consume steps, at the top level or as the options of a Choose step (several consumes on one Gloomhaven
                // empty step: a chain of Choose steps over them, Ethereal Manifestation bottom)
                var all = new List<CAbility>(linkedAbilities);
                foreach (CAbilityChooseAbility ch in linkedAbilities.OfType<CAbilityChooseAbility>())
                    if (ch.ChooseAbilities != null) all.AddRange(ch.ChooseAbilities);
                var steps = all.OfType<CAbilityConsume>().SelectMany(c => c.Consumes ?? new List<AbilityConsume>()).Where(c => c != null).ToList();
                if (steps.Count == 0) return;
                var list = consumes != null ? new List<AbilityConsume>(consumes) : new List<AbilityConsume>();
                int added = 0;
                foreach (AbilityConsume c in steps)
                    if (!list.Any(x => x?.Name == c.Name)) { list.Add(c); added++; }
                if (added == 0) return;
                consumes = list;
                if (!_reported) { _reported = true; Plugin.Log.LogInfo($"Gloomhaven Consume step layout: {added} step consume(s) found for the layout of {filename}"); }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("GHStepConsumeLayout: " + e.Message); }
        }
    }

    /// <summary>The consume buttons of a pack card's Choose options (see GHStepConsumeLayout): their consume data found.</summary>
    [HarmonyPatch]
    internal static class GHChooseOptionConsumes
    {
        // the card's consume buttons look their consume up through AbilityCardYMLData.GetConsumes, which holds the top-level
        // Consume steps but not the options of a Choose step: the button got nothing and the hand threw ("Could not process
        // updating cards", ConsumeButton.Enable, 2026-11-02 bench). A pack card's Choose options' consumes are added
        [HarmonyPatch(typeof(ScenarioRuleLibrary.YML.AbilityCardYMLData), nameof(ScenarioRuleLibrary.YML.AbilityCardYMLData.GetConsumes))]
        [HarmonyPostfix]
        private static void WithChooseOptions(ScenarioRuleLibrary.YML.AbilityCardYMLData __instance, ref IEnumerable<AbilityConsume> __result)
        {
            try
            {
                if (__instance == null || __instance.ID < 20000 || __instance.ID >= 40000) return;
                var extra = new List<AbilityConsume>();
                foreach (var data in new[] { __instance.TopActionCardData, __instance.BottomActionCardData })
                {
                    if (data == null) continue;
                    foreach (CAbilityChooseAbility ch in CAbilityCard.GetActionAbilities(data).OfType<CAbilityChooseAbility>())
                        foreach (CAbilityConsume c in (ch.ChooseAbilities ?? new List<CAbility>()).OfType<CAbilityConsume>())
                            if (c.Consumes != null) extra.AddRange(c.Consumes.Where(x => x != null));
                }
                if (extra.Count > 0) __result = __result.Concat(extra).ToList();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("GHStepConsumeLayout consumes: " + e.Message); }
        }
    }
}
