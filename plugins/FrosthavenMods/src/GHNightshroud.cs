using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Nightshroud's class mat, Invisible part (the printed mat, the user's decision A4: as close
    /// to the physical design as the engine allows). Only the pack's Nightshroud2ID hero is touched: inert without the 2e
    /// pack. Rules (mat text, then the 2e FAQ's Eclipse rulings):
    ///  1. "You start each scenario with Invisible": given when the scenario's first phase starts (StartScenarioEffects,
    ///     a first load only; a reloaded save keeps the token it saved).
    ///  2. "...and do not lose Invisible at the end of your turns": every Invisible token she gains (her cards, her mat,
    ///     an ally's) is held with the Never trigger instead of Turns, so only a rule below removes it.
    ///  3. "While you are Invisible, you cannot perform attack abilities" (unless Swallowed by Night's bottom bonus, card
    ///     32927, is active): an attack ability of hers is cancelled when it would be performed (the game skips it, as
    ///     for an attack with no target), "or open doors": closed doors are walls for her path (as for a hero summon),
    ///     "you may move through enemies as if they were your allies": hostile figures don't block her path (she still
    ///     can't end a move on one: the game's own destination check), "and enemies treat the hex you occupy as
    ///     unoccupied": Frosthaven already lets enemies move through an Invisible figure; the AI may now also end its move
    ///     there (CAIMoveNew.CanStopOnTopOf). "If an enemy ends its movement in your hex, you must Teleport to the closest
    ///     empty hex and suffer hazardous terrain damage": after that enemy's move she gets an inline Teleport to the
    ///     closest empty hex (fewest hexes away; a tie: the player picks among them) and a Damage ability of the scenario's
    ///     hazardous terrain damage.
    ///  4. "When all other characters are exhausted or have escaped, you lose Invisible and cannot gain it" (no other
    ///     living character in the scenario, a solo Nightshroud included): checked at every turn end, exhaustion and round
    ///     end; any Invisible she would gain then is refused.
    ///  5. "At the end of each round in which you perform an attack, gain Invisible": every attack ability of hers that
    ///     happened is noted; at the round's end she gains Invisible, unless every such attack was Shadowstrike's bottom
    ///     attack with its consume taken (the round marker of card 32926: "this attack does not cause you to gain
    ///     Invisible at the end of the round").
    /// The Tear rules (enter a Tear to lose Invisible, the two-Tear limit, Tear hexes, Prepare for the Kill) are in
    /// GHTears.cs. Logs "GHRule NightshroudInvisible card {id} ..." each time a rule acts.
    /// </summary>
    [HarmonyPatch]
    internal static class GHNightshroud
    {
        public const string ClassId = "Nightshroud2ID";
        public const int BlackKnivesCard = 32908, PrepareCard = 32916, ShadowstrikeCard = 32926, SwallowedByNightCard = 32927;
        private const string Rule = "NightshroudInvisible";

        // rule 5: the base card of every attack ability of hers that happened this round, per hero (guid)
        private static readonly Dictionary<string, List<int>> AttacksThisRound = new Dictionary<string, List<int>>();
        // Shadowstrike's bottom marker was seen active this round
        private static readonly HashSet<string> MarkerThisRound = new HashSet<string>();
        private static readonly HashSet<string> Refused = new HashSet<string>();
        private static readonly HashSet<Guid> CancelledAttacks = new HashSet<Guid>();

        public static bool Is(CActor a) => a is CPlayerActor p && p.CharacterClass?.ID == ClassId;

        public static List<CPlayerActor> Heroes() =>
            ScenarioManager.Scenario?.PlayerActors?.Where(p => Is(p) && !p.IsDead).ToList() ?? new List<CPlayerActor>();

        public static bool HasInvisible(CActor a) => a?.Tokens != null && a.Tokens.HasKey(CCondition.EPositiveCondition.Invisible);

        /// <summary>Under the mat's Invisible restrictions now (rule 3).</summary>
        public static bool Hidden(CActor a) => Is(a) && HasInvisible(a);

        /// <summary>No other character is still in the scenario (rule 4).</summary>
        public static bool Alone(CActor her) =>
            ScenarioManager.Scenario?.PlayerActors?.Any(p => p != her && !p.IsDead && p.CauseOfDeath != CActor.ECauseOfDeath.ActorRemovedFromMap) != true;

        /// <summary>The active (not finished) bonus of one of her cards on her, or null. Read from her cards directly (the
        /// game's FindAllActiveBonuses also finishes expired bonuses, a side effect not wanted in the middle of an ability).</summary>
        public static CActiveBonus CardBonus(CActor her, int cardId)
        {
            try
            {
                CCharacterClass cls = (her as CPlayerActor)?.CharacterClass;
                if (cls == null) return null;
                return cls.ActivatedCards.Cast<CBaseCard>().Concat(cls.RoundAbilityCards)
                    .Where(c => c != null && c.ID == cardId && c.ActiveBonuses != null)
                    .SelectMany(c => c.ActiveBonuses)
                    .FirstOrDefault(b => b != null && b.Actor == her && !b.Finished() && !b.Finishing());
            }
            catch (Exception) { return null; }
        }

        public static int CardId(CAbility ability)
        {
            try { return ability?.GetAbilityBaseCard()?.ID ?? 0; }
            catch (Exception) { return 0; }
        }

        public static void Log(int card, string detail) => Plugin.Log.LogInfo($"GHRule {Rule} card {card} {detail}");

        private static void Lose(CPlayerActor her, string why)
        {
            if (!HasInvisible(her)) return;
            her.Tokens.RemovePositiveToken(CCondition.EPositiveCondition.Invisible);
            Log(0, $"class mat: {why}: Invisible lost");
        }

        private static void Gain(CPlayerActor her, string why)
        {
            if (Alone(her))
            {
                // the printed rule read literally (the lead's decision 2026-11-02): a solo Nightshroud is never Invisible
                if (Refused.Add(her.ActorGuid)) Log(0, $"class mat: {why}: no other character is in the scenario (all other characters exhausted or escaped, or none: a solo Nightshroud): no Invisible");
                return;
            }
            if (HasInvisible(her)) return;
            her.ApplyCondition(her, CCondition.EPositiveCondition.Invisible, 1, EConditionDecTrigger.Never);
            Log(0, $"class mat: {why}: Invisible gained");
        }

        private static void CheckAlone()
        {
            foreach (CPlayerActor her in Heroes())
                if (Alone(her)) Lose(her, "all other characters are exhausted or have escaped");
        }

        // ---- 1. start Invisible; state reset

        [HarmonyPatch(typeof(PhaseManager), nameof(PhaseManager.SetNextPhase))]
        [HarmonyPostfix]
        private static void ScenarioStart(CPhase.PhaseType type)
        {
            if (type != CPhase.PhaseType.StartScenarioEffects) return;
            try
            {
                AttacksThisRound.Clear(); MarkerThisRound.Clear(); Refused.Clear(); CancelledAttacks.Clear();
                GHTears.Reset();
                foreach (CPlayerActor her in Heroes()) Gain(her, "the scenario starts");
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: scenario start: " + e); }
        }

        // ---- 2. never lost at the end of her turns

        [HarmonyPatch(typeof(CTokens), nameof(CTokens.AddPositiveToken), new[] { typeof(CCondition.EPositiveCondition), typeof(int),
            typeof(EConditionDecTrigger), typeof(CActor), typeof(CActor), typeof(bool) })]
        [HarmonyPrefix]
        private static void KeepToken(CTokens __instance, CCondition.EPositiveCondition token, ref EConditionDecTrigger decTrigger, bool recall)
        {
            if (recall || token != CCondition.EPositiveCondition.Invisible || !Is(__instance.Actor) || decTrigger == EConditionDecTrigger.Never) return;
            decTrigger = EConditionDecTrigger.Never;
        }

        [HarmonyPatch(typeof(CTokens), nameof(CTokens.AddPositiveToken), new[] { typeof(CActor), typeof(PositiveConditionPair), typeof(bool) })]
        [HarmonyPrefix]
        private static void KeepPair(CTokens __instance, PositiveConditionPair pair, bool recall)
        {
            if (recall || pair == null || pair.PositiveCondition != CCondition.EPositiveCondition.Invisible || !Is(__instance.Actor)) return;
            pair.ConditionDecTrigger = EConditionDecTrigger.Never;
        }

        // ---- 4. cannot gain it once alone

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyCondition), new[] { typeof(CActor), typeof(CCondition.EPositiveCondition), typeof(int),
            typeof(EConditionDecTrigger), typeof(string), typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static bool RefuseWhenAlone(CActor __instance, CCondition.EPositiveCondition condition)
        {
            try
            {
                if (condition != CCondition.EPositiveCondition.Invisible || !Is(__instance) || !Alone(__instance)) return true;
                if (Refused.Add(__instance.ActorGuid)) Log(0, "class mat: all other characters are exhausted or have escaped: cannot gain Invisible");
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: " + e); return true; }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.KillActor))]
        [HarmonyPostfix]
        private static void AfterKill(CActor actor)
        {
            try { if (actor is CPlayerActor) CheckAlone(); }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: " + e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.LogEndTurn))]
        [HarmonyPostfix]
        private static void AfterTurn(CActor actor)
        {
            try
            {
                CheckAlone();
                if (Is(actor)) { NoteMarker(actor); GHTears.EndOfAbilityPoint(actor, null); ClawsNextTurnEnded(actor); }
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: " + e); }
        }

        // Claws of the Night top (card 32921): "At the end of your next turn, if you do not have Invisible, you may perform:
        // Attack 5". The card's EndTurnAbility bonus fires only when its requirements are met (not Invisible, next turn);
        // otherwise the engine keeps it, and it fired at a LATER turn's end. Here: when the next turn ended (the bonus is
        // from an earlier round) and it is still unfinished, it ends (the card goes to the discard pile).
        public const int ClawsCard = 32921;

        private static void ClawsNextTurnEnded(CActor her)
        {
            CActiveBonus claws = CardBonus(her, ClawsCard);
            if (claws == null || claws.InternalRoundNumber <= 0) return;   // played this turn: its "next turn" is still to come
            if (claws.HasBehaviour(CActiveBonus.EActiveBonusBehaviourType.EndTurnAbility) != true) return;
            claws.Finish();
            Log(ClawsCard, $"Claws of the Night: the next turn ended{(HasInvisible(her) ? " while Invisible" : " without the attack")}: the card ends");
        }

        // ---- 3. no attacks while Invisible

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.Perform))]
        [HarmonyPrefix]
        private static void NoAttack(CAbilityAttack __instance)
        {
            try
            {
                CActor her = __instance.TargetingActor;
                if (!Hidden(her) || __instance.AbilityHasHappened || __instance.AbilityHasBeenCancelled) return;
                if (CardBonus(her, SwallowedByNightCard) != null) return;
                __instance.SetCancelAbilityFlag(cancel: true, forceImmediate: true);
                if (CancelledAttacks.Add(__instance.ID)) Log(CardId(__instance), $"class mat: {__instance.Name} not performed (attack abilities are not allowed while Invisible)");
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: attack check: " + e); }
        }

        // ---- 3. doors and enemies on her path

        [ThreadStatic] private static CActor _mover;

        [HarmonyPatch(typeof(CPlayerActor), nameof(CPlayerActor.FindPlayerPath))]
        [HarmonyPrefix]
        private static void PathStart(CActor actorFindingPath, out CActor __state)
        {
            __state = _mover;
            _mover = actorFindingPath;
        }

        [HarmonyPatch(typeof(CPlayerActor), nameof(CPlayerActor.FindPlayerPath))]
        [HarmonyFinalizer]
        private static Exception PathEnd(Exception __exception, CActor __state)
        {
            _mover = __state;
            return __exception;
        }

        private static readonly MethodInfo VisibleBlocks = AccessTools.PropertyGetter(typeof(CActor), nameof(CActor.VisibleBlocksPathing));
        private static bool _throughReported, _doorReported;

        [HarmonyPatch(typeof(CPlayerActor), nameof(CPlayerActor.FindPlayerPath))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> PathRules(IEnumerable<CodeInstruction> code)
        {
            int blocks = 0, doors = 0;
            foreach (CodeInstruction c in code)
            {
                if (c.Calls(VisibleBlocks))
                {
                    blocks++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GHNightshroud), nameof(BlocksHer))).WithLabels(c.labels).WithBlocks(c.blocks);
                    continue;
                }
                if (c.opcode == OpCodes.Isinst && c.operand is Type t && t == typeof(CHeroSummonActor))
                {
                    doors++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GHNightshroud), nameof(DoorsAreWalls))).WithLabels(c.labels).WithBlocks(c.blocks);
                    continue;
                }
                yield return c;
            }
            if (blocks == 0 || doors == 0) Plugin.Log.LogWarning($"Nightshroud class mat: path rules not fully applied (blockers {blocks}, doors {doors}): the game's code changed");
        }

        /// <summary>A figure blocks the path being found, unless the mover is the Invisible Nightshroud and the figure is
        /// an enemy of hers (not a map object): "move through enemies as if they were your allies".</summary>
        private static bool BlocksHer(CActor blocker)
        {
            bool blocks = blocker.VisibleBlocksPathing;
            if (!blocks || !(_mover is CPlayerActor her) || blocker is CObjectActor) return blocks;
            if (!Hidden(her) || CActor.AreActorsAllied(her.Type, blocker.Type, her, blocker)) return blocks;
            if (!_throughReported) { _throughReported = true; Log(0, "class mat: moves through enemies while Invisible"); }
            return false;
        }

        /// <summary>The game makes closed doors walls for a hero summon's path ("is CHeroSummonActor"); the Invisible
        /// Nightshroud gets the same: "cannot open doors".</summary>
        private static object DoorsAreWalls(object actor)
        {
            if (actor is CHeroSummonActor) return actor;
            if (actor is CPlayerActor p && Hidden(p))
            {
                if (!_doorReported) { _doorReported = true; Log(0, "class mat: closed doors are walls while Invisible"); }
                return actor;
            }
            return null;
        }

        // ---- 3. enemies may end their move in her hex; she then teleports away and suffers hazardous terrain damage

        [HarmonyPatch(typeof(CAIMoveNew), nameof(CAIMoveNew.CanStopOnTopOf))]
        [HarmonyPostfix]
        private static void EnemyMayStop(CActor movingActor, CActor otherActor, ref bool __result)
        {
            if (__result || movingActor == null || !Hidden(otherActor)) return;
            if (!CActor.AreActorsEnemies(movingActor.Type, otherActor.Type, movingActor, otherActor)) return;
            __result = true;
        }

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.AbilityEnded))]
        [HarmonyPostfix]
        private static void EnemyEndedOnHer(CAbilityMove __instance)
        {
            try
            {
                CActor mover = __instance.TargetingActor;
                if (mover == null || mover.IsDead || mover is CPlayerActor) return;
                foreach (CPlayerActor her in Heroes())
                {
                    if (her == mover || her.ArrayIndex != mover.ArrayIndex) continue;
                    if (!CActor.AreActorsEnemies(mover.Type, her.Type, mover, her)) continue;
                    PushOut(her, mover);
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: enemy in her hex: " + e); }
        }

        private static bool IsEmpty(CTile t, CActor her)
        {
            if (t == null || t.m_HexMap == null || !t.m_HexMap.Revealed) return false;
            CNode node = ScenarioManager.PathFinder.GetNode(t.m_ArrayIndex);
            if (node == null || !node.Walkable || node.Blocked) return false;
            if (ScenarioManager.Scenario.FindActorsAt(t.m_ArrayIndex)?.Any(a => a != null && a != her && !a.IsDead) == true) return false;
            // tokens (a Tear, loot) don't make a hex non-empty (the mat, the 2e FAQ); overlay tiles do
            return t.m_Props.All(p => p.ObjectType == ScenarioManager.ObjectImportType.Shadow
                || p.ObjectType == ScenarioManager.ObjectImportType.MoneyToken
                || p.ObjectType == ScenarioManager.ObjectImportType.PressurePlate
                || p.ObjectType == ScenarioManager.ObjectImportType.VFXProp
                || p.ObjectType == ScenarioManager.ObjectImportType.TerrainVisualEffect
                || p.ObjectType == ScenarioManager.ObjectImportType.TerrainLampVisualEffect
                || (p is CObjectDoor d && d.DoorIsOpen));
        }

        private static void PushOut(CPlayerActor her, CActor enemy)
        {
            // every empty hex at the smallest distance: with a tie the player picks (the inline teleport's own tile
            // choice; one hex = no choice)
            var closest = new List<CTile>();
            int best = int.MaxValue;
            foreach (CTile t in ScenarioManager.Tiles)
            {
                if (t == null || t.m_ArrayIndex == her.ArrayIndex || !IsEmpty(t, her)) continue;
                int d = Point.HexDist(her.ArrayIndex, t.m_ArrayIndex);
                if (d < best) { best = d; closest.Clear(); }
                if (d == best) closest.Add(t);
            }
            CTile target = closest.FirstOrDefault();
            var list = new List<CAbility>();
            if (target != null)
            {
                CAbilityTeleport tp = (CAbilityTeleport)CAbilityTeleport.CreateDefaultTeleport(new CAbilityTeleport.TeleportData());
                tp.InlineSubAbilityTiles.AddRange(closest);
                tp.IsInlineSubAbility = true;
                tp.SetCanSkip(canSkip: false);
                tp.SetMiscVar(GHTears.OwnTag, new TValue(true));
                list.Add(tp);
            }
            CAbility dmg = CAbilityDamage.CreateDefaultDamage(ScenarioManager.HazardousTerrainDamage, 0, 1, isMonster: false, "", isMultipass: true,
                new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Self));
            if (dmg.MiscAbilityData == null) dmg.MiscAbilityData = new AbilityData.MiscAbilityData();
            dmg.MiscAbilityData.AutotriggerAbility = true;
            dmg.MiscAbilityData.IgnoreStun = true;
            dmg.SetMiscVar(GHTears.OwnTag, new TValue(true));
            list.Add(dmg);
            if (GameState.InternalCurrentActor != her) GameState.OverrideCurrentActorForOneAction(her);
            PhaseManager.CurrentPhaseAction?.StackNextAbilities(list, her, killAfter: false, stackToNextCurrent: true);
            Log(0, $"class mat: {enemy.GetPrefabName()} ended its move in her hex: Teleport to {(target == null ? "nowhere (no empty hex)" : closest.Count == 1 ? target.m_ArrayIndex.ToString() : $"one of {closest.Count} equally close empty hexes (the player picks: {string.Join(" ", closest.Select(c => c.m_ArrayIndex.ToString()))})")} and hazardous terrain damage {ScenarioManager.HazardousTerrainDamage}");
        }

        // ---- 5. attacks this round, and the round's end

        private static void NoteMarker(CActor her)
        {
            if (CardBonus(her, ShadowstrikeCard) != null) MarkerThisRound.Add(her.ActorGuid);
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void AnyAbilityEnded(CAbility __instance)
        {
            try
            {
                CActor her = __instance.TargetingActor;
                if (!Is(her)) return;
                NoteMarker(her);
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: " + e); }
        }

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.AbilityEnded))]
        [HarmonyPostfix]
        private static void AttackEnded(CAbilityAttack __instance)
        {
            try
            {
                CActor her = __instance.TargetingActor;
                if (!Is(her) || !__instance.AbilityHasHappened) return;
                if (!AttacksThisRound.TryGetValue(her.ActorGuid, out List<int> l)) AttacksThisRound[her.ActorGuid] = l = new List<int>();
                l.Add(CardId(__instance));
                NoteMarker(her);
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: " + e); }
        }

        [HarmonyPatch(typeof(CPhaseEndRound), "OnNextStep")]
        [HarmonyPrefix]
        private static void RoundEnd()
        {
            try
            {
                foreach (CPlayerActor her in Heroes())
                {
                    NoteMarker(her);
                    if (!AttacksThisRound.TryGetValue(her.ActorGuid, out List<int> cards) || cards.Count == 0) continue;
                    bool marker = MarkerThisRound.Contains(her.ActorGuid);
                    if (cards.All(c => marker && c == ShadowstrikeCard))
                    {
                        Log(ShadowstrikeCard, "class mat: attacked this round, but only with Shadowstrike's consumed bottom: no Invisible");
                        continue;
                    }
                    if (HasInvisible(her)) continue;
                    Gain(her, "attacked this round, round end");
                }
                CheckAlone();
            }
            catch (Exception e) { Plugin.Log.LogError("Nightshroud class mat: round end: " + e); }
            finally { AttacksThisRound.Clear(); MarkerThisRound.Clear(); CancelledAttacks.Clear(); }
        }
    }
}
