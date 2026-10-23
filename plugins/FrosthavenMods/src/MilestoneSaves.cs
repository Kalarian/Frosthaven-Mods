using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FHGUI;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// Milestone saves: a named save at the start of each scenario and when the outpost opens after one, so a bad state
    /// (a desync, a crash, a misclick) noticed later can still be undone. The game keeps only its newest 15 autosaves,
    /// a few minutes apart, so they cover the last hour or two of play; named saves are never deleted by the game.
    ///
    /// Made with the game's own Save menu call (SaveData.ManualSaveCurrentAdventureData): the scenario one right after
    /// the game's own autosave at round 0, the outpost one on the frame the scenario result has landed on the map (the
    /// game autosaves in the outpost only on a few actions, so that one can't ride on an autosave). They show in the
    /// normal Load list. Only the
    /// newest <see cref="Keep"/> of ours are kept; the player's own named saves and the autosaves are never touched.
    /// Host (or solo) only: saves live on the host's PC, and nothing is sent to other players. Not in Iron Man (no named
    /// saves there).
    /// </summary>
    [HarmonyPatch]
    internal static class MilestoneSaves
    {
        internal const int Keep = 20;
        private const string Prefix = "Mod backup - ";

        private static bool _outpostSaveDue, _saving;
        private static uint _endedScenario;
        private static int _lastPollFrame = -1;
        private static string _last;

        /// <summary>A scenario ended (not restarted): back up the outpost as soon as it is quiet. The game itself autosaves
        /// in the outpost only on a few actions (a character created, a scenario entered), so this one is ours.</summary>
        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPostfix]
        private static void ScenarioEnded(ScenarioResult result)
        {
            if (result == null || result.isRestarting) return;
            CampaignState state = Singleton<FHMapChoreographer>.InstanceFast?.GameState ?? SaveData.Instance?.Global?.CurrentAdventureData?.FrosthavenSaveData?.GameState;
            _endedScenario = state?.ActiveScenario?.ScenarioID.ID ?? 0;
            _outpostSaveDue = true;
        }

        /// <summary>Makes the outpost backup once the map is up and the game isn't saving: on the frame the scenario result
        /// lands, before loot distribution and the outpost events, which is the point to come back to.</summary>
        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Poll()
        {
            if (!_outpostSaveDue || _saving || UnityEngine.Time.frameCount == _lastPollFrame) return;
            _lastPollFrame = UnityEngine.Time.frameCount;
            try
            {
                if (!ModRules.IsOn(ModRules.MilestoneSaves)) { _outpostSaveDue = false; return; }
                GlobalData global = SaveData.Instance?.Global;
                PartyAdventureData data = global?.CurrentAdventureData;
                if (data == null || global.CurrentGameState != EGameState.Map || SaveData.Instance.IsSavingData) return;
                FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
                if (view == null || !view.HasInit || view.CurrentState == null) return;
                _outpostSaveDue = false;
                if (FFSNetwork.IsOnline && !FFSNetwork.IsHost) return;
                if (data.IsIronMan || global.GameMode != EGameMode.Campaign || data.PartyName == "Tutorial") return;
                uint week = data.FrosthavenSaveData?.GameState?.Outpost?.Week ?? 0;
                string name = _endedScenario > 0 ? $"{Prefix}week {week} - after scenario {_endedScenario}" : $"{Prefix}week {week} - outpost";
                if (name == _last) return;
                _saving = true;
                SaveOutpost(name);
            }
            catch (Exception e)
            {
                _outpostSaveDue = false;
                Plugin.Log.LogWarning("Milestone saves: the outpost backup failed: " + e.Message);
            }
        }

        private static async void SaveOutpost(string name)
        {
            try
            {
                _last = name;
                await SaveData.Instance.ManualSaveCurrentAdventureData(name);
                CoopLog.Write("MILESTONE SAVE '" + name + "'");
                await Trim();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Milestone saves: could not save: " + e.Message); }
            finally { _saving = false; }
        }

        [HarmonyPatch(typeof(SaveData), nameof(SaveData.AutoSaveCurrentAdventureData))]
        [HarmonyPostfix]
        private static void AfterAutoSave(ref Task __result)
        {
            if (__result != null && ModRules.IsOn(ModRules.MilestoneSaves)) __result = Then(__result);
        }

        // The game awaits the returned task (a scenario autosave holds the game until it finishes), so ours runs inside it,
        // in the same context as the game's own save.
        private static async Task Then(Task autosave)
        {
            await autosave;
            try
            {
                string name = MilestoneName();
                if (name == null || name == _last) return;
                _last = name;
                await SaveData.Instance.ManualSaveCurrentAdventureData(name);
                CoopLog.Write("MILESTONE SAVE '" + name + "'");
                await Trim();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Milestone saves: could not save: " + e.Message);
            }
        }

        private static string MilestoneName()
        {
            if (FFSNetwork.IsOnline && !FFSNetwork.IsHost) return null;
            GlobalData global = SaveData.Instance?.Global;
            PartyAdventureData data = global?.CurrentAdventureData;
            if (data == null || data.IsIronMan || global.GameMode != EGameMode.Campaign || data.PartyName == "Tutorial") return null;
            CampaignState state = data.FrosthavenSaveData?.GameState;
            if (state?.Outpost == null) return null;
            uint week = state.Outpost.Week;

            if (global.CurrentGameState != EGameState.Scenario) return null;   // the outpost backup is made by Poll, not from an autosave
            // The game autosaves at the start of a scenario (round 0) and at each round after.
            if (state.ActiveScenario == null || ScenarioRuleLibrary.GameState.RoundCount > 1) return null;
            return $"{Prefix}week {week} - scenario {state.ActiveScenario.ScenarioID.ID} start";
        }

        /// <summary>Delete our oldest backups of this campaign beyond <see cref="Keep"/> (the save and its screenshot), the
        /// way the game trims its autosaves.</summary>
        private static async Task Trim()
        {
            PartyAdventureData data = SaveData.Instance?.Global?.CurrentAdventureData;
            if (data == null) return;
            string dir = await data.PartySaveDir;
            if (!Directory.Exists(dir)) return;
            foreach (FileInfo old in new DirectoryInfo(dir).GetFiles(Prefix + "*.dat")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep))
            {
                try
                {
                    string png = Path.ChangeExtension(old.FullName, ".png");
                    old.Delete();
                    if (File.Exists(png)) File.Delete(png);
                    CoopLog.Write("MILESTONE SAVE removed '" + Path.GetFileNameWithoutExtension(old.Name) + "'");
                }
                catch (Exception e) { Plugin.Log.LogWarning("Milestone saves: could not delete " + old.Name + ": " + e.Message); }
            }
        }
    }
}
