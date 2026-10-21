using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using FHGUI.DTO;
using FHGUI.Services;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// Extra rows in the post-scenario Battle Stats table (the screen with XP and accolades). The game already records
    /// about 40 kinds of events per character during a scenario (CPlayerStats lists: every damage instance, kill,
    /// attack modifier drawn, heal, card lost, element, door, trap ...) but its table sums only 8 of them. A postfix on
    /// ScenarioResultViewService.BuildBattleStatsTable appends our rows, computed from Party.LastScenarioStats - the
    /// same records the game's own rows come from. Display only: nothing changes in the game, nothing needs syncing.
    /// The raw records also go to FrosthavenMods\stats\ so the numbers can be checked against what happened, and each
    /// scenario is added to the campaign totals (CampaignStats).
    /// </summary>
    [HarmonyPatch]
    internal static class BattleStats
    {
        /// <summary>One extra row. Its value per character is a number (Num), or a ratio Num / Den (shown as "-" when Den
        /// is 0). Campaign totals add Num and Den over the scenarios (Agg.Sum), or keep the highest Num (Agg.Max), so a
        /// ratio stays a true campaign-wide average. The highest value gets the gold highlight, like the game's rows.
        /// A section row ("OFFENSE") has no values.</summary>
        internal enum Agg { Sum, Max }

        internal sealed class Row
        {
            public string Label;
            public Func<CPlayerStatsScenario, double> Num;
            public Func<CPlayerStatsScenario, double> Den;
            public Agg Agg;
            public string Format = "0";
            public bool Section;
            public Func<Sprite> Icon;

            public double? Value(double num, double den) => Den == null ? num : den > 0 ? num / den : (double?)null;

            public string Text(double? value) => value == null ? "-" : value.Value.ToString(Format);
        }

        private static readonly HashSet<string> SectionLabels = new HashSet<string>();

        private static Row Section(string label)
        {
            SectionLabels.Add(label);
            return new Row { Label = label, Section = true };
        }

        private static Row Int(string label, Func<Sprite> icon, Func<CPlayerStatsScenario, int> f) =>
            new Row { Label = label, Icon = icon, Num = s => f(s) };

        private static Row Best(string label, Func<Sprite> icon, Func<CPlayerStatsScenario, int> f) =>
            new Row { Label = label, Icon = icon, Num = s => f(s), Agg = Agg.Max };

        private static Row Ratio(string label, Func<Sprite> icon, string format, Func<CPlayerStatsScenario, double> num, Func<CPlayerStatsScenario, double> den) =>
            new Row { Label = label, Icon = icon, Num = num, Den = den, Format = format };

        internal static IEnumerable<Row> AllRows => Rows;

        internal static bool IsSection(string label) => SectionLabels.Contains(label ?? "");

        internal static Sprite IconOf(Row row) => row.Section ? null : SafeIcon(row.Icon);

        // ---- icons: the game's own sprites (battle stat icons, condition icons, element icons)

        private static Func<Sprite> Stat(string name) => () => UIInfoTools.Instance.GetBattleStatIcon(name);

        private static Func<Sprite> Good(CCondition.EPositiveCondition c) => () => UIInfoTools.Instance.GetIconPositiveCondition(c);

        private static Func<Sprite> Bad(CCondition.ENegativeCondition c) => () => UIInfoTools.Instance.GetIconNegativeCondition(c);

        private static readonly Func<Sprite> Element = () => UIInfoTools.Instance.GetElementIcon(ElementInfusionBoardManager.EElement.Any);

        private static readonly Func<Sprite> Dealt = Stat("DamageDealt"), Taken = Stat("DamageTaken"), Killed = Stat("EnemiesDefeated"),
            Healed = Stat("DamageHealed"), Looted = Stat("ItemsLooted"), Chests = Stat("ChestsLooted"), Used = Stat("ItemsUsed"), Xp = Stat("XPGained");

        private static Sprite SafeIcon(Func<Sprite> icon)
        {
            if (icon == null) return null;
            try { return icon(); } catch { return null; }
        }

        private static int Sum<T>(IEnumerable<T> list, Func<T, int> f) => list?.Sum(f) ?? 0;

        private static int Count<T>(IEnumerable<T> list, Func<T, bool> f = null) => list == null ? 0 : f == null ? list.Count() : list.Count(f);

        private static int Max<T>(IEnumerable<T> list, Func<T, int> f) => list == null || !list.Any() ? 0 : list.Max(f);

        // Attack modifier names as the game logs them: rich text such as "<b><color=#FFA414>+2</color></b>".
        private static readonly System.Text.RegularExpressions.Regex Tags = new System.Text.RegularExpressions.Regex("<[^>]*>");

        private static string Plain(string m) => m == null ? null : Tags.Replace(m, "").Trim();

        /// <summary>The number on a +N / -N card (null for x2, null and special cards).</summary>
        private static int? Number(string m)
        {
            string p = Plain(m);
            if (string.IsNullOrEmpty(p) || (p[0] != '+' && p[0] != '-')) return null;
            return int.TryParse(p, out int v) ? v : (int?)null;
        }
        private static bool Has(string m, params string[] parts) => m != null && parts.Any(p => m.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsCrit(string m) => Has(m, "x2", "*2", "2x", "\u00d72", "double", "crit");

        private static bool IsNull(string m) => Has(m, "null", "x0", "*0", "\u00d70", "miss");

        // ---- summons count as part of their owner (the user's call). The game already files a summon's damage dealt
        // and taken, kills and heals on the owner (flagged PerformedBySummons). Its attack modifier draws, abilities
        // and elements only go to the game's shared summon pool (StatsState.m_HeroSummonStats), so they are matched
        // back here: each owner's HeroSummons records name their summons' class IDs and GUIDs. A class claimed by two
        // owners is left out rather than guessed.

        private static Dictionary<string, CPlayerStatsScenario> _summonShare = new Dictionary<string, CPlayerStatsScenario>();
        private static List<string> _summonLog = new List<string>();

        private static CPlayerStatsScenario Pool(CPlayerStatsScenario s) =>
            s != null && s.CharacterID != null && _summonShare.TryGetValue(s.CharacterID, out CPlayerStatsScenario p) ? p : null;

        private static void ShareSummonPool(List<CPlayerStatsScenario> stats)
        {
            _summonShare = new Dictionary<string, CPlayerStatsScenario>();
            _summonLog = new List<string>();
            CPlayerStatsScenario pool = ScenarioManager.CurrentScenarioState?.Stats?.m_HeroSummonStats;
            if (pool == null) { _summonLog.Add("no summon pool"); return; }
            var classOwners = new Dictionary<string, HashSet<string>>();
            var guidOwner = new Dictionary<string, string>();
            foreach (CPlayerStatsScenario s in stats)
            {
                foreach (CPlayerStatsHeroSummon h in s.HeroSummons ?? new List<CPlayerStatsHeroSummon>())
                {
                    if (h.ClassID != null)
                    {
                        if (!classOwners.TryGetValue(h.ClassID, out HashSet<string> owners)) classOwners[h.ClassID] = owners = new HashSet<string>();
                        owners.Add(s.CharacterID);
                    }
                    if (h.GUID != null) guidOwner[h.GUID] = s.CharacterID;
                }
            }
            string Owner(string classID, string guid)
            {
                if (guid != null && guidOwner.TryGetValue(guid, out string byGuid)) return byGuid;
                if (classID != null && classOwners.TryGetValue(classID, out HashSet<string> owners) && owners.Count == 1) return owners.First();
                return null;
            }
            CPlayerStatsScenario Share(string owner)
            {
                if (!_summonShare.TryGetValue(owner, out CPlayerStatsScenario share)) _summonShare[owner] = share = new CPlayerStatsScenario((string)null);
                return share;
            }
            int unmatched = 0;
            foreach (CPlayerStatsModifiers m in pool.Modifiers ?? new List<CPlayerStatsModifiers>())
            {
                string owner = Owner(m.ActionData?.MainActorClassID, m.ActionData?.MainActorGUID);
                if (owner == null) { unmatched++; _summonLog.Add($"  pool MOD r{m.Round} class={m.ActionData?.MainActorClassID} -> nobody"); continue; }
                Share(owner).Modifiers.Add(m);
                _summonLog.Add($"  pool MOD r{m.Round} class={m.ActionData?.MainActorClassID} {string.Join(",", (m.Modifier ?? new List<string>()).Select(Plain))} -> {owner}");
            }
            foreach (CPlayerStatsAbilities a in pool.Abilities ?? new List<CPlayerStatsAbilities>())
            {
                string owner = Owner(a.ActionData?.MainActorClassID, a.ActionData?.MainActorGUID);
                if (owner == null) { unmatched++; continue; }
                Share(owner).Abilities.Add(a);
            }
            foreach (CPlayerStatsElement e in (pool.Infusions ?? new List<CPlayerStatsElement>()).Concat(pool.Consumed ?? new List<CPlayerStatsElement>()))
            {
                string owner = Owner(e.ActorClassID, null);
                if (owner == null) { unmatched++; continue; }
                (e.StatsElementType == CPlayerStatsElement.EStatsElementType.Consumed ? Share(owner).Consumed : Share(owner).Infusions).Add(e);
            }
            _summonLog.Insert(0, $"summon pool: {pool.Modifiers?.Count ?? 0} modifier draws, {pool.Abilities?.Count ?? 0} abilities, " +
                                 $"{(pool.Infusions?.Count ?? 0) + (pool.Consumed?.Count ?? 0)} element records; {unmatched} not matched to an owner; " +
                                 "summons by owner: " + string.Join(", ", classOwners.Select(kv => kv.Key + "=" + string.Join("/", kv.Value))));
        }

        private static IEnumerable<string> Mods(CPlayerStatsScenario s) =>
            (s.Modifiers ?? new List<CPlayerStatsModifiers>()).Concat(Pool(s)?.Modifiers ?? new List<CPlayerStatsModifiers>())
                .SelectMany(m => m.Modifier ?? new List<string>()).Select(Plain);

        private static int Infused(CPlayerStatsScenario s) => Count(s.Infusions) + Count(Pool(s)?.Infusions);

        private static int ConsumedCount(CPlayerStatsScenario s) => Count(s.Consumed) + Count(Pool(s)?.Consumed);

        /// <summary>Abilities used, each once: the game can record the same ability more than once (seen in the raw
        /// log: one Heal card logged twice for one heal), so records sharing the ability's ID, round and type count once.
        /// (HeroSummons is no use for counting summons: the game adds a record each time a summon moves.)</summary>
        private static IEnumerable<CPlayerStatsAbilities> Abilities(CPlayerStatsScenario s) =>
            (s.Abilities ?? new List<CPlayerStatsAbilities>()).Concat(Pool(s)?.Abilities ?? new List<CPlayerStatsAbilities>())
                .GroupBy(a => (a.AbilityID, a.Round, a.ActionData?.AbilityType, a.ActionData?.ActingAbilityName)).Select(g => g.First());

        private static int Overkill(CPlayerStatsKill k) => Math.Max(0, k.LastDamageAmount - k.PreDamageHealth);

        // What the game's records mean (checked in StatsDataStorage / GameState, 2026-10-20):
        // - DamageDealt / DamageReceived / Kills / Heals also hold your summons' entries, flagged PerformedBySummons:
        //   counted as yours (summons are an extension of their owner); the "by summons" rows show their share.
        // - Damage Avoided = a whole hit negated (lose cards, prevent-damage effects); Shielded is set for enemy targets
        //   only; Shield is the target's shield value on that hit.
        // - Trap records are springing ("Activate") and disarming ("Disarm") traps.
        // - RemovedCondition is conditions removed FROM the character (wearing off included), so it is not used.
        private static bool Attack(CPlayerStatsDamage d) => d.ActionData?.AbilityType == CAbility.EAbilityType.Attack;

        private static bool EnemyKill(CPlayerStatsKill k) => k.ActorWhoDiedType != "Player" && k.ActorWhoDiedType != "HeroSummon";

        private static bool Sprung(CPlayerStatsTrap t) => string.Equals(t.TrapAction, "Activate", StringComparison.OrdinalIgnoreCase);

        private static readonly List<Row> Rows = new List<Row>
        {
            Section("OFFENSE"),
            Best("Biggest hit", Dealt, s => Max(s.DamageDealt, d => d.FinalDamageAmount)),
            Int("Hits landed", Dealt, s => Count(s.DamageDealt, d => Attack(d) && d.FinalDamageAmount > 0)),
            Ratio("Damage per round", Dealt, "0.0", s => Sum(s.DamageDealt, d => d.FinalDamageAmount), s => s.RoundsPlayed),
            Int("Overkill", Killed, s => Sum(s.Kills?.Where(EnemyKill), Overkill)),
            // The attack's strength before the shield isn't recorded, so the amount blocked can't be known when nothing
            // got through (an attack of 2 into shield 5 logs Shielded=5): count the hits a shield stopped instead.
            Int("Hits stopped by shields", Dealt, s => Count(s.DamageDealt, d => Attack(d) && d.Shielded > 0 && d.FinalDamageAmount == 0)),
            Int("Poison bonus damage", Bad(CCondition.ENegativeCondition.Poison), s => Sum(s.DamageDealt, d => d.PoisonDamage)),
            Int("Melee hits", Dealt, s => Count(s.DamageDealt, d => Attack(d) && d.IsMelee && d.FinalDamageAmount > 0)),
            Int("Ranged hits", Dealt, s => Count(s.DamageDealt, d => Attack(d) && !d.IsMelee && d.FinalDamageAmount > 0)),
            Int("Area attacks", Dealt, s => Count(Abilities(s), a => a.AreaEffect == true)),
            Int("Damage by summons", Dealt, s => Sum(s.DamageDealt, d => d.PerformedBySummons ? d.FinalDamageAmount : 0)),
            Int("Kills by summons", Killed, s => Count(s.Kills, k => k.PerformedBySummons && EnemyKill(k))),

            Section("DEFENSE"),
            Best("Biggest hit taken", Taken, s => Max(s.DamageReceived?.Where(d => d.CauseOfDamage != CActor.ECauseOfDamage.Self), d => d.FinalDamageAmount)),
            Int("Self-inflicted damage", Taken, s => Sum(s.DamageReceived, d => d.CauseOfDamage == CActor.ECauseOfDamage.Self ? d.FinalDamageAmount : 0)),
            Int("Shield on hits taken", Good(CCondition.EPositiveCondition.Ward), s => Sum(s.DamageReceived, d => d.Avoided == 0 ? Math.Max(0, d.Shield) : 0)),
            Int("Hits negated", Good(CCondition.EPositiveCondition.Invisible), s => Count(s.DamageReceived, d => d.Avoided > 0)),
            Int("Damage negated", Good(CCondition.EPositiveCondition.Ward), s => Sum(s.DamageReceived, d => d.Avoided)),
            Int("Trap damage taken", Taken, s => Sum(s.Trap?.Where(Sprung), t => t.TrapDamage)),
            Int("Exhausted", Bad(CCondition.ENegativeCondition.Bane), s => s.PlayerSurvivedScenario ? 0 : 1),

            Section("SUPPORT"),
            // Heals on someone already at full health are logged with 0: only count the ones that restored health.
            Int("Heals", Healed, s => Count(s.Heals, h => h.HealAmount > 0)),
            Int("Poison/wound cleared", Good(CCondition.EPositiveCondition.Regenerate), s => Count(s.Heals, h => h.PoisonRemoved || h.WoundRemoved)),
            Int("Summons", Good(CCondition.EPositiveCondition.Strengthen), s => Count(Abilities(s), a => a.ActionData?.AbilityType == CAbility.EAbilityType.Summon)),

            Section("LUCK"),
            Int("Modifiers drawn", Good(CCondition.EPositiveCondition.Bless), s => Mods(s).Count()),
            Ratio("Average modifier", Good(CCondition.EPositiveCondition.Bless), "+0.0;-0.0;0.0",
                s => Mods(s).Select(Number).Where(v => v.HasValue).Sum(v => v.Value), s => Mods(s).Select(Number).Count(v => v.HasValue)),
            Int("Crits (x2)", Good(CCondition.EPositiveCondition.Bless), s => Mods(s).Count(IsCrit)),
            Int("Nulls", Bad(CCondition.ENegativeCondition.Curse), s => Mods(s).Count(IsNull)),
            Int("Attacks with advantage", Good(CCondition.EPositiveCondition.Advantage), s => Count(Abilities(s), a => a.Advantage != null && a.Advantage.Any(v => v))),

            Section("CARDS AND ELEMENTS"),
            Int("Cards lost", Used, s => Count(s.LoseCards)),
            Int("Cards recovered", Used, s => Count(s.RecoverCards)),
            Int("Elements infused", Element, Infused),
            Int("Elements consumed", Element, ConsumedCount),

            Section("EXPLORATION"),
            Int("Doors opened", Chests, s => Sum(s.Door, d => d.Doors)),
            Int("Loot picked up", Looted, s => Count(s.Loot)),
            Int("Traps sprung", Taken, s => Count(s.Trap, Sprung)),
            Int("Traps disarmed", Good(CCondition.EPositiveCondition.Immovable), s => Count(s.Trap, t => !Sprung(t))),
            Int("Enemies forced into traps", Killed, s => Count(s.ForcedActorsIntoTraps)),
            Int("Rounds played", Xp, s => s.RoundsPlayed),
        };

        [HarmonyPatch(typeof(ScenarioResultViewService), nameof(ScenarioResultViewService.BuildBattleStatsTable))]
        [HarmonyPostfix]
        private static void AddRows(BattleResultDTO battleResults, ref DebriefingTableDTO __result)
        {
            if (!ModRules.IsOn(ModRules.BattleStats)) return;
            try
            {
                List<CPlayerStatsScenario> stats = ScenarioResultViewService.Party?.LastScenarioStats;
                if (__result?.Cells == null || stats == null || battleResults?.IndividualRewards == null) return;
                var columns = battleResults.IndividualRewards
                    .Select(h => stats.FirstOrDefault(s => s.CharacterID == h.BattleStats?.CharacterID || s.CharacterID == h.ClassID))
                    .ToList();
                ShareSummonPool(stats);
                CampaignStats.Record(battleResults, columns, __result);
                int oldRows = __result.RowHeaders.Count, cols = __result.ColumHeaders.Count;
                var cells = new DebriefingTableDTO.Cell[oldRows + Rows.Count, cols];
                for (int r = 0; r < oldRows; r++)
                    for (int c = 0; c < cols; c++)
                        cells[r, c] = __result.Cells[r, c];
                for (int i = 0; i < Rows.Count; i++)
                {
                    Row row = Rows[i];
                    __result.RowHeaders.Add(new DebriefingTableDTO.Header { Text = row.Label, Icon = IconOf(row) });
                    var values = Enumerable.Range(0, cols).Select(c =>
                    {
                        CPlayerStatsScenario s = c < columns.Count ? columns[c] : null;
                        return row.Section || s == null ? (double?)null : row.Value(row.Num(s), row.Den?.Invoke(s) ?? 0);
                    }).ToList();
                    FillRow(cells, oldRows + i, row, values);
                }
                __result.Cells = cells;
                WriteRawLog(stats);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Battle stats: could not add the extra rows: " + e);
            }
        }

        /// <summary>One row of cells; the highest value (above 0) is highlighted.</summary>
        internal static void FillRow(DebriefingTableDTO.Cell[,] cells, int r, Row row, List<double?> values)
        {
            double best = values.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Max();
            for (int c = 0; c < values.Count; c++)
                cells[r, c] = row.Section
                    ? new DebriefingTableDTO.Cell { Text = "" }
                    : new DebriefingTableDTO.Cell { Text = row.Text(values[c]), IsHighlighted = best > 0 && values[c] == best };
        }

        /// <summary>Section rows ("OFFENSE" ...) are titles: no icon box and no value bars. The table lays its cells out
        /// in a grid, so the bars are made invisible (not switched off, which would shift the grid). The table's
        /// objects are pooled and reused, so every other row is set back to normal.</summary>
        [HarmonyPatch(typeof(FHGUI.Elements.UIDebriefingTable), nameof(FHGUI.Elements.UIDebriefingTable.Init))]
        [HarmonyPostfix]
        private static void StyleSections(FHGUI.Elements.UIDebriefingTable.Params @params,
            List<FHGUI.Elements.UIDebriefingTableHeader> ___m_RowHeaders, List<FHGUI.Elements.UIDebriefingTableCell> ___m_Cells)
        {
            try
            {
                DebriefingTableDTO data = @params.TableData;
                if (data?.Cells == null || ___m_RowHeaders == null || ___m_Cells == null) return;
                int rows = data.Cells.GetLength(0);
                for (int r = 0; r < rows && r < ___m_RowHeaders.Count; r++)
                {
                    bool section = IsSection(data.RowHeaders[r].Text);
                    var icon = Traverse.Create(___m_RowHeaders[r]).Field("Icon").GetValue<Image>();
                    if (icon != null) icon.enabled = !section && data.RowHeaders[r].Icon != null;
                    for (int k = r; k < ___m_Cells.Count && k < data.Cells.Length; k += rows)
                    {
                        var group = ___m_Cells[k].GetComponent<CanvasGroup>();
                        if (group == null) { if (!section) continue; group = ___m_Cells[k].gameObject.AddComponent<CanvasGroup>(); }
                        group.alpha = section ? 0f : 1f;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Battle stats: could not style the section rows: " + e.Message);
            }
        }

        [HarmonyPatch(typeof(FHGUI.Modules.ScenarioDebriefingModule), nameof(FHGUI.Modules.ScenarioDebriefingModule.Show))]
        [HarmonyPostfix]
        private static void DumpLayout(FHGUI.Modules.ScenarioDebriefingModule __instance) =>
            GiveResourcesPatches.Safe("dump the debrief layout", () => UiDump.Once("Scenario debriefing (battle stats)", __instance.transform));

        // ---- phase 1: raw records, to check the numbers

        private static string _lastLogged;

        private static void WriteRawLog(List<CPlayerStatsScenario> stats)
        {
            try
            {
                string key = string.Join("|", stats.Select(s => s.CharacterID + ":" + (s.DamageDealt?.Count ?? 0) + ":" + s.RoundsPlayed));
                if (key == _lastLogged) return; // the table is built more than once per screen
                _lastLogged = key;
                var sb = new StringBuilder();
                sb.AppendLine($"===== scenario stats {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Frosthaven Mods {Plugin.Version} =====");
                foreach (string line in _summonLog) sb.AppendLine(line);
                foreach (CPlayerStatsScenario s in stats)
                {
                    sb.AppendLine($"--- {s.CharacterID}: rounds={s.RoundsPlayed} survived={s.PlayerSurvivedScenario}");
                    sb.AppendLine("  counts: " + string.Join(" ", typeof(CPlayerStats).GetProperties()
                        .Where(p => typeof(System.Collections.ICollection).IsAssignableFrom(p.PropertyType))
                        .Select(p => $"{p.Name}={(p.GetValue(s) as System.Collections.ICollection)?.Count ?? 0}")));
                    foreach (var d in s.DamageDealt ?? new List<CPlayerStatsDamage>())
                        sb.AppendLine($"  DEALT r{d.Round} {d.FinalDamageAmount} to {d.ActorWhoReceivedDamageMonsterType ?? d.ActorWhoReceivedDamageClassID} (hp {d.HealthBeforeDamage}) shielded={d.Shielded} shield={d.Shield} avoided={d.Avoided} poison={d.PoisonDamage} melee={d.IsMelee} cause={d.CauseOfDamage} ability={d.ActionData?.ActingAbilityName} type={d.ActionData?.AbilityType} summon={d.PerformedBySummons}");
                    foreach (var d in s.DamageReceived ?? new List<CPlayerStatsDamage>())
                        sb.AppendLine($"  TAKEN r{d.Round} {d.FinalDamageAmount} from {d.ActorWhoDealtDamageMonsterType ?? d.ActorWhoDealtDamageClassID} shielded={d.Shielded} shield={d.Shield} avoided={d.Avoided} cause={d.CauseOfDamage} summon={d.PerformedBySummons}");
                    foreach (var k in s.Kills ?? new List<CPlayerStatsKill>())
                        sb.AppendLine($"  KILL r{k.Round} {k.ActorWhoDiedMonsterType ?? k.ActorWhoDiedClassID} type={k.ActorWhoDiedType} cause={k.CauseOfDeath} last={k.LastDamageAmount} hpBefore={k.PreDamageHealth} max={k.MaxHealth} summon={k.PerformedBySummons}");
                    foreach (var m in s.Modifiers ?? new List<CPlayerStatsModifiers>())
                        sb.AppendLine($"  MOD r{m.Round} {string.Join(",", (m.Modifier ?? new List<string>()).Select(Plain))}");
                    foreach (var a in s.Abilities ?? new List<CPlayerStatsAbilities>())
                        sb.AppendLine($"  ABILITY r{a.Round} {a.ActionData?.ActingAbilityName} type={a.ActionData?.AbilityType} amount={a.Amount} targets={a.Targets} aoe={a.AreaEffect} adv={string.Join(",", a.Advantage ?? new List<bool>())} default={a.DefaultAction} id={a.AbilityID}");
                    foreach (var h in s.Heals ?? new List<CPlayerStatsHeal>())
                        sb.AppendLine($"  HEAL r{h.Round} {h.HealAmount} poisonRemoved={h.PoisonRemoved} woundRemoved={h.WoundRemoved}");
                    foreach (var c in s.LoseCards ?? new List<CPlayerStatsLoseCard>())
                        sb.AppendLine($"  LOSECARD r{c.Round} avoided={c.DamageAvoided}");
                    foreach (var e in s.Infusions ?? new List<CPlayerStatsElement>())
                        sb.AppendLine($"  INFUSE r{e.Round} {e.Element}");
                    foreach (var e in s.Consumed ?? new List<CPlayerStatsElement>())
                        sb.AppendLine($"  CONSUME r{e.Round} {e.Element}");
                    foreach (var t in s.Trap ?? new List<CPlayerStatsTrap>())
                        sb.AppendLine($"  TRAP r{t.Round} {t.TrapAction} damage={t.TrapDamage}");
                    foreach (var l in s.Loot ?? new List<CPlayerStatsLoot>())
                        sb.AppendLine($"  LOOT r{l.Round} {l.LootType} endOfTurn={l.EndOfTurn}");
                }
                string dir = Path.Combine(Paths.GameRootPath, "FrosthavenMods", "stats");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"scenario-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), sb.ToString());
                Plugin.Log.LogInfo($"Battle stats: raw records written ({stats.Count} characters)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Battle stats: could not write the raw records: " + e.Message);
            }
        }
    }
}
