using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven's "destroy an obstacle, then hit everything next to it" (the Demolitionist's Explode top: "Target all
    /// enemies adjacent to the destroyed obstacle; each suffers 2 damage and Stun"). A sub-ability marked
    /// AllTargetsAdjacentToParentTargets looks for its figures around the parent's targets
    /// (CAbilityTargeting.Start): around a Create's or a terrain create's chosen hexes, else around the figures the
    /// parent targeted. A destroy-obstacle parent targets hexes, not figures, so the damage found nobody (test harness,
    /// 2026-10-28: the rock destroyed, the 2 damage "no outcome" with enemies beside it). No Frosthaven card has this
    /// shape, so nothing of the base game changes; without the content pack this is inert.
    ///   - For the length of Start only, the sub-ability's parent is a stand-in Create holding the destroyed hexes, so
    ///     the game's own Create branch finds the figures beside them; the real parent is put back at once.
    /// </summary>
    [HarmonyPatch]
    internal static class GHDestroyAdjacent
    {
        private static readonly AccessTools.FieldRef<CAbility, List<CTile>> TilesSelected =
            AccessTools.FieldRefAccess<CAbility, List<CTile>>("m_TilesSelected");

        private static bool _reported;

        [HarmonyPatch(typeof(CAbilityTargeting), nameof(CAbilityTargeting.Start))]
        [HarmonyPrefix]
        private static void BesideDestroyed(CAbilityTargeting __instance, out CAbility __state)
        {
            __state = null;
            if (!(__instance.ParentAbility is CAbilityDestroyObstacle destroy)) return;
            if (__instance.MiscAbilityData?.AllTargetsAdjacentToParentTargets != true) return;
            List<CTile> destroyed = destroy.TilesSelected;
            if (destroyed == null || destroyed.Count == 0) return;
            var stand = new CAbilityCreate("RockSingle");
            TilesSelected(stand) = new List<CTile>(destroyed);
            __state = destroy;
            __instance.ParentAbility = stand;
            if (!_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Gloomhaven destroy-obstacle: {__instance.Name} targets the figures beside {destroyed.Count} destroyed hex(es)");
            }
        }

        [HarmonyPatch(typeof(CAbilityTargeting), nameof(CAbilityTargeting.Start))]
        [HarmonyFinalizer]
        private static void PutBack(CAbilityTargeting __instance, CAbility __state)
        {
            if (__state != null) __instance.ParentAbility = __state;
        }
    }
}
