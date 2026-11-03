using System;
using System.Collections.Generic;
using System.Linq;
using AStar;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Silent Knife's printed rules that her cards' data can't express (card-accuracy pass, rest-b):
    ///   - Practiced Reflexes bottom (32308, move marked GHMayNotSpringTraps): "You may choose not to spring traps in hexes
    ///     you enter with this movement." Each trap the move would spring asks her (Frosthaven's own Choose-not-to-trigger
    ///     perk box and texts) instead of the old Jump, which also passed enemies and still sprang the last hex.
    ///     "GHRule MayNotSpringTraps".
    ///   - Trickster's Reversal top (32312): "+(X+2) Attack, where X is the Shield value of the target": the bonus's +2 gets
    ///     the target's Shield added where the attack's numbers are built (CAttackActiveBonus_BuffAttack.ReferenceStrength),
    ///     so a x2 modifier doubles X too (the old Pierce 99 gave the same damage only without a doubling).
    ///     "GHRule TrickstersReversal".
    ///   - Gruesome Advantage bottom (32318): "When you next perform a melee attack targeting an enemy that has Poison this
    ///     round, after the attack ability perform: Attack 2, Range 3, Poison". The bonus fires once the whole attack ability
    ///     is over (Frosthaven's DuringActionAbilityOnAttackAbilityFinished), and only if one of its targets had Poison when
    ///     it was attacked (noted before each target's hit, so an attack that poisons doesn't set it off).
    ///     "GHRule GruesomeAdvantage".
    ///   - Spring the Trap bottom (32324): "Designate one adjacent trap. Move 5, Jump. Relocate the designated trap to an
    ///     adjacent featureless hex. If that hex is occupied by an enemy, spring the trap on that enemy and gain XP 1." The
    ///     traps adjacent to her when the move starts are the designated ones (the move trap step offers only those); the
    ///     destinations add featureless hexes next to her that hold an enemy, and a trap placed there springs on the enemy
    ///     at once (credited to her) with XP 1. "GHRule SpringTheTrap".
    ///   - Watch it Burn bottom (32328): "During your turn, you may play this card from your hand to perform this action (in
    ///     addition to the two cards you have played for the round)." While the card is in her hand and an enemy is
    ///     adjacent (Attack 5 is a melee attack), she is asked at the start of her turn, before her second card action and
    ///     at the end of her turn; on yes the card goes from her hand to her discard pile (no lost icon; QUESTIONS.md C) and
    ///     its bottom abilities are performed. "GHRule WatchItBurn".
    ///   - Fence (perk SilentKnife09): "Whenever you complete a city event, draw an attack modifier card as though you were
    ///     performing an Attack 4. Gain an amount of gold equal to the damage the attack would have dealt." Frosthaven's
    ///     outpost events stand for city events (QUESTIONS.md A8; road and boat events don't count): when one is completed,
    ///     a card is drawn from her modifier deck (her class deck with her perks, shuffled from the event and the
    ///     character, so every co-op peer draws the same), rolling cards add up, and she gains the damage as gold.
    ///     "GHRule Fence".
    ///   - Mastery 1 (FrosthavenMods_FlankedOrIsolatedOnly): every target she attacks this scenario stood next to one of her
    ///     allies, or next to none of its own allies; Value attacks or more. Each attack is checked as it happens.
    ///   - Mastery 2 (FrosthavenMods_MoneyTokensAtEnd): "End 3 scenarios with 12 or more money tokens": the money tokens she
    ///     holds when a scenario ends (GHMoneyTokens' SilentKnifeMoney) are read in the scenario result every peer receives;
    ///     each scenario ending with Amount or more adds one to a career count kept in synced campaign state (a marker in
    ///     CampaignState.LootedChestTreasureTableIDs, as the Boneshaper's career count in DwarfTweaks); at Value on a won
    ///     scenario the mastery is completed (not necessarily in a row; QUESTIONS.md C). "GHRule MoneyTokensAtEnd".
    /// Everything is gated on the pack's SilentKnifeID class, its card IDs (20000-39999) and its perk: without the 2nd
    /// edition pack this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSilentKnife
    {
        internal const string ClassId = "SilentKnifeID";
        internal const int PracticedReflexes = 32308, TrickstersReversal = 32312, GruesomeAdvantage = 32318,
                           SpringTheTrap = 32324, WatchItBurn = 32328;
        internal const string FencePerk = "SilentKnife09";
        internal const string FlankedTag = "FrosthavenMods_FlankedOrIsolatedOnly";
        internal const string MoneyEndTag = "FrosthavenMods_MoneyTokensAtEnd";

        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"Gloomhaven Silent Knife: {what} failed: {e}");
        }

        private static bool IsKnife(CActor a) => a != null && a.Class?.ID == ClassId;

        private static int CardOf(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; } catch { return 0; }
        }

        private static string Name(CActor a) => GHPlague.NameOf(a);

        // ================================================================ Practiced Reflexes bottom

        [HarmonyPatch(typeof(CObjectProp), nameof(CObjectProp.TryChooseToSkipTrap))]
        [HarmonyPostfix]
        private static void MayNotSpring(CActor actor, CObjectProp prop, CAbility ability, ref bool __result)
        {
            try
            {
                if (__result || actor == null || prop == null || !(ability is CAbilityMove) || !GHPlague.Marked(ability, "GHMayNotSpringTraps")) return;
                if (prop.ObjectType != ScenarioManager.ObjectImportType.Trap || prop.RuntimeAttachedActor != null) return;
                // a character with Frosthaven's own perk was already asked by the engine
                if (actor.Class is CCharacterClass c && c.Perks.Any(p => p.Flags.HasFlag(EPerkFlag.ChooseToNotTriggerTrap))) return;
                bool skip = GHPlague.Ask(actor, false, "PERK_ChooseNotTriggerTrap_Desc", "PERK_ChooseNotTriggerTrap_Confirm", "PERK_ChooseNotTriggerTrap_Trigger");
                Plugin.Log.LogInfo($"GHRule MayNotSpringTraps card {CardOf(ability)} {Name(actor)} {(skip ? "does not spring" : "springs")} the trap at {prop.ArrayIndex}");
                __result = skip;
            }
            catch (Exception e) { Report("Practiced Reflexes", e); }
        }

        // ================================================================ Trickster's Reversal top

        private static readonly HashSet<string> TricksterLogged = new HashSet<string>();

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.ReferenceStrength))]
        [HarmonyPostfix]
        private static void PlusShield(CAttackActiveBonus_BuffAttack __instance, CAbility ability, CActor target, ref int __result)
        {
            try
            {
                if (__result <= 0 || target == null || ability == null || __instance?.ActiveBonus?.BaseCard?.ID != TrickstersReversal) return;
                int shield = target.CalculateShield(ability);
                if (shield <= 0) return;
                __result += shield;
                string key = $"{ability.ID} {target.ActorGuid}";
                if (TricksterLogged.Add(key))
                {
                    if (TricksterLogged.Count > 500) { TricksterLogged.Clear(); TricksterLogged.Add(key); }
                    Plugin.Log.LogInfo($"GHRule TrickstersReversal card {TrickstersReversal} +{__result} Attack on {Name(target)} (2 + its Shield {shield})");
                }
            }
            catch (Exception e) { Report("Trickster's Reversal", e); }
        }

        // ================================================================ Gruesome Advantage bottom

        // attack abilities of the Silent Knife that attacked a target which had Poison before its hit
        private static readonly HashSet<CAbility> PoisonedTargetAttacks = new HashSet<CAbility>();

        [HarmonyPatch(typeof(CAbilityAttack), "AttackActor")]
        [HarmonyPrefix]
        private static void BeforeHit(CAbilityAttack ability, CActor targetingActor, CAttackSummary.TargetSummary attackingTargetSummary)
        {
            try
            {
                if (!IsKnife(targetingActor)) return;
                CActor target = attackingTargetSummary?.ActorToAttack;
                if (target == null) return;
                NoteFlankedOrIsolated(targetingActor, target);
                if (!target.Tokens.HasKey(CCondition.ENegativeCondition.Poison)) return;
                if (PoisonedTargetAttacks.Count > 200) PoisonedTargetAttacks.Clear();
                PoisonedTargetAttacks.Add(ability);   // AttackActor is static: the attack ability is its first parameter
                if (attackingTargetSummary.OriginalAbility != null) PoisonedTargetAttacks.Add(attackingTargetSummary.OriginalAbility);
            }
            catch (Exception e) { Report("attack notes", e); }
        }

        [HarmonyPatch(typeof(CDuringActionAbilityActiveBonus_TriggerAbilityOnAttackAbilityFinished), nameof(CDuringActionAbilityActiveBonus_TriggerAbilityOnAttackAbilityFinished.OnAttackAbilityFinished))]
        [HarmonyPrefix]
        private static bool GruesomeGate(CDuringActionAbilityActiveBonus_TriggerAbilityOnAttackAbilityFinished __instance, CAbilityAttack attackAbility)
        {
            try
            {
                if (__instance?.ActiveBonus?.BaseCard?.ID != GruesomeAdvantage || attackAbility == null) return true;
                bool poisoned = PoisonedTargetAttacks.Contains(attackAbility);
                PoisonedTargetAttacks.Remove(attackAbility);
                if (!attackAbility.IsMeleeAttack) return false;
                Plugin.Log.LogInfo($"GHRule GruesomeAdvantage card {GruesomeAdvantage} melee attack ability ended: {(poisoned ? "a target had Poison when attacked: Attack 2, Range 3, Poison follows" : "no target had Poison when attacked: no extra attack")}");
                return poisoned;
            }
            catch (Exception e) { Report("Gruesome Advantage", e); return true; }
        }

        // ================================================================ Spring the Trap bottom

        private static readonly Dictionary<string, List<CTile>> Designated = new Dictionary<string, List<CTile>>();
        private static readonly AccessTools.FieldRef<CAbilityMoveTrap, List<CTile>> OriginalTrapTiles =
            AccessTools.FieldRefAccess<CAbilityMoveTrap, List<CTile>>("m_OriginalTilesInRange");
        private static readonly AccessTools.FieldRef<CAbilityMoveTrap, CAbilityMoveTrap.EMoveTrapState> TrapState =
            AccessTools.FieldRefAccess<CAbilityMoveTrap, CAbilityMoveTrap.EMoveTrapState>("m_State");
        [ThreadStatic] private static CAbilityMoveTrap s_DestinationsFor;
        [ThreadStatic] private static HashSet<CTile> s_EnemyHexes;

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.Start))]
        [HarmonyPostfix]
        private static void DesignateTrap(CAbilityMove __instance)
        {
            try
            {
                if (!GHPlague.Marked(__instance, "GHDesignateAdjacentTrap") || __instance.TargetingActor == null) return;
                CActor me = __instance.TargetingActor;
                var traps = ScenarioManager.GetAllAdjacentTiles(ScenarioManager.Tiles[me.ArrayIndex.X, me.ArrayIndex.Y])
                    .Where(t => t?.Trap != null).ToList();
                Designated[me.ActorGuid] = traps;
                Plugin.Log.LogInfo($"GHRule SpringTheTrap card {SpringTheTrap} {traps.Count} adjacent trap(s) designated before the move: {string.Join(", ", traps.Select(t => t.m_ArrayIndex.ToString()))}");
            }
            catch (Exception e) { Report("Spring the Trap (designate)", e); }
        }

        [HarmonyPatch(typeof(CAbilityMoveTrap), nameof(CAbilityMoveTrap.Start))]
        [HarmonyPostfix]
        private static void OnlyDesignated(CAbilityMoveTrap __instance)
        {
            try
            {
                if (!GHPlague.Marked(__instance, "GHMoveTrapOntoEnemy") || __instance.TargetingActor == null) return;
                if (!Designated.TryGetValue(__instance.TargetingActor.ActorGuid, out List<CTile> traps)) return;
                __instance.TilesInRange.RemoveAll(t => !traps.Contains(t));
                OriginalTrapTiles(__instance)?.RemoveAll(t => !traps.Contains(t));
                Plugin.Log.LogInfo($"GHRule SpringTheTrap card {SpringTheTrap} relocate: {__instance.TilesInRange.Count} designated trap(s) still on the map");
            }
            catch (Exception e) { Report("Spring the Trap (traps)", e); }
        }

        [HarmonyPatch(typeof(CAbilityMoveTrap), nameof(CAbilityMoveTrap.Perform))]
        [HarmonyPrefix]
        private static void BeforePerform(CAbilityMoveTrap __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (TrapState(__instance) != CAbilityMoveTrap.EMoveTrapState.SelectMoveToTile || !GHPlague.Marked(__instance, "GHMoveTrapOntoEnemy")) return;
                s_DestinationsFor = __instance;
                s_EnemyHexes = new HashSet<CTile>();
                __state = true;
            }
            catch (Exception e) { Report("Spring the Trap (destinations)", e); }
        }

        [HarmonyPatch(typeof(CAbilityMoveTrap), nameof(CAbilityMoveTrap.Perform))]
        [HarmonyFinalizer]
        private static Exception AfterPerform(Exception __exception, CAbilityMoveTrap __instance, bool __state)
        {
            if (__state)
            {
                if (s_EnemyHexes != null && s_EnemyHexes.Count > 0)
                    Plugin.Log.LogInfo($"GHRule SpringTheTrap card {SpringTheTrap} destinations include {s_EnemyHexes.Count} featureless hex(es) with an enemy");
                s_DestinationsFor = null;
                s_EnemyHexes = null;
            }
            return __exception;
        }

        /// <summary>The destination list (empty hexes around her): add the featureless hexes there holding an enemy.</summary>
        [HarmonyPatch(typeof(GameState), nameof(GameState.GetTilesInRange), new[] { typeof(Point), typeof(int), typeof(CAbility.EAbilityTargeting), typeof(bool), typeof(bool),
            typeof(List<CTile>), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        private static void AddEnemyHexes(Point arrayIndex, int range, bool emptyTilesOnly, List<CTile> __result)
        {
            CAbilityMoveTrap ability = s_DestinationsFor;
            if (ability == null || !emptyTilesOnly || __result == null) return;
            try
            {
                CActor me = ability.TargetingActor;
                foreach (CTile t in ScenarioManager.GetAllAdjacentTiles(ScenarioManager.Tiles[arrayIndex.X, arrayIndex.Y]))
                {
                    if (t == null || __result.Contains(t)) continue;
                    CActor there = ScenarioManager.Scenario.FindActorAt(t.m_ArrayIndex);
                    if (there == null || there.IsDead || !CActor.AreActorsEnemies(me.Type, there.Type, me, there)) continue;
                    if (!CAbilityFilter.IsTileFeatureless(t)) continue;
                    __result.Add(t);
                    s_EnemyHexes?.Add(t);
                }
            }
            catch (Exception e) { Report("Spring the Trap (enemy hexes)", e); }
        }

        /// <summary>The destination filter (empty hexes): the enemy hexes added above pass.</summary>
        [HarmonyPatch(typeof(CAbilityFilter), nameof(CAbilityFilter.IsValidTile))]
        [HarmonyPostfix]
        private static void EnemyHexValid(CTile tile, CAbilityFilter.EFilterTile tileFilter, ref bool __result)
        {
            if (__result || s_DestinationsFor == null || s_EnemyHexes == null || tileFilter != CAbilityFilter.EFilterTile.EmptyHex) return;
            if (s_EnemyHexes.Contains(tile)) __result = true;
        }

        /// <summary>The relocated trap lands under an enemy: it springs on that enemy (credited to her) and she gains XP 1.</summary>
        [HarmonyPatch(typeof(CTile), nameof(CTile.SpawnProp))]
        [HarmonyPostfix]
        private static void TrapLanded(CTile __instance, CObjectProp prop, CAbility fromAbility)
        {
            try
            {
                if (!(fromAbility is CAbilityMoveTrap) || !(prop is CObjectTrap trap) || !GHPlague.Marked(fromAbility, "GHMoveTrapOntoEnemy")) return;
                CActor me = fromAbility.TargetingActor;
                CActor enemy = ScenarioManager.Scenario.FindActorAt(__instance.m_ArrayIndex);
                if (me == null || enemy == null || enemy.IsDead || !CActor.AreActorsEnemies(me.Type, enemy.Type, me, enemy)) return;
                trap.Activate(enemy, me);
                me.GainXP(1);
                Plugin.Log.LogInfo($"GHRule SpringTheTrap card {SpringTheTrap} the trap relocated onto {Name(enemy)} springs on it; {Name(me)} gains XP 1");
            }
            catch (Exception e) { Report("Spring the Trap (spring)", e); }
        }

        // ================================================================ Watch it Burn bottom

        private static CPhase s_LastStart, s_LastEnd;
        private static CActor s_TurnActor;
        private static int s_CardActions;

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.FindApplicableActiveBonuses),
            new[] { typeof(CActor), typeof(CAbility.EAbilityType), typeof(CActiveBonus.EActiveBonusBehaviourType), typeof(bool) })]
        [HarmonyPostfix]
        private static void TurnHooks(CActor actor, CAbility.EAbilityType type, CActiveBonus.EActiveBonusBehaviourType behaviourType)
        {
            if (type != CAbility.EAbilityType.AddActiveBonus || actor == null || actor != GameState.InternalCurrentActor) return;
            try
            {
                if (behaviourType == CActiveBonus.EActiveBonusBehaviourType.StartTurnAbility && PhaseManager.CurrentPhase is CPhaseStartTurn start
                    && !ReferenceEquals(start, s_LastStart))
                {
                    s_LastStart = start;
                    s_TurnActor = actor;
                    s_CardActions = 0;
                    OfferWatchItBurn(actor, "at the start");
                }
                else if (behaviourType == CActiveBonus.EActiveBonusBehaviourType.EndTurnAbility && PhaseManager.CurrentPhase is CPhaseEndTurn end
                         && !end.m_SentEndTurnMessage && !ReferenceEquals(end, s_LastEnd))
                {
                    s_LastEnd = end;
                    OfferWatchItBurn(actor, "at the end");
                }
            }
            catch (Exception e) { Report("Watch it Burn (turn)", e); }
        }

        [HarmonyPatch(typeof(CPhaseAction), MethodType.Constructor)]
        [HarmonyPostfix]
        private static void BetweenActions(CPhaseAction __instance)
        {
            try
            {
                if (GameState.CurrentActionInitiator != GameState.EActionInitiator.AbilityCard) return;
                CActor me = GameState.InternalCurrentActor;
                if (!IsKnife(me) || !ReferenceEquals(me, s_TurnActor) || ++s_CardActions != 2) return;
                List<CAbility> abilities = AskWatchItBurn(me as CPlayerActor, "before her second action");
                GameState.CurrentActorAction current = GameState.CurrentAction;
                if (abilities == null || current?.Action == null || __instance?.RemainingPhaseAbilities == null) return;
                for (int i = abilities.Count - 1; i >= 0; i--)
                    __instance.RemainingPhaseAbilities.Insert(0, new CPhaseAction.CPhaseAbility(abilities[i], me, current.BaseCard, current.Action.ID));
            }
            catch (Exception e) { Report("Watch it Burn (between actions)", e); }
        }

        private static void OfferWatchItBurn(CActor actor, string when)
        {
            if (!IsKnife(actor) || GameState.PendingScenarioModifierAbilities == null) return;
            List<CAbility> abilities = AskWatchItBurn(actor as CPlayerActor, when + " of her turn");
            if (abilities == null) return;
            foreach (CAbility a in abilities) GameState.PendingScenarioModifierAbilities.Add(new Tuple<CAbility, CActor>(a, actor));
        }

        private static List<CAbility> AskWatchItBurn(CPlayerActor me, string when)
        {
            if (me == null || me.IsDead || me.CharacterClass == null) return null;
            CAbilityCard card = me.CharacterClass.HandAbilityCards.FirstOrDefault(c => c != null && c.ID == WatchItBurn);
            if (card?.BottomAction?.Abilities == null) return null;
            // Attack 5 with no range is a melee attack: only worth asking with an enemy next to her
            bool enemyAdjacent = ScenarioManager.Scenario.AllAliveActors.Any(a => a != null && !a.IsDead && CActor.AreActorsEnemies(me.Type, a.Type, me, a)
                                                                                  && CPathFinder.IsAdjacent(me.ArrayIndex, a.ArrayIndex));
            if (!enemyAdjacent) return null;
            if (!GHPlague.Ask(me, false, "FHMODS_SK_WATCHITBURN", "FHMODS_SK_PLAY", "FHMODS_SK_NOTNOW", Name(me))) return null;
            me.CharacterClass.MoveAbilityCard(card, me.CharacterClass.HandAbilityCards, me.CharacterClass.DiscardedAbilityCards, "HandAbilityCards", "DiscardedAbilityCards");
            Plugin.Log.LogInfo($"GHRule WatchItBurn card {WatchItBurn} {Name(me)} plays Watch it Burn from her hand {when}: its bottom action is performed, the card goes to her discard pile");
            return card.BottomAction.Abilities.Select(a => CAbility.CopyAbility(a, generateNewID: false)).ToList();
        }

        // ================================================================ Mastery 1: only flanked or isolated targets

        private static object s_FlankScenario;
        private static int s_FlankAttacks, s_FlankBad;

        private static void NoteFlankedOrIsolated(CActor me, CActor target)
        {
            if (!CActor.AreActorsEnemies(me.Type, target.Type, me, target)) return;
            object scenario = ScenarioManager.CurrentScenarioState;
            if (!ReferenceEquals(scenario, s_FlankScenario)) { s_FlankScenario = scenario; s_FlankAttacks = 0; s_FlankBad = 0; }
            s_FlankAttacks++;
            List<CActor> around = ScenarioManager.Scenario.AllAliveActors
                .Where(a => a != null && !a.IsDead && a != target && CPathFinder.IsAdjacent(a.ArrayIndex, target.ArrayIndex)).ToList();
            bool flanked = around.Any(a => a != me && CActor.AreActorsAllied(me.Type, a.Type, me, a));
            bool isolated = !around.Any(a => CActor.AreActorsAllied(target.Type, a.Type, target, a));
            if (flanked || isolated) return;
            s_FlankBad++;
            Plugin.Log.LogInfo($"GHRule FlankedOrIsolated card 0 mastery: {Name(me)} attacked {Name(target)}, next to none of her allies and next to an ally of its own (mastery failed)");
        }

        // ================================================================ Mastery 2: end 3 scenarios with 12+ money tokens

        private const string MoneyMarker = "FHMODS:SKMoney12:";
        private static readonly FHRL.MasteryID MoneyMastery = new FHRL.MasteryID("SilentKnife_MASTERY_2");
        private static int s_CareerShown;

        [HarmonyPatch(typeof(FHRL.ScenarioController), nameof(FHRL.ScenarioController.CompleteActiveScenario))]
        [HarmonyPrefix]
        private static void MoneyCareer(FHRL.ScenarioController __instance, FHRL.ScenarioResult result)
        {
            if (result == null || result.isRestarting) return;
            try
            {
                FHRL.CampaignState state = __instance.CampaignController.State;
                foreach (FHRL.ScenarioResult.CharacterResult r in result.Characters)
                {
                    if (r.Character?.ID.ID != ClassId) continue;
                    FHRL.CharacterState c = state.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null || c.CompletedMasteryIDs.Contains(MoneyMastery)) continue;
                    CActor knife = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(IsKnife);
                    int held = knife?.GetCharacterResourceAmount(GHMoneyTokens.Money) ?? 0;
                    string prefix = MoneyMarker + c.ID.ID + "=";
                    string old = state.LootedChestTreasureTableIDs.FirstOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
                    int total = old != null && int.TryParse(old.Substring(prefix.Length), out int n) ? n : 0;
                    if (held >= 12)
                    {
                        total++;
                        if (old != null) state.LootedChestTreasureTableIDs.Remove(old);
                        state.LootedChestTreasureTableIDs.Add(prefix + total);
                    }
                    s_CareerShown = total;
                    Plugin.Log.LogInfo($"GHRule MoneyTokensAtEnd card 0 mastery: {GiveRules.Name(c)} ends the scenario with {held} money tokens; scenarios ended with 12 or more: {total} of 3");
                    if (total >= 3 && result.ScenarioWon && !r.MasteriesCompleted.Contains(MoneyMastery))
                    {
                        r.MasteriesCompleted.Add(MoneyMastery);
                        Plugin.Log.LogInfo($"GHRule MoneyTokensAtEnd card 0 mastery: {GiveRules.Name(c)} completes the mastery");
                    }
                }
            }
            catch (Exception e) { Report("Money tokens mastery", e); }
        }

        // ================================================================ the two mastery tags

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool AnswerMasteries(CUnlockConditionTarget unlockConditionTarget, ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null) return true;
            try
            {
                if (tags.Contains(FlankedTag))
                {
                    int needed = Math.Max(1, unlockConditionTarget.Value);
                    bool current = ReferenceEquals(ScenarioManager.CurrentScenarioState, s_FlankScenario);
                    int attacks = current ? s_FlankAttacks : 0, bad = current ? s_FlankBad : 0;
                    totalConditions = needed + 1;
                    conditionsMet = Math.Min(attacks, needed) + (bad == 0 ? 1 : 0);
                    __result = bad == 0 && attacks >= needed;
                    Plugin.Log.LogInfo($"GHRule FlankedOrIsolated card 0 mastery: {attacks} attacks (needs {needed}), {bad} on a target neither flanked nor isolated{(__result ? ": met" : "")}");
                    return false;
                }
                if (tags.Contains(MoneyEndTag))
                {
                    // completed by the scenario result (MoneyCareer); this only shows the career count
                    totalConditions = Math.Max(1, unlockConditionTarget.Value);
                    conditionsMet = Math.Min(s_CareerShown, totalConditions);
                    __result = false;
                    return false;
                }
            }
            catch (Exception e) { Report("mastery check", e); }
            return true;
        }

        // ================================================================ Fence (outpost events)

        [HarmonyPatch(typeof(FHRL.OutpostEventController), "IterateCurrentEvent")]
        [HarmonyPrefix]
        private static void BeforeEvent(FHRL.OutpostEventController __instance, out FHRL.OutpostEventState __state)
        {
            __state = null;
            try { __state = __instance.EventToResolve; } catch { }
        }

        [HarmonyPatch(typeof(FHRL.OutpostEventController), "IterateCurrentEvent")]
        [HarmonyPostfix]
        private static void AfterEvent(FHRL.OutpostEventController __instance, FHRL.OutpostEventState __state)
        {
            try
            {
                if (__state == null || !__state.Completed || __state.Def == null || __state.Def.Type != FHRL.OutpostEventType.Outpost) return;
                FHRL.CampaignController campaign = __instance.CampaignController;
                foreach (FHRL.CharacterState c in campaign.State.ActiveCharacters.ToList())
                {
                    if (c == null || c.ClassID.ID != ClassId || !c.PerkIDs.Any(p => p.ID == FencePerk)) continue;
                    int damage = FenceDraw(c, __state.ID.ID, out string drawn);
                    if (damage > 0) AddGold(campaign.CharacterController, c, damage);
                    Plugin.Log.LogInfo($"GHRule Fence card 0 Fence: {GiveRules.Name(c)} completed outpost event {__state.ID.ID}: drew {drawn} as an Attack 4, gains {damage} gold");
                    CoopLog.Write($"FENCE: {GiveRules.Name(c)} drew {drawn} after event {__state.ID.ID}: +{damage} gold");
                }
            }
            catch (Exception e) { Report("Fence", e); }
        }

        private static void AddGold(FHRL.CharacterController controller, FHRL.CharacterState c, int gold) =>
            AccessTools.Method(typeof(FHRL.CharacterController), "Action_AddGold").Invoke(controller, new object[] { c, gold });

        /// <summary>One attack modifier draw for an Attack 4 from her deck (class deck plus her perks' cards), rolling cards
        /// added up. Shuffled with a seed from the event and the character, so every peer draws the same card.</summary>
        private static int FenceDraw(FHRL.CharacterState c, string eventId, out string drawn)
        {
            CSRLYML yml = ScenarioRuleClient.SRLYML;
            CharacterYMLData data = yml.Characters.First(x => x.ID == ClassId);
            List<AttackModifierYMLData> deck = data.AttackModifierDeck.GetAllAttackModifiers.ToList();
            var perks = c.PerkIDs.Select(p => yml.Perks.FirstOrDefault(y => y.ID == p.ID)).Where(p => p != null).ToList();
            CCharacterClass.ApplyPerksToList(perks, ref deck);
            uint seed = 2166136261;
            foreach (char ch in eventId + "|" + c.ID.ID + "|" + c.Gold + "|" + c.PerkIDs.Count) seed = (seed ^ ch) * 16777619;
            var rng = new Random((int)(seed & 0x7FFFFFFF));
            var order = deck.OrderBy(_ => rng.Next()).ToList();
            int attack = 4;
            var names = new List<string>();
            foreach (AttackModifierYMLData card in order)
            {
                names.Add(card.Name);
                if (AttackModifierYMLData.TryParseMathModifier(card.MathModifier ?? "", out char op, out int v))
                {
                    if (op == '+') attack += v;
                    else if (op == '-') attack -= v;
                    else if (op == '*' || op == 'x' || op == 'X') attack *= v;
                }
                if (!card.Rolling) break;
            }
            drawn = string.Join(" + ", names);
            return Math.Max(0, attack);
        }

        // ================================================================ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_SK_WATCHITBURN"] = "Watch it Burn: {0} may play this card from her hand now to perform Attack 5 (the card goes to her discard pile).",
            ["FHMODS_SK_PLAY"] = "Play it",
            ["FHMODS_SK_NOTNOW"] = "Not now",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_SK_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
