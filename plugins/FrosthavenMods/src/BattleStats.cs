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

namespace FrosthavenMods
{
    /// <summary>
    /// Extra rows in the post-scenario Battle Stats table (the screen with XP and accolades). The game already records
    /// about 40 kinds of events per character during a scenario (CPlayerStats lists: every damage instance, kill,
    /// attack modifier drawn, heal, card lost, element, door, trap ...) but its table sums only 8 of them. A postfix on
    /// ScenarioResultViewService.BuildBattleStatsTable appends our rows, computed from Party.LastScenarioStats - the
    /// same records the game's own rows come from. Display only: nothing changes in the game, nothing needs syncing.
    /// Phase 1 also writes the raw records to FrosthavenMods\stats\ so the numbers can be checked against what happened.
    /// </summary>
    [HarmonyPatch]
    internal static class BattleStats
    {
        /// <summary>One extra row: a label, a value per character, and whether the highest value gets the gold
        /// highlight (like the game's rows). A section row has no values.</summary>
        private sealed class Row
        {
            public string Label;
            public Func<CPlayerStatsScenario, string> Value;
            public Func<CPlayerStatsScenario, double> Score; // for the highlight; null = no highlight
            public bool Section;
        }

        private static Row Section(string label) => new Row { Label = label, Section = true };

        private static Row Int(string label, Func<CPlayerStatsScenario, int> f) =>
            new Row { Label = label, Value = s => f(s).ToString(), Score = s => f(s) };

        private static int Sum<T>(IEnumerable<T> list, Func<T, int> f) => list?.Sum(f) ?? 0;

        private static int Count<T>(IEnumerable<T> list, Func<T, bool> f = null) => list == null ? 0 : f == null ? list.Count() : list.Count(f);

        private static int Max<T>(IEnumerable<T> list, Func<T, int> f) => list == null || !list.Any() ? 0 : list.Max(f);

