using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Doomstalker's two masteries, answered like GHMasteries (a marker in the mastery target's
    /// EnemyFamilies; this prefix answers only its own markers and leaves every other target to GHMasteries / the game):
    ///   FrosthavenMods_DoomedKillsTotal       "Kill 20 enemies that have Doom": Value kills of doomed enemies by the
    ///                                         character itself (not its summons) in the scenario (user default: within
    ///                                         one scenario, like the other masteries)
    ///   FrosthavenMods_HeroPlusSummonAttacks  "In a single round, you and one of your summons must perform a combined
    ///                                         total of 6 or more attack abilities": per round, the character's attack
    ///                                         abilities that happened plus those of its busiest summon, Value or more
    /// Logs "GHRule DoomedKills card 0 ..." / "GHRule HeroSummonAttacks card 0 ..." when met.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMasteriesDoomstalker2
    {
        internal const string DoomedKillsTag = "FrosthavenMods_DoomedKillsTotal";
        internal const string AttacksTag = "FrosthavenMods_HeroPlusSummonAttacks";
        private static readonly HashSet<string> Reported = new HashSet<string>();

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Answer(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || !(tags.Contains(DoomedKillsTag) || tags.Contains(AttacksTag))) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            try
            {
                if (tags.Contains(DoomedKillsTag)) DoomedKills(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
                else HeroPlusSummonAttacks(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
            }
            catch (Exception e)
            {
                if (Reported.Add("err")) Plugin.Log.LogError("Doomstalker (2e) masteries: " + e);
                totalConditions = Math.Max(1, unlockConditionTarget.Value);
                conditionsMet = 0;
                __result = false;
            }
            return false;
        }

        private static bool IsEnemy(string t) => t == CActor.EType.Enemy.ToString() || t == CActor.EType.Enemy2.ToString();

        private static string SelfGuid(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(g => !string.IsNullOrEmpty(g));

        private static void DoomedKills(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value), count = 0;
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsKill k in s.Kills ?? Enumerable.Empty<CPlayerStatsKill>())
                {
                    if (k == null || !k.Doom || !IsEnemy(k.ActorWhoDiedType) || k.ActionData == null) continue;
                    if (k.ActionData.PerformedBySummons || k.ActorWhoKilledType == CActor.EType.HeroSummon.ToString()) continue;
                    count++;
                }
            total = needed; met = Math.Min(count, needed); result = count >= needed;
            if (result && Reported.Add("kills " + count)) Plugin.Log.LogInfo($"GHRule DoomedKills card 0 mastery Kill 20 enemies that have Doom: {count} doomed enemies killed this scenario");
        }

        private static void HeroPlusSummonAttacks(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Value);
            string self = SelfGuid(stats);
            // round -> performer -> distinct attack abilities that happened
            var perRound = new Dictionary<int, Dictionary<string, HashSet<Guid>>>();
            foreach (CPlayerStats s in stats)
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                {
                    if (a == null || !a.HasHappened || a.ActionData == null || a.ActionData.AbilityType != CAbility.EAbilityType.Attack) continue;
                    string who = a.ActionData.MainActorGUID;
                    if (string.IsNullOrEmpty(who)) continue;
                    bool hero = who == self;
                    bool summon = a.ActionData.PerformedBySummons || a.ActionData.MainActorType == CActor.EType.HeroSummon.ToString();
                    if (!hero && !summon) continue;
                    if (!perRound.TryGetValue(a.Round, out var byWho)) perRound[a.Round] = byWho = new Dictionary<string, HashSet<Guid>>();
                    if (!byWho.TryGetValue(who, out var ids)) byWho[who] = ids = new HashSet<Guid>();
                    ids.Add(a.AbilityID);
                }
            int best = 0, bestRound = 0;
            foreach (var kv in perRound)
            {
                int heroCount = kv.Value.TryGetValue(self ?? "", out var h) ? h.Count : 0;
                int summonCount = kv.Value.Where(p => p.Key != self).Select(p => p.Value.Count).DefaultIfEmpty(0).Max();
                if (heroCount + summonCount > best) { best = heroCount + summonCount; bestRound = kv.Key; }
            }
            total = needed; met = Math.Min(best, needed); result = best >= needed;
            if (result && Reported.Add("attacks " + bestRound)) Plugin.Log.LogInfo($"GHRule HeroSummonAttacks card 0 mastery 6 attack abilities: {best} by you and one summon in round {bestRound}");
        }
    }
}
