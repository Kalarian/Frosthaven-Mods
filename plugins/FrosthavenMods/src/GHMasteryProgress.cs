using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapRuleLibrary.MapState;
using MapRuleLibrary.Party;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;
using ScenarioState = ScenarioRuleLibrary.ScenarioState;

namespace FrosthavenMods
{
    /// <summary>
    /// Mastery progress lines during play, for the 2nd edition pack's masteries (2e card-accuracy pass, task mastery-logs:
    /// the harness's masterycheck proves a mastery by its progress lines). The game re-checks the active battle goals and
    /// masteries after every batch of scenario events (StatsDataStorage.ScrapeEventLogAndCheckBattleGoals ->
    /// CMapScenarioState.CheckBattleGoals(useCurrentScenarioStats: true)), but a mastery marked CheckAtEndOfScenario is
    /// only judged at the scenario's end, so a bench that closes the game earlier never saw it. After each such in-play
    /// check this postfix asks each 2nd edition mastery's targets once more, as a dry run (CUnlockCondition.CheckTarget
    /// with the current scenario's statistics; the answer is not stored in the mastery's state, so the rules and the
    /// game's own judgement are unchanged), and logs
    ///   GHRule Mastery&lt;Tag&gt; card 0 &lt;Class&gt; mastery &lt;n&gt; (&lt;name&gt;): &lt;met&gt;/&lt;total&gt;   (or "met (x/y)")
    /// whenever that progress changes (Tag = the target's FrosthavenMods_ marker without its prefix). Only masteries of the
    /// 2nd edition classes are asked; without the pack none exists, so this does nothing.
    /// <see cref="Once"/> is the shared "log when it changes" helper the mastery answers use for their own detail lines
    /// (they run on every in-play check now, so an unconditional line would repeat after every event batch).
    /// </summary>
    [HarmonyPatch]
    internal static class GHMasteryProgress
    {
        /// <summary>The 2nd edition pack's classes (tools/gh2e ORDER): their mastery IDs are "&lt;Class&gt;_MASTERY_&lt;n&gt;".</summary>
        private static readonly HashSet<string> Classes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Bruiser", "Cragheart2", "Mindthief2", "SilentKnife", "Spellweaver2", "Tinkerer2", "Berserker2", "Doomstalker2",
            "Elementalist2", "Nightshroud2", "Plagueherald2", "Quartermaster2", "Sawbones2", "Sunkeeper2", "Soothsinger2",
            "Soultether", "Wildfury",
        };

        private static ScenarioState _scenario;
        private static readonly Dictionary<string, string> Last = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly object Gate = new object();

        // the dry run's answer, caught after every prefix (the mastery answers return false and set these)
        [ThreadStatic] private static bool _probing;
        [ThreadStatic] private static bool _caught;
        [ThreadStatic] private static int _total, _met;

