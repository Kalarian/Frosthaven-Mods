using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Mastery conditions the game's battle-goal filters can't express, for the ported classes' crossover masteries
    /// (tools/gh_crossover.py). The game checks a battle goal by running each of its targets through one big filter
    /// switch over the party's per-round statistics; the conditions here need two records at once, rounds in a row, or
    /// an "either" the filters don't offer. So a mastery file tags its target with a marker in EnemyFamilies, a
    /// free-text list the parser never validates, and this prefix answers the tagged target from the same records.
    /// Untagged targets are untouched; without the pack nothing in the game's own files carries a marker. Markers:
    ///   FrosthavenMods_DamageStreak        (Brute) Value rounds in a row with Amount damage to enemies per turn and
    ///                                      Targets different cards played over them
    ///   FrosthavenMods_KillsNearAllies     (Scoundrel) Value kills of enemies with another character adjacent to them
    ///   FrosthavenMods_KillsAwayFromAllies (Scoundrel) Value kills of enemies with no other character adjacent
    ///   FrosthavenMods_ElementEveryTurn    (Sunkeeper) one of Elements infused or consumed on every turn taken
    ///   FrosthavenMods_AllySupportEveryTurn (Sawbones) an ally helped (heal, shield, supply card, buff, recovery) on
    ///                                      every turn taken
    ///   FrosthavenMods_SingleAbilityDamage (Sawbones) one attack ability dealing Amount damage to enemies, all its
    ///                                      targets together
    ///   FrosthavenMods_ItemsEveryTurn      (Quartermaster) an item used or refreshed on every turn taken
    ///   FrosthavenMods_UsedCardTop         (Quartermaster, a fail condition) the top action of card Amount performed
    /// "Every turn taken" means every round in which the character started a turn, so a character who is exhausted
    /// stops being judged. Allies adjacent to a dying enemy are read from the kill record's count of players next to
    /// it, minus the killer when the killing blow was a melee attack; summons are not counted.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMasteries
    {
        internal const string StreakTag = "FrosthavenMods_DamageStreak";
        internal const string KillsNearTag = "FrosthavenMods_KillsNearAllies";
        internal const string KillsAwayTag = "FrosthavenMods_KillsAwayFromAllies";
        internal const string ElementTag = "FrosthavenMods_ElementEveryTurn";
        internal const string AllySupportTag = "FrosthavenMods_AllySupportEveryTurn";
        internal const string SingleAbilityTag = "FrosthavenMods_SingleAbilityDamage";
        internal const string ItemsTag = "FrosthavenMods_ItemsEveryTurn";
        internal const string UsedCardTopTag = "FrosthavenMods_UsedCardTop";
        // Group 2 (2026-10-27)
        internal const string NearObstaclesTag = "FrosthavenMods_AttacksNearObstaclesOnly";   // (Cragheart) every attack's targets stood next to an obstacle or a wall
        internal const string AllyDamageOrHealTag = "FrosthavenMods_AllyDamageOrHealEveryRound"; // (Cragheart) an ally damaged or healed by the character every round
        internal const string KillsNonAttackTag = "FrosthavenMods_KillsByNonAttack";          // (Plagueherald) Value kills by something other than an attack
        internal const string FourTargetAttacksTag = "FrosthavenMods_AttacksWithFourTargets"; // (Plagueherald) Value distinct attack abilities with 4+ enemy targets
        internal const string InvisibleEveryTurnTag = "FrosthavenMods_InvisibleEveryTurn";    // (Nightshroud) invisible at the start or the end of every turn
        internal const string LoseHpEveryTurnTag = "FrosthavenMods_LoseHpEveryTurn";          // (Berserker) lost at least one hit point on every turn
        internal const string UsedCardBottomTag = "FrosthavenMods_UsedCardBottom";            // (Berserker, a fail condition) the bottom action of card Amount performed
        internal const string OneHpEveryTurnTag = "FrosthavenMods_OneHpEveryTurn";            // (Berserker) exactly one hit point at the end of every turn
        // Group 3, Jaws of the Lion (2026-10-27; untested until the group is on the bench)
        internal const string KillsEnemyTurnsTag = "FrosthavenMods_KillsDuringEnemyTurns";     // (Red Guard) Value kills that happened on someone else's turn
        internal const string ForcedMovesTag = "FrosthavenMods_EnemiesForcedToMove";          // (Red Guard) Value distinct enemies pushed or pulled
        internal const string CommandedDamageTag = "FrosthavenMods_CommandedDamageInTurn";    // (Voidwarden) Value damage in one round by controlled or granted attacks
        internal const string ConditionEveryRoundTag = "FrosthavenMods_ConditionGivenEveryRound"; // (Voidwarden) one of the listed conditions given every round
        internal const string AttackActionsTag = "FrosthavenMods_AttackActionsWithDamage";    // (Demolitionist) Value different attack actions each dealing Amount
        internal const string ObstaclesTag = "FrosthavenMods_ObstaclesDestroyed";             // (Demolitionist) Value obstacles destroyed
        // The Hatchet's two (FrosthavenMods_FavoriteEveryTurn, _FavoriteEveryRoundWithEnemies) need the Favorite's moves,
        // which the statistics do not record: a runtime hook when the class is ported. Until then the game's own
        // filter answers them (CountXRounds Value 1: a round played).

        private static readonly HashSet<CAbility.EAbilityType> SupportTypes = new HashSet<CAbility.EAbilityType>
        {
            CAbility.EAbilityType.Heal, CAbility.EAbilityType.Shield, CAbility.EAbilityType.GiveSupplyCard,
            CAbility.EAbilityType.AddActiveBonus, CAbility.EAbilityType.RecoverDiscardedCards, CAbility.EAbilityType.RecoverLostCards,
            CAbility.EAbilityType.Bless, CAbility.EAbilityType.Strengthen, CAbility.EAbilityType.Ward, CAbility.EAbilityType.Regenerate,
            CAbility.EAbilityType.RemoveConditions, CAbility.EAbilityType.ImmunityTo,
        };

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Answer(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || !tags.Any(t => t != null && t.StartsWith("FrosthavenMods_", StringComparison.Ordinal))) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            try
            {
                if (tags.Contains(StreakTag)) Streak(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(KillsNearTag)) Kills(unlockConditionTarget, stats, true, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(KillsAwayTag)) Kills(unlockConditionTarget, stats, false, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(ElementTag)) EveryTurn(stats, r => ElementInRound(unlockConditionTarget, stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(AllySupportTag)) EveryTurn(stats, r => AllySupportInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(ItemsTag)) EveryTurn(stats, r => ItemsInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(SingleAbilityTag)) SingleAbility(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(UsedCardTopTag)) UsedCard(unlockConditionTarget, stats, "T", ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(UsedCardBottomTag)) UsedCard(unlockConditionTarget, stats, "B", ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(NearObstaclesTag)) AttacksNearObstacles(stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(AllyDamageOrHealTag)) EveryTurn(stats, r => AllyDamagedOrHealedInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(KillsNonAttackTag)) KillsByNonAttack(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(FourTargetAttacksTag)) AttacksWithTargets(unlockConditionTarget, stats, 4, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(InvisibleEveryTurnTag)) EveryTurn(stats, r => InvisibleInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(LoseHpEveryTurnTag)) EveryTurn(stats, r => LostHpInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(OneHpEveryTurnTag)) EveryTurn(stats, r => EndedTurnAtOneHp(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(KillsEnemyTurnsTag)) KillsDuringOtherTurns(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(ForcedMovesTag)) EnemiesForcedToMove(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(CommandedDamageTag)) CommandedDamage(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(ConditionEveryRoundTag)) EveryTurn(stats, r => ConditionGivenInRound(tags, stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(AttackActionsTag)) AttackActionsWithDamage(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(ObstaclesTag)) ObstaclesDestroyed(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else return true;   // a marker of a newer pack this plugin doesn't know: the game's own filter answers
            }
            catch (Exception e)
            {
                string tag = tags.First(t => t != null && t.StartsWith("FrosthavenMods_", StringComparison.Ordinal));
                if (Reported.Add(tag)) Plugin.Log.LogError($"Gloomhaven masteries: could not check {tag}: {e}");
                totalConditions = Math.Max(1, unlockConditionTarget.Value);
                conditionsMet = 0;
                __result = false;
            }
            return false;
        }

        // ------------------------------------------------------------------ the conditions

        /// <summary>Value rounds in a row with Amount damage to enemies on each of the character's turns and Targets
        /// different ability cards played over them (the Brute's second mastery).</summary>
        private static void Streak(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int rounds = Math.Max(1, t.Value), damage = Math.Max(0, t.Amount), cards = Math.Max(0, t.Targets);
            total = rounds;
            met = 0;
            result = false;
            var dealt = new Dictionary<int, int>();
            var played = new Dictionary<int, HashSet<int>>();
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                    if (d != null && IsEnemy(d.ActorWhoReceivedDamageType)) dealt[d.Round] = (dealt.TryGetValue(d.Round, out int sum) ? sum : 0) + d.FinalDamageAmount;
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                {
                    if (a == null || a.ActionData == null || a.ActionData.CardType != CBaseCard.ECardType.CharacterAbility || a.ActionData.CardID == int.MaxValue) continue;
                    if (!played.TryGetValue(a.Round, out HashSet<int> set)) played[a.Round] = set = new HashSet<int>();
                    set.Add(a.ActionData.CardID);
                }
            }
            int last = dealt.Keys.Concat(played.Keys).DefaultIfEmpty(0).Max();
            int run = 0, best = 0;
            for (int r = 1; r <= last; r++)
            {
                run = dealt.TryGetValue(r, out int sum) && sum >= damage ? run + 1 : 0;
                best = Math.Max(best, run);
                if (run < rounds) continue;
                var distinct = new HashSet<int>();
                for (int k = r - rounds + 1; k <= r; k++) if (played.TryGetValue(k, out HashSet<int> set)) distinct.UnionWith(set);
                if (distinct.Count >= cards) { met = rounds; result = true; return; }
            }
            met = Math.Min(best, rounds - 1);
        }

        /// <summary>Value kills of enemies that had (near) or lacked (away) another character adjacent when they fell.</summary>
        private static void Kills(CUnlockConditionTarget t, List<CPlayerStats> stats, bool near, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), count = 0;
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsKill k in s.Kills ?? Enumerable.Empty<CPlayerStatsKill>())
                {
                    if (k == null || !IsEnemy(k.ActorWhoDiedType)) continue;
                    int players = k.ActorWhoDiedPlayerAdjacency;
                    bool killerAdjacent = k.TargetAdjacent || KillingBlowWasMelee(s, k);
                    int allies = Math.Max(0, players - (killerAdjacent ? 1 : 0));
                    if (near ? allies >= 1 : allies == 0) count++;
                }
            }
            total = needed;
            met = Math.Min(count, needed);
            result = count >= needed;
        }

        /// <summary>Whether the last damage the character dealt to the victim before it died came from a melee attack.</summary>
        private static bool KillingBlowWasMelee(CPlayerStats s, CPlayerStatsKill k)
        {
            string victim = k.ActionData?.MainActorGUID;
            if (string.IsNullOrEmpty(victim)) return false;
            CPlayerStatsDamage last = (s.DamageDealt ?? new List<CPlayerStatsDamage>())
                .Where(d => d != null && d.Round == k.Round && d.ActorWhoReceivedDamageGUID == victim && d.BaseData != null && k.BaseData != null && d.BaseData.EventOrder <= k.BaseData.EventOrder)
                .OrderByDescending(d => d.BaseData.EventOrder).FirstOrDefault();
            return last != null && last.IsMelee;
        }

        /// <summary>The condition holds in every round the character started a turn in (none: not met yet).</summary>
        private static void EveryTurn(List<CPlayerStats> stats, Func<int, bool> inRound, ref int total, ref int met, ref bool result)
        {
            List<int> rounds = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Where(x => x != null && x.Round > 0)
                .Select(x => x.Round).Distinct().OrderBy(r => r).ToList();
            total = Math.Max(1, rounds.Count);
            met = rounds.Count(inRound);
            result = rounds.Count > 0 && met == rounds.Count;
        }

        private static bool ElementInRound(CUnlockConditionTarget t, List<CPlayerStats> stats, int round)
        {
            var wanted = new HashSet<ElementInfusionBoardManager.EElement>(t.Elements ?? new List<ElementInfusionBoardManager.EElement>());
            if (wanted.Count == 0) return false;
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsElement e in s.Infusions ?? Enumerable.Empty<CPlayerStatsElement>())
                    if (e != null && e.Round == round && (wanted.Contains(e.Element) || (e.Infused != null && e.Infused.Any(wanted.Contains)))) return true;
                foreach (CPlayerStatsElement e in s.Consumed ?? Enumerable.Empty<CPlayerStatsElement>())
                    if (e != null && e.Round == round && wanted.Contains(e.Element)) return true;
            }
            return false;
        }

        private static bool AllySupportInRound(List<CPlayerStats> stats, int round)
        {
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                {
                    if (a == null || a.Round != round || a.ActionData == null) continue;
                    if (!a.IsBuff && !SupportTypes.Contains(a.ActionData.AbilityType)) continue;
                    string self = a.ActionData.MainActorGUID;
                    List<string> guids = a.TargetGUIDs ?? new List<string>();
                    for (int i = 0; i < guids.Count; i++)
                    {
                        if (string.IsNullOrEmpty(guids[i]) || guids[i] == self) continue;
                        if (a.TargetTypes != null && i < a.TargetTypes.Count && a.TargetTypes[i] != CActor.EType.Player && a.TargetTypes[i] != CActor.EType.HeroSummon && a.TargetTypes[i] != CActor.EType.Ally) continue;
                        return true;
                    }
                }
            return false;
        }

        private static bool ItemsInRound(List<CPlayerStats> stats, int round)
        {
            foreach (CPlayerStats s in stats)
            {
                if ((s.Items ?? Enumerable.Empty<CPlayerStatsItem>()).Any(i => i != null && i.Round == round)) return true;
                if ((s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>()).Any(a => a != null && a.Round == round && a.ActionData != null
                        && a.ActionData.AbilityType.ToString().IndexOf("Refresh", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            }
            return false;
        }

        /// <summary>One attack ability of the character's dealing Amount damage to enemies over all its targets.</summary>
        private static void SingleAbility(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Amount);
            var sums = new Dictionary<string, int>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                {
                    if (d == null || d.ActionData == null || !IsEnemy(d.ActorWhoReceivedDamageType) || d.ActionData.AbilityType != CAbility.EAbilityType.Attack) continue;
                    string key = $"{d.Round}/{d.ActionData.CardID}/{d.ActionData.ActingAbilityName}";
                    sums[key] = (sums.TryGetValue(key, out int sum) ? sum : 0) + d.FinalDamageAmount;
                }
            int best = sums.Values.DefaultIfEmpty(0).Max();
            total = needed;
            met = Math.Min(best, needed);
            result = best >= needed;
        }

        /// <summary>The top ("T") or bottom ("B") action of card Amount was performed (a fail condition).</summary>
        private static void UsedCard(CUnlockConditionTarget t, List<CPlayerStats> stats, string side, ref int total, ref int met, ref bool result)
        {
            bool used = stats.SelectMany(s => s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>()).Any(a => a != null && a.ActionData != null
                && a.ActionData.CardID == t.Amount && !string.IsNullOrEmpty(a.ActionData.ActingAbilityName) && a.ActionData.ActingAbilityName.StartsWith(side, StringComparison.Ordinal));
            total = 1;
            met = used ? 1 : 0;
            result = used;
        }

        // ------------------------------------------------------------------ Group 2 (2026-10-27)

        /// <summary>Every enemy the character's attacks damaged stood next to an obstacle or a wall when hit (the damage
        /// record carries the target's obstacle and wall adjacency); at least one such attack.</summary>
        private static void AttacksNearObstacles(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int attacks = 0, near = 0;
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                {
                    if (d == null || d.ActionData == null || !IsEnemy(d.ActorWhoReceivedDamageType) || d.ActionData.AbilityType != CAbility.EAbilityType.Attack) continue;
                    attacks++;
                    if (d.ActionData.MainActorObstacleAdjacent > 0 || d.ActionData.MainActorWallAdjacent) near++;
                }
            total = 1;
            result = attacks > 0 && near == attacks;
            met = result ? 1 : 0;
        }

        /// <summary>An ally (another player or a hero summon, not the character) took damage from the character or was healed by them in the round.</summary>
        private static bool AllyDamagedOrHealedInRound(List<CPlayerStats> stats, int round)
        {
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                    if (d != null && d.Round == round && d.FinalDamageAmount > 0 && IsAlly(d.ActorWhoReceivedDamageType) && d.ActorWhoReceivedDamageGUID != d.ActorWhoDealtDamageGUID) return true;
                foreach (CPlayerStatsHeal h in s.Heals ?? Enumerable.Empty<CPlayerStatsHeal>())
                    if (h != null && h.Round == round && h.HealAmount > 0 && h.ActionData != null && IsAlly(h.ActionData.MainActorType) && h.ActionData.MainActorGUID != h.ActionData.SecondaryActorGUID) return true;
            }
            return false;
        }

        /// <summary>Value enemies killed by something other than an attack (a kill ability, damage, a trap...).</summary>
        private static void KillsByNonAttack(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), count = 0;
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsKill k in s.Kills ?? Enumerable.Empty<CPlayerStatsKill>())
                {
                    if (k == null || !IsEnemy(k.ActorWhoDiedType)) continue;
                    bool attack = k.ActionData != null && k.ActionData.AbilityType == CAbility.EAbilityType.Attack && k.CauseOfDeath != CActor.ECauseOfDeath.KillAbility;
                    if (!attack) count++;
                }
            total = needed;
            met = Math.Min(count, needed);
            result = count >= needed;
        }

        /// <summary>Value distinct attack abilities (card + ability name) each performed against at least <paramref name="targets"/> enemies.</summary>
        private static void AttacksWithTargets(CUnlockConditionTarget t, List<CPlayerStats> stats, int targets, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            var hit = new HashSet<string>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                {
                    if (a == null || a.ActionData == null || a.ActionData.AbilityType != CAbility.EAbilityType.Attack || a.TargetGUIDs == null) continue;
                    int enemies = 0;
                    for (int i = 0; i < a.TargetGUIDs.Count; i++)
                        if (a.TargetTypes == null || i >= a.TargetTypes.Count || a.TargetTypes[i] == CActor.EType.Enemy || a.TargetTypes[i] == CActor.EType.Enemy2) enemies++;
                    if (enemies >= targets) hit.Add($"{a.ActionData.CardID}/{a.ActionData.ActingAbilityName}");
                }
            total = needed;
            met = Math.Min(hit.Count, needed);
            result = hit.Count >= needed;
        }

        /// <summary>Invisible when the turn started or when it ended.</summary>
        private static bool InvisibleInRound(List<CPlayerStats> stats, int round)
        {
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsStartTurn x in s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>())
                    if (x != null && x.Round == round && HasInvisible(x.ActionData)) return true;
                foreach (CPlayerStatsEndTurn x in s.EndTurn ?? Enumerable.Empty<CPlayerStatsEndTurn>())
                    if (x != null && x.Round == round && HasInvisible(x.ActionData)) return true;
            }
            return false;
        }

        private static bool HasInvisible(CPlayerStatsAction.CPlayerStatsActionData data) =>
            data?.MainActorPositiveConditions != null && data.MainActorPositiveConditions.Any(p => p != null && p.PositiveCondition == CCondition.EPositiveCondition.Invisible);

        /// <summary>The character lost at least one hit point during the round, from anything (an enemy, a card's own cost, a trap).</summary>
        private static bool LostHpInRound(List<CPlayerStats> stats, int round) =>
            stats.SelectMany(s => s.DamageReceived ?? Enumerable.Empty<CPlayerStatsDamage>()).Any(d => d != null && d.Round == round && d.FinalDamageAmount > 0);

        /// <summary>The character's turn ended with exactly one hit point.</summary>
        private static bool EndedTurnAtOneHp(List<CPlayerStats> stats, int round) =>
            stats.SelectMany(s => s.EndTurn ?? Enumerable.Empty<CPlayerStatsEndTurn>()).Any(x => x != null && x.Round == round && x.ActionData != null && x.ActionData.MainActorHealth == 1);

        // ------------------------------------------------------------------ Group 3, Jaws of the Lion (2026-10-27)

        /// <summary>The character's own GUID, from its turn records.</summary>
        private static string SelfGuid(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(g => !string.IsNullOrEmpty(g));

        /// <summary>Value enemies the character killed on a turn that was not its own (retaliate, wound, a trap they were
        /// pulled onto...): the kill record names whose turn it was.</summary>
        private static void KillsDuringOtherTurns(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), count = 0;
            string self = SelfGuid(stats);
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsKill k in s.Kills ?? Enumerable.Empty<CPlayerStatsKill>())
                {
                    if (k == null || !IsEnemy(k.ActorWhoDiedType) || k.ActionData == null) continue;
                    string turn = k.ActionData.TurnActorGUID;
                    if (!string.IsNullOrEmpty(turn) && turn != self) count++;
                }
            total = needed;
            met = Math.Min(count, needed);
            result = count >= needed;
        }

        /// <summary>Value distinct enemies the character pushed or pulled. (The sheet also asks that every enemy in the
        /// scenario moved at least once; the statistics hold no roster of enemies, so the count stands for it.)</summary>
        private static void EnemiesForcedToMove(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            var moved = new HashSet<string>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                {
                    if (a == null || a.ActionData == null || a.TargetGUIDs == null) continue;
                    if (a.ActionData.AbilityType != CAbility.EAbilityType.Push && a.ActionData.AbilityType != CAbility.EAbilityType.Pull) continue;
                    for (int i = 0; i < a.TargetGUIDs.Count; i++)
                        if (!string.IsNullOrEmpty(a.TargetGUIDs[i]) && (a.TargetTypes == null || i >= a.TargetTypes.Count || a.TargetTypes[i] == CActor.EType.Enemy || a.TargetTypes[i] == CActor.EType.Enemy2))
                            moved.Add(a.TargetGUIDs[i]);
                }
            total = needed;
            met = Math.Min(moved.Count, needed);
            result = moved.Count >= needed;
        }

        /// <summary>Value damage to enemies in one round from attacks the character did not make itself: an actor it
        /// controlled (the record names the controlling class) or an ally attacking on the character's turn (granted).</summary>
        private static void CommandedDamage(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            string self = SelfGuid(stats);
            string selfClass = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Select(x => x?.ActionData?.MainActorClassID).FirstOrDefault(c => !string.IsNullOrEmpty(c));
            var perRound = new Dictionary<int, int>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                {
                    if (d == null || d.ActionData == null || !IsEnemy(d.ActorWhoReceivedDamageType) || d.ActionData.AbilityType != CAbility.EAbilityType.Attack) continue;
                    bool controlled = !string.IsNullOrEmpty(d.ActionData.ControlledByClassID) && d.ActionData.ControlledByClassID == selfClass;
                    bool granted = !string.IsNullOrEmpty(self) && d.ActionData.TurnActorGUID == self && d.ActorWhoDealtDamageGUID != self;
                    if (!controlled && !granted) continue;
                    perRound[d.Round] = (perRound.TryGetValue(d.Round, out int sum) ? sum : 0) + d.FinalDamageAmount;
                }
            int best = perRound.Values.DefaultIfEmpty(0).Max();
            total = needed;
            met = Math.Min(best, needed);
            result = best >= needed;
        }

        /// <summary>One of the conditions named beside the marker (Poison, Strengthen, Bless, Ward...) applied to someone by the
        /// character in the round.</summary>
        private static bool ConditionGivenInRound(List<string> tags, List<CPlayerStats> stats, int round)
        {
            var wanted = new HashSet<CAbility.EAbilityType>();
            foreach (string tag in tags)
                if (!string.IsNullOrEmpty(tag) && !tag.StartsWith("FrosthavenMods_", StringComparison.Ordinal) && Enum.TryParse(tag, true, out CAbility.EAbilityType type)) wanted.Add(type);
            if (wanted.Count == 0) return false;
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                    if (a != null && a.Round == round && a.ActionData != null && wanted.Contains(a.ActionData.AbilityType) && a.TargetGUIDs != null && a.TargetGUIDs.Any(g => !string.IsNullOrEmpty(g))) return true;
            return false;
        }

        /// <summary>Value different attack actions (a card half) each of which dealt Amount damage to enemies in one performance.</summary>
        private static void AttackActionsWithDamage(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), amount = Math.Max(1, t.Amount);
            var sums = new Dictionary<string, int>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                {
                    if (d == null || d.ActionData == null || !IsEnemy(d.ActorWhoReceivedDamageType) || d.ActionData.AbilityType != CAbility.EAbilityType.Attack) continue;
                    string half = string.IsNullOrEmpty(d.ActionData.ActingAbilityName) ? "?" : d.ActionData.ActingAbilityName.Substring(0, 1);
                    string key = $"{d.Round}/{d.ActionData.CardID}/{half}";
                    sums[key] = (sums.TryGetValue(key, out int sum) ? sum : 0) + d.FinalDamageAmount;
                }
            var actions = new HashSet<string>(sums.Where(p => p.Value >= amount).Select(p => p.Key.Substring(p.Key.IndexOf('/') + 1)));
            total = needed;
            met = Math.Min(actions.Count, needed);
            result = actions.Count >= needed;
        }

        /// <summary>Value obstacles destroyed by the character. (The sheet's second clause, no obstacle left at the end
        /// but those allies placed, is not in the statistics; the count stands for the mastery.)</summary>
        private static void ObstaclesDestroyed(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            int count = stats.Sum(s => (s.DestroyedObstacles ?? Enumerable.Empty<CPlayerStatsDestroyObstacle>()).Count(o => o != null));
            total = needed;
            met = Math.Min(count, needed);
            result = count >= needed;
        }

        private static bool IsAlly(string actorType) =>
            actorType == CActor.EType.Player.ToString() || actorType == CActor.EType.HeroSummon.ToString();

        private static bool IsEnemy(string actorType) =>
            actorType == CActor.EType.Enemy.ToString() || actorType == CActor.EType.Enemy2.ToString();

        private static readonly HashSet<string> Reported = new HashSet<string>();
    }
}
