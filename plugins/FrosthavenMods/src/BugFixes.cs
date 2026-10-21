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

        // ---- co-op desync fixes (docs/GAME_BUGS.md)

        /// <summary>Replaces one call instruction (a getter or constructor) with a call to our own method that has the
        /// same stack effect. Logs when the instruction isn't found (a game update changed the method).</summary>
        private static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> code, System.Reflection.MethodBase target,
            System.Reflection.MethodInfo replacement, string what, bool loadThisFirst = false)
        {
            int swapped = 0;
            foreach (CodeInstruction ins in code)
            {
                if ((ins.opcode == System.Reflection.Emit.OpCodes.Call || ins.opcode == System.Reflection.Emit.OpCodes.Callvirt
                     || ins.opcode == System.Reflection.Emit.OpCodes.Newobj) && Equals(ins.operand, target))
                {
                    if (loadThisFirst) yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0).MoveLabelsFrom(ins);
                    var call = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, replacement);
                    if (!loadThisFirst) call.MoveLabelsFrom(ins);
                    yield return call;
                    swapped++;
                    continue;
                }
                yield return ins;
            }
            if (swapped == 0) Plugin.Log.LogWarning($"Game bug fixes: '{what}' not applied (the game's code changed)");
        }

        // D1: a failed outpost command left CampaignController._currentResult set ("command in progress"), and every
        // later command on that PC failed in BeginResult until the game restarted.
        private static readonly AccessTools.FieldRef<FHRL.CampaignController, FHRL.CampaignController.Result> CurrentResult =
            AccessTools.FieldRefAccess<FHRL.CampaignController, FHRL.CampaignController.Result>("_currentResult");

        [HarmonyPatch(typeof(FHRL.MultiplayerGameController), "ApplyCommand")]
        [HarmonyFinalizer]
        private static Exception ClearFailedCommand(FHRL.MultiplayerGameController __instance, Exception __exception)
        {
            if (__exception != null && On)
            {
                try
                {
                    FHRL.CampaignController campaign = __instance.CampaignController;
                    if (campaign != null && CurrentResult(campaign) != null)
                    {
                        CurrentResult(campaign) = null;
                        Plugin.Log.LogWarning("Game bug fixes: a failed outpost command was cleaned up (the game would have frozen this PC's outpost)");
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("Game bug fixes: failed-command clean-up: " + e); }
            }
            return __exception;
        }

        // D4: the Strategy favor shuffled each monster deck with a new, clock-seeded random generator inside a command
        // every PC runs, so each player drew different cards. Use the campaign's own deterministic generator, as the
        // game's other overload (DrawMonsterCardsForStrategyFavor(MonsterDef)) already does.
        [HarmonyPatch(typeof(FHRL.ScenarioController), nameof(FHRL.ScenarioController.Action_DrawMonsterCardsForStrategyFavor))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> StrategyFavorDice(IEnumerable<CodeInstruction> code) =>
            Swap(code, AccessTools.Constructor(typeof(FHRL.FHRandom), Type.EmptyTypes),
                AccessTools.Method(typeof(BugFixes), nameof(StrategyRandom)), "Strategy favor dice", loadThisFirst: true);

        private static FHRL.FHRandom StrategyRandom(FHRL.ScenarioController controller) =>
            On && controller?.CampaignController?.State?.Random != null ? controller.CampaignController.State.Random : new FHRL.FHRandom();

        // D2: Restful Slippers' short rest shuffled the discard pile with the shared scenario dice in screen code on the
        // resting player's PC only (the other PCs receive just the chosen card), so the dice drifted apart. The choice
        // is shown only to that player, so it doesn't need the shared dice: use a local generator.
        [HarmonyPatch]
        private static class ImprovedShortRest
        {
            private static System.Reflection.MethodBase TargetMethod() =>
                AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CardsHandUI), "PerformImprovedShortRestCoroutine"));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
                Swap(code, AccessTools.PropertyGetter(typeof(ScenarioState), nameof(ScenarioState.ScenarioRNG)),
                    AccessTools.Method(typeof(BugFixes), nameof(ShortRestRandom)), "Restful Slippers dice");
        }

        private static readonly SharedLibrary.Random LocalDice = new SharedLibrary.Random();

        private static SharedLibrary.Random ShortRestRandom(ScenarioState state) => On || state == null ? LocalDice : state.ScenarioRNG;

        // D10: "infuse any element" without a picker drew from each PC's own generator (SharedClient.GlobalRNG) inside
        // the rules engine; monsters use the scenario dice for the same thing.
        [HarmonyPatch(typeof(CAbilityInfuse), nameof(CAbilityInfuse.DoInfuse))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> InfuseAnyDice(IEnumerable<CodeInstruction> code) =>
            Swap(code, AccessTools.PropertyGetter(typeof(SharedLibrary.Client.SharedClient), nameof(SharedLibrary.Client.SharedClient.GlobalRNG)),
                AccessTools.Method(typeof(BugFixes), nameof(InfuseRandom)), "infuse-any dice");

        private static SharedLibrary.Random InfuseRandom() =>
            On && ScenarioManager.CurrentScenarioState?.ScenarioRNG != null ? ScenarioManager.CurrentScenarioState.ScenarioRNG : SharedLibrary.Client.SharedClient.GlobalRNG;
    }
}
