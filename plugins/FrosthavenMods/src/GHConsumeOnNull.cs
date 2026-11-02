using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven's "consume an element to also do this" hung on an empty step (a Null ability the consume's ability names
    /// as its parent: 37 halves in the 1st edition pack, 43 in the 2nd, e.g. the Sunkeeper's Solar Bastion "consume Light:
    /// recover a discarded card"). No Frosthaven card does this. The rules handle it (CPhaseAction stacks the consume's
    /// ability in place of the empty step and tells the client), but Frosthaven's client handler for that message
    /// (Choreographer.Handle_StackedAugmentToNullAbility) calls CardsActionControlller.RefreshConsumeBar(null, null),
    /// which reads the null ability at once: switching such a consume on at its step ended the scenario on the game's
    /// error screen ("Could not process Update Initiative Track"; found by the test harness, 2026-11-01). The refresh is
    /// only the consume bar's display; with no ability to refresh it for, it is skipped and the stacked ability runs as
    /// the rules meant. Without the content packs no card has such a consume and this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHConsumeOnNull
    {
        private static bool _reported;

        [HarmonyPatch(typeof(CardsActionControlller), nameof(CardsActionControlller.RefreshConsumeBar))]
        [HarmonyPrefix]
        private static bool Before(CAbility ability)
        {
            if (ability != null) return true;
            if (!_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo("Gloomhaven consume on an empty step: the consume bar refresh with no ability is skipped (Frosthaven's handler passes none)");
            }
            return false;
        }
    }
}
