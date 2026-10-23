using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// Part of the Battle stats rule: each finished scenario's full battle record, as JSON, for the companion app's Stats
    /// tab (FrosthavenMods\stats\runs\&lt;campaign&gt;\*.json), plus a live snapshot during a scenario
    /// (FrosthavenMods\stats\live-&lt;campaign&gt;.json).
    ///
    /// Only real play is kept. The source is the game's own per-scenario records (ScenarioState.Stats), which are saved
    /// with the scenario: loading a save (after a crash, or by choice) rewinds them to that save, and a restart starts
    /// them over. Nothing of ours is kept alongside, so there is nothing to rewind.
    /// - Staged when the results screen opens (PartyAdventureData.ScrapeStats, the game's final scrape) for a Win, Lose
    ///   or Resign; a Restart is never staged.
    /// - Committed (written) only when the game commits the outcome to the campaign (SaveData.EndScenario), or when a
    ///   lost scenario is retried from its results screen (the loss happened; the retry is a new attempt). Quitting or
    ///   crashing on the results screen commits nothing: the game hasn't recorded that outcome either.
    /// - The live snapshot is rewritten in full at each of the game's autosaves in a scenario, so it always matches the
    ///   save; it is removed when the scenario's record is committed and whenever the game autosaves outside a scenario.
    /// A record is written once (its file name carries a hash of the scenario's identity and its records).
    /// </summary>
    [HarmonyPatch]
    internal static class StatsExport
    {
        private const int Format = 1;
        private static bool On => ModRules.IsOn(ModRules.BattleStats);

        private static JObject _staged;
        private static string _stagedOutcome;
        private static ScenarioState _stagedState;

        private static string StatsDir => Path.Combine(Paths.GameRootPath, "FrosthavenMods", "stats");

        // ---- staging, committing, the live snapshot

        [HarmonyPatch(typeof(PartyAdventureData), nameof(PartyAdventureData.ScrapeStats))]
        [HarmonyPostfix]
        private static void Stage(EResult result)
        {
            _staged = null;
            if (!On || (result != EResult.Win && result != EResult.Lose && result != EResult.Resign)) return;
            try
            {
                _staged = Record(result.ToString(), inProgress: false);
                _stagedOutcome = result.ToString();
                _stagedState = ScenarioManager.CurrentScenarioState;
                Plugin.Log.LogInfo($"Battle stats: {_stagedOutcome} staged; written once the game commits it");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Battle stats: scenario record not staged: " + e);
            }
        }

        [HarmonyPatch(typeof(SaveData), nameof(SaveData.EndScenario))]
        [HarmonyPrefix]
        private static void CommitOnEnd(EResult result)
        {
            if (_staged == null) return;
            if (result.ToString() == _stagedOutcome) Commit("the game recorded the outcome");
            else Plugin.Log.LogInfo($"Battle stats: staged {_stagedOutcome} dropped (the scenario ended as {result})");
            _staged = null;
        }

        [HarmonyPatch(typeof(UIManager), nameof(UIManager.RestartScenarioFromInitial))]
        [HarmonyPatch(typeof(UIManager), nameof(UIManager.RegenerateAndRestartScenarioKeepGoldAndXP))]
        [HarmonyPrefix]
        private static void CommitOnRetry()
        {
            if (_staged == null) return;
            if (_stagedOutcome == "Lose" && ReferenceEquals(ScenarioManager.CurrentScenarioState, _stagedState))
                Commit("a lost scenario was retried");
            _staged = null;
        }

        private static void Commit(string why)
        {
            try
            {
                string campaign = (string)_staged["campaign"] ?? "campaign";
                string dir = Path.Combine(StatsDir, "runs", Safe(campaign));
                Directory.CreateDirectory(dir);
                string body = _staged.ToString(Formatting.None);
                string hash = Hash((string)_staged["scenario"]?["id"] + "|" + (string)_staged["scenario"]?["seed"] + "|" + body.Length + "|" +
                                   _staged["actors"]?.ToString(Formatting.None));
                if (Directory.GetFiles(dir, "*-" + hash + ".json").Any())
                {
                    Plugin.Log.LogInfo("Battle stats: scenario record already written");
                    return;
                }
                string name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Safe((string)_staged["scenario"]?["id"] ?? "scenario")}-{hash}.json";
                File.WriteAllText(Path.Combine(dir, name), body, new UTF8Encoding(false));
                DeleteLive(campaign);
                Plugin.Log.LogInfo($"Battle stats: {_stagedOutcome} recorded ({why}): {name}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Battle stats: scenario record not written: " + e);
            }
        }

        [HarmonyPatch(typeof(SaveData), nameof(SaveData.AutoSaveCurrentAdventureData))]
        [HarmonyPostfix]
        private static void Live()
        {
            if (!On) return;
            try
            {
                string campaign = SaveData.Instance?.Global?.CurrentAdventureData?.PartyName;
                if (string.IsNullOrEmpty(campaign)) return;
                if (SaveData.Instance.Global.CurrentGameState != EGameState.Scenario || ScenarioManager.CurrentScenarioState?.Stats == null)
                {
                    DeleteLive(campaign);
                    return;
                }
                Directory.CreateDirectory(StatsDir);
                File.WriteAllText(LivePath(campaign), Record("InProgress", inProgress: true).ToString(Formatting.None), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Battle stats: live snapshot not written: " + e.Message);
            }
        }

        private static string LivePath(string campaign) => Path.Combine(StatsDir, $"live-{Safe(campaign)}.json");

        private static void DeleteLive(string campaign)
        {
            try { File.Delete(LivePath(campaign)); } catch { }
        }

        // ---- the record

        private static JObject Record(string outcome, bool inProgress)
        {
            ScenarioState state = ScenarioManager.CurrentScenarioState;
            StatsState stats = state?.Stats ?? throw new InvalidOperationException("no scenario stats");
            PartyAdventureData adventure = SaveData.Instance.Global.CurrentAdventureData;
            var mapState = adventure?.AdventureMapState;
            var rec = new JObject
            {
                ["format"] = Format,
                ["mod"] = Plugin.Version,
                ["written"] = DateTime.Now.ToString("s"),
                ["inProgress"] = inProgress,
                ["outcome"] = outcome,
                ["campaign"] = adventure?.PartyName,
                ["rules"] = ModRules.Describe(ModRules.Current),
                ["scenario"] = new JObject
                {
                    ["id"] = mapState?.CurrentMapScenarioState?.ID ?? state.ID,
                    ["level"] = state.Level,
                    ["seed"] = Try(() => state.Seed.ToString()),
                    ["rounds"] = state.RoundNumber,
                },
            };
            Try(() => { rec["week"] = adventure.FrosthavenCampaign.Outpost.Calendar.Week; return 0; });

            var party = new JArray();
            foreach (CPlayerActor p in ScenarioManager.Scenario?.AllPlayers ?? new List<CPlayerActor>())
            {
                string id = p.CharacterClass?.CharacterID;
                var c = new JObject { ["class"] = id, ["name"] = p.CharacterName, ["maxHp"] = p.MaxHealth, ["xp"] = p.XP,
                                      ["exhausted"] = p.IsDeadForObjectives };
                Try(() =>
                {
                    var map = mapState?.MapParty?.SelectedCharacters?.FirstOrDefault(m => m.CharacterID == id);
                    if (map != null) c["level"] = map.Level;
                    return 0;
                });
                if (p.CharacterClass != null)
                    c["deck"] = Deck(p.CharacterClass.AttackModifierCards, p.CharacterClass.DiscardedAttackModifierCards);
                party.Add(c);
            }
            rec["party"] = party;
            var monsterDeck = MonsterClassManager.EnemyMonsterAttackModifierDeck;
            if (monsterDeck != null) rec["monsterDeck"] = Deck(monsterDeck.AttackModifierCards, monsterDeck.DiscardedAttackModifierCards);

            var actors = new JObject();
            foreach (KeyValuePair<string, CPlayerStatsScenario> kv in stats.m_PlayerStats ?? new Dictionary<string, CPlayerStatsScenario>())
                actors[kv.Key] = Events(kv.Value);
            if (stats.m_HeroSummonStats != null) actors["_summons"] = Events(stats.m_HeroSummonStats);
            if (stats.m_MonsterStats != null) actors["_monsters"] = Events(stats.m_MonsterStats);
            rec["actors"] = actors;
            return rec;
        }

        /// <summary>A modifier deck, bless and curse cards left out (they come and go during a scenario), as the cards'
        /// modifiers ("+1", "-2", "*0", "*2", rolling ones marked "R"): the companion's luck charts compare each draw
        /// with this deck's average.</summary>
        private static JArray Deck(List<AttackModifierYMLData> pile, List<AttackModifierYMLData> discard)
        {
            var cards = new JArray();
            foreach (AttackModifierYMLData c in (pile ?? new List<AttackModifierYMLData>()).Concat(discard ?? new List<AttackModifierYMLData>()))
            {
                if (c == null || c.IsBless || c.IsCurse) continue;
                cards.Add((c.Rolling ? "R" : "") + (c.MathModifier ?? c.Name));
            }
            return cards;
        }

        /// <summary>Every list of one character's (or the summons', or the monsters') records, each record flattened to its
        /// plain fields: the round and event order, the acting and target actors, the card and ability, amounts, flags.</summary>
        private static JObject Events(CPlayerStatsScenario s)
        {
            var o = new JObject
            {
                ["survived"] = s.PlayerSurvivedScenario,
                ["rounds"] = s.RoundsPlayed,
            };
            foreach (PropertyInfo p in typeof(CPlayerStats).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(IList).IsAssignableFrom(p.PropertyType)) continue;
                if (!(p.GetValue(s) is IList list) || list.Count == 0) continue;
                var arr = new JArray();
                foreach (object item in list)
                    if (item != null) arr.Add(Flatten(item, 0));
                o[p.Name] = arr;
            }
            return o;
        }

        private static JObject Flatten(object item, int depth)
        {
            var j = new JObject();
            foreach (PropertyInfo p in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = p.GetValue(item); } catch { continue; }
                JToken t = Value(v, depth);
                if (t == null) continue;
                // the shared base data (round, event order) and action data (actors, card, ability) are lifted to the top
                if (t is JObject inner && (p.Name == "BaseData" || p.Name == "ActionData"))
                    foreach (JProperty ip in inner.Properties()) j[ip.Name] = ip.Value;
                else j[p.Name] = t;
            }
            return j;
        }

        private static JToken Value(object v, int depth)
        {
            switch (v)
            {
                case null: return null;
                case string s: return s.Length == 0 ? null : s;
                case bool b: return b;
                case int i: return i;
                case long l: return l;
                case float f: return f;
                case double d: return d;
                case uint u: return u;
                case Enum e: return e.ToString();
                case DateTime dt: return dt.ToString("s");
            }
            if (v is IEnumerable seq)
            {
                var arr = new JArray();
                foreach (object x in seq)
                {
                    JToken t = Value(x, depth + 1);
                    if (t != null) arr.Add(t);
                }
                return arr.Count == 0 ? null : arr;
            }
            Type type = v.GetType();
            // nested records of the game's stats (base data, action data, condition pairs): one level of plain fields
            if (depth < 2 && type.Namespace == "ScenarioRuleLibrary" &&
                (type.Name.Contains("Data") || type.Name.EndsWith("Pair") || type.Name.StartsWith("CPlayerStats")))
                return Flatten(v, depth + 1);
            return v.ToString();
        }

        // ---- helpers

        private static T Try<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }

        private static string Safe(string s) =>
            new string((s ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim();

        private static string Hash(string s)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))).Replace("-", "").Substring(0, 10).ToLowerInvariant();
        }
    }
}
