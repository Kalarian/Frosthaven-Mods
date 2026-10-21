using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// Scenario map: after each of the game's autosaves, writes the campaign's scenario progress (completed, unlocked and
    /// locked-out scenarios, read sections, the calendar, puzzles, personal quests, stickers) to
    /// &lt;game&gt;\FrosthavenMods\scenarios\progress-&lt;party&gt;.txt for tools/scenario_map.py, which draws the unlock
    /// map coloured by that progress. The save files themselves are a binary .NET format, so this is the readable copy.
    ///
    /// Every player's game writes its own copy (each has the full campaign state in co-op). It reads the state and sends
    /// nothing: display only.
    /// </summary>
    [HarmonyPatch]
    internal static class ScenarioProgress
    {
        private static string _lastText;

        [HarmonyPatch(typeof(SaveData), nameof(SaveData.AutoSaveCurrentAdventureData))]
        [HarmonyPostfix]
        private static void AfterAutoSave(ref Task __result)
        {
            if (__result != null && ModRules.IsOn(ModRules.ScenarioMap)) __result = Then(__result);
        }

        private static async Task Then(Task autosave)
        {
            await autosave;
            try { Write(); }
            catch (Exception e) { Plugin.Log.LogWarning("Scenario map: could not write the progress file: " + e.Message); }
        }

        private static void Write()
        {
            GlobalData global = SaveData.Instance?.Global;
            PartyAdventureData data = global?.CurrentAdventureData;
            if (data == null || global.GameMode != EGameMode.Campaign || data.PartyName == "Tutorial") return;
            CampaignState state = data.FrosthavenSaveData?.GameState;
            if (state?.Outpost == null) return;

            var lines = new List<string>
            {
                "# Frosthaven Mods scenario progress, for tools/scenario_map.py",
                "party\t" + Clean(data.PartyName),
                "week\t" + state.Outpost.Week,
                "travel\t" + (state.CanPlaySledScenarios ? "sled " : "") + (state.CanPlayBoatScenarios ? "boat " : "") +
                (state.CanPlayClimbingGearScenarios ? "climbing" : ""),
            };
            if (state.ActiveScenario != null) lines.Add("active\t" + state.ActiveScenario.ScenarioID.ID);
            foreach (ScenarioState s in state.UnlockedScenarios) lines.Add(Scenario(s, false));
            foreach (ScenarioState s in state.LockedScenarios) lines.Add(Scenario(s, true));
            foreach (CampaignSectionID id in state.ActivatedCampaignSectionIDs) lines.Add("section\t" + id.ID);
            foreach (CampaignSectionID id in state.PendingCampaignSectionIDs) lines.Add("pending\t" + id.ID);
            foreach (CalendarEventState e in state.Outpost.CalendarEvents.Where(e => e != null && !e.Triggered))
                lines.Add("calendar\t" + e.SectionID.ID + "\t" + e.TriggerAtWeek);
            foreach (CampaignSectionID id in state.Outpost.JobPostings) lines.Add("job\t" + id.ID);
            foreach (PuzzleState p in state.UnlockedPuzzles)
                lines.Add("puzzle\t" + p.ID.ID + "\t" + (p.IsCompleted ? "solved" : "open"));
            foreach (PersonalQuestState q in state.CompletedPersonalQuests) lines.Add("quest\t" + q.ID.ID + "\tdone");
            foreach (CharacterState c in state.PartyCharacters.Where(c => c.PersonalQuest != null))
                lines.Add("quest\t" + c.PersonalQuest.ID.ID + "\t" + Clean(c.Name));
            foreach (CampaignStickerID id in state.Stickers) lines.Add("sticker\t" + id.ID);

            string text = string.Join("\n", lines) + "\n";
            if (text == _lastText) return;
            string safe = new string(data.PartyName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            string dir = Path.Combine(Paths.GameRootPath, "FrosthavenMods", "scenarios");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "progress-" + safe + ".txt"), text);
            _lastText = text;
        }

        private static string Scenario(ScenarioState s, bool locked)
        {
            var flags = new List<string>();
            if (s.Completed) flags.Add("completed");
            if (locked || s.Locked) flags.Add("locked");
            if (s.Mandatory) flags.Add("mandatory");
            if (s.Completed) flags.Add("ending=" + s.ConclusionIndex.ToString(CultureInfo.InvariantCulture));
            return "scenario\t" + s.ScenarioID.ID + "\t" + (flags.Count == 0 ? "unlocked" : string.Join(",", flags)) + "\t" +
                   (s.UnlockedByScenarioID.HasValue ? s.UnlockedByScenarioID.Value.ID.ToString(CultureInfo.InvariantCulture) : "");
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}
