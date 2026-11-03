using System;
using System.Collections.Generic;
using System.Linq;
using FHRL;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;
using ScenarioState = ScenarioRuleLibrary.ScenarioState;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Berserker's and Bruiser's masteries (2e card-accuracy pass, task rest-a), answered like
    /// GHMasteries.cs: the mastery file tags its target with a marker in EnemyFamilies, and this prefix answers only its own
    /// markers (anything else passes on to GHMasteries / the game).
    ///   FrosthavenMods_KillsByRetaliate      (Berserker) Value enemies killed by your retaliate (GHRetaliate2e reports
    ///                                        them), within one scenario
    ///   FrosthavenMods_HalfHpEveryTurn       (Berserker) every turn ended at half your maximum hit points or lower, and
    ///                                        not exhausted
    ///   FrosthavenMods_KillPushedThisRound   (Bruiser) Value different scenarios in each of which you killed an enemy you
    ///                                        pushed or pulled that round; the scenarios already done are kept in synced
    ///                                        campaign state ("FHMODS:PushKill:&lt;character id&gt;:&lt;scenario&gt;" in
    ///                                        CampaignState.LootedChestTreasureTableIDs, as the Boneshaper career count)
    ///   FrosthavenMods_DamageStreakRound     (Bruiser) Value rounds in a row with Amount damage you caused to enemies in
    ///                                        each whole round (retaliate and other turns included) and Targets different
    ///                                        ability cards played over them
    /// The in-scenario counts the statistics don't hold are kept for the scenario being played: the retaliate kills and the
    /// push-kill flag are also saved with the scenario (GHSavedCounts hidden resources, a reload keeps them), and so are
    /// the forced-moved enemies (this round's mark on the enemy) and the last three rounds' damage tally (on the Bruiser;
    /// the statistics' damage records back older rounds). Logs "GHRule &lt;Tag&gt; card 0 ..." when a check answers.
    /// </summary>
    [HarmonyPatch]
    internal static class GHMasteriesRestA
    {
        internal const string KillsByRetaliateTag = "FrosthavenMods_KillsByRetaliate";
        internal const string HalfHpTag = "FrosthavenMods_HalfHpEveryTurn";
        internal const string KillPushedTag = "FrosthavenMods_KillPushedThisRound";
        internal const string DamageRoundTag = "FrosthavenMods_DamageStreakRound";
        private const string PushKillMarker = "FHMODS:PushKill:";

        // saved counts (GHSavedCounts slots on the character): Berserker slot 1 = kills by retaliate this scenario;
        // Bruiser slot 2 = "killed an enemy it pushed or pulled that round" in this scenario (slot 1 is Defensive Tactics)
        internal const int SlotRetaliateKills = 1, SlotPushKill = 2;
        // Bruiser slots 3-5 = round number + 1 of the last three rounds' damage tally (index round % 3), slots 6-8 = that
        // round's damage to enemies; on the forced-moved ENEMY: slot 10 = round number + 1 of the round the Bruiser pushed
        // or pulled it (an enemy holds no other GHSavedCounts slot). A reload keeps both.
        internal const int SlotStreakRound = 3, SlotStreakDamage = 6, SlotPushedOnTarget = 10;

        private static int SavedRoundDamage(CActor p, int round)
        {
            int i = ((round % 3) + 3) % 3;
            return GHSavedCounts.Get(p, SlotStreakRound + i) == round + 1 ? GHSavedCounts.Get(p, SlotStreakDamage + i) : 0;
        }

        private static void SaveRoundDamage(CActor p, int round, int damage)
        {
            int i = ((round % 3) + 3) % 3;
            GHSavedCounts.Set(p, SlotStreakRound + i, round + 1);
            GHSavedCounts.Set(p, SlotStreakDamage + i, damage);
        }

        private static ScenarioState _scenario;
        private static readonly Dictionary<string, int> RetaliateKills = new Dictionary<string, int>();
        private static readonly HashSet<string> Pushed = new HashSet<string>();        // "pusher/target/round"
        private static readonly HashSet<string> PushKillScenario = new HashSet<string>(); // pusher guids with a qualifying kill
        private static readonly Dictionary<string, Dictionary<int, int>> RoundDamage = new Dictionary<string, Dictionary<int, int>>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void Fresh()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            if (ReferenceEquals(s, _scenario)) return;
            _scenario = s;
            RetaliateKills.Clear();
            Pushed.Clear();
            PushKillScenario.Clear();
            RoundDamage.Clear();
        }

        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;
        private static string G(CActor a) => a?.ActorGuid ?? "";

        // ------------------------------------------------------------------ notes from the rules

        internal static void NoteRetaliateKill(CActor me, CActor victim)
        {
            Fresh();
            if (!(me is CPlayerActor) || victim == null || !CActor.AreActorsEnemies(victim.Type, me.Type, victim, me)) return;
            int saved = GHSavedCounts.Add(me, SlotRetaliateKills);
            RetaliateKills[G(me)] = Math.Max(saved, (RetaliateKills.TryGetValue(G(me), out int n) ? n : 0) + 1);
            Plugin.Log.LogInfo($"GHRule KillsByRetaliate card 0 {GHPlague.NameOf(me)}'s retaliate killed {GHPlague.NameOf(victim)} ({RetaliateKills[G(me)]} this scenario)");
        }

        internal static void NoteForced(CActor me, CActor target)
        {
            Fresh();
            if (!(me is CPlayerActor) || target == null) return;
            Pushed.Add($"{G(me)}/{G(target)}/{Round}");
            GHSavedCounts.Set(target, SlotPushedOnTarget, Round + 1);
        }

        internal static void NoteAttack(CActor me) { }

        // ------------------------------------------------------------------ hooks

        [HarmonyPatch(typeof(GameState), nameof(GameState.KillActor))]
        [HarmonyPostfix]
        private static void Killed(CActor targetingActor, CActor actor, bool __result)
        {
            try
            {
                if (!__result || !(targetingActor is CPlayerActor me) || actor == null) return;
                Fresh();
                // the memory note, or after a reload the saved round on the enemy (only a Bruiser sets it)
                bool pushed = Pushed.Contains($"{G(me)}/{G(actor)}/{Round}")
                    || (me.Class?.ID == GHBruiser2e.ClassId && GHSavedCounts.Get(actor, SlotPushedOnTarget) == Round + 1);
                if (!pushed) return;
                GHSavedCounts.Set(me, SlotPushKill, 1);
                if (PushKillScenario.Add(G(me)))
                    Plugin.Log.LogInfo($"GHRule KillPushedThisRound card 0 {GHPlague.NameOf(me)} killed {GHPlague.NameOf(actor)}, which it pushed or pulled this round (this scenario counts)");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra kill pushed")) Plugin.Log.LogError("GHMasteriesRestA (kill): " + e); }
        }

        private struct Hp { public bool On; public int Health; public string Source; }

        [HarmonyPatch]
        private static class DamageTally
        {
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(GameState))
                .First(m => m.Name == nameof(GameState.ActorBeenDamaged) && m.GetParameters().Any(p => p.Name == "triggeredPreventDamage"));

            private static void Prefix(CActor actor, CActor damageSource, out Hp __state)
            {
                __state = default;
                try
                {
                    if (actor == null || !(damageSource is CPlayerActor p) || p.Class?.ID != GHBruiser2e.ClassId) return;
                    if (!CActor.AreActorsEnemies(actor.Type, p.Type, actor, p)) return;
                    __state = new Hp { On = true, Health = actor.Health, Source = G(p) };
                }
                catch (Exception e)
                {
                    __state = default;
                    if (GHClasses.Reported.Add("ra damage tally pre")) Plugin.Log.LogError("GHMasteriesRestA (damage, before): " + e);
                }
            }

            private static void Postfix(CActor actor, Hp __state)
            {
                if (!__state.On) return;
                try
                {
                    int dealt = Math.Max(0, __state.Health - Math.Max(0, actor.Health));
                    if (dealt <= 0) return;
                    Fresh();
                    if (!RoundDamage.TryGetValue(__state.Source, out Dictionary<int, int> d)) RoundDamage[__state.Source] = d = new Dictionary<int, int>();
                    CPlayerActor p = GHSavedCounts.Player(__state.Source);
                    // after a reload the memory tally is empty: the saved one carries on
                    int before = Math.Max(d.TryGetValue(Round, out int n) ? n : 0, SavedRoundDamage(p, Round));
                    d[Round] = before + dealt;
                    if (p != null) SaveRoundDamage(p, Round, d[Round]);
                }
                catch (Exception e) { if (GHClasses.Reported.Add("ra damage tally")) Plugin.Log.LogError("GHMasteriesRestA (damage): " + e); }
            }
        }

        /// <summary>The scenarios already counted for the Bruiser's first mastery are kept when a scenario ends (each peer
        /// gets the same Everybody command, so the synced campaign state stays the same everywhere).</summary>
        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPrefix]
        private static void KeepPushKill(ScenarioController __instance, ScenarioResult result)
        {
            try
            {
                if (result == null || result.isRestarting) return;
                Fresh();
                // after a reload the memory set is empty: the hidden saved flag on the Bruiser still says so
                foreach (CPlayerActor p in (ScenarioManager.Scenario?.PlayerActors ?? new List<CPlayerActor>()).Concat(ScenarioManager.Scenario?.ExhaustedPlayers ?? new List<CPlayerActor>()))
                    if (p?.Class?.ID == GHBruiser2e.ClassId && GHSavedCounts.Get(p, SlotPushKill) > 0) PushKillScenario.Add(G(p));
                if (PushKillScenario.Count == 0) return;
                CampaignState state = __instance.CampaignController.State;
                string scenario = ScenarioKey();
                foreach (ScenarioResult.CharacterResult r in result.Characters)
                {
                    if (r.Character?.ID.ID != GHBruiser2e.ClassId) continue;
                    CharacterState c = state.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null) continue;
                    string marker = PushKillMarker + c.ID.ID + ":" + scenario;
                    if (state.LootedChestTreasureTableIDs.Contains(marker)) continue;
                    state.LootedChestTreasureTableIDs.Add(marker);
                    Plugin.Log.LogInfo($"GHRule KillPushedThisRound card 0 Bruiser mastery: scenario {scenario} recorded ({StoredScenarios(state, null).Count} so far)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra push-kill keep")) Plugin.Log.LogError("GHMasteriesRestA: push-kill scenario not recorded: " + e); }
        }

        private static string ScenarioKey()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            return s?.ID ?? "?";
        }

        private static HashSet<string> StoredScenarios(CampaignState state, string scenarioExcluded)
        {
            var set = new HashSet<string>();
            if (state?.LootedChestTreasureTableIDs == null) return set;
            CharacterState c = state.PartyCharacters?.FirstOrDefault(p => p?.ClassDef?.ID.ID == GHBruiser2e.ClassId);
            if (c == null) return set;
            string prefix = PushKillMarker + c.ID.ID + ":";
            foreach (string m in state.LootedChestTreasureTableIDs.Where(x => x != null && x.StartsWith(prefix, StringComparison.Ordinal)))
                set.Add(m.Substring(prefix.Length));
            return set;
        }

        // ------------------------------------------------------------------ the answers

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Answer(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null) return true;
            bool kills = tags.Contains(KillsByRetaliateTag), half = tags.Contains(HalfHpTag), pushed = tags.Contains(KillPushedTag), streak = tags.Contains(DamageRoundTag);
            if (!kills && !half && !pushed && !streak) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            try
            {
                Fresh();
                string self = SelfGuid(stats);
                int need = Math.Max(1, unlockConditionTarget.Value);
                if (kills)
                {
                    int n = Math.Max(!string.IsNullOrEmpty(self) && RetaliateKills.TryGetValue(self, out int k) ? k : 0, GHSavedCounts.Get(self, SlotRetaliateKills));
                    totalConditions = need; conditionsMet = Math.Min(n, need); __result = n >= need;
                    Log(KillsByRetaliateTag, $"Berserker mastery: {n} of {need} kills by retaliate this scenario");
                }
                else if (half) HalfHp(stats, self, ref totalConditions, ref conditionsMet, ref __result);
                else if (pushed)
                {
                    HashSet<string> done = StoredScenarios(GHClassesRule.LiveState, null);
                    bool here = !string.IsNullOrEmpty(self) && (PushKillScenario.Contains(self) || GHSavedCounts.Get(self, SlotPushKill) > 0);
                    int n = done.Count + (here && !done.Contains(ScenarioKey()) ? 1 : 0);
                    totalConditions = need; conditionsMet = Math.Min(n, need); __result = n >= need;
                    Log(KillPushedTag, $"Bruiser mastery: {n} of {need} scenarios with a kill of an enemy pushed or pulled that round");
                }
                else Streak(unlockConditionTarget, stats, self, ref totalConditions, ref conditionsMet, ref __result);
            }
            catch (Exception e)
            {
                if (GHClasses.Reported.Add("ra mastery")) Plugin.Log.LogError("GHMasteriesRestA: " + e);
                totalConditions = 1; conditionsMet = 0; __result = false;
            }
            return false;
        }

        private static void Log(string tag, string text)
        {
            string key = tag + text;
            if (!Logged.Add(key)) return;
            if (Logged.Count > 200) { Logged.Clear(); Logged.Add(key); }
            Plugin.Log.LogInfo($"GHRule {tag.Replace("FrosthavenMods_", "")} card 0 {text}");
        }

        private static string SelfGuid(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(g => !string.IsNullOrEmpty(g));

        /// <summary>Every turn the character took ended at half its maximum hit points (rounded up) or lower, and it is
        /// not exhausted.</summary>
        private static void HalfHp(List<CPlayerStats> stats, string self, ref int total, ref int met, ref bool result)
        {
            CPlayerActor live = ScenarioManager.Scenario?.AllPlayers?.FirstOrDefault(p => p != null && p.ActorGuid == self);
            bool exhausted = ScenarioManager.Scenario?.ExhaustedPlayers?.Any(p => p != null && p.ActorGuid == self) == true;
            List<int> rounds = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Where(x => x != null && x.Round > 0)
                .Select(x => x.Round).Distinct().OrderBy(r => r).ToList();
            var ends = stats.SelectMany(s => s.EndTurn ?? Enumerable.Empty<CPlayerStatsEndTurn>()).Where(x => x?.ActionData != null).ToList();
            int ok = rounds.Count(r => ends.Any(x => x.Round == r && x.ActionData.MainActorHealth <= Half(x.ActionData.MainActorMaxHealth, live)));
            total = Math.Max(1, rounds.Count) + 1;
            met = ok + (exhausted ? 0 : 1);
            result = rounds.Count > 0 && ok == rounds.Count && !exhausted;
            Log(HalfHpTag, $"Berserker mastery: {ok} of {rounds.Count} turns ended at half hit points or lower{(exhausted ? ", exhausted" : "")}");
        }

        private static int Half(int maxHealth, CPlayerActor live)
        {
            int max = maxHealth > 0 ? maxHealth : (live?.MaxHealth ?? 0);
            return (int)Math.Ceiling(max / 2.0);
        }

        /// <summary>Value rounds in a row, each with Amount damage the character caused to enemies over the whole round
        /// (the statistics' damage records or this plugin's tally of every damage it caused, whichever is larger) and
        /// Targets different ability cards played over them.</summary>
        private static void Streak(CUnlockConditionTarget t, List<CPlayerStats> stats, string self, ref int total, ref int met, ref bool result)
        {
            int rounds = Math.Max(1, t.Value), damage = Math.Max(0, t.Amount), cards = Math.Max(0, t.Targets);
            var dealt = new Dictionary<int, int>();
            var played = new Dictionary<int, HashSet<int>>();
            foreach (CPlayerStats s in stats)
            {
                foreach (CPlayerStatsDamage d in s.DamageDealt ?? Enumerable.Empty<CPlayerStatsDamage>())
                    if (d != null && (d.ActorWhoReceivedDamageType == CActor.EType.Enemy.ToString() || d.ActorWhoReceivedDamageType == CActor.EType.Enemy2.ToString()))
                        dealt[d.Round] = (dealt.TryGetValue(d.Round, out int sum) ? sum : 0) + d.FinalDamageAmount;
                foreach (CPlayerStatsAbilities a in s.Abilities ?? Enumerable.Empty<CPlayerStatsAbilities>())
                {
                    if (a == null || a.ActionData == null || a.ActionData.CardType != CBaseCard.ECardType.CharacterAbility || a.ActionData.CardID == int.MaxValue) continue;
                    if (!played.TryGetValue(a.Round, out HashSet<int> set)) played[a.Round] = set = new HashSet<int>();
                    set.Add(a.ActionData.CardID);
                }
            }
            if (!string.IsNullOrEmpty(self) && RoundDamage.TryGetValue(self, out Dictionary<int, int> mine))
                foreach (KeyValuePair<int, int> kv in mine) dealt[kv.Key] = Math.Max(dealt.TryGetValue(kv.Key, out int v) ? v : 0, kv.Value);
            // the last three rounds' tally saved on the Bruiser (a reload emptied the memory one)
            CPlayerActor me = GHSavedCounts.Player(self);
            if (me != null)
                for (int i = 0; i < 3; i++)
                {
                    int r = GHSavedCounts.Get(me, SlotStreakRound + i) - 1;
                    if (r > 0) dealt[r] = Math.Max(dealt.TryGetValue(r, out int v) ? v : 0, GHSavedCounts.Get(me, SlotStreakDamage + i));
                }
            int last = dealt.Keys.Concat(played.Keys).DefaultIfEmpty(0).Max();
            int run = 0, best = 0;
            total = rounds; met = 0; result = false;
            for (int r = 1; r <= last; r++)
            {
                run = dealt.TryGetValue(r, out int sum) && sum >= damage ? run + 1 : 0;
                best = Math.Max(best, run);
                if (run < rounds) continue;
                var distinct = new HashSet<int>();
                for (int k = r - rounds + 1; k <= r; k++) if (played.TryGetValue(k, out HashSet<int> set)) distinct.UnionWith(set);
                if (distinct.Count >= cards)
                {
                    met = rounds; result = true;
                    Log(DamageRoundTag, $"Bruiser mastery: rounds {r - rounds + 1}-{r} each with {damage}+ damage to enemies (whole rounds) and {distinct.Count} different cards");
                    return;
                }
            }
            met = Math.Min(best, rounds - 1);
            Log(DamageRoundTag, $"Bruiser mastery: best run {best} round(s) with {damage}+ damage");
        }
    }
}
