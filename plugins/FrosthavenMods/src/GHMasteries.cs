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
    ///   Group 2 and Group 3 markers are listed at their constants below.
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
        // The Hatchet's two need the Favorite's moves, which the statistics do not record: the resource hooks below note
        // the round of every throw or recovery (the Hatchet's own resource count changes either way), and both tags
        // ask for one in every round the Hatchet took a turn (the second's "with an enemy on the map at the start of
        // the turn" is read as every turn: a scenario with no enemy left is over).
        internal const string FavoriteEveryTurnTag = "FrosthavenMods_FavoriteEveryTurn";
        internal const string FavoriteEveryRoundTag = "FrosthavenMods_FavoriteEveryRoundWithEnemies";
        // Groups 4-6 (2026-10-27, written with the sheets, untested until each class is on the bench)
        internal const string AllElementsTag = "FrosthavenMods_AllElementsInfusedAndConsumed";  // (Spellweaver) every element infused and consumed
        internal const string LossRepeatedTag = "FrosthavenMods_LossActionsRepeated";          // (Spellweaver) Value lost halves performed Amount times each
        internal const string AugmentsTriggeredTag = "FrosthavenMods_AugmentsTriggered";        // (Mindthief) Value augments fired Amount times each
        internal const string NeverAttackedTag = "FrosthavenMods_NeverAttacked";               // (Mindthief) no attack ever targeted the character
        internal const string TwoElementAttacksTag = "FrosthavenMods_AttacksConsumingTwoElements"; // (Elementalist) Value attack halves consuming two elements
        internal const string InfuseThenConsumeTag = "FrosthavenMods_InfuseThenConsume";       // (Elementalist) Amount infusions in a turn, Amount consumptions the next
        internal const string NewSongTag = "FrosthavenMods_NewSongAfterRests";                 // (Soothsinger) a new song on the first turn and after each rest
        internal const string AllCursesBlessesTag = "FrosthavenMods_AllCursesAndBlessesOut";   // (Soothsinger) every curse and bless in the decks at once
        internal const string HealOrDebuffTag = "FrosthavenMods_HealOrDebuffEveryTurn";        // (Tinkerer) an ally healed or an enemy debuffed every turn
        internal const string LossBeforeRestsTag = "FrosthavenMods_LossActionsBeforeRests";    // (Tinkerer) Amount lost halves before every rest
        internal const string NoRepeatedDoomTag = "FrosthavenMods_NoRepeatedDoom";             // (Doomstalker) no doom half performed twice
        internal const string DoomedKillsTag = "FrosthavenMods_DoomedKillsInTurn";             // (Doomstalker) Value doomed enemies killed in one turn
        internal const string SummonStreakTag = "FrosthavenMods_SummonDamageStreak";           // (Beast Tyrant) Value rounds in a row with Amount summon damage
        internal const string EnemiesDebuffedTag = "FrosthavenMods_EnemiesDebuffed";           // (Beast Tyrant) Value distinct enemies given a negative condition
        internal const string LavaGolemTag = "FrosthavenMods_LavaGolemAllScenario";            // (Summoner) the Lava Golem out on turn 1 and never killed
        internal const string SummonCardsTag = "FrosthavenMods_SummonsFromDifferentCards";     // (Summoner) Value cards whose summon ability was performed

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
                else if (tags.Contains(FavoriteEveryTurnTag) || tags.Contains(FavoriteEveryRoundTag)) EveryTurn(stats, r => FavoriteMovedInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(AllElementsTag)) AllElements(stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(LossRepeatedTag)) LossActionsRepeated(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(AugmentsTriggeredTag)) AugmentsTriggered(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(NeverAttackedTag)) NeverAttacked(stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(TwoElementAttacksTag)) AttacksConsumingTwo(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(InfuseThenConsumeTag)) InfuseThenConsume(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(NewSongTag)) NewSongAfterRests(stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(AllCursesBlessesTag)) NotAnsweredYet(ref totalConditions, ref conditionsMet, ref __result);   // needs the decks at runtime: with the Soothsinger's port
                else if (tags.Contains(HealOrDebuffTag)) EveryTurn(stats, r => HealOrDebuffInRound(stats, r), ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(LossBeforeRestsTag)) LossActionsBeforeRests(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(NoRepeatedDoomTag)) NoRepeatedDoom(stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(DoomedKillsTag)) DoomedKillsInTurn(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(SummonStreakTag)) SummonDamageStreak(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(EnemiesDebuffedTag)) EnemiesDebuffed(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(LavaGolemTag)) LavaGolemAllScenario(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else if (tags.Contains(SummonCardsTag)) SummonsFromDifferentCards(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
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

        // ---- the Favorite's moves (the Hatchet): resource changes on the player, by round, for the current scenario

        private static readonly Dictionary<string, HashSet<int>> FavoriteMoves = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private static ScenarioState _favoriteScenario;

        [HarmonyPatch(typeof(CActor), nameof(CActor.AddCharacterResource))]
        [HarmonyPostfix]
        private static void ResourceAdded(CActor __instance, string resourceID) => NoteFavorite(__instance, resourceID);

        [HarmonyPatch(typeof(CActor), nameof(CActor.RemoveCharacterResource))]
        [HarmonyPostfix]
        private static void ResourceRemoved(CActor __instance, string resourceID) => NoteFavorite(__instance, resourceID);

        private static void NoteFavorite(CActor actor, string resourceID)
        {
            try
            {
                if (!(actor is CPlayerActor) || string.IsNullOrEmpty(resourceID) || resourceID.IndexOf("Favorite", StringComparison.OrdinalIgnoreCase) < 0) return;
                ScenarioState scenario = ScenarioManager.CurrentScenarioState;
                if (scenario == null) return;
                if (!ReferenceEquals(scenario, _favoriteScenario)) { FavoriteMoves.Clear(); _favoriteScenario = scenario; }
                string guid = actor.ActorGuid ?? "";
                if (!FavoriteMoves.TryGetValue(guid, out HashSet<int> rounds)) FavoriteMoves[guid] = rounds = new HashSet<int>();
                rounds.Add(scenario.RoundNumber);
            }
            catch (Exception e) { if (Reported.Add("favorite")) Plugin.Log.LogError("Gloomhaven masteries: the Favorite's move was not noted: " + e); }
        }

        private static bool FavoriteMovedInRound(List<CPlayerStats> stats, int round)
        {
            string self = SelfGuid(stats);
            return !string.IsNullOrEmpty(self) && FavoriteMoves.TryGetValue(self, out HashSet<int> rounds) && rounds.Contains(round);
        }

        // ------------------------------------------------------------------ Groups 4-6 (2026-10-27)

        private static readonly HashSet<CAbility.EAbilityType> NegativeConditionTypes = new HashSet<CAbility.EAbilityType>
        {
            CAbility.EAbilityType.Poison, CAbility.EAbilityType.Wound, CAbility.EAbilityType.Muddle, CAbility.EAbilityType.Immobilize,
            CAbility.EAbilityType.Disarm, CAbility.EAbilityType.Stun, CAbility.EAbilityType.Curse, CAbility.EAbilityType.Bane,
            CAbility.EAbilityType.Brittle, CAbility.EAbilityType.Impair,
        };

        private static readonly ElementInfusionBoardManager.EElement[] SixElements =
        {
            ElementInfusionBoardManager.EElement.Fire, ElementInfusionBoardManager.EElement.Ice, ElementInfusionBoardManager.EElement.Air,
            ElementInfusionBoardManager.EElement.Earth, ElementInfusionBoardManager.EElement.Light, ElementInfusionBoardManager.EElement.Dark,
        };

        /// <summary>A condition this plugin cannot judge yet: not met, never a free pass.</summary>
        private static void NotAnsweredYet(ref int total, ref int met, ref bool result) { total = 1; met = 0; result = false; }

        private static IEnumerable<CPlayerStatsAbilities> AbilityRecords(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>()).Where(a => a != null && a.ActionData != null);

        /// <summary>"T"/"B" + card ID: the card half an ability record belongs to.</summary>
        private static string Half(CPlayerStatsAction.CPlayerStatsActionData d) =>
            (string.IsNullOrEmpty(d.ActingAbilityName) ? "?" : d.ActingAbilityName.Substring(0, 1)) + d.CardID;

        private static bool IsEnemyTarget(CPlayerStatsAbilities a, int i) =>
            a.TargetTypes == null || i >= a.TargetTypes.Count || a.TargetTypes[i] == CActor.EType.Enemy || a.TargetTypes[i] == CActor.EType.Enemy2;

        /// <summary>The rounds in which the character rested, from its turn records.</summary>
        private static List<int> RestRounds(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Concat<CPlayerStatsAction>(stats.SelectMany(s => s.EndTurn ?? Enumerable.Empty<CPlayerStatsEndTurn>()))
                .Where(x => x != null && x.ActionData != null && x.ActionData.MainActorResting).Select(x => x.Round).Distinct().OrderBy(r => r).ToList();

        private static List<int> TurnRounds(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Where(x => x != null && x.Round > 0).Select(x => x.Round).Distinct().OrderBy(r => r).ToList();

        /// <summary>Every element infused at least once and consumed at least once (twelve conditions).</summary>
        private static void AllElements(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            var infused = new HashSet<ElementInfusionBoardManager.EElement>();
            var consumed = new HashSet<ElementInfusionBoardManager.EElement>();
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsElement e in s.Infusions ?? Enumerable.Empty<CPlayerStatsElement>())
                    if (e != null) { infused.Add(e.Element); if (e.Infused != null) infused.UnionWith(e.Infused); }
                foreach (CPlayerStatsElement e in s.Consumed ?? Enumerable.Empty<CPlayerStatsElement>())
                    if (e != null) consumed.Add(e.Element);
            }
            total = 12;
            met = SixElements.Count(infused.Contains) + SixElements.Count(consumed.Contains);
            result = met == total;
        }

        /// <summary>Performances of each card half with a lost icon, counted by round.</summary>
        private static Dictionary<string, HashSet<int>> LossPerformances(List<CPlayerStats> stats)
        {
            var perf = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (CPlayerStatsAbilities a in AbilityRecords(stats))
            {
                if (a.ActionData.CardType != CBaseCard.ECardType.CharacterAbility || a.ActionData.CardID == int.MaxValue) continue;
                if (a.DiscardType != DiscardType.Lost && a.DiscardType != DiscardType.PermanentlyLost) continue;
                string half = Half(a.ActionData);
                if (!perf.TryGetValue(half, out HashSet<int> rounds)) perf[half] = rounds = new HashSet<int>();
                rounds.Add(a.Round);
            }
            return perf;
        }

        /// <summary>Value different lost halves each performed Amount times.</summary>
        private static void LossActionsRepeated(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), times = Math.Max(1, t.Amount);
            int count = LossPerformances(stats).Values.Count(r => r.Count >= times);
            total = needed; met = Math.Min(count, needed); result = count >= needed;
        }

        /// <summary>Value augments whose on-attack abilities fired Amount times each (the attack hook below counts them).</summary>
        private static void AugmentsTriggered(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), times = Math.Max(1, t.Amount);
            string self = SelfGuid(stats);
            int count = !string.IsNullOrEmpty(self) && AugmentFires.TryGetValue(self, out Dictionary<int, int> fires) ? fires.Values.Count(n => n >= times) : 0;
            total = needed; met = Math.Min(count, needed); result = count >= needed;
        }

        /// <summary>No attack ever reached the character (a dodged or avoided attack still leaves a damage record).</summary>
        private static void NeverAttacked(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            bool attacked = stats.SelectMany(s => s.DamageReceived ?? Enumerable.Empty<CPlayerStatsDamage>())
                .Any(d => d != null && d.ActionData != null && d.ActionData.AbilityType == CAbility.EAbilityType.Attack);
            total = 1; met = attacked ? 0 : 1; result = !attacked;
        }

        /// <summary>Value different attack halves performed in a round in which two different elements were consumed (the
        /// element records carry no card, so the round stands for the performance).</summary>
        private static void AttacksConsumingTwo(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            var consumed = new Dictionary<int, HashSet<ElementInfusionBoardManager.EElement>>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsElement e in s.Consumed ?? Enumerable.Empty<CPlayerStatsElement>())
                {
                    if (e == null) continue;
                    if (!consumed.TryGetValue(e.Round, out HashSet<ElementInfusionBoardManager.EElement> set)) consumed[e.Round] = set = new HashSet<ElementInfusionBoardManager.EElement>();
                    set.Add(e.Element);
                }
            var halves = new HashSet<string>(StringComparer.Ordinal);
            foreach (CPlayerStatsAbilities a in AbilityRecords(stats))
                if (a.ActionData.AbilityType == CAbility.EAbilityType.Attack && consumed.TryGetValue(a.Round, out HashSet<ElementInfusionBoardManager.EElement> set) && set.Count >= 2)
                    halves.Add(Half(a.ActionData));
            total = needed; met = Math.Min(halves.Count, needed); result = halves.Count >= needed;
        }

        /// <summary>A turn with Amount infusions followed by the character's next turn with Amount consumptions.</summary>
        private static void InfuseThenConsume(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Amount);
            var infusedIn = new Dictionary<int, int>();
            var consumedIn = new Dictionary<int, int>();
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsElement e in s.Infusions ?? Enumerable.Empty<CPlayerStatsElement>())
                    if (e != null) infusedIn[e.Round] = (infusedIn.TryGetValue(e.Round, out int n) ? n : 0) + Math.Max(1, e.Infused?.Count ?? 0);
                foreach (CPlayerStatsElement e in s.Consumed ?? Enumerable.Empty<CPlayerStatsElement>())
                    if (e != null) consumedIn[e.Round] = (consumedIn.TryGetValue(e.Round, out int n) ? n : 0) + 1;
            }
            List<int> turns = TurnRounds(stats);
            bool done = false;
            for (int i = 0; i + 1 < turns.Count && !done; i++)
                done = infusedIn.TryGetValue(turns[i], out int a) && a >= needed && consumedIn.TryGetValue(turns[i + 1], out int b) && b >= needed;
            total = 2;
            met = done ? 2 : (infusedIn.Values.Any(v => v >= needed) ? 1 : 0);
            result = done;
        }

        /// <summary>On the first turn and on the turn after every rest, a song not sung before in the scenario.</summary>
        private static void NewSongAfterRests(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            List<int> turns = TurnRounds(stats);
            List<int> rests = RestRounds(stats);
            var songs = new List<(int round, string half)>();
            foreach (CPlayerStatsAbilities a in AbilityRecords(stats))
                if (a.ActionData.AbilityType == CAbility.EAbilityType.PlaySong) songs.Add((a.Round, Half(a.ActionData)));
            var due = new List<int>();
            if (turns.Count > 0) due.Add(turns[0]);
            foreach (int r in rests) { int next = turns.FirstOrDefault(x => x > r); if (next > 0) due.Add(next); }
            due = due.Distinct().OrderBy(r => r).ToList();
            int ok = 0;
            var sung = new HashSet<string>(StringComparer.Ordinal);
            foreach (int r in turns)
            {
                bool fresh = false;
                foreach ((int round, string half) in songs.Where(x => x.round == r)) fresh |= sung.Add(half);
                if (due.Contains(r) && fresh) ok++;
            }
            total = Math.Max(1, due.Count); met = Math.Min(ok, total); result = due.Count > 0 && ok == due.Count;
        }

        /// <summary>An ally healed by the character, or an enemy given a negative condition, in the round.</summary>
        private static bool HealOrDebuffInRound(List<CPlayerStats> stats, int round)
        {
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsHeal h in s.Heals ?? Enumerable.Empty<CPlayerStatsHeal>())
                    if (h != null && h.Round == round && h.HealAmount > 0 && h.ActionData != null && IsAlly(h.ActionData.MainActorType) && h.ActionData.MainActorGUID != h.ActionData.SecondaryActorGUID) return true;
            foreach (CPlayerStatsAbilities a in AbilityRecords(stats))
            {
                if (a.Round != round || !NegativeConditionTypes.Contains(a.ActionData.AbilityType) || a.TargetGUIDs == null) continue;
                for (int i = 0; i < a.TargetGUIDs.Count; i++) if (!string.IsNullOrEmpty(a.TargetGUIDs[i]) && IsEnemyTarget(a, i)) return true;
            }
            return false;
        }

        /// <summary>Amount lost halves performed before the first rest and between every two rests.</summary>
        private static void LossActionsBeforeRests(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Amount);
            List<int> rests = RestRounds(stats);
            List<int> lossRounds = LossPerformances(stats).Values.SelectMany(r => r).OrderBy(r => r).ToList();
            int previous = 0, ok = 0;
            foreach (int rest in rests)
            {
                if (lossRounds.Count(r => r > previous && r < rest) >= needed) ok++;
                previous = rest;
            }
            if (rests.Count == 0) { total = 1; met = lossRounds.Count >= needed ? 1 : 0; result = met == 1; return; }
            total = rests.Count; met = ok; result = ok == rests.Count;
        }

        /// <summary>At least one doom performed and no doom half performed in two different rounds.</summary>
        private static void NoRepeatedDoom(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            var rounds = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (CPlayerStatsAbilities a in AbilityRecords(stats))
            {
                if (a.ActionData.AbilityType != CAbility.EAbilityType.AddDoom) continue;
                string half = Half(a.ActionData);
                if (!rounds.TryGetValue(half, out HashSet<int> set)) rounds[half] = set = new HashSet<int>();
                set.Add(a.Round);
            }
            total = 1;
            result = rounds.Count > 0 && rounds.Values.All(r => r.Count == 1);
            met = result ? 1 : 0;
        }

        /// <summary>Value doomed enemies killed in one of the character's turns (the kill record's Doom flag).</summary>
        private static void DoomedKillsInTurn(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            string self = SelfGuid(stats);
            var perRound = new Dictionary<int, int>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsKill k in s.Kills ?? Enumerable.Empty<CPlayerStatsKill>())
                {
                    if (k == null || !k.Doom || !IsEnemy(k.ActorWhoDiedType) || k.ActionData == null) continue;
                    if (!string.IsNullOrEmpty(self) && !string.IsNullOrEmpty(k.ActionData.TurnActorGUID) && k.ActionData.TurnActorGUID != self) continue;
                    perRound[k.Round] = (perRound.TryGetValue(k.Round, out int n) ? n : 0) + 1;
                }
            int best = perRound.Values.DefaultIfEmpty(0).Max();
            total = needed; met = Math.Min(best, needed); result = best >= needed;
        }

        /// <summary>Value rounds in a row with Amount damage to enemies dealt by the character's summons.</summary>
        private static void SummonDamageStreak(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int rounds = Math.Max(1, t.Value), amount = Math.Max(1, t.Amount);
            var dealt = new Dictionary<int, int>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                {
                    if (d == null || d.ActionData == null || !IsEnemy(d.ActorWhoReceivedDamageType)) continue;
                    if (!d.ActionData.PerformedBySummons && d.ActorWhoDealtDamageType != CActor.EType.HeroSummon.ToString()) continue;
                    dealt[d.Round] = (dealt.TryGetValue(d.Round, out int n) ? n : 0) + d.FinalDamageAmount;
                }
            int last = dealt.Keys.DefaultIfEmpty(0).Max(), run = 0, best = 0;
            for (int r = 1; r <= last; r++) { run = dealt.TryGetValue(r, out int n) && n >= amount ? run + 1 : 0; best = Math.Max(best, run); }
            total = rounds; met = Math.Min(best, rounds); result = best >= rounds;
        }

        /// <summary>Value distinct enemies given a negative condition by the character or its summons.</summary>
        private static void EnemiesDebuffed(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (CPlayerStatsAbilities a in AbilityRecords(stats))
            {
                if (!NegativeConditionTypes.Contains(a.ActionData.AbilityType) || a.TargetGUIDs == null) continue;
                for (int i = 0; i < a.TargetGUIDs.Count; i++) if (!string.IsNullOrEmpty(a.TargetGUIDs[i]) && IsEnemyTarget(a, i)) enemies.Add(a.TargetGUIDs[i]);
            }
            total = needed; met = Math.Min(enemies.Count, needed); result = enemies.Count >= needed;
        }

        /// <summary>The Lava Golem (its card is Amount) summoned on the character's first turn and no summon of that
        /// name among the character's deaths.</summary>
        private static void LavaGolemAllScenario(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            List<int> turns = TurnRounds(stats);
            bool summoned = turns.Count > 0 && AbilityRecords(stats).Any(a => a.Round == turns[0] && a.ActionData.AbilityType == CAbility.EAbilityType.Summon && (t.Amount <= 0 || a.ActionData.CardID == t.Amount));
            bool died = stats.SelectMany(s => s.Deaths ?? Enumerable.Empty<CPlayerStatsKill>()).Any(k => k != null && k.ActorWhoDiedType == CActor.EType.HeroSummon.ToString()
                && !string.IsNullOrEmpty(k.ActorWhoDiedClassID) && k.ActorWhoDiedClassID.IndexOf("LavaGolem", StringComparison.OrdinalIgnoreCase) >= 0);
            total = 2; met = (summoned ? 1 : 0) + (summoned && !died ? 1 : 0); result = summoned && !died;
        }

        /// <summary>Value different cards whose summon ability was performed.</summary>
        private static void SummonsFromDifferentCards(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            var cards = new HashSet<int>(AbilityRecords(stats).Where(a => a.ActionData.AbilityType == CAbility.EAbilityType.Summon && a.ActionData.CardID != int.MaxValue).Select(a => a.ActionData.CardID));
            total = needed; met = Math.Min(cards.Count, needed); result = cards.Count >= needed;
        }

        // ---- augment fires (the Mindthief): each time an attack's augment abilities are stacked, per augment, for the current scenario

        private static readonly Dictionary<string, Dictionary<int, int>> AugmentFires = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        private static ScenarioState _augmentScenario;
        private static readonly AccessTools.FieldRef<CAbility, List<CAugment>> AttackAugments = AccessTools.FieldRefAccess<CAbility, List<CAugment>>("m_AbilityAugments");

        [HarmonyPatch(typeof(CAbilityAttack), "ProcessAbilityAugments")]
        [HarmonyPostfix]
        private static void AugmentsFired(CAbilityAttack __instance, bool __result)
        {
            try
            {
                if (!__result || __instance?.TargetingActor == null) return;
                ScenarioState scenario = ScenarioManager.CurrentScenarioState;
                if (scenario == null) return;
                if (!ReferenceEquals(scenario, _augmentScenario)) { AugmentFires.Clear(); _augmentScenario = scenario; }
                string guid = __instance.TargetingActor.ActorGuid ?? "";
                if (!AugmentFires.TryGetValue(guid, out Dictionary<int, int> fires)) AugmentFires[guid] = fires = new Dictionary<int, int>();
                foreach (CAugment augment in AttackAugments(__instance) ?? new List<CAugment>())
                {
                    if (augment == null) continue;
                    int key = augment.RegisteredBaseCard != null ? augment.RegisteredBaseCard.ID : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(augment);
                    fires[key] = (fires.TryGetValue(key, out int n) ? n : 0) + 1;
                }
            }
            catch (Exception e) { if (Reported.Add("augments")) Plugin.Log.LogError("Gloomhaven masteries: an augment's fire was not noted: " + e); }
        }

        private static bool IsAlly(string actorType) =>
            actorType == CActor.EType.Player.ToString() || actorType == CActor.EType.HeroSummon.ToString();

        private static bool IsEnemy(string actorType) =>
            actorType == CActor.EType.Enemy.ToString() || actorType == CActor.EType.Enemy2.ToString();

        private static readonly HashSet<string> Reported = new HashSet<string>();
    }
}
