using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Crash guard for the content packs: CharacterClassManager.FindAllActiveBonuses() is a lazy iterator straight over
    /// every class's ActivatedCards and each card's ActiveBonuses list. Many engine sites run bonus code in the body of a
    /// foreach over it, e.g. CAbilityAttack.Perform's FinaliseAttack step: for every bonus with
    /// DamageOnCurseModApplied it calls TriggerAbility while the enumeration is still open. Anything that finishes a
    /// bonus there (CActiveBonus.Finish -> CheckForFinishedActiveBonuses removes it from the card's list and the card
    /// from ActivatedCards), adds one, or kills a figure whose bonuses then end, throws
    /// "InvalidOperationException: Collection was modified" from CCharacterClass.FindActiveBonuses and stops the
    /// scenario ("An error occurred within the Scenario Rule Library").
    ///
    /// Frosthaven's own cards never do that at those sites, but pack cards do: Gloomhaven 2nd edition Compounding
    /// Infection (Plagueherald2, card 33021) is a one-use DamageOnCurseModApplied bonus; when a monster draws a Curse
    /// and the player says yes, GHCompoundingInfection gives Curse, Curse, Plague, Plague and ends the bonus (the card
    /// goes to the discard pile) inside that very foreach (bench 2026-11-03: a Hound's attack, round 5). The 1st edition
    /// Baneful Hex and the Nightshroud2 curse-damage perk deal damage there, which can kill.
    ///
    /// The fix: while a Gloomhaven class (either pack) is in the scenario, FindAllActiveBonuses returns a snapshot list
    /// of the same bonuses in the same order. Every caller sees exactly what it saw before at the moment of the call;
    /// changes made while it walks the list take effect for the next call instead of breaking the walk. Bonuses ended
    /// during the walk are still visited, so the pack's handlers check Finished()/Finishing() themselves (Compounding
    /// Infection does). The engine's own first step (GameState.CheckForFinishedActiveBonuses) runs at the call instead
    /// of at the first MoveNext; every caller enumerates immediately, so nothing changes. Not a rule: no GHRule line.
    /// Inert without the packs (Frosthaven-only parties keep the game's own lazy iterator).
    /// </summary>
    [HarmonyPatch]
    internal static class GHBonusSnapshot
    {
        private static bool PackPartyInScenario()
        {
            var players = ScenarioManager.Scenario?.AllPlayers;
            if (players == null) return false;
            foreach (CPlayerActor p in players)
            {
                ECharacter model = p?.CharacterClass?.CharacterModel ?? ECharacter.None;
                if (model != ECharacter.None && model < ECharacter.Infuser) return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(CharacterClassManager), nameof(CharacterClassManager.FindAllActiveBonuses), new[] { typeof(CActor), typeof(bool) })]
        [HarmonyPostfix]
        private static void Snapshot(ref IEnumerable<CActiveBonus> __result)
        {
            if (__result == null || __result is List<CActiveBonus>) return;
            bool pack;
            try { pack = PackPartyInScenario(); }
            catch { return; }
            if (!pack) return;
            // a used compiler iterator hands out a fresh copy on the next GetEnumerator, so one retry starts clean
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    __result = __result.ToList();
                    return;
                }
                catch (InvalidOperationException e)
                {
                    if (attempt == 1 && GHClasses.Reported.Add("GHBonusSnapshot")) Plugin.Log.LogError("GHBonusSnapshot: " + e);
                }
            }
        }
    }
}
