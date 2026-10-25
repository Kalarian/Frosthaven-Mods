using System;
using System.Collections.Generic;
using System.Linq;
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

        // ---- batch 2: gameplay bugs (docs/GAME_BUGS.md)

        // O1: finishing a REBUILD also paid the 2-morale "second build this week" cost (rules: rebuild any number of
        // wrecked buildings; only the 2nd build/upgrade costs morale). The extra cost is zeroed while a rebuild finishes.
        [ThreadStatic] private static bool _finishingRebuild;

        [HarmonyPatch(typeof(FHRL.BuildingsController), "Action_FinishBuildingDonation")]
        [HarmonyPrefix]
        private static void RebuildStart(FHRL.BuildingState building, bool cancelled) =>
            _finishingRebuild = On && !cancelled && building != null && building.WreckedOrRebuilding;

        [HarmonyPatch(typeof(FHRL.BuildingsController), "Action_FinishBuildingDonation")]
        [HarmonyFinalizer]
        private static Exception RebuildEnd(Exception __exception) { _finishingRebuild = false; return __exception; }

        [HarmonyPatch(typeof(FHRL.BuildingsController), nameof(FHRL.BuildingsController.GetConstructionExtraMoraleCost))]
        [HarmonyPostfix]
        private static void NoRebuildMorale(ref uint __result)
        {
            if (_finishingRebuild) __result = 0;
        }

        // O3: morale only had an upper bound (rules: the track goes from 0 to 20).
        [HarmonyPatch(typeof(FHRL.OutpostController), "Action_AddMorale")]
        [HarmonyPostfix]
        private static void MoraleFloor(FHRL.OutpostController __instance)
        {
            if (!On) return;
            FHRL.OutpostState outpost = __instance.CampaignController?.State?.Outpost;
            if (outpost != null && outpost.Morale < 0) outpost.Morale = 0;
        }

        // O4: the Jeweler's percentage discount was shown and checked (CanBuyItem passes applyPerc) but not charged
        // (Action_BuyItem calls GetDiscountedCost without it). Charge what the screen shows.
        [ThreadStatic] private static bool _shopBuy;

        [HarmonyPatch(typeof(FHRL.TradingController), nameof(FHRL.TradingController.BuyItem),
            new[] { typeof(FHRL.CharacterState), typeof(FHRL.ItemDef), typeof(bool), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static void ShopBuyStart(bool travellingMerchant) => _shopBuy = On && !travellingMerchant;

        [HarmonyPatch(typeof(FHRL.TradingController), nameof(FHRL.TradingController.BuyItem),
            new[] { typeof(FHRL.CharacterState), typeof(FHRL.ItemDef), typeof(bool), typeof(bool), typeof(bool) })]
        [HarmonyFinalizer]
        private static Exception ShopBuyEnd(Exception __exception) { _shopBuy = false; return __exception; }

        [HarmonyPatch(typeof(FHRL.TradingController), nameof(FHRL.TradingController.GetDiscountedCost))]
        [HarmonyPrefix]
        private static void JewelerDiscount(ref bool applyPerc)
        {
            if (_shopBuy) applyPerc = true;
        }

        // O2: PQ 14 ("a quest in each of 5 areas") counted every scenario in an area, so 5 in one area completed it.
        [HarmonyPatch(typeof(FHRL.PQ_14), nameof(FHRL.PQ_14.IncProgress))]
        [HarmonyPostfix]
        private static void OnePerArea(FHRL.PersonalQuestState state)
        {
            if (!On || state?.ProgressTrackers == null) return;
            for (int i = 0; i < state.ProgressTrackers.Length; i++)
                if (state.ProgressTrackers[i] > 1) state.SetProgress(1, i);
        }

        // O6: "week in this season" was Week % 10, so the 10th week of a season counted as week 0 (e.g. SO-28 saw
        // 10 weeks left in week 10, and next-summer retirement sections landed in winter).
        [HarmonyPatch(typeof(FHRL.Calendar), nameof(FHRL.Calendar.GetWeekInThisSeason))]
        [HarmonyPostfix]
        private static void WeekInSeason(ref FHRL.Calendar __instance, ref uint __result)
        {
            if (On && __instance.Week > 0) __result = (__instance.Week - 1) % 10 + 1;
        }

        // O7: SO-10's "if winter, second week of next summer" was hard-coded as week 22 (year 1), so from year 2 the
        // delivery landed in the past and was lost after paying 15 gold.
        [HarmonyPatch(typeof(FHRL.OutpostEvents.SO_10_OutpostEventDef), "GetWeeksToDelivery")]
        [HarmonyPostfix]
        private static void SO10Delivery(FHRL.CampaignState state, ref int __result)
        {
            if (!On || state?.Outpost == null) return;
            uint week = state.Outpost.Week;
            var arrival = new FHRL.Calendar(week + 3);
            if (arrival.IsSummer) return;
            __result = (int)(20 * arrival.Year + 2 - week); // second week of the summer after that winter
        }

        // C1: a reshuffle in the middle of a rolling-modifier chain kept the in-use card twice: the game matches cards in
        // use by reference, but what's in use is a copy (ChangeModifierValues copies every drawn card), so the original
        // was shuffled back AND the copy added to the discards. Point the discard entry at the copy so the game's own
        // matching finds it.
        [HarmonyPatch(typeof(CActor), nameof(CActor.ShuffleAttackModifierCards))]
        [HarmonyPrefix]
        private static void KeepInUseOnce(List<AttackModifierYMLData> discarded, List<AttackModifierYMLData> inUse)
        {
            if (!On || inUse == null || discarded == null) return;
            Safe(() =>
            {
                foreach (AttackModifierYMLData used in inUse)
                {
                    if (used == null || discarded.Contains(used)) continue;
                    int i = discarded.FindLastIndex(d => d != null && !inUse.Contains(d) && d.Name == used.Name);
                    if (i >= 0) discarded[i] = used;
                }
            });
        }

        private static SharedLibrary.Random InfuseRandom() =>
            On && ScenarioManager.CurrentScenarioState?.ScenarioRNG != null ? ScenarioManager.CurrentScenarioState.ScenarioRNG : SharedLibrary.Client.SharedClient.GlobalRNG;

        // ---- batch 3 (docs/GAME_BUGS.md)

        // R2: the loot deck takes the first k cards of each material's / coin's shuffled pool, and the game's off-by-one
        // shuffle never leaves a card where it was, so the pool's first cards (the "+2 at 2-3 players" materials) were
        // drawn less often: with 2 lumber in the loot table, 0.57 of those cards per scenario instead of 0.75. The same
        // shuffle picks the 3 cards a Restful Slippers short rest offers. Both now use a fair shuffle with the same dice.
        private static readonly System.Reflection.MethodInfo GameShuffle = AccessTools.FirstMethod(typeof(Extensions),
            m => m.Name == nameof(Extensions.Shuffle) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);

        private static void Shuffle<T>(IList<T> list, SharedLibrary.Random rng)
        {
            if (!On || list == null) { list.Shuffle(rng); return; }
            rng = rng ?? ScenarioManager.CurrentScenarioState.ScenarioRNG;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }

        /// <summary>Replaces every call of the game's generic shuffle in a method with our <see cref="Shuffle{T}"/>.</summary>
        private static IEnumerable<CodeInstruction> FairShuffles(IEnumerable<CodeInstruction> code, string what)
        {
            int swapped = 0;
            foreach (CodeInstruction ins in code)
            {
                if (ins.opcode == System.Reflection.Emit.OpCodes.Call && ins.operand is System.Reflection.MethodInfo m
                    && m.IsGenericMethod && m.GetGenericMethodDefinition() == GameShuffle)
                {
                    ins.operand = AccessTools.Method(typeof(BugFixes), nameof(Shuffle)).MakeGenericMethod(m.GetGenericArguments());
                    swapped++;
                }
                yield return ins;
            }
            if (swapped == 0) Plugin.Log.LogWarning($"Game bug fixes: '{what}' not applied (the game's code changed)");
        }

        [HarmonyPatch(typeof(CLootDeck), nameof(CLootDeck.Generate))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> FairLootDeck(IEnumerable<CodeInstruction> code) => FairShuffles(code, "fair loot deck");

        [HarmonyPatch]
        private static class FairShortRestChoices
        {
            private static System.Reflection.MethodBase TargetMethod() =>
                AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CardsHandUI), "PerformImprovedShortRestCoroutine"));

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => FairShuffles(code, "fair Restful Slippers choices");
        }

        // R3: WO-33 option A draws a town guard card per target "for threat" with dontUse, which took it out of the deck
        // without filing it with the used cards, so the reshuffle at the next attack never brought it back: up to 8 cards
        // (possibly the Success or Wrecked card) lost for the rest of the campaign. File it with the used cards.
        [HarmonyPatch(typeof(FHRL.AttackController), "DrawTownGuardCard")]
        [HarmonyPostfix]
        private static void ReturnThreatCard(FHRL.AttackController __instance, bool dontUse, FHRL.TownGuardAttackModifierDef __result)
        {
            if (!On || !dontUse || __result == null) return;
            FHRL.OutpostState outpost = __instance.CampaignController?.State?.Outpost;
            outpost?.TownGuardUsedDeckIDs.Add(__result.ID);
        }

        // R4: attack events whose card gives a target order shuffled their targets anyway (and SO-23 also took
        // odd-numbered buildings). The attack drops unbuilt and wrecked buildings from the list afterwards. Ties ("players
        // break ties") keep the game's random order; SO-23's "closest to the Workshop" stays random, as the game does for
        // every "closest to" event.
        [HarmonyPatch(typeof(FHRL.OutpostEvents.WO_33_OutpostEventDef), nameof(FHRL.OutpostEventDef.GenerateAttackTargets))]
        [HarmonyPostfix]
        private static void WO33Targets(ref List<FHRL.BuildingState> __result) // even-numbered, descending
        {
            if (On && __result != null) __result = __result.OrderByDescending(b => b.ID.ID).ToList();
        }

        [HarmonyPatch(typeof(FHRL.OutpostEvents.WO_34_OutpostEventDef), nameof(FHRL.OutpostEventDef.GenerateAttackTargets))]
        [HarmonyPostfix]
        private static void WO34Targets(ref List<FHRL.BuildingState> __result) // 1 to 40, descending
        {
            if (On && __result != null) __result = __result.OrderByDescending(b => b.ID.ID).ToList();
        }

        [HarmonyPatch(typeof(FHRL.OutpostEvents.WO_35_OutpostEventDef), nameof(FHRL.OutpostEventDef.GenerateAttackTargets))]
        [HarmonyPostfix]
        private static void WO35Targets(ref List<FHRL.BuildingState> __result) // highest level first
        {
            if (On && __result != null) __result = __result.OrderByDescending(b => b.Level).ToList();
        }

        [HarmonyPatch(typeof(FHRL.OutpostEvents.WO_71_OutpostEventDef), nameof(FHRL.OutpostEventDef.GenerateAttackTargets))]
        [HarmonyPostfix]
        private static void WO71Targets(ref List<FHRL.BuildingState> __result) // lowest level first
        {
            if (On && __result != null) __result = __result.OrderBy(b => b.Level).ToList();
        }

        [HarmonyPatch(typeof(FHRL.OutpostEvents.SO_23_OutpostEventDef), nameof(FHRL.OutpostEventDef.GenerateAttackTargets))]
        [HarmonyPostfix]
        private static void SO23Targets(ref List<FHRL.BuildingState> __result) // even-numbered only
        {
            if (On && __result != null) __result = __result.Where(b => b.IsEvenID).ToList();
        }

        /// <summary>The Load window's preview showed heroes at the level (and gold) they had when they were last put in
        /// their party slots or went to a scenario. The save's summary (PartyAdventureData.GenerateHeader) reads the
        /// Gloomhaven-era copy of the party (MapParty.SelectedCharactersArray), which Frosthaven rebuilds from its own
        /// character records only on slot assignment, on heading to the scenario map, and after a scenario's results,
        /// not when a hero levels up or earns gold in the outpost. So a party levelled and then saved in the outpost
        /// showed level 1. This writes each summary hero's level and gold from Frosthaven's own record, matched by
        /// class. Display only: the summary feeds the save list, nothing in play.</summary>
        [HarmonyPatch(typeof(PartyAdventureData), "GenerateHeader")]
        [HarmonyPostfix]
        private static void SavePreviewLevels(PartyAdventureData __instance, ref PartyAdventureData.Header __result)
        {
            if (!On || __result.LastSavedCharacterInfo == null) return;
            try
            {
                IEnumerable<FHRL.CharacterState> party = __instance.FrosthavenCampaign?.PartyCharacters;
                if (party == null) return;
                PartyAdventureData.Header.HeaderCharacterInfo[] heroes = __result.LastSavedCharacterInfo;
                for (int i = 0; i < heroes.Length; i++)
                {
                    if (!heroes[i].IsValid) continue;
                    string id = heroes[i].ID;
                    FHRL.CharacterState real = party.FirstOrDefault(c => c != null && c.ClassID.ID == id);
                    if (real == null) continue;
                    heroes[i].Level = (int)real.Level;
                    heroes[i].Gold = (int)real.Gold;
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Game bug fixes: save preview levels skipped: " + e); }
        }
    }
}
