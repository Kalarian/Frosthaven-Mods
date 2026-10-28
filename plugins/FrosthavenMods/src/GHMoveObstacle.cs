using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven's "move an obstacle" (the Cragheart's Clear the Way top, the Demolitionist's Nowhere to Run bottom). No
    /// Frosthaven card moves an obstacle, and in Frosthaven the move never finished: its remove step sends the client a
    /// destroy-obstacle message and waits for the ability to be stepped on, which Frosthaven's client handler never does
    /// (it only removes the prop). The game sat on "Select targets to attack" with nothing to click (found by the test
    /// harness, 2026-10-28). Frosthaven's own Destroy Obstacle steps itself right after sending the same message
    /// (CAbilityDestroyObstacle: PhaseManager.StepComplete after the message); this does the same for the move's remove
    /// step. Without the content pack no card moves an obstacle and this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMoveObstacle
    {
        private static readonly AccessTools.FieldRef<CAbilityMoveObstacle, CAbilityMoveObstacle.MoveObstacleState> State =
            AccessTools.FieldRefAccess<CAbilityMoveObstacle, CAbilityMoveObstacle.MoveObstacleState>("m_State");
        private static readonly AccessTools.FieldRef<CAbilityMoveObstacle, int> MovingIndex =
            AccessTools.FieldRefAccess<CAbilityMoveObstacle, int>("m_MovingObstacleIndex");
        private static readonly AccessTools.FieldRef<CAbilityMoveObstacle, List<CTile>> SelectedObstacles =
            AccessTools.FieldRefAccess<CAbilityMoveObstacle, List<CTile>>("m_SelectedObstacleTiles");

        private static bool _reported;

        [HarmonyPatch(typeof(CAbilityMoveObstacle), nameof(CAbilityMoveObstacle.Perform))]
        [HarmonyPrefix]
        private static void Before(CAbilityMoveObstacle __instance, out bool __state)
        {
            // the remove step about to send its destroy message: an obstacle selected and not yet moved
            __state = State(__instance) == CAbilityMoveObstacle.MoveObstacleState.RemoveObstacle
                && __instance.TilesSelected != null && __instance.TilesSelected.Count != 0
                && SelectedObstacles(__instance) != null && MovingIndex(__instance) < SelectedObstacles(__instance).Count;
        }

        [HarmonyPatch(typeof(CAbilityMoveObstacle), nameof(CAbilityMoveObstacle.Perform))]
        [HarmonyPostfix]
        private static void After(CAbilityMoveObstacle __instance, bool __state)
        {
            if (!__state) return;
            if (!_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Gloomhaven move obstacle: {__instance.Name}: the remove step steps on (Frosthaven's client never does)");
            }
            PhaseManager.StepComplete();
        }
    }
}
