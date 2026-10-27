using System;
using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Augments (Gloomhaven's Mindthief: a persistent rider on every attack, "Augment:" on the card) in Frosthaven's
    /// engine. The engine still carries the whole system (CAugment, CActor.Augments, AddAugmentOrSong, the
    /// AugmentContainer prefab), but no Frosthaven card has an augment, so one path never ran there and has a bug:
    /// CAbilityAttack.StartAttackingActors reads m_MultiPassActors.Count for the augment's per-target abilities BEFORE
    /// the same method creates that list (it is built further down, from the targets), so the first pass of any
    /// multi-pass attack by an actor with an ability augment throws NullReferenceException (the probe of 2026-10-27:
    /// Feedback Loop's attack, tools/ilat on the trace's IL offset 0x53). Attacks are multi-pass unless a card says
    /// "MultiPassAttack: False" (Frosthaven's area attacks), so this is every ordinary attack.
    ///
    /// The fix creates the list from the targets a moment early, exactly as the game does below, and only in the case
    /// that throws (a multi-pass attack with ability augments pending): every Frosthaven card runs the untouched code.
    /// </summary>
    [HarmonyPatch]
    internal static class GHAugments
    {
        private static readonly AccessTools.FieldRef<CAbilityAttack, bool> MultiPass =
            AccessTools.FieldRefAccess<CAbilityAttack, bool>("m_MultiPassAttack");
        private static readonly AccessTools.FieldRef<CAbilityAttack, List<CActor>> MultiPassActors =
            AccessTools.FieldRefAccess<CAbilityAttack, List<CActor>>("m_MultiPassActors");
        private static readonly AccessTools.FieldRef<CAbility, List<CAugment>> AbilityAugments =
            AccessTools.FieldRefAccess<CAbility, List<CAugment>>("m_AbilityAugments");
        private static readonly AccessTools.FieldRef<CAbility, bool> AugmentsProcessed =
            AccessTools.FieldRefAccess<CAbility, bool>("m_AugmentAbilitiesProcessed");

        private static bool _reported;

        [HarmonyPatch(typeof(CAbilityAttack), "StartAttackingActors")]
        [HarmonyPrefix]
        private static void TargetsBeforeAugments(CAbilityAttack __instance)
        {
            try
            {
                if (!MultiPass(__instance) || MultiPassActors(__instance) != null || AugmentsProcessed(__instance)) return;
                List<CAugment> augments = AbilityAugments(__instance);
                if (augments == null || augments.Count == 0) return;
                MultiPassActors(__instance) = new List<CActor>(__instance.ActorsToTarget ?? new List<CActor>());
                if (!_reported)
                {
                    _reported = true;
                    Plugin.Log.LogInfo($"Gloomhaven augments: {__instance.TargetingActor?.Class?.ID}'s attack carries {augments.Count} augment(s); the target list is built before they are processed (the game's own order throws)");
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven augments: " + e); }
        }
    }
}
