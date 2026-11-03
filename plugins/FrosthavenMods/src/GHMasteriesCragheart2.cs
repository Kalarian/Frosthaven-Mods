using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The Gloomhaven 2nd edition Cragheart's two masteries (2e card-accuracy pass, task rest-a), answered the same way
    /// as GHMasteries: the mastery file tags its battle-goal target with a marker in EnemyFamilies and this prefix answers
    /// only its own markers (anything else goes on to GHMasteries and the game's filters).
    ///   FrosthavenMods_ObstacleEveryRound  every round of the scenario (1 to the last round the character took a turn
    ///                                      in), the character created, destroyed or moved at least one obstacle. Read
    ///                                      from the statistics (Create / DestroyObstacle / MoveObstacle abilities that
    ///                                      happened, destroyed-obstacle records) plus a note of every obstacle spawned
    ///                                      with the character as its owner (a modifier card's Create, which keeps no
    ///                                      ability record).
    ///   FrosthavenMods_Heal8Ranged8Melee8  in one scenario: Amount hit points actually restored (overheal not counted;
    ///                                      the heal record holds the restored amount) by the character's heals in a
    ///                                      single round, on anyone; Amount damage to enemies with one ranged attack
    ///                                      ability (all its targets together); and Amount with one melee attack ability.
    /// An exhausted character is judged at its exhaustion (FH rulebook p.48: "they can still achieve a mastery if its
    /// requirements were met at the time they became exhausted"), so the rounds counted end at its last turn; a Create
    /// that placed nothing leaves no "happened" record (the engine sets AbilityHasHappened only when it spawns).
    /// Logs "GHRule ObstacleEveryRound / Heal8Ranged8Melee8 card 0 ..." when checked.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMasteriesCragheart2
    {
        internal const string ObstacleTag = "FrosthavenMods_ObstacleEveryRound";
        internal const string HealDamageTag = "FrosthavenMods_Heal8Ranged8Melee8";

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Answer(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || (!tags.Contains(ObstacleTag) && !tags.Contains(HealDamageTag))) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            try
            {
                if (tags.Contains(ObstacleTag)) ObstacleEveryRound(stats, ref totalConditions, ref conditionsMet, ref __result);
                else HealDamage(unlockConditionTarget, stats, ref totalConditions, ref conditionsMet, ref __result);
            }
            catch (Exception e)
            {
                if (GHClasses.Reported.Add("cragheart2 mastery")) Plugin.Log.LogError("Cragheart (2e) masteries: could not check: " + e);
                totalConditions = 1;
                conditionsMet = 0;
                __result = false;
            }
            return false;
        }

        private static string SelfGuid(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(g => !string.IsNullOrEmpty(g));

        private static readonly HashSet<CAbility.EAbilityType> ObstacleTypes = new HashSet<CAbility.EAbilityType>
        {
            CAbility.EAbilityType.Create, CAbility.EAbilityType.DestroyObstacle, CAbility.EAbilityType.MoveObstacle,
        };

        private static void ObstacleEveryRound(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            var rounds = new HashSet<int>();
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                    if (a != null && a.HasHappened && a.ActionData != null && !a.ActionData.PerformedBySummons && ObstacleTypes.Contains(a.ActionData.AbilityType)) rounds.Add(a.Round);
                foreach (CPlayerStatsDestroyObstacle d in s.DestroyedObstacles ?? Enumerable.Empty<CPlayerStatsDestroyObstacle>())
                    if (d != null) rounds.Add(d.Round);
            }
            string self = SelfGuid(stats);
            if (!string.IsNullOrEmpty(self) && Spawned.TryGetValue(self, out HashSet<int> spawned) && ReferenceEquals(_spawnScenario, ScenarioManager.CurrentScenarioState))
                rounds.UnionWith(spawned);
            rounds.UnionWith(SavedSpawnRounds(GHSavedCounts.Player(self)));
            int last = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Where(x => x != null).Select(x => x.Round).DefaultIfEmpty(0).Max();
            total = Math.Max(1, last);
            met = Enumerable.Range(1, Math.Max(0, last)).Count(rounds.Contains);
            result = last > 0 && met == last;
            Plugin.Log.LogInfo($"GHRule ObstacleEveryRound card 0 Cragheart mastery: an obstacle created, destroyed or moved in {met} of {last} rounds");
        }

        private static void HealDamage(CUnlockConditionTarget t, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, t.Amount);
            var healed = new Dictionary<int, int>();
            var ranged = new Dictionary<string, int>();
            var melee = new Dictionary<string, int>();
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsHeal h in s.Heals ?? Enumerable.Empty<CPlayerStatsHeal>())
                    if (h != null && h.ActionData != null && !h.ActionData.PerformedBySummons && h.HealAmount > 0)
                        healed[h.Round] = (healed.TryGetValue(h.Round, out int n) ? n : 0) + h.HealAmount;
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                {
                    if (d == null || d.ActionData == null || d.ActionData.PerformedBySummons || d.ActionData.AbilityType != CAbility.EAbilityType.Attack) continue;
                    if (d.ActorWhoReceivedDamageType != "Enemy" && d.ActorWhoReceivedDamageType != "Enemy2") continue;
                    string key = $"{d.Round}/{d.ActionData.CardID}/{d.ActionData.ActingAbilityName}";
                    Dictionary<string, int> into = d.IsMelee ? melee : ranged;
                    into[key] = (into.TryGetValue(key, out int sum) ? sum : 0) + d.FinalDamageAmount;
                }
            }
            int bestHeal = healed.Values.DefaultIfEmpty(0).Max(), bestRanged = ranged.Values.DefaultIfEmpty(0).Max(), bestMelee = melee.Values.DefaultIfEmpty(0).Max();
            total = 3;
            met = (bestHeal >= needed ? 1 : 0) + (bestRanged >= needed ? 1 : 0) + (bestMelee >= needed ? 1 : 0);
            result = met == 3;
            Plugin.Log.LogInfo($"GHRule Heal8Ranged8Melee8 card 0 Cragheart mastery: best round heal {bestHeal}, best ranged attack {bestRanged}, best melee attack {bestMelee} (need {needed} each)");
        }

        // ---- obstacles spawned with a character as owner, by round (current scenario only)

        // The spawn notes are also saved with the scenario (a reload keeps them): GHSavedCounts slots 1-4 on the 2nd edition
        // Cragheart hold the rounds 1-36 as bits (9 rounds per slot, a slot holds up to 999); later rounds stay in memory.
        private const int RoundsPerSlot = 9, SpawnSlots = 4;

        private static void SaveSpawnRound(CPlayerActor p, int round)
        {
            if (p?.Class?.ID != "Cragheart2ID" || round < 1 || round > RoundsPerSlot * SpawnSlots) return;
            int slot = 1 + (round - 1) / RoundsPerSlot, bit = 1 << ((round - 1) % RoundsPerSlot);
            int v = GHSavedCounts.Get(p, slot);
            if ((v & bit) == 0) GHSavedCounts.Set(p, slot, v | bit);
        }

        private static IEnumerable<int> SavedSpawnRounds(CPlayerActor p)
        {
            if (p?.Class?.ID != "Cragheart2ID") yield break;
            for (int slot = 1; slot <= SpawnSlots; slot++)
            {
                int v = GHSavedCounts.Get(p, slot);
                for (int b = 0; b < RoundsPerSlot; b++)
                    if ((v & (1 << b)) != 0) yield return (slot - 1) * RoundsPerSlot + b + 1;
            }
        }

        private static readonly Dictionary<string, HashSet<int>> Spawned = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private static ScenarioState _spawnScenario;

        [HarmonyPatch(typeof(CTile), nameof(CTile.SpawnProp))]
        [HarmonyPostfix]
        private static void ObstacleSpawned(CObjectProp prop)
        {
            try
            {
                if (!(prop is CObjectObstacle) || string.IsNullOrEmpty(prop.OwnerGUID)) return;
                ScenarioState scenario = ScenarioManager.CurrentScenarioState;
                if (scenario == null) return;
                if (!ReferenceEquals(scenario, _spawnScenario)) { Spawned.Clear(); _spawnScenario = scenario; }
                if (!Spawned.TryGetValue(prop.OwnerGUID, out HashSet<int> rounds)) Spawned[prop.OwnerGUID] = rounds = new HashSet<int>();
                rounds.Add(scenario.RoundNumber);
                SaveSpawnRound(GHSavedCounts.Player(prop.OwnerGUID), scenario.RoundNumber);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("cragheart2 spawn")) Plugin.Log.LogError("Cragheart (2e) masteries: obstacle spawn not noted: " + e); }
        }
    }
}
