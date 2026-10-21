using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using FHRL;
using HarmonyLib;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Co-op test logging. Every line goes to the BepInEx log and is appended to FrosthavenMods\coop-log.txt (BepInEx
    /// overwrites its own log at each launch). tools/coop_compare.py compares two players' files.
    /// - every outpost multiplayer command: sender, which copy ran it (local = what this player sees; host-check = the
    ///   host's authoritative copy), and whether it failed (a failure on host-check means everyone rolls it back)
    /// - house-rule changes and receipts (HouseRulesPatches), gifts (GiveResourcesCommands)
    /// - snapshots: every character's gold/materials/items (owned and worn), the herb supply, the rules, and a hash of all of it; taken after
    ///   each gift / rule change and on Ctrl+F8 ("checkpoint #n" - both players press it together to compare)
    /// </summary>
    [HarmonyPatch]
    internal static class CoopLog
    {
        private static string _file;
        private static int _checkpoint;
        private static int _lastPollFrame = -1;

        public static void SessionStart()
        {
            try
            {
                string dir = Path.Combine(Paths.GameRootPath, "FrosthavenMods");
                Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, "coop-log.txt");
                Write($"===== session start {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Frosthaven Mods {Plugin.Version} =====");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Coop log file unavailable: " + e.Message); }
        }

        public static void Write(string line)
        {
            Plugin.Log.LogInfo("Coop " + line);
            DesyncRecorder.Note("OUTPOST " + line);
            if (_file == null) return;
            try { File.AppendAllText(_file, $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); } catch { }
        }

        /// <summary>Which copy of the campaign a controller is: "local" (what this player sees) or "host-check".</summary>
        public static string Copy(MultiplayerGameController game)
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || game == null) return "?";
            if (map.GameServer != null && map.GameServer.GameController == game) return "host-check";
            if (map.GameClient != null && map.GameClient.GameController == game) return "local";
            return "other";
        }

        public static string Role()
        {
            if (!FFSNetwork.IsOnline) return "solo";
            return FFSNetwork.IsHost ? "host" : "client";
        }

        public static string Me() => Singleton<FHMapChoreographer>.InstanceFast?.GameClient?.PlayerID.ToString() ?? "?";

        /// <summary>Gold/materials/items of every character, herbs, rules, and a short hash of all of it.</summary>
        public static void Snapshot(string label, MultiplayerGameController game = null)
        {
            try
            {
                var map = Singleton<FHMapChoreographer>.InstanceFast;
                game = game ?? map?.GameClient?.GameController;
                CampaignState state = game?.CampaignController?.State;
                if (state == null) { Write($"SNAPSHOT {label}: no campaign"); return; }
                var body = new StringBuilder();
                foreach (CharacterState c in state.AllCharacters.Where(c => c.PartyState != CharacterPartyStateType.Retired)
                             .OrderBy(c => c.ClassID.ID).ThenBy(c => c.Name))
                {
                    string items = string.Join(",", c.OwnedItemIDs.Select(i => i.ID).OrderBy(i => i));
                    string worn = string.Join(",", c.EquipedItemIDs.Select(i => i.ID).OrderBy(i => i));
                    body.Append($" | {GiveRules.Name(c)}({c.ClassID.ID.Replace("ID", "")},{c.PartyState}) gold={c.Gold} lumber={c.Resources.Lumber} metal={c.Resources.Metal} hide={c.Resources.Hide}" +
                                $" items={(items.Length == 0 ? "-" : items)} worn={(worn.Length == 0 ? "-" : worn)}");
                }
                OutpostResources o = state.Outpost.Resources;
                body.Append($" | herbs={o.Arrowvine},{o.Axenut},{o.Corpsecap},{o.Flamefruit},{o.Rockroot},{o.Snowthistle}");
                body.Append($" | rules={(int)ModRules.Current:X}");
                string content = body.ToString();
                Write($"SNAPSHOT {label} [{Copy(game)}] role={Role()} me={Me()} hash={Hash(content)}{content}");
            }
            catch (Exception e) { Write($"SNAPSHOT {label}: failed {e.Message}"); }
        }

        private static string Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s) { h ^= ch; h *= 16777619; }
                return h.ToString("X8");
            }
        }

        // ---- every outpost multiplayer command, on every copy it runs on

        [HarmonyPatch(typeof(MultiplayerGameController), nameof(MultiplayerGameController.ExecuteCommand))]
        [HarmonyFinalizer]
        private static Exception LogCommand(MultiplayerGameController __instance, MultiplayerCommand command, Exception __exception)
        {
            try
            {
                string name = command?.Name ?? "?";
                if (name.EndsWith("GetGameStateHash")) return __exception;
                string result = __exception == null ? "ok" : "FAILED: " + (__exception.InnerException ?? __exception).Message;
                Write($"CMD {name} from={command?.PlayerID} on={Copy(__instance)} role={Role()} -> {result}");
            }
            catch { }
            return __exception; // never change the game's behaviour
        }

        // ---- Ctrl+F8 checkpoint

        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Poll()
        {
            if (Time.frameCount == _lastPollFrame) return;
            _lastPollFrame = Time.frameCount;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed || !kb.f8Key.wasPressedThisFrame) return;
            _checkpoint++;
            Snapshot("checkpoint #" + _checkpoint);
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map?.GameServer != null) Snapshot("checkpoint #" + _checkpoint, map.GameServer.GameController);
        }
    }
}