        /// <summary>A new scenario (a reload too) starts the change records afresh; no scenario (the results screen) keeps them.</summary>
        private static void Fresh()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            if (s == null || ReferenceEquals(s, _scenario)) return;
            _scenario = s;
            Last.Clear();
            Logged.Clear();
        }

        /// <summary>Logs "GHRule {rule} card 0 {detail}" once per scenario for each different detail (a mastery answer's
        /// progress line: the detail carries the counts, so a new count logs a new line).</summary>
        internal static void Once(string rule, string detail)
        {
            if (IsNew(rule, detail)) Plugin.Log.LogInfo($"GHRule {rule} card 0 {detail}");
        }

        /// <summary>True the first time this scenario that this rule has this detail (the caller then logs it).</summary>
        internal static bool IsNew(string rule, string detail)
        {
            lock (Gate)
            {
                Fresh();
                string key = rule + "\n" + detail;
                if (!Logged.Add(key)) return false;
                if (Logged.Count > 2000) { Logged.Clear(); Logged.Add(key); }
                return true;
            }
        }

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPostfix]
        private static void Caught(ref int totalConditions, ref int conditionsMet)
        {
            if (!_probing) return;
            _caught = true;
            _total = totalConditions;
            _met = conditionsMet;
        }

        [HarmonyPatch(typeof(CMapScenarioState), nameof(CMapScenarioState.CheckBattleGoals))]
        [HarmonyPostfix]
        private static void AfterCheck(CMapScenarioState __instance, bool useCurrentScenarioStats)
        {
            if (!useCurrentScenarioStats || ScenarioManager.CurrentScenarioState?.Stats == null) return;
            List<Tuple<string, CBattleGoalState>> masteries = __instance?.MasteryBattleGoalStates;
            if (masteries == null || masteries.Count == 0) return;
            foreach (Tuple<string, CBattleGoalState> entry in masteries.ToList())
            {
                CBattleGoalState goal = entry?.Item2;
                string id = goal?.ID;
                if (string.IsNullOrEmpty(id)) continue;
                int cut = id.IndexOf("_MASTERY_", StringComparison.OrdinalIgnoreCase);
                if (cut <= 0 || !Classes.Contains(id.Substring(0, cut))) continue;
                try { Probe(goal, id.Substring(0, cut), id.Substring(cut + "_MASTERY_".Length)); }
                catch (Exception e) { if (GHClasses.Reported.Add("mastery progress " + id)) Plugin.Log.LogError($"GHMasteryProgress ({id}): {e}"); }
                finally { _probing = false; }
            }
        }

        private static void Probe(CBattleGoalState goal, string cls, string number)
        {
            CUnlockConditionState state = goal.BattleGoalConditionState;
            CUnlockCondition condition = state?.UnlockCondition;
            List<CUnlockConditionState.CUnlockConditionTargetState> targets = state?.UnlockConditionTargetStates;
            if (condition == null || targets == null || targets.Count == 0) return;
            int met = 0, total = 0;
            bool all = true;
            string tag = null;
            foreach (CUnlockConditionState.CUnlockConditionTargetState t in targets)
            {
                CUnlockConditionTarget target = t?.UnlockConditionTarget;
                if (target == null) continue;
                tag = tag ?? target.TargetEnemyFamilies?.FirstOrDefault(x => x != null && x.StartsWith("FrosthavenMods_", StringComparison.Ordinal));
                _probing = true;
                _caught = false;
                bool ok = condition.CheckTarget(target, t.CompletedValue, out int targetsMet, out int overrideTotal, state.OverrideCharacterID,
                                                state.OverrideCharacterName, useCurrentScenarioStats: true);
                _probing = false;
                int tt = _caught ? _total : (overrideTotal >= 0 ? overrideTotal : Math.Max(1, target.Value));
                int tm = _caught ? _met : targetsMet;
                total += Math.Max(0, tt);
                met += Math.Max(0, Math.Min(tm, Math.Max(0, tt)));
                all &= ok;
            }
            string rule = "Mastery" + (tag != null ? tag.Substring("FrosthavenMods_".Length) : cls + number);
            string progress = all ? $"met ({met}/{total})" : $"{met}/{total}";
            string key = cls + "/" + number;
            lock (Gate)
            {
                Fresh();
                if (Last.TryGetValue(key, out string before) && before == progress) return;
                Last[key] = progress;
            }
            Plugin.Log.LogInfo($"GHRule {rule} card 0 {cls} mastery {number} ({Name(goal)}): {progress}");
        }

        private static string Name(CBattleGoalState goal)
        {
            string id = goal.ID;
            lock (Gate) { if (Names.TryGetValue(id, out string known)) return known; }
            string name = id;
            try
            {
                string key = goal.BattleGoal?.LocalisedName;
                // the pack's own text (a plain dictionary read: this runs on the rule library's worker thread, where the
                // game's localisation lookup is not safe to call)
                if (!string.IsNullOrEmpty(key))
                {
                    string t = GHClasses.StringsStartingWith(key).FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                    if (!string.IsNullOrEmpty(t)) name = t;
                }
            }
            catch { }
            lock (Gate) Names[id] = name;
            return name;
        }
    }
}
