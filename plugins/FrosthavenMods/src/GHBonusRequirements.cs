using System;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A toggle "at the end of an action" bonus with a requirement (Gloomhaven's Elementalist, Volatile Consumption top:
    /// "once per action, if you consumed an element, damage 1 or heal 1", ElementsConsumedThisAction) in Frosthaven's
    /// engine. CPhaseAction.EndAbilitySynchronise asks such a bonus RequirementsMet() with no actor, and
    /// CAbilityRequirements.MeetsAbilityRequirements reads actor.ActiveBonuses at once: NullReferenceException in the
    /// rules thread, the game's error screen (the Group 4 bench of 2026-10-29). No Frosthaven card has a requirement on
    /// such a bonus, so the path never ran there.
    ///
    /// The fix answers the missing actor with the bonus's own holder, only when the game passed none and the bonus has
    /// requirements (the case that throws): every other call runs the untouched code.
    /// </summary>
    [HarmonyPatch]
    internal static class GHBonusRequirements
    {
        private static bool _reported;

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.RequirementsMet))]
        [HarmonyPrefix]
        private static void HolderAsActor(CActiveBonus __instance, ref CActor actor)
        {
            try
            {
                if (actor != null || __instance?.Ability?.ActiveBonusData?.Requirements == null) return;
                actor = __instance.Actor ?? __instance.Ability.TargetingActor;
                if (!_reported && actor != null)
                {
                    _reported = true;
                    Plugin.Log.LogInfo($"Gloomhaven bonuses: {__instance.Ability.Name}'s requirement checked for its holder {actor.Class?.ID} (the game passed no actor)");
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven bonuses: " + e); }
        }
    }
}