        // Attack modifier names as the game logs them (unlocalized). Checked against the raw log in phase 1.
        private static bool IsCrit(string m) => m != null && (m.IndexOf("x2", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("double", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("crit", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsNull(string m) => m != null && (m.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("x0", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("miss", StringComparison.OrdinalIgnoreCase) >= 0);

        private static IEnumerable<string> Mods(CPlayerStatsScenario s) => s.Modifiers?.SelectMany(m => m.Modifier ?? new List<string>()) ?? Enumerable.Empty<string>();

        private static int Overkill(CPlayerStatsKill k) => Math.Max(0, k.LastDamageAmount - k.PreDamageHealth);

        private static readonly List<Row> Rows = new List<Row>
        {
            Section("OFFENSE"),
            Int("Biggest hit", s => Max(s.DamageDealt, d => d.FinalDamageAmount)),
            Int("Hits landed", s => Count(s.DamageDealt, d => d.FinalDamageAmount > 0)),
            new Row
            {
                Label = "Damage per round",
                Value = s => s.RoundsPlayed <= 0 ? "-" : (Sum(s.DamageDealt, d => d.FinalDamageAmount) / (double)s.RoundsPlayed).ToString("0.0"),
                Score = s => s.RoundsPlayed <= 0 ? 0 : Sum(s.DamageDealt, d => d.FinalDamageAmount) / (double)s.RoundsPlayed,
            },
            Int("Overkill", s => Sum(s.Kills, Overkill)),
            Int("Lost to enemy shields", s => Sum(s.DamageDealt, d => d.Shielded)),
            Int("Poison bonus damage", s => Sum(s.DamageDealt, d => d.PoisonDamage)),
            Int("Melee hits", s => Count(s.DamageDealt, d => d.IsMelee && d.FinalDamageAmount > 0)),
            Int("Ranged hits", s => Count(s.DamageDealt, d => !d.IsMelee && d.FinalDamageAmount > 0)),
            Int("Area attacks", s => Count(s.Abilities, a => a.AreaEffect == true)),
            Int("Kills by summons", s => Count(s.Kills, k => k.PerformedBySummons)),

            Section("DEFENSE"),
            Int("Biggest hit taken", s => Max(s.DamageReceived, d => d.FinalDamageAmount)),
            Int("Blocked by shield", s => Sum(s.DamageReceived, d => d.Shielded + d.ItemShielded)),
            Int("Attacks avoided", s => Count(s.DamageReceived, d => d.Avoided > 0)),
            Int("Damage negated by cards", s => Sum(s.LoseCards, c => c.DamageAvoided)),
            Int("Trap damage taken", s => Sum(s.Trap, t => t.TrapDamage)),
            Int("Exhausted", s => s.PlayerSurvivedScenario ? 0 : 1),

            Section("SUPPORT"),
            Int("Heals", s => Count(s.Heals)),
            Int("Conditions removed", s => Count(s.RemovedCondition)),
            Int("Summons", s => Count(s.HeroSummons)),

            Section("LUCK"),
            Int("Modifiers drawn", s => Mods(s).Count()),
            Int("Crits (x2)", s => Mods(s).Count(IsCrit)),
            Int("Nulls", s => Mods(s).Count(IsNull)),
            Int("Attacks with advantage", s => Count(s.Abilities, a => a.Advantage != null && a.Advantage.Any(v => v))),

            Section("CARDS AND ELEMENTS"),
            Int("Cards lost to damage", s => Count(s.LoseCards)),
            Int("Cards recovered", s => Count(s.RecoverCards)),
            Int("Elements infused", s => Count(s.Infusions)),
            Int("Elements consumed", s => Count(s.Consumed)),

            Section("EXPLORATION"),
            Int("Doors opened", s => Sum(s.Door, d => d.Doors)),
            Int("Loot picked up", s => Count(s.Loot)),
            Int("Traps sprung", s => Count(s.Trap)),
            Int("Enemies forced into traps", s => Count(s.ForcedActorsIntoTraps)),
            Int("Rounds played", s => s.RoundsPlayed),
        };

        [HarmonyPatch(typeof(ScenarioResultViewService), nameof(ScenarioResultViewService.BuildBattleStatsTable))]
        [HarmonyPostfix]
        private static void AddRows(BattleResultDTO battleResults, ref DebriefingTableDTO __result)
        {
            try
            {
                List<CPlayerStatsScenario> stats = ScenarioResultViewService.Party?.LastScenarioStats;
                if (__result?.Cells == null || stats == null || battleResults?.IndividualRewards == null) return;
                var columns = battleResults.IndividualRewards
                    .Select(h => stats.FirstOrDefault(s => s.CharacterID == h.BattleStats?.CharacterID || s.CharacterID == h.ClassID))
                    .ToList();
                int oldRows = __result.RowHeaders.Count, cols = __result.ColumHeaders.Count;
                var cells = new DebriefingTableDTO.Cell[oldRows + Rows.Count, cols];
                for (int r = 0; r < oldRows; r++)
                    for (int c = 0; c < cols; c++)
                        cells[r, c] = __result.Cells[r, c];
                for (int i = 0; i < Rows.Count; i++)
                {
                    Row row = Rows[i];
                    __result.RowHeaders.Add(new DebriefingTableDTO.Header { Text = row.Label });
                    double best = 0;
                    if (!row.Section && row.Score != null)
                        best = columns.Where(s => s != null).Select(row.Score).DefaultIfEmpty(0).Max();
                    for (int c = 0; c < cols; c++)
                    {
                        CPlayerStatsScenario s = c < columns.Count ? columns[c] : null;
                        if (row.Section || s == null) { cells[oldRows + i, c] = new DebriefingTableDTO.Cell { Text = "" }; continue; }
                        double score = row.Score?.Invoke(s) ?? 0;
                        cells[oldRows + i, c] = new DebriefingTableDTO.Cell { Text = row.Value(s), IsHighlighted = best > 0 && score == best };
                    }
                }
                __result.Cells = cells;
                WriteRawLog(stats);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Battle stats: could not add the extra rows: " + e);
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
                foreach (CPlayerStatsScenario s in stats)
                {
                    sb.AppendLine($"--- {s.CharacterID}: rounds={s.RoundsPlayed} survived={s.PlayerSurvivedScenario}");
                    sb.AppendLine("  counts: " + string.Join(" ", typeof(CPlayerStats).GetProperties()
                        .Where(p => typeof(System.Collections.ICollection).IsAssignableFrom(p.PropertyType))
                        .Select(p => $"{p.Name}={(p.GetValue(s) as System.Collections.ICollection)?.Count ?? 0}")));
                    foreach (var d in s.DamageDealt ?? new List<CPlayerStatsDamage>())
                        sb.AppendLine($"  DEALT r{d.Round} {d.FinalDamageAmount} to {d.ActorWhoReceivedDamageMonsterType ?? d.ActorWhoReceivedDamageClassID} (hp {d.HealthBeforeDamage}) shielded={d.Shielded} avoided={d.Avoided} poison={d.PoisonDamage} melee={d.IsMelee} cause={d.CauseOfDamage} ability={d.ActionData?.ActingAbilityName} type={d.ActionData?.AbilityType} summon={d.PerformedBySummons}");
                    foreach (var d in s.DamageReceived ?? new List<CPlayerStatsDamage>())
                        sb.AppendLine($"  TAKEN r{d.Round} {d.FinalDamageAmount} from {d.ActorWhoDealtDamageMonsterType ?? d.ActorWhoDealtDamageClassID} shielded={d.Shielded} item={d.ItemShielded} avoided={d.Avoided} cause={d.CauseOfDamage}");
                    foreach (var k in s.Kills ?? new List<CPlayerStatsKill>())
                        sb.AppendLine($"  KILL r{k.Round} {k.ActorWhoDiedMonsterType ?? k.ActorWhoDiedClassID} type={k.ActorWhoDiedType} cause={k.CauseOfDeath} last={k.LastDamageAmount} hpBefore={k.PreDamageHealth} max={k.MaxHealth} summon={k.PerformedBySummons}");
                    foreach (var m in s.Modifiers ?? new List<CPlayerStatsModifiers>())
                        sb.AppendLine($"  MOD r{m.Round} {string.Join(",", m.Modifier ?? new List<string>())}");
                    foreach (var a in s.Abilities ?? new List<CPlayerStatsAbilities>())
                        sb.AppendLine($"  ABILITY r{a.Round} {a.ActionData?.ActingAbilityName} type={a.ActionData?.AbilityType} amount={a.Amount} targets={a.Targets} aoe={a.AreaEffect} adv={string.Join(",", a.Advantage ?? new List<bool>())} default={a.DefaultAction}");
                    foreach (var h in s.Heals ?? new List<CPlayerStatsHeal>())
                        sb.AppendLine($"  HEAL r{h.Round} {h.HealAmount} poisonRemoved={h.PoisonRemoved} woundRemoved={h.WoundRemoved}");
                    foreach (var c in s.LoseCards ?? new List<CPlayerStatsLoseCard>())
                        sb.AppendLine($"  LOSECARD r{c.Round} avoided={c.DamageAvoided}");
                    foreach (var e in s.Infusions ?? new List<CPlayerStatsElement>())
                        sb.AppendLine($"  INFUSE r{e.Round} {e.Element}");
                    foreach (var e in s.Consumed ?? new List<CPlayerStatsElement>())
                        sb.AppendLine($"  CONSUME r{e.Round} {e.Element}");
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
