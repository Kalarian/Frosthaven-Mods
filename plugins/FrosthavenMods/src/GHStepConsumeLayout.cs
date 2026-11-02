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
                var steps = linkedAbilities.OfType<CAbilityConsume>().SelectMany(c => c.Consumes ?? new List<AbilityConsume>()).Where(c => c != null).ToList();
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
}
