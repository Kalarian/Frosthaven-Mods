using System;
using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Game bug fixes": fixes for bugs in the game's own code. Every fix here changes play (randomness or
    /// rules), so it is a campaign rule - every co-op peer has it on or off together - and it is locked during online
    /// scenarios (see HouseRulesPatches).
    ///
    /// Attack modifier decks (checked 2026-10-20, simulated over millions of draws in scratchpad amd_sim.py):
    /// - The game's shuffle (ScenarioRuleLibrary.Extensions.Shuffle) is off by one: j = rng.Next(num) after num--,
    ///   so a card can never stay where it was (Sattolo's algorithm: only single-cycle orders).
    /// - Decks are built and reshuffled by inserting cards at rng.Next(0, Count), which never picks the bottom: the
    ///   first card of every class's / monster's deck list (a +0) is always at the bottom of the starting deck.
    /// Together: about +0.25 percentage points of nulls and of x2s (6.25% vs 5.99% per draw), taken from the +0s.
    /// The fix: a correct Fisher-Yates shuffle of the deck right after the game builds or reshuffles it (a uniform
    /// shuffle of any order is uniform), drawing from the scenario's own RNG, so every peer does the same.
    /// (The generic Extensions.Shuffle is not patched directly: it is shared by many decks and patching a generic
    /// method is fragile.)
    /// </summary>
    [HarmonyPatch]
    internal static class BugFixes
    {
        private static bool On => ModRules.IsOn(ModRules.BugFixes);

        private static void FairShuffle(List<AttackModifierYMLData> deck)
        {
            SharedLibrary.Random rng = ScenarioManager.CurrentScenarioState?.ScenarioRNG;
            if (deck == null || rng == null) return;
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                AttackModifierYMLData t = deck[i];
                deck[i] = deck[j];
                deck[j] = t;
            }
        }

        private static void Safe(Action action)
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError("Game bug fixes: fair shuffle skipped: " + e); }
        }

        /// <summary>Every reshuffle (end of round after a null / x2, or an empty deck), players and monsters.</summary>
        [HarmonyPatch(typeof(CActor), nameof(CActor.ShuffleAttackModifierCards))]
        [HarmonyPostfix]
        private static void Reshuffled(List<AttackModifierYMLData> current)
        {
            if (On) Safe(() => FairShuffle(current));
        }

        /// <summary>A character's starting deck (built by insertion) or its in-place shuffle.</summary>
        [HarmonyPatch(typeof(CCharacterClass), nameof(CCharacterClass.ResetAttackModifierDeck))]
        [HarmonyPostfix]
        private static void CharacterDeckBuilt(CCharacterClass __instance)
        {
            if (On) Safe(() => FairShuffle(__instance.AttackModifierCards));
        }

        /// <summary>A monster deck's starting order (built by insertion; only when the game rebuilt it).</summary>
        [HarmonyPatch(typeof(CMonsterAttackModifierDeck), nameof(CMonsterAttackModifierDeck.ResetAttackModifiers))]
        [HarmonyPostfix]
        private static void MonsterDeckBuilt(CMonsterAttackModifierDeck __instance, AttackModifierDeckState deckState, bool forceResetAvailable)
        {
            if (!On) return;
            bool rebuilt = ScenarioManager.CurrentScenarioState?.EnemyClassManager == null || deckState?.AttackModifierCardsAvailable == null || forceResetAvailable;
            if (rebuilt) Safe(() => FairShuffle(__instance.AttackModifierCards));
        }

        [HarmonyPatch(typeof(CMonsterAttackModifierDeck), nameof(CMonsterAttackModifierDeck.RandomizeAvailableAttackModifierCardsOrder))]
        [HarmonyPostfix]
        private static void MonsterDeckShuffled(CMonsterAttackModifierDeck __instance)
        {
            if (On) Safe(() => FairShuffle(__instance.AttackModifierCards));
        }
    }
}
