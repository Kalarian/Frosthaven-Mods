using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Cragheart's card rules Frosthaven's engine has no data for (2e card-accuracy pass,
    /// task rest-a). Each is keyed on a MiscVar the pack's card sets, so without the 2nd edition pack nothing here acts.
    ///
    /// - Backup Ammunition top (card 32106), MiscVars GHSingleTargetOnly on the AddTarget bonus: "on your next four
    ///   single-target ranged attack abilities, add +1 Target". Frosthaven's BuffTarget bonus adds its target to every
    ///   ranged attack (area or multi-target too) and spends a slot each time. Here the bonus's strength is 0 and no slot
    ///   is spent unless the attack ability, as written (its target count when it started, before bonuses), has one
    ///   target and no area.
    /// - Stone Pummel bottom (card 32122), MiscVars GHStonePummel on the bonus's DestroyObstacle: "whenever you attack an
    ///   adjacent enemy, you may destroy one 1-hex obstacle adjacent to both you and that enemy to add +2 Attack".
    ///   Frosthaven's BuffIfCanPerformSubAbility asks for a melee attack and offers any obstacle within range 1 of the
    ///   attacker. Here: the target must be adjacent to you (melee or ranged attack alike), the offer is made only if a
    ///   destroyable 1-hex obstacle is adjacent to both of you, and the destroy step lets you pick only such an obstacle.
    /// - Rocky End bottom (card 32127), MiscVars GHPushExitedHexes on the push's Create: "create 1-hex obstacle tiles in
    ///   each featureless hex the target exits with the push". The create's hexes are the push's real path (where the
    ///   target started and every hex it passed, the hex it ended in excluded: CAbilityPush.m_StartingPoint +
    ///   m_AbilityPath), kept when featureless and empty (the create's TileFilter), so a bent push is followed exactly.
    /// - Wave of Destruction bottom (card 32129), MiscVars GHMoveThroughObstacles on the Move: "you may enter hexes
    ///   containing obstacles with this movement; when you do, destroy the obstacle". While that move is being chosen and
    ///   performed, every destroyable obstacle stops blocking the path finder (CNode.Blocked, the one flag obstacles set),
    ///   so the move is an ordinary move (enemies still block, traps and terrain still count) that may pass through or end
    ///   in an obstacle's hex; the card's next step destroys every obstacle on the move path (AllTargetsOnMovePath) and
    ///   feeds the +2 Attack per obstacle. The obstacles block again as soon as the move is done or any other ability
    ///   starts (an undo, a cancelled move).
    /// Logs "GHRule {Rule} card {id} ..." each time a rule acts.
    /// </summary>
    [HarmonyPatch]
    internal static class GHCragheart2e
    {
        private const string SingleTargetTag = "GHSingleTargetOnly";
        private const string StonePummelTag = "GHStonePummel";
        private const string PushExitedTag = "GHPushExitedHexes";
        private const string ThroughObstaclesTag = "GHMoveThroughObstacles";

        private static int CardId(CAbility ability)
        {
            try { return ability?.GetAbilityBaseCard()?.ID ?? 0; }
            catch (Exception) { return 0; }
        }

        private static bool Tagged(CAbility ability, string tag)
        {
            try { return ability != null && ability.MiscVars != null && ability.GetMiscVar(tag).GetBool(); }
            catch (Exception) { return false; }
        }

        private static void Error(string what, Exception e)
        {
            if (GHClasses.Reported.Add("cragheart2e " + what)) Plugin.Log.LogError($"Cragheart (2e) {what}: {e}");
        }

        // ------------------------------------------------------------------ Backup Ammunition: single-target only

        private static readonly AccessTools.FieldRef<CBespokeBehaviour, CActiveBonus> BonusOf =
            AccessTools.FieldRefAccess<CBespokeBehaviour, CActiveBonus>("m_ActiveBonus");

        /// <summary>The attack ability as written has one target and no area.</summary>
        private static bool SingleTarget(CAbility ability)
        {
            if (ability == null || ability.AreaEffect != null) return false;
            int targets = ability.HasStarted ? ability.OriginalTargetCount : ability.NumberTargets;
            return targets == 1;
        }

        private static bool SingleTargetBonus(CBespokeBehaviour behaviour, out CActiveBonus bonus)
        {
            bonus = BonusOf(behaviour);
            return bonus != null && Tagged(bonus.Ability, SingleTargetTag);
        }

        [HarmonyPatch(typeof(CAddTargetActiveBonus_BuffTarget), nameof(CAddTargetActiveBonus_BuffTarget.ReferenceStrength))]
        [HarmonyPostfix]
        private static void SingleTargetStrength(CAddTargetActiveBonus_BuffTarget __instance, CAbility ability, ref int __result)
        {
            try
            {
                if (__result == 0 || !(ability is CAbilityAttack) || !SingleTargetBonus(__instance, out _)) return;
                if (!SingleTarget(ability)) __result = 0;
            }
            catch (Exception e) { Error("single-target strength", e); }
        }

        [HarmonyPatch(typeof(CAddTargetActiveBonus_BuffTarget), nameof(CAddTargetActiveBonus_BuffTarget.OnPreActorIsAttacking))]
        [HarmonyPrefix]
        private static bool SingleTargetSlot(CAddTargetActiveBonus_BuffTarget __instance, CAbilityAttack attackAbility)
        {
            try
            {
                if (!SingleTargetBonus(__instance, out CActiveBonus bonus)) return true;
                bool single = SingleTarget(attackAbility);
                if (single && bonus.IsValidAttackType(attackAbility))
                    Plugin.Log.LogInfo($"GHRule SingleTargetOnly card {CardId(bonus.Ability)} +1 Target on {attackAbility.Name} (single-target ranged attack); a slot is used");
                else if (!single && bonus.IsValidAttackType(attackAbility))
                    Plugin.Log.LogInfo($"GHRule SingleTargetOnly card {CardId(bonus.Ability)} {attackAbility.Name} is not single-target (targets {attackAbility.OriginalTargetCount}, area {attackAbility.AreaEffect != null}): no +1 Target, no slot used");
                return single;
            }
            catch (Exception e) { Error("single-target slot", e); return true; }
        }

        // ------------------------------------------------------------------ Stone Pummel: obstacle adjacent to both

        private static readonly AccessTools.FieldRef<CAttackEffect, bool> StackedForBuff =
            AccessTools.FieldRefAccess<CAttackEffect, bool>("stackedSubAbilityForBuff");
        private static readonly FieldInfo ValidTilesField = AccessTools.Field(typeof(CAbility), "m_ValidTilesInAreaEffect");

        // the obstacle hexes the current Stone Pummel offer may destroy (set when the offer is made)
        private static List<CTile> _pummelTiles;
        private static int _pummelCard;

        /// <summary>A destroyable obstacle that fills exactly one hex, on this tile.</summary>
        private static CObjectObstacle OneHexObstacle(CTile tile)
        {
            if (tile == null) return null;
            CObjectObstacle obstacle = tile.FindProp(ScenarioManager.ObjectImportType.Obstacle) as CObjectObstacle;
            if (obstacle == null)
            {
                CTile propTile = null;
                obstacle = CObjectProp.FindPropWithPathingBlocker(tile.m_ArrayIndex, ref propTile);
            }
            if (obstacle == null || obstacle.OverrideDisallowDestroyAndMove || obstacle.Activated) return null;
            if (obstacle.PropHealthDetails != null && obstacle.PropHealthDetails.HasHealth) return null;
            if (obstacle.PathingBlockers != null && obstacle.PathingBlockers.Count > 1) return null;
            return obstacle;
        }

        private static bool Adjacent(CTile a, CTile b) =>
            a != null && b != null && ScenarioManager.IsTileAdjacent(a.m_ArrayIndex.X, a.m_ArrayIndex.Y, b.m_ArrayIndex.X, b.m_ArrayIndex.Y);

        [HarmonyPatch(typeof(CAttackEffect), nameof(CAttackEffect.StackAbilityForBonus))]
        [HarmonyPrefix]
        private static bool StonePummelOffer(CAttackEffect __instance, CActor target, CAbilityAttack attackAbility, List<CAbility> stackAbilities, ref bool __result)
        {
            try
            {
                CAbility destroy = stackAbilities?.FirstOrDefault(a => a is CAbilityDestroyObstacle && Tagged(a, StonePummelTag));
                if (destroy == null) return true;
                _pummelTiles = null;
                CActor attacker = attackAbility?.TargetingActor;
                CTile here = attacker == null ? null : ScenarioManager.Tiles[attacker.ArrayIndex.X, attacker.ArrayIndex.Y];
                CTile there = target == null ? null : ScenarioManager.Tiles[target.ArrayIndex.X, target.ArrayIndex.Y];
                int card = CardId(attackAbility);
                List<CTile> both = new List<CTile>();
                if (Adjacent(here, there))
                    foreach (CTile tile in GameState.GetTilesInRange(attacker, 1, CAbility.EAbilityTargeting.Range, emptyTilesOnly: false, ignoreBlocked: true))
                        if (tile != here && Adjacent(tile, here) && Adjacent(tile, there) && OneHexObstacle(tile) != null) both.Add(tile);
                if (both.Count == 0)
                {
                    StackedForBuff(__instance) = false;
                    __result = false;
                    Plugin.Log.LogInfo($"GHRule StonePummel card 32122 no offer on {target?.GetPrefabName()}: {(Adjacent(here, there) ? "no 1-hex obstacle adjacent to both" : "the target is not adjacent")}");
                    return false;
                }
                _pummelTiles = both;
                _pummelCard = card;
                Plugin.Log.LogInfo($"GHRule StonePummel card 32122 offer on {target?.GetPrefabName()}: {both.Count} obstacle(s) adjacent to both");
                return true;
            }
            catch (Exception e) { Error("Stone Pummel offer", e); return true; }
        }

        private static void PummelFilter(CAbilityDestroyObstacle ability)
        {
            try
            {
                if (!Tagged(ability, StonePummelTag) || _pummelTiles == null) return;
                if (ValidTilesField.GetValue(ability) is List<CTile> valid) valid.RemoveAll(t => !_pummelTiles.Contains(t));
                ability.TilesInRange?.RemoveAll(t => !_pummelTiles.Contains(t));
                Plugin.Log.LogInfo($"GHRule StonePummel card 32122 destroy limited to {_pummelTiles.Count} obstacle(s) adjacent to you and the target");
            }
            catch (Exception e) { Error("Stone Pummel destroy filter", e); }
        }

        [HarmonyPatch(typeof(CAbilityDestroyObstacle), nameof(CAbilityDestroyObstacle.Start))]
        [HarmonyPostfix]
        private static void PummelStart(CAbilityDestroyObstacle __instance) => PummelFilter(__instance);

        [HarmonyPatch(typeof(CAbilityDestroyObstacle), nameof(CAbilityDestroyObstacle.Restart))]
        [HarmonyPostfix]
        private static void PummelRestart(CAbilityDestroyObstacle __instance) => PummelFilter(__instance);

        // ------------------------------------------------------------------ Rocky End: the hexes the target exits

        private static readonly MethodInfo WillBlock = AccessTools.Method(typeof(CAbility), "CheckIfWillBeBlocking", new[] { typeof(CTile), typeof(bool) });

        [HarmonyPatch(typeof(CAbilityCreate), nameof(CAbilityCreate.Start))]
        [HarmonyPostfix]
        private static void PushExitedHexes(CAbilityCreate __instance)
        {
            try
            {
                if (!Tagged(__instance, PushExitedTag)) return;
                if (!(__instance.ParentAbility is CAbilityPush push))
                {
                    if (GHClasses.Reported.Add("rocky end parent")) Plugin.Log.LogWarning($"Cragheart (2e) Rocky End: the create's parent is {__instance.ParentAbility?.GetType().Name ?? "none"}, not a push");
                    return;
                }
                var exited = new List<CTile>();
                List<Point> path = push.m_AbilityPath ?? new List<Point>();
                if (path.Count > 0)
                {
                    exited.Add(ScenarioManager.Tiles[push.m_StartingPoint.X, push.m_StartingPoint.Y]);
                    for (int i = 0; i < path.Count - 1; i++) exited.Add(ScenarioManager.Tiles[path[i].X, path[i].Y]);
                }
                var hexes = new List<CTile>();
                foreach (CTile tile in exited.Where(t => t != null).Distinct())
                {
                    if (!CAbilityFilter.IsValidTile(tile, __instance.TileFilter, false, null, null, __instance.VarsContext)) continue;
                    if (WillBlock != null && (bool)WillBlock.Invoke(__instance, new object[] { tile, false })) continue;
                    hexes.Add(tile);
                }
                __instance.TilesInRange = hexes;
                Plugin.Log.LogInfo($"GHRule PushExitedHexes card 32127 push moved {path.Count} hex(es); obstacles in {hexes.Count} of the {exited.Count} exited hex(es) (featureless and empty)");
            }
            catch (Exception e) { Error("Rocky End exited hexes", e); }
        }

        // ------------------------------------------------------------------ Wave of Destruction: move through obstacles

        private static CAbilityMove _throughMove;
        private static ScenarioState _throughScenario;
        private static readonly List<CObjectObstacle> Unblocked = new List<CObjectObstacle>();
        private static readonly FieldInfo PathCache = AccessTools.Field(typeof(CPathFinder), "s_FindPathCache");

        private static void ClearPathCache()
        {
            try { (PathCache?.GetValue(null) as IDictionary)?.Clear(); }
            catch (Exception e) { Error("path cache", e); }
        }

        private static void SetBlocked(CObjectObstacle obstacle, bool blocked)
        {
            CPathFinder finder = ScenarioManager.PathFinder;
            if (finder == null || obstacle?.PathingBlockers == null) return;
            foreach (TileIndex t in obstacle.PathingBlockers)
                if (finder.Nodes[t.X, t.Y] != null) finder.Nodes[t.X, t.Y].Blocked = blocked;
        }

        private static void Unblock(CAbilityMove move)
        {
            Reblock("a new move");
            ScenarioState scenario = ScenarioManager.CurrentScenarioState;
            if (scenario?.Props == null) return;
            foreach (CObjectObstacle obstacle in scenario.Props.OfType<CObjectObstacle>())
            {
                if (obstacle.OverrideDisallowDestroyAndMove || obstacle.Activated) continue;
                if (obstacle.PropHealthDetails != null && obstacle.PropHealthDetails.HasHealth) continue;
                SetBlocked(obstacle, false);
                Unblocked.Add(obstacle);
            }
            _throughMove = move;
            _throughScenario = scenario;
            ClearPathCache();
            Plugin.Log.LogInfo($"GHRule MoveThroughObstacles card {CardId(move)} {move.TargetingActor?.GetPrefabName()} may enter {Unblocked.Count} obstacle(s) with this move");
        }

        private static void Reblock(string why)
        {
            if (_throughMove == null && Unblocked.Count == 0) return;
            int card = CardId(_throughMove);
            int back = 0;
            ScenarioState scenario = ScenarioManager.CurrentScenarioState;
            if (scenario?.Props != null && ReferenceEquals(scenario, _throughScenario))
                foreach (CObjectObstacle obstacle in Unblocked)
                    if (scenario.Props.Contains(obstacle) && !obstacle.Activated) { SetBlocked(obstacle, true); back++; }
            Unblocked.Clear();
            _throughMove = null;
            _throughScenario = null;
            ClearPathCache();
            Plugin.Log.LogInfo($"GHRule MoveThroughObstacles card {card} obstacles block again ({back}; {why})");
        }

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.Start))]
        [HarmonyPrefix]
        private static void ThroughStart(CAbilityMove __instance)
        {
            try
            {
                if (Tagged(__instance, ThroughObstaclesTag)) Unblock(__instance);
                else if (_throughMove != null) Reblock("another move started");
            }
            catch (Exception e) { Error("move through obstacles (start)", e); }
        }

        [HarmonyPatch(typeof(CAbilityMove), nameof(CAbilityMove.Perform))]
        [HarmonyPostfix]
        private static void ThroughPerform(CAbilityMove __instance)
        {
            try
            {
                if (_throughMove != null && ReferenceEquals(__instance, _throughMove)
                    && (__instance.m_State == CAbilityMove.EMoveState.MoveDone || __instance.m_State == CAbilityMove.EMoveState.UnableToMove))
                    Reblock("the move is done");
            }
            catch (Exception e) { Error("move through obstacles (done)", e); }
        }

        /// <summary>Any other ability starting (the destroy step, an undo to another card, the next turn): block again.</summary>
        [HarmonyPatch(typeof(CAbility), nameof(CAbility.Start))]
        [HarmonyPrefix]
        private static void OtherAbilityStart(CAbility __instance)
        {
            try
            {
                if (_throughMove != null && !ReferenceEquals(__instance, _throughMove) && !(__instance is CAbilityMove)) Reblock("next ability " + __instance.AbilityType);
            }
            catch (Exception e) { Error("move through obstacles (next ability)", e); }
        }
    }
}
