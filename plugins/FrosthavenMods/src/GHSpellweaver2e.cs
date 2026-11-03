using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Spellweaver's printed rules that are not card data (card-accuracy pass, 2026-11-02):
    ///   - Ice Armor top (card 32410) may go on an ally's summon (2e: summons are allies). Frosthaven moves a given card
    ///     only to a character, so on a summon the bonus is held by the summon and the card stays in her active area;
    ///     either way the slots' XP is hers (Frosthaven would give it to the holder). "GHRule IceArmor".
    ///   - Freezing Nova top (MiscVars GH2E_ClosestTargets N): "target the N enemies closest to you within 5 hexes": the
    ///     Range targeting (line of sight, QUESTIONS.md B) keeps only the N enemies nearest by range; enemies tied at the
    ///     cut-off all stay a choice. "GHRule ClosestTargets".
    ///   - Warm Up top (card 32418): "On the next attack ability you perform from an action with [lost], add +1 Attack to
    ///     all your attacks": the bonus counts (and shows) only on an attack of an action whose half has a lost icon;
    ///     every attack of that ability gets it, and its one slot is spent when that attack ability ends. "GHRule WarmUp".
    ///   - Freezing Vortex top (MiscVars GH2E_ObstacleOnKill on its kill): each target the kill kills leaves a one-hex
    ///     obstacle (Frozen Fist's) in its hex when that hex is featureless. "GHRule FreezingVortex".
    ///   - Spent Power perk card (AMC_GH2E_Spellweaver2_Lost_P1): "+1, add +2 Attack if drawn during an action with
    ///     [lost]": the drawn +1 becomes +3 then (GameState.ChangeModifierValues, as GHModifierRewrites). "GHRule SpentPower".
    ///   - Etheric Bond (perk Spellweaver209): "Whenever you short rest, if Reviving Ether is in your discard pile, first
    ///     recover it": when her short rest starts (before the random card is picked), card 32401 goes from her discard
    ///     pile to her hand. "GHRule EthericBond".
    ///   - Mastery FrosthavenMods_FireAndIceSameTurn: Value turns in which she consumed both Fire and Ice (element records).
    /// Gated on the pack's card IDs, perk IDs, misc vars and the Spellweaver2ID class: inert without the 2nd edition pack.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSpellweaver2e
    {
        internal const string ClassId = "Spellweaver2ID";
        private const int RevivingEther = 32401, IceArmor = 32410, WarmUp = 32418;
        private const string EthericBond = "Spellweaver209";
        private const string SpentPowerCard = "AMC_GH2E_Spellweaver2_Lost_P1";
        internal const string FireIceTag = "FrosthavenMods_FireAndIceSameTurn";

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"Gloomhaven 2e Spellweaver: {what} failed: {e}");
        }

        /// <summary>The current card action has a lost icon (and <paramref name="ability"/>, if given, is from that card).</summary>
        internal static bool IsLostAction(CAbility ability)
        {
            GameState.CurrentActorAction cur = GameState.CurrentAction;
            if (cur?.Action == null || !(cur.BaseCard is CAbilityCard card)) return false;
            if (ability != null)
            {
                CBaseCard bc = null;
                try { bc = ability.GetAbilityBaseCard(); } catch { }
                if (bc != null && bc.ID != card.ID) return false;
            }
            DiscardType d = cur.Action == card.TopAction ? card.GetAbilityCardYML.TopDiscardType
                : cur.Action == card.BottomAction ? card.GetAbilityCardYML.BottomDiscardType : DiscardType.None;
            return d == DiscardType.Lost || d == DiscardType.PermanentlyLost;
        }

        // ================================================================== Ice Armor: the XP is hers

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.UpdateXPTracker))]
        [HarmonyPrefix]
        private static bool BeforeTracker(CActiveBonus __instance, bool processTracker, ref bool processXP)
        {
            if (__instance == s_InApply) return false;   // Warm Up: spent when its attack ability ends
            try
            {
                if (!processTracker || !processXP || __instance?.BaseCard?.ID != IceArmor) return true;
                if (!(__instance.Caster is CPlayerActor caster) || __instance.Actor == caster) return true;
                int index = __instance.TrackerIndex;
                List<int> tracker = __instance.Ability?.ActiveBonusData?.Tracker;
                List<int> pattern = __instance.Ability?.ActiveBonusYML?.TrackerPattern;
                int xp = tracker != null && index >= 0 && index < tracker.Count ? tracker[index]
                    : pattern != null && index >= 0 && index < pattern.Count ? pattern[index] : -1;
                if (xp < 0) return true;
                if (xp > 0) caster.GainXP(xp, __instance.Ability.XPSourceKey);
                processXP = false;
                Plugin.Log.LogInfo($"GHRule IceArmor card {IceArmor} {__instance.Actor?.GetPrefabName()} negated a source of damage (slot {index + 1}); {xp} XP to {GHPlague.NameOf(caster)}");
            }
            catch (Exception e) { Report("Ice Armor XP", e); }
            return true;
        }

        // ================================================================== Freezing Nova: the N closest

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.GetValidActorsInRange), new[] { typeof(CAbility) })]
        [HarmonyPostfix]
        private static void AfterValidActors(CAbility ability)
        {
            try
            {
                if (ability?.MiscVars == null || ability.TargetingActor == null) return;
                TValue v = ability.GetMiscVar("GH2E_ClosestTargets");
                if (v.isUnknown) return;
                int n = v.GetInt();
                List<CActor> valid = ability.ValidActorsInRange;
                if (n <= 0 || valid == null || valid.Count <= n) return;
                CActor me = ability.TargetingActor;
                var dist = new Dictionary<CActor, int>();
                int max = Math.Max(1, ability.Range);
                for (int r = 1; r <= max && dist.Count < valid.Count; r++)
                {
                    var tiles = new HashSet<CTile>(GameState.GetTilesInRange(me.ArrayIndex, r, CAbility.EAbilityTargeting.Range, emptyTilesOnly: false, ignoreBlocked: true, ignoreLOS: true));
                    foreach (CActor a in valid)
                        if (!dist.ContainsKey(a) && tiles.Contains(ScenarioManager.Tiles[a.ArrayIndex.X, a.ArrayIndex.Y])) dist[a] = r;
                }
                foreach (CActor a in valid) if (!dist.ContainsKey(a)) dist[a] = max + 1;
                int cut = valid.Select(a => dist[a]).OrderBy(d => d).ElementAt(n - 1);
                List<CActor> keep = valid.Where(a => dist[a] <= cut).ToList();
                if (keep.Count == valid.Count) return;
                valid.Clear();
                valid.AddRange(keep);
                Plugin.Log.LogInfo($"GHRule ClosestTargets card {ability.GetAbilityBaseCard()?.ID ?? 0} the {n} closest enemies (range {cut} or less, ties kept): {string.Join(", ", keep.Select(GHPlague.NameOf))}");
            }
            catch (Exception e) { Report("closest targets", e); }
        }

        // ================================================================== Warm Up

        [ThreadStatic] private static CActiveBonus s_InApply;
        private static readonly HashSet<CActiveBonus> s_WarmUpUsed = new HashSet<CActiveBonus>();

        private static bool IsWarmUp(CActiveBonus b) =>
            b?.BaseCard?.ID == WarmUp && b.HasBehaviour(CActiveBonus.EActiveBonusBehaviourType.BuffAttack);

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ReferenceStrength))]
        [HarmonyPrefix]
        private static bool WarmUpStrength(CActiveBonus __instance, CAbility ability, ref int __result)
        {
            try
            {
                if (ability == null || !(ability is CAbilityAttack) || !IsWarmUp(__instance)) return true;
                if (IsLostAction(ability)) return true;
                __result = 0;
                return false;
            }
            catch (Exception e) { Report("Warm Up strength", e); return true; }
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), "ApplyBuffs")]
        [HarmonyPrefix]
        private static bool WarmUpApply(CAttackActiveBonus_BuffAttack __instance, CAbilityAttack attackAbility, out CActiveBonus __state)
        {
            __state = s_InApply;
            try
            {
                CActiveBonus bonus = __instance?.ActiveBonus;
                if (!IsWarmUp(bonus)) return true;
                if (!IsLostAction(attackAbility)) return false;   // not an attack of a lost action: nothing applies, no slot
                if (s_WarmUpUsed.Add(bonus))
                    Plugin.Log.LogInfo($"GHRule WarmUp card {WarmUp} +1 Attack on every attack of {attackAbility?.GetAbilityBaseCard()?.Name} (an action with a lost icon)");
                s_InApply = bonus;
            }
            catch (Exception e) { Report("Warm Up", e); }
            return true;
        }

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), "ApplyBuffs")]
        [HarmonyFinalizer]
        private static Exception WarmUpApplied(Exception __exception, CActiveBonus __state)
        {
            s_InApply = __state;
            return __exception;
        }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.FinishIfNoRemaining))]
        [HarmonyPrefix]
        private static bool HoldWarmUp(CActiveBonus __instance) => __instance != s_InApply;

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.OnAttackAbilityFinished))]
        [HarmonyPostfix]
        private static void WarmUpDone(CAttackActiveBonus_BuffAttack __instance)
        {
            try
            {
                CActiveBonus bonus = __instance?.ActiveBonus;
                if (bonus == null || !s_WarmUpUsed.Remove(bonus)) return;
                bonus.UpdateXPTracker();
                bonus.FinishIfNoRemaining();
                Plugin.Log.LogInfo($"GHRule WarmUp card {WarmUp} the attack ability ended: the slot is spent");
            }
            catch (Exception e) { Report("Warm Up end", e); }
        }

        // ================================================================== Freezing Vortex

        [HarmonyPatch(typeof(CAbilityKill), nameof(CAbilityKill.ApplyToActor))]
        [HarmonyPrefix]
        private static void BeforeKill(CAbilityKill __instance, CActor actor, out CTile __state)
        {
            __state = null;
            try
            {
                if (actor == null || __instance?.MiscVars == null || !__instance.GetMiscVar("GH2E_ObstacleOnKill").GetBool()) return;
                __state = ScenarioManager.Tiles[actor.ArrayIndex.X, actor.ArrayIndex.Y];
            }
            catch (Exception e) { Report("Freezing Vortex", e); }
        }

        [HarmonyPatch(typeof(CAbilityKill), nameof(CAbilityKill.ApplyToActor))]
        [HarmonyPostfix]
        private static void AfterKill(CAbilityKill __instance, CActor actor, CTile __state)
        {
            if (__state == null) return;
            try
            {
                if (!actor.IsDead && ScenarioManager.Scenario.HasActor(actor)) return;   // not killed (prevented)
                int card = __instance.GetAbilityBaseCard()?.ID ?? 0;
                CActor other = ScenarioManager.Scenario.FindActorAt(__state.m_ArrayIndex);
                if ((other != null && !other.IsDead && other != actor) || !CAbilityFilter.IsTileFeatureless(__state))
                {
                    Plugin.Log.LogInfo($"GHRule FreezingVortex card {card} {actor.GetPrefabName()} killed; its hex {__state.m_ArrayIndex} is not featureless: no obstacle");
                    return;
                }
                float y = 0.5f * ScenarioManager.CurrentScenarioState.ScenarioRNG.Next(180) + 0.75f * ScenarioManager.CurrentScenarioState.ScenarioRNG.Next(360);
                var obstacle = new CObjectObstacle("FrozenFistObstacle", ScenarioManager.ObjectImportType.Obstacle, new TileIndex(__state.m_ArrayIndex), null,
                    new CVector3(0f, y, 0f), new List<TileIndex> { new TileIndex(__state.m_ArrayIndex) }, __instance.TargetingActor, __state.m_HexMap.MapGuid, ignoresFlyAndJump: false);
                __state.SpawnProp(obstacle, notifyClient: true, 0.5f, __instance);
                Plugin.Log.LogInfo($"GHRule FreezingVortex card {card} {actor.GetPrefabName()} killed: a 1-hex obstacle in its featureless hex {__state.m_ArrayIndex}");
            }
            catch (Exception e) { Report("Freezing Vortex obstacle", e); }
        }

        // ================================================================== Spent Power

        [HarmonyPatch(typeof(GameState), nameof(GameState.ChangeModifierValues))]
        [HarmonyPostfix]
        private static void SpentPower(List<AttackModifierYMLData> modifiers, CAbilityAttack attackAbility)
        {
            try
            {
                if (modifiers == null) return;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    AttackModifierYMLData card = modifiers[i];
                    if (card == null || card.Name != SpentPowerCard || card.MathModifier != "+1") continue;
                    if (!IsLostAction(attackAbility)) continue;
                    AttackModifierYMLData copy = card.Copy(createCard: true);
                    copy.OriginalModifier = card.MathModifier;
                    copy.MathModifier = "+3";
                    copy.ModifiedFromModifier = card.Card;
                    ScenarioRuleClient.MessageHandler(new CChangeModifier_MessageData(GameState.InternalCurrentActor) { m_OriginalModifier = copy.OriginalModifier });
                    modifiers[i] = copy;
                    Plugin.Log.LogInfo($"GHRule SpentPower card 0 Spent Power: +1 drawn during an action with a lost icon ({attackAbility?.GetAbilityBaseCard()?.Name}): +3");
                }
            }
            catch (Exception e) { Report("Spent Power", e); }
        }

        // ================================================================== Etheric Echo bottom: Ward as the lost action starts

        private const int EthericEcho = 32413;

        /// <summary>"When you next perform an action with [lost] this round, gain Ward": the card's round bonus BOnLost fires
        /// when such an action ENDS (Frosthaven's DuringActionAbilityOnActionEnded), too late for damage she takes during
        /// it (retaliate on a lost attack, a trap on a lost move). So as the first ability of her next lost action starts,
        /// she gains Ward and the bonus is finished (its end-of-action trigger then never runs; if this hook misses, it is
        /// the fallback). "GHRule EthericEcho".</summary>
        [HarmonyPatch(typeof(CAbility), nameof(CAbility.Start))]
        [HarmonyPrefix]
        private static void WardAtLostStart(CAbility __instance, CActor targetingActor)
        {
            try
            {
                CActor her = targetingActor ?? __instance?.TargetingActor;
                if (!(her is CPlayerActor p) || p.CharacterClass?.ID != ClassId || GameState.InternalCurrentActor != her) return;
                CActiveBonus echo = her.ActiveBonuses?.FirstOrDefault(b => b?.BaseCard?.ID == EthericEcho && b.Ability?.Name == "BOnLost" && !b.Finished() && !b.Finishing());
                if (echo == null || !IsLostAction(__instance)) return;
                CBaseCard card = null;
                try { card = __instance.GetAbilityBaseCard(); } catch { }
                if (card == null || card.ID == EthericEcho) return;   // Etheric Echo's own bottom is no lost action
                her.ApplyCondition(her, CCondition.EPositiveCondition.Ward);
                echo.Finish();
                Plugin.Log.LogInfo($"GHRule EthericEcho card {EthericEcho} {GHPlague.NameOf(her)} starts a lost action ({card.ID} {__instance.Name}): Ward now, before its abilities");
            }
            catch (Exception e) { Report("Etheric Echo", e); }
        }

        // ================================================================== Etheric Bond

        private static readonly System.Reflection.FieldInfo WorkThread = AccessTools.Field(typeof(ScenarioRuleClient), "s_WorkThread");

        [HarmonyPatch(typeof(CPlayerToShortRest_MessageData), MethodType.Constructor, new[] { typeof(CActor) })]
        [HarmonyPostfix]
        private static void BeforeShortRest(CActor actorSpawningMessage)
        {
            try
            {
                // only the rule library's own message (CPhaseShortRest.OnNextStep, the worker thread every machine runs):
                // online, Choreographer.Handle_PlayerToShortRest builds a second one on the main thread after the sync
                // point, and a card move from there could run on one machine only (a hand desync)
                if (WorkThread?.GetValue(null) != null && !ScenarioRuleClient.IsWorkerThread) return;
                if (!(actorSpawningMessage is CPlayerActor p) || !GHPerks.HasPerk(p, EthericBond)) return;
                CCharacterClass cls = p.CharacterClass;
                if (cls == null || cls.HasShortRested || !cls.ShortRest) return;
                CAbilityCard ether = cls.DiscardedAbilityCards?.FirstOrDefault(c => c?.ID == RevivingEther);
                if (ether == null) return;
                cls.MoveAbilityCard(ether, cls.DiscardedAbilityCards, cls.HandAbilityCards, "DiscardedAbilityCards", "HandAbilityCards");
                Plugin.Log.LogInfo($"GHRule EthericBond card 0 Etheric Bond: {GHPlague.NameOf(p)} short rests; Reviving Ether recovered from the discard pile first");
            }
            catch (Exception e) { Report("Etheric Bond", e); }
        }

        // ================================================================== mastery: Fire and Ice in one turn

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Answer(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || !tags.Contains(FireIceTag)) return true;
            try
            {
                int needed = Math.Max(1, unlockConditionTarget.Value);
                var byRound = new Dictionary<int, HashSet<ElementInfusionBoardManager.EElement>>();
                foreach (CPlayerStats s in (comparePlayerStats ?? new List<CPlayerStats>()).Where(x => x != null))
                    foreach (CPlayerStatsElement e in s.Consumed ?? Enumerable.Empty<CPlayerStatsElement>())
                    {
                        if (e == null || e.Round <= 0) continue;
                        if (!byRound.TryGetValue(e.Round, out var set)) byRound[e.Round] = set = new HashSet<ElementInfusionBoardManager.EElement>();
                        set.Add(e.Element);
                    }
                int turns = byRound.Values.Count(set => set.Contains(ElementInfusionBoardManager.EElement.Fire) && set.Contains(ElementInfusionBoardManager.EElement.Ice));
                totalConditions = needed; conditionsMet = Math.Min(turns, needed); __result = turns >= needed;
                GHMasteryProgress.Once("FireAndIceSameTurn", $"mastery: {turns} turn(s) consuming both Fire and Ice (needs {needed}){(__result ? ": met" : "")}");
            }
            catch (Exception e)
            {
                Report("mastery Fire and Ice", e);
                totalConditions = Math.Max(1, unlockConditionTarget.Value); conditionsMet = 0; __result = false;
            }
            return false;
        }
    }
}
