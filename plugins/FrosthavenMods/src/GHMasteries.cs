using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A mastery condition the game's battle-goal filters can't express: "across N consecutive rounds, play X different
    /// ability cards and cause enemies to suffer at least D damage on each of your turns" (the Brute's second mastery on
    /// Frosthaven's crossover sheet). The game checks a battle goal by running each of its targets through one big
    /// filter switch over the party's per-round statistics; none of its filters looks at rounds in a row with two
    /// conditions at once. So the mastery file (tools/gh_crossover.py) tags its target with a marker in EnemyFamilies,
    /// a free-text list the parser never validates, and this prefix answers the tagged target from the same records:
    /// rounds in a row = Value, damage per turn = Amount, different cards = Targets. Untagged targets are untouched.
    /// Inert without the pack: nothing in the game's own files carries the marker.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMasteries
    {
        internal const string StreakTag = "FrosthavenMods_DamageStreak";

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Streak(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            if (unlockConditionTarget?.TargetEnemyFamilies == null || !unlockConditionTarget.TargetEnemyFamilies.Contains(StreakTag)) return true;
            int rounds = Math.Max(1, unlockConditionTarget.Value), damage = Math.Max(0, unlockConditionTarget.Amount), cards = Math.Max(0, unlockConditionTarget.Targets);
            totalConditions = rounds;
            conditionsMet = 0;
            __result = false;
            try
            {
                var dealt = new Dictionary<int, int>();
                var played = new Dictionary<int, HashSet<int>>();
                foreach (CPlayerStats stats in comparePlayerStats ?? Enumerable.Empty<CPlayerStats>())
                {
                    if (stats == null) continue;
                    foreach (CPlayerStatsDamage d in stats.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                        if (d != null && IsEnemy(d.MainActorType)) dealt[d.Round] = (dealt.TryGetValue(d.Round, out int sum) ? sum : 0) + d.FinalDamageAmount;
                    foreach (CPlayerStatsAbilities a in stats.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                    {
                        if (a == null || a.CardType != CBaseCard.ECardType.CharacterAbility || a.CardID == int.MaxValue) continue;
                        if (!played.TryGetValue(a.Round, out HashSet<int> set)) played[a.Round] = set = new HashSet<int>();
                        set.Add(a.CardID);
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
                    if (distinct.Count >= cards) { conditionsMet = rounds; __result = true; return false; }
                }
                conditionsMet = Math.Min(best, rounds - 1);
            }
            catch (Exception e)
            {
                if (Reported.Add("streak")) Plugin.Log.LogWarning("Gloomhaven classes: could not check the damage-streak mastery: " + e);
            }
            return false;
        }

        /// <summary>Damage to an enemy: the records name the damaged actor's type; allies, players and the party's own
        /// summons don't count.</summary>
        private static bool IsEnemy(string actorType) =>
            !string.IsNullOrEmpty(actorType) && actorType != "Ally" && actorType != "Player" && actorType != "HeroSummon";

        private static readonly HashSet<string> Reported = new HashSet<string>();
    }
}
