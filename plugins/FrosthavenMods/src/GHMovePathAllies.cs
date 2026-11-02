using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// "All allies you move through" (the 2nd edition Sunkeeper's Path of Glory bottom: Strengthen and Bless every ally the
    /// move passed): Frosthaven only aims a move-path ability at enemies. The rules record every figure the move passes
    /// (CAbilityMove.AllTargetActorsOnPath, allies included), but SharedAbilityTargeting.GetValidActorsInRange drops each
    /// allied one before the ability's filter is asked (AreActorsAllied ? null). The harness found the half never had a
    /// target (2026-11-02). After the game's own list is built, the allies on the path that the ability's own filter
    /// accepts are added, as the enemies are. Only an ability with AllTargetsOnMovePath whose filter allows allies is
    /// touched: no Frosthaven card has one, so without the content packs this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMovePathAllies
    {
        private static bool _reported;

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.GetValidActorsInRange), new[] { typeof(CAbility) })]
        [HarmonyPostfix]
        private static void After(CAbility ability)
        {
            if (ability == null || !ability.AllTargetsOnMovePath || ability.AbilityFilter == null || ability.TargetingActor == null) return;
            if (!ability.AbilityFilter.HasTargetTypeFlag(CAbilityFilter.EFilterTargetType.Ally)) return;
            int added = 0;
            foreach (CActor a in CAbilityMove.AllTargetActorsOnPath.ToList())
            {
                if (a == null || a == ability.TargetingActor || a.IsDead || a.PhasedOut) continue;
                if (!CActor.AreActorsAllied(ability.TargetingActor.Type, a.Type, ability.TargetingActor, a)) continue;
                if (ability.ValidActorsInRange.Contains(a)) continue;
                if (!ability.AbilityFilter.IsValidTarget(a, ability.TargetingActor, ability.IsTargetedAbility, useTargetOriginalType: false, ability.MiscAbilityData?.CanTargetInvisible)) continue;
                ability.ValidActorsInRange.Add(a);
                added++;
            }
            if (added > 0 && !_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Gloomhaven move-path ally ability: {added} ally(ies) on the path added to {ability.Name}'s targets");
            }
        }
    }
}
