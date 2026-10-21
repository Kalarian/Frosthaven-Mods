using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using FHGUI;
using FHGUI.DTO;
using FHGUI.Elements;
using FHGUI.Services;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// Campaign-long battle stats: every scenario's numbers (the game's 8 rows and ours) are added to a running total per
    /// character, and the Statistics page gets "This scenario" / "Campaign" buttons to switch the table.
    ///
    /// Storage: FrosthavenMods\stats\campaign-&lt;campaign name&gt;.txt in the game folder, one per campaign, plain lines
    /// ("scenario &lt;key&gt;" for each scenario already counted, "stat &lt;character&gt; &lt;name&gt; &lt;value&gt;"). Not beside the
    /// save: the game only reaches its save folder asynchronously, and a co-op client doesn't have the host's save. Each
    /// player keeps their own totals. A character is "class|name", so a retired character and a later one of the same
    /// class stay apart. The game's own lifetime records (CPlayerRecords) are never updated by the game (its UpdateStats
    /// has no callers), so there was nothing to seed from: totals start with the first scenario played with the mod.
    /// </summary>
    [HarmonyPatch]
    internal static class CampaignStats
    {
        private const string Played = "scenarios", Vanilla = "v:";

        private static string _file;
        private static readonly HashSet<string> Counted = new HashSet<string>();
        private static readonly Dictionary<string, Dictionary<string, double>> Totals = new Dictionary<string, Dictionary<string, double>>();

        // The current Statistics page: its columns and the game's own rows, to build the campaign table from.
        private static List<string> _characters = new List<string>();
        private static List<DebriefingTableDTO.Header> _columnHeaders = new List<DebriefingTableDTO.Header>();
        private static List<DebriefingTableDTO.Header> _vanillaHeaders = new List<DebriefingTableDTO.Header>();
        private static List<string> _vanillaKeys = new List<string>();

        // ---- recording

        /// <summary>Called by BattleStats while the scenario table is built (before our rows are added to it).</summary>
        internal static void Record(BattleResultDTO results, List<CPlayerStatsScenario> columns, DebriefingTableDTO vanillaTable)
        {
            try
            {
                Load();
                _characters = results.IndividualRewards.Select(h => CharacterKey(h.BattleStats?.CharacterID ?? h.ClassID)).ToList();
                _columnHeaders = vanillaTable.ColumHeaders.ToList();
                _vanillaHeaders = vanillaTable.RowHeaders.ToList();
                _vanillaKeys = results.IndividualRewards.FirstOrDefault()?.BattleStats?.GetBattleStats().Select(b => b.NameKey).ToList() ?? new List<string>();

                string key = ScenarioKey(columns);
                if (_file == null || Counted.Contains(key)) return; // the page is built more than once per scenario
                for (int c = 0; c < _characters.Count; c++)
                {
                    CPlayerStatsScenario s = c < columns.Count ? columns[c] : null;
                    if (s == null) continue;
                    Dictionary<string, double> t = For(_characters[c]);
                    Add(t, Played, 1, BattleStats.Agg.Sum);
                    foreach (HeroBattleStatDTO.BattleStat b in results.IndividualRewards[c].BattleStats?.GetBattleStats() ?? Enumerable.Empty<HeroBattleStatDTO.BattleStat>())
                        Add(t, Vanilla + b.NameKey, b.Current, BattleStats.Agg.Sum);
                    foreach (BattleStats.Row row in BattleStats.AllRows.Where(r => !r.Section))
                    {
                        Add(t, row.Label, row.Num(s), row.Agg);
                        if (row.Den != null) Add(t, row.Label + "/den", row.Den(s), BattleStats.Agg.Sum);
                    }
                }
                Counted.Add(key);
                Save();
                Plugin.Log.LogInfo($"Battle stats: scenario added to the campaign totals ({Counted.Count} so far)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Battle stats: could not update the campaign totals: " + e);
            }
        }

        private static void Add(Dictionary<string, double> t, string name, double value, BattleStats.Agg agg)
        {
            t.TryGetValue(name, out double old);
            t[name] = agg == BattleStats.Agg.Max ? Math.Max(old, value) : old + value;
        }

        private static Dictionary<string, double> For(string character)
        {
            if (!Totals.TryGetValue(character, out Dictionary<string, double> t)) Totals[character] = t = new Dictionary<string, double>();
            return t;
        }

        /// <summary>"class|name" of the party member with this class.</summary>
        private static string CharacterKey(string classID)
        {
            var c = ScenarioResultViewService.Party?.SelectedCharacters?.FirstOrDefault(m => m.CharacterID == classID);
            return (classID ?? "?") + "|" + (c?.CharacterName ?? "");
        }

        /// <summary>Tells scenarios apart: the scenario, its seed and length, and how much happened in it.</summary>
        private static string ScenarioKey(List<CPlayerStatsScenario> columns)
        {
            ScenarioState state = ScenarioManager.CurrentScenarioState;
            string what = string.Join(",", columns.Where(s => s != null).Select(s => $"{s.CharacterID}:{s.RoundsPlayed}:{s.DamageDealt?.Count ?? 0}:{s.Abilities?.Count ?? 0}"));
            return $"{state?.ID}/{state?.Seed}/{state?.RoundNumber}/{what}".Replace(' ', '_');
        }

        // ---- the file

        private static void Load()
        {
            string campaign = SaveData.Instance?.Global?.CurrentAdventureData?.PartyName;
            if (string.IsNullOrEmpty(campaign)) { _file = null; return; }
            string safe = new string(campaign.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            string file = Path.Combine(Paths.GameRootPath, "FrosthavenMods", "stats", "campaign-" + safe + ".txt");
            if (file == _file) return;
            _file = file;
            Counted.Clear();
            Totals.Clear();
            if (!File.Exists(file)) return;
            foreach (string line in File.ReadAllLines(file))
            {
                string[] p = line.Split('\t');
                if (p.Length == 2 && p[0] == "scenario") Counted.Add(p[1]);
                else if (p.Length == 4 && p[0] == "stat" && double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    For(p[1])[p[2]] = v;
            }
        }

        private static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            var sb = new StringBuilder();
            sb.AppendLine("# Frosthaven Mods campaign battle stats (written by the mod; one line per value)");
            foreach (string key in Counted) sb.Append("scenario\t").AppendLine(key);
            foreach (var character in Totals)
                foreach (var stat in character.Value)
                    sb.Append("stat\t").Append(character.Key).Append('\t').Append(stat.Key).Append('\t')
                        .AppendLine(stat.Value.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(_file, sb.ToString());
        }

        // ---- the campaign table

        private static double Get(string character, string name) =>
            Totals.TryGetValue(character, out Dictionary<string, double> t) && t.TryGetValue(name, out double v) ? v : 0;

        private static DebriefingTableDTO BuildCampaignTable()
        {
            Load();
            var played = new BattleStats.Row { Label = "Scenarios played" };
            var rows = new List<(DebriefingTableDTO.Header Header, BattleStats.Row Row, Func<string, double?> Value)>
            {
                (new DebriefingTableDTO.Header { Text = played.Label, Icon = UIInfoTools.Instance.GetBattleStatIcon("XPGained") }, played, ch => Get(ch, Played)),
            };
            for (int i = 0; i < _vanillaHeaders.Count && i < _vanillaKeys.Count; i++)
            {
                string name = Vanilla + _vanillaKeys[i];
                rows.Add((_vanillaHeaders[i], new BattleStats.Row { Label = _vanillaHeaders[i].Text }, ch => Get(ch, name)));
            }
            foreach (BattleStats.Row row in BattleStats.AllRows)
            {
                BattleStats.Row r = row;
                rows.Add((new DebriefingTableDTO.Header { Text = r.Label, Icon = BattleStats.IconOf(r) }, r,
                    ch => r.Section ? null : r.Value(Get(ch, r.Label), Get(ch, r.Label + "/den"))));
            }
            var table = new DebriefingTableDTO
            {
                ColumHeaders = _columnHeaders.ToList(),
                RowHeaders = rows.Select(x => x.Header).ToList(),
                Cells = new DebriefingTableDTO.Cell[rows.Count, _columnHeaders.Count],
            };
            for (int r = 0; r < rows.Count; r++)
                BattleStats.FillRow(table.Cells, r, rows[r].Row, Enumerable.Range(0, _columnHeaders.Count)
                    .Select(c => c < _characters.Count ? rows[r].Value(_characters[c]) : null).ToList());
            return table;
        }

        // ---- "This scenario" / "Campaign" buttons on the Statistics page

        private const string ScenarioButton = "FrosthavenMods_StatsScenario", CampaignButton = "FrosthavenMods_StatsCampaign";
        private const float Scale = 0.55f, Gap = 10f, Right = 30f, InactiveAlpha = 0.45f;

        private static DebriefingTableDTO _scenarioTable;
        private static bool _switching;

        [HarmonyPatch(typeof(UIDebriefingBattleStats), nameof(UIDebriefingBattleStats.Init))]
        [HarmonyPostfix]
        private static void AddButtons(UIDebriefingBattleStats __instance, UIDebriefingBattleStats.Params @params)
        {
            if (_switching || !ModRules.IsOn(ModRules.BattleStats)) return;
            GiveResourcesPatches.Safe("add the campaign stats buttons", () =>
            {
                _scenarioTable = @params.TableData; // a new Statistics page always opens on this scenario
                Transform header = __instance.transform.Find("Header");
                if (header == null) return;
                FHButton scenario = Button(__instance, header, ScenarioButton, "This scenario", 1);
                FHButton campaign = Button(__instance, header, CampaignButton, "Campaign", 0);
                Show(__instance, campaign: false);
            });
        }

        private static FHButton Button(UIDebriefingBattleStats page, Transform header, string name, string text, int slot)
        {
            Transform existing = header.Find(name);
            FHButton button = existing != null ? existing.GetComponent<FHButton>() : null;
            if (button == null)
            {
                FHButton template = Template(page);
                button = ButtonCopy.Make(template, header, name, text, () => { });
                if (button == null) return null;
                var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;
                var rt = (RectTransform)button.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.localScale = new Vector3(Scale, Scale, 1f);
                float width = rt.rect.width * Scale;
                rt.anchoredPosition = new Vector2(-Right - slot * (width + Gap), 0f);
                UiDump.Once("Statistics page header (with buttons)", header);
            }
            bool isCampaign = name == CampaignButton;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => GiveResourcesPatches.Safe("switch the stats view", () => Show(page, isCampaign)));
            return button;
        }

        /// <summary>A text button to copy: the debrief screen's own footer button ("FH_ButtonBar_Common"). When the page
        /// is set up, that part of the screen is still switched off, so the search includes inactive objects, then any
        /// loaded copy of that button, then any text button on the screen.</summary>
        private static FHButton Template(Component near)
        {
            bool Bar(FHButton b) => b != null && b.name.StartsWith("FH_ButtonBar_Common");
            FHButton[] onScreen = near.transform.root.GetComponentsInChildren<FHButton>(true);
            FHButton template = onScreen.FirstOrDefault(Bar)
                ?? Resources.FindObjectsOfTypeAll<FHButton>().FirstOrDefault(Bar)
                ?? onScreen.FirstOrDefault(b => b.GetComponentInChildren<TMPro.TextMeshProUGUI>(true) != null);
            Plugin.Log.LogInfo("Battle stats: toggle buttons copied from " + (template != null ? template.name : "nothing"));
            return template;
        }

        private static void Show(UIDebriefingBattleStats page, bool campaign)
        {
            DebriefingTableDTO table = campaign ? BuildCampaignTable() : _scenarioTable;
            if (table == null) return;
            if (campaign || page.Data.TableData != _scenarioTable)
            {
                _switching = true;
                try { page.Init(new UIDebriefingBattleStats.Params { TableData = table }); }
                finally { _switching = false; }
                var scroll = page.PersonalRewardsTable?.GetComponentInChildren<ScrollRect>(true);
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            }
            Transform header = page.transform.Find("Header");
            Dim(header?.Find(ScenarioButton), campaign);
            Dim(header?.Find(CampaignButton), !campaign);
        }

        private static void Dim(Transform button, bool dim)
        {
            if (button == null) return;
            var group = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dim ? InactiveAlpha : 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }
}
