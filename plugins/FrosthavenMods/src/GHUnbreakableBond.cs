using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI.Scenario.Services;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Wildfury's Unbreakable Bond bottom (2e card-accuracy pass, task wildfury-hard): "On the next three
    /// rounds, skip your card selection and your turn. Your character token does not advance on any of your other
    /// persistent abilities. The Bear performs their turn twice at initiative 14 each round with you controlling it."
    /// GH2e FAQ (Unbreakable Bond, Two Minis): you play no cards and do nothing that happens on your turn (your wound
    /// doesn't hurt you, your end-of-turn conditions don't expire); for focus you are last in initiative; the Bear takes two
    /// full turns (its conditions end after the first, its wound hurts it twice); your other persistent abilities work as
    /// normal but their use slots don't move.
    ///
    /// State: the card's persistent marker bonus (MiscVars GHUnbreakableBond, three slots) and the round it was played
    /// (CActiveBonus.ActiveBonusStartRound). Both are in the save, so nothing here is local: a bond round is any round after
    /// the play round while the marker is out. Every rule below reads only scenario state and runs inside the rule
    /// library on every client the same way (co-op safe); the UI patches only stop the local player picking cards.
    ///   - Card selection: her hand can't be picked from (cards and long rest; an active card can still be removed, which
    ///     ends the bond as the rulebook allows), she counts as ready, and the engine's "everyone has two cards or a long
    ///     rest" gate lets her through with none (the gate alone sees a long rest flag for her; it is cleared right after).
    ///   - Her turn: never taken (marked played before the turn order is walked), initiative 99 (last, for focus).
    ///   - The Bear: initiative 14, placed in the turn order by it (not just before her as a summon normally is), its turn
    ///     under her player's control (the engine's own player-controlled summon turn), and after its first turn it is
    ///     marked not yet played, so the turn order gives it its second turn at once (Frosthaven's own boss second turn,
    ///     BossHasMonsterCardExtraTurnScenarioScript). Which turn it is comes from PlayedAtLeastOnceThisRound (saved).
    ///   - Her other ability cards' slots don't move while she is bound (CActiveBonus.UpdateXPTracker held); their effects
    ///     still happen. This card's own slot moves once at the end of each bond round (XP on slots 1 and 3) and the card
    ///     is lost after the third.
    ///   - The Vital Force's +2 ("first attack each round", a once-per-turn bonus in data) is not renewed for the Bear's
    ///     second turn of a bond round.
    /// Inert without the 2nd edition pack: everything keys on the Wildfury class (WildfuryID), her Bear (WildfuryBearID)
    /// and the marker only pack card 33627 carries. Each act logs "GHRule UnbreakableBond card 33627 ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHUnbreakableBond
    {
        private const string Marker = "GHUnbreakableBond";
        internal const int CardId = 33627;
        private const string VitalForceId = "VitalForceID";
        internal const int BearInitiative = 14;
        internal const int HerInitiative = 99;

        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? -1;

        private static void Log(string detail) => Plugin.Log.LogInfo($"GHRule UnbreakableBond card {CardId} {detail}");

        /// <summary>Logs once per round per key (the hooks run many times a round).</summary>
        private static void LogOnce(string key, string detail)
        {
            if (Logged.Count > 400) Logged.Clear();
            if (Logged.Add(Round + " " + key)) Log(detail);
        }

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"Gloomhaven 2e Unbreakable Bond: {what}: {e}");
        }

        /// <summary>Her Unbreakable Bond marker bonus while the card is out, or null.</summary>
        internal static CActiveBonus Bond(CActor her) =>
            her is CPlayerActor p && p.Class?.ID == GHWildfury.ClassId ? GHWildfury.CardBonus(p, Marker) : null;

        /// <summary>This round is one of the bond's three rounds for <paramref name="her"/> (any round after the play round
        /// while the card is out).</summary>
        internal static bool Bound(CActor her)
        {
            if (!(her is CPlayerActor p) || p.Class?.ID != GHWildfury.ClassId || p.IsDead) return false;
            CActiveBonus b = Bond(p);
            return b != null && b.ActiveBonusStartRound < Round;
        }

        private static bool IsBoundBear(CActor a) => GHWildfury.IsBear(a) && Bound(GHWildfury.SummonerOf(a));

        private static IEnumerable<CPlayerActor> BoundHeroes() =>
            ScenarioManager.Scenario?.PlayerActors?.Where(Bound).ToList() ?? new List<CPlayerActor>();

        // ------------------------------------------------------------------ initiative

        [HarmonyPatch(typeof(CPlayerActor), nameof(CPlayerActor.Initiative))]
        [HarmonyPostfix]
        private static void HerInitiativeLast(CPlayerActor __instance, ref int __result)
        {
            try
            {
                if (__instance?.Class?.ID == GHWildfury.ClassId && Bound(__instance)) __result = HerInitiative;
            }
            catch (Exception e) { Report("her initiative", e); }
        }

        [HarmonyPatch(typeof(CHeroSummonActor), nameof(CHeroSummonActor.Initiative))]
        [HarmonyPostfix]
        private static void BearInitiative14(CHeroSummonActor __instance, ref int __result)
        {
            try
            {
                if (IsBoundBear(__instance)) __result = BearInitiative;
            }
            catch (Exception e) { Report("Bear initiative", e); }
        }

        /// <summary>The engine puts a living summoner's summons just before her in the turn order whatever their initiative;
        /// a bound Bear goes where initiative 14 puts it instead.</summary>
        [HarmonyPatch(typeof(GameState), nameof(GameState.SortIntoInitiativeAndIDOrder))]
        [HarmonyPostfix]
        private static void PlaceBear()
        {
            try
            {
                List<CActor> order = GameState.InitiativeSortedActors;
                if (order == null || order.Count == 0) return;
                foreach (CPlayerActor her in BoundHeroes())
                {
                    CHeroSummonActor bear = GHWildfury.BearOf(her);
                    if (bear == null || !order.Remove(bear)) continue;
                    int at = order.FindIndex(x => x != null && GameState.s_ActorInitiativeComparer.Compare(bear, x) < 0);
                    if (at < 0) order.Add(bear); else order.Insert(at, bear);
                    LogOnce("order " + bear.ActorGuid, $"round {Round}: {GHPlague.NameOf(her)} is bound: the Bear acts at initiative {BearInitiative} (turn order position {order.IndexOf(bear) + 1} of {order.Count}), {GHPlague.NameOf(her)} at {HerInitiative}");
                }
            }
            catch (Exception e) { Report("turn order", e); }
        }

        // ------------------------------------------------------------------ card selection

        /// <summary>The engine's card selection gate (NextPhase from the ShortRest phase): every living character needs two
        /// round cards or a declared long rest. A bound Wildfury passes with none: she carries the long rest flag only while
        /// the gate runs (her initiative is 99 by HerInitiativeLast either way; no long rest is ever performed). Per call
        /// state (NextPhase re-enters itself through the next phase's first step).</summary>
        [HarmonyPatch(typeof(GameState), nameof(GameState.NextPhase))]
        [HarmonyPrefix]
        private static void SelectionGateBefore(out List<CPlayerActor> __state)
        {
            __state = null;
            try
            {
                if (PhaseManager.PhaseType != CPhase.PhaseType.ShortRest) return;
                foreach (CPlayerActor her in BoundHeroes())
                {
                    if (her.IsLongRestSelected || her.CharacterClass.RoundAbilityCards.Count >= 2) continue;
                    her.IsLongRestSelected = true;
                    (__state ?? (__state = new List<CPlayerActor>())).Add(her);
                    LogOnce("select " + her.ActorGuid, $"round {Round}: {GHPlague.NameOf(her)} skips card selection (bond round {BondRoundNumber(her)} of 3)");
                }
            }
            catch (Exception e) { Report("card selection gate", e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.NextPhase))]
        [HarmonyFinalizer]
        private static void SelectionGateAfter(List<CPlayerActor> __state)
        {
            try
            {
                if (__state != null) foreach (CPlayerActor her in __state) her.IsLongRestSelected = false;
            }
            catch (Exception e) { Report("card selection gate (after)", e); }
        }

        private static int BondRoundNumber(CPlayerActor her)
        {
            CActiveBonus b = Bond(her);
            return b == null ? 0 : b.TrackerIndex + 1;
        }

        /// <summary>UI: she is ready for the end-of-selection button with no cards.</summary>
        [HarmonyPatch(typeof(PlayerViewService), nameof(PlayerViewService.IsPlayerCardsHandSelectionReady))]
        [HarmonyPostfix]
        private static void ReadyWithoutCards(CPlayerActor playerActor, ref bool __result)
        {
            try
            {
                if (!__result && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest && Bound(playerActor)) __result = true;
            }
            catch (Exception e) { Report("ready without cards", e); }
        }

        /// <summary>UI: in card selection her hand and the long rest can't be picked; her active cards can still be removed.</summary>
        [HarmonyPatch(typeof(CardsHandUI), "SetMode")]
        [HarmonyPrefix]
        private static void LockHand(CardsHandUI __instance, CardHandMode newMode, ref bool longRestAvailable, ref Func<CAbilityCard, bool> cardSelectableFilter)
        {
            try
            {
                if (newMode != CardHandMode.CardsSelection || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest) return;
                CPlayerActor her = __instance?.PlayerActor;
                if (!Bound(her)) return;
                List<CAbilityCard> active = her.CharacterClass.ActivatedAbilityCards;
                longRestAvailable = false;
                cardSelectableFilter = c => active != null && active.Contains(c);
            }
            catch (Exception e) { Report("lock hand", e); }
        }

        /// <summary>UI: open the card hand on a character who still has to choose, not on a bound Wildfury.</summary>
        [HarmonyPatch(typeof(CardsHandManager), nameof(CardsHandManager.DecidePlayerToShowCardsFor))]
        [HarmonyPostfix]
        private static void ShowAnotherHand(ref CPlayerActor __result)
        {
            try
            {
                if (__result == null || !Bound(__result)) return;
                CPlayerActor other = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p != null && !p.IsDead && !Bound(p) && p.IsUnderMyControl()
                    && p.CharacterClass.RoundAbilityCards.Count < p.MaxRoundCardsCount && !p.CharacterClass.LongRest);
                if (other != null) __result = other;
            }
            catch (Exception e) { Report("show another hand", e); }
        }

        // ------------------------------------------------------------------ turns

        private static CActor _bearFirstTurnEnding;

        /// <summary>Before the turn order is walked: a bound Wildfury's turn is marked taken (she never starts one), and a
        /// bound Bear whose first turn just ended gets its second.</summary>
        [HarmonyPatch(typeof(GameState), nameof(GameState.MoveToNextActor))]
        [HarmonyPrefix]
        private static void BeforeNextActor()
        {
            try
            {
                SecondBearTurn();
                foreach (CPlayerActor her in BoundHeroes())
                {
                    if (her.PlayedThisRound) continue;
                    // never left with selected cards and no turn to play them (the UI stops this; a safety net)
                    foreach (CAbilityCard card in her.CharacterClass.RoundAbilityCards.ToList())
                        her.CharacterClass.MoveAbilityCard(card, her.CharacterClass.RoundAbilityCards, her.CharacterClass.HandAbilityCards, "RoundAbilityCards", "HandAbilityCards");
                    her.PlayedAtLeastOnceThisRound = true;
                    her.PlayedThisRound = true;
                    CHeroSummonActor bear = GHWildfury.BearOf(her);
                    LogOnce("skip " + her.ActorGuid, $"round {Round}: {GHPlague.NameOf(her)} skips her turn (bond round {BondRoundNumber(her)} of 3){(bear == null ? "; no Bear on the map" : "")}");
                }
            }
            catch (Exception e) { Report("skip her turn", e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.EndTurnCheckNextActorAndMoveToNextPhase))]
        [HarmonyPrefix]
        private static void TurnEnding()
        {
            try
            {
                CActor a = GameState.InternalCurrentActor;
                if (PhaseManager.PreviousPhase == null && a != null && !a.IsTakingExtraTurn && !a.PlayedAtLeastOnceThisRound && IsBoundBear(a))
                    _bearFirstTurnEnding = a;
            }
            catch (Exception e) { Report("Bear turn end", e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.EndTurnCheckNextActorAndMoveToNextPhase))]
        [HarmonyPostfix]
        private static void TurnEnded() => SecondBearTurn();

        /// <summary>The Bear's first turn of a bond round has ended (it is now marked played, once): mark it not played, so the
        /// turn order gives it its second turn next (it stands at initiative 14 and everyone before it has gone).</summary>
        private static void SecondBearTurn()
        {
            try
            {
                CActor bear = _bearFirstTurnEnding;
                if (bear == null || !bear.PlayedThisRound || !bear.PlayedAtLeastOnceThisRound) return;
                _bearFirstTurnEnding = null;
                if (bear.IsDead || !IsBoundBear(bear)) return;
                bear.PlayedThisRound = false;
                Log($"round {Round}: the Bear's first turn has ended: it takes its second turn at initiative {BearInitiative}");
            }
            catch (Exception e) { Report("Bear second turn", e); }
        }

        /// <summary>A bound Bear's turn is played by her player (the engine's player-controlled summon turn, queued the way
        /// MoveToNextActor queues one for a PlayerControlled summon).</summary>
        [HarmonyPatch(typeof(GameState), nameof(GameState.OverrideActorForOneTurn))]
        [HarmonyPrefix]
        private static void BearUnderControl(CActor currentActor)
        {
            try
            {
                if (currentActor == null || currentActor.IsTakingExtraTurn || !IsBoundBear(currentActor)) return;
                CPlayerActor her = GHWildfury.SummonerOf(currentActor);
                GameState.QueueOverrideActorForOneTurn(currentActor, CActor.EType.Player, null, null, her);
                Log($"round {Round}: the Bear's {(currentActor.PlayedAtLeastOnceThisRound ? "second" : "first")} turn, controlled by {GHPlague.NameOf(her)}'s player");
            }
            catch (Exception e) { Report("Bear control", e); }
        }

        // ------------------------------------------------------------------ use slots

        private static bool _advancingBond;

        /// <summary>While she is bound, her other ability cards' slots don't move (their effects still happen), and this
        /// card's slot moves only at the round's end (AdvanceBond).</summary>
        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.UpdateXPTracker))]
        [HarmonyPrefix]
        private static bool HoldSlots(CActiveBonus __instance, bool processTracker)
        {
            try
            {
                if (!processTracker || _advancingBond || __instance == null) return true;
                if (!(__instance.Caster is CPlayerActor her) || her.Class?.ID != GHWildfury.ClassId) return true;
                if (!(__instance.BaseCard is CAbilityCard) || __instance.BaseCard.CardType != CBaseCard.ECardType.CharacterAbility) return true;
                bool bond = ReferenceEquals(__instance, Bond(her));
                if (!bond && !Bound(her)) return true;
                if (!bond) Log($"round {Round}: {GHPlague.NameOf(her)} is bound: the slot of {__instance.BaseCard.Name} ({__instance.Ability?.Name}) does not move (its effect still happens)");
                return false;
            }
            catch (Exception e) { Report("hold slots", e); return true; }
        }

        /// <summary>The end of a bond round (the engine's end-of-round step for her round bonuses): this card's slot moves
        /// (1 XP after the first and the third), and after the third the card goes to the lost pile.</summary>
        [HarmonyPatch(typeof(CClass), nameof(CClass.MarkRoundActiveBonusesFinished))]
        [HarmonyPrefix]
        private static void AdvanceBond(CActor actor)
        {
            try
            {
                if (!(actor is CPlayerActor her) || her.Class?.ID != GHWildfury.ClassId || !Bound(her)) return;
                CActiveBonus b = Bond(her);
                int xpBefore = her.XP;
                _advancingBond = true;
                try { b.UpdateXPTracker(); }
                finally { _advancingBond = false; }
                int done = b.TrackerIndex;
                b.FinishIfNoRemaining();
                Log($"round {Round} ends: bond round {done} of 3 done{(her.XP > xpBefore ? $", {GHPlague.NameOf(her)} gains {her.XP - xpBefore} XP" : "")}{(b.Finished() || b.Finishing() ? ": the card is lost" : "")}");
            }
            catch (Exception e) { Report("advance the bond", e); }
        }

        // ------------------------------------------------------------------ Vital Force: once a round for the Bear

        /// <summary>The Vital Force's +2 is a once-per-turn bonus in data (= once a round for one-turn figures); at the start
        /// of a bound Bear's second turn it is not renewed for the Bear (no other figure has had a turn in between).</summary>
        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ResetRestriction))]
        [HarmonyPrefix]
        private static bool KeepVitalForceUsed(CActiveBonus __instance, CActiveBonus.EActiveBonusRestrictionType restrictionType)
        {
            try
            {
                if (restrictionType != CActiveBonus.EActiveBonusRestrictionType.OncePerTurn || __instance?.Caster?.Class?.ID != VitalForceId) return true;
                CActor bear = GameState.InternalCurrentActor;
                if (bear == null || !bear.PlayedAtLeastOnceThisRound || bear.PlayedThisRound || !IsBoundBear(bear)) return true;
                if (__instance.RestrictedActors == null || !__instance.RestrictedActors.Contains(bear)) return true;
                LogOnce("vital " + bear.ActorGuid, $"round {Round}: the Bear used the Vital Force's +2 on its first turn: not again on its second");
                return false;
            }
            catch (Exception e) { Report("Vital Force", e); return true; }
        }
    }
}
