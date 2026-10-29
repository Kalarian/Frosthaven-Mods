using System;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Moving a doom (Gloomhaven's Doomstalker, Lead to Slaughter bottom: "transfer all dooms from their target to another
    /// enemy") in Frosthaven's engine. CActor.TransferDoom finishes the old doom bonus before it adds the new one, and
    /// Frosthaven's CActiveBonus.Finish checks the hero's finished bonuses at once (Gloomhaven's doesn't): the doom card,
    /// for that moment without a live bonus, goes to the discard pile and loses its selected half; the new doom then sits
    /// on a card with no selected action, the next autosave throws in AbilityDeckState.Save, and the save coroutine dies
    /// with client messages blocked: the turn hangs with a grey end button (the Group 5 run of 2026-10-29, twice).
    ///
    /// The fix holds that immediate check back while a transfer runs and does it once the new doom is on, as Gloomhaven
    /// does. Only a doom transfer is touched (no Frosthaven card has dooms).
    /// </summary>
    [HarmonyPatch]
    internal static class GHDoomTransfer
    {
        [ThreadStatic] private static bool _active;
        private static bool _reported;

        [HarmonyPatch(typeof(CActor), nameof(CActor.TransferDoom))]
        [HarmonyPrefix]
        private static void Start() => _active = true;

        [HarmonyPatch(typeof(CActor), nameof(CActor.TransferDoom))]
        [HarmonyFinalizer]
        private static void End(CActor __instance)
        {
            _active = false;
            try
            {
                (__instance?.Class as CCharacterClass)?.CheckForFinishedActiveBonuses(__instance);
                if (!_reported) { _reported = true; Plugin.Log.LogInfo("Gloomhaven dooms: a doom moved; the finished-bonus check ran after the new doom was on"); }
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven dooms: " + e); }
        }

        [HarmonyPatch(typeof(CCharacterClass), nameof(CCharacterClass.CheckForFinishedActiveBonuses))]
        [HarmonyPrefix]
        private static bool Hold(ref bool __result)
        {
            if (!_active) return true;
            __result = false;
            return false;
        }
    }
}
