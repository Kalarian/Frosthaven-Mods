using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BepInEx;
using FFSNet;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Desync recorder: logs enough on every co-op player's PC that, after a desync, everyone's files can be compared
    /// (tools/desync_compare.py) to find where the games split. Logging only; nothing in play changes. Online only.
    ///
    /// How the game syncs (read in its code): scenarios are lockstep - every PC runs the same rules on the same inputs
    /// (GameActions, ID [player:n]); the host relays them. DesyncDetector fingerprints the state at checkpoints (each
    /// turn, rest, round start): an "important" MD5 (HP, positions, conditions, hands, decks, elements) and a "full" one
    /// (also the RNGs). The host sends its fingerprints, only clients compare; on a mismatch the client fetches the
    /// host's full state dump, logs one line of difference and (major) ends the session. The game would save both dumps
    /// with StateDumper.SaveStateDump, but in the retail build that method is empty, and its Player.log is overwritten at
    /// the next launch. The outpost uses host-checked commands (already logged by CoopLog, mirrored here).
    ///
    /// Files, per game launch: FrosthavenMods\desync\&lt;yyyyMMdd-HHmmss&gt;\
    /// - timeline.txt: actions sent / queued / applied, side actions, checkpoint fingerprints (+ RNG positions), outpost
    ///   commands, desync events
    /// - steps.txt: every rules-engine message the game processes (round, type, actor), the finest-grained order
    /// - on a desync: state dumps (own, and the host's as received), the game's difference summary, a copy of Player.log;
    ///   a "desync now" signal goes to every player (riding on the game's LogReproStep text message) so they all save
    ///   their state too
    /// Ctrl+F7 zips the latest sessions + coop-log + Player logs to the desktop. The last 10 sessions are kept.
    /// </summary>
    [HarmonyPatch]
    internal static class DesyncRecorder
    {
        private const int KeepSessions = 10, ZipSessions = 3, MaxCaptures = 5;
        private const string Signal = "FHMODS-DESYNC";

        private static readonly object Gate = new object();
        private static string _dir;
        private static StreamWriter _timeline, _steps;
        private static int _stepsSinceFlush, _lastPollFrame = -1;
        private static float _lastFlush;
        private static readonly HashSet<string> Handled = new HashSet<string>();

        private static string Root => Path.Combine(Paths.GameRootPath, "FrosthavenMods", "desync");

        private static bool Online
        {
            get { try { return FFSNetwork.IsOnline; } catch { return false; } }
        }

        // ---- files

        private static bool Open()
        {
            if (_dir != null) return true;
            try
            {
                Directory.CreateDirectory(Root);
                foreach (string old in Directory.GetDirectories(Root).OrderByDescending(d => d).Skip(KeepSessions - 1))
                    try { Directory.Delete(old, true); } catch { }
                _dir = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(_dir);
                _timeline = new StreamWriter(Path.Combine(_dir, "timeline.txt"), true, Encoding.UTF8) { AutoFlush = true };
                _steps = new StreamWriter(Path.Combine(_dir, "steps.txt"), true, Encoding.UTF8);
                _timeline.WriteLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Frosthaven Mods {Plugin.Version}, game {Application.version}, " +
                                    $"role={CoopLog.Role()} me={Me()} =====");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Desync recorder: cannot write its files: " + e.Message);
                _dir = null;
                return false;
            }
        }

        private static string Me()
        {
            try { return PlayerRegistry.MyPlayer == null ? "?" : (FFSNetwork.IsHost ? "host" : "client") + PlayerRegistry.MyPlayer.PlayerID; }
            catch { return "?"; }
        }

        private static string Where()
        {
            try { return $"r{ScenarioRuleLibrary.GameState.RoundCount} {PhaseManager.PhaseType}"; }
            catch { return "-"; }
        }

        /// <summary>A line in timeline.txt (only in online games).</summary>
        internal static void Note(string line)
        {
            if (!Online) return;
            lock (Gate)
            {
                if (!Open()) return;
                try { _timeline.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{Me()}] {Where()} {line}"); } catch { }
            }
        }

        private static void Step(string line)
        {
            if (!Online) return;
            lock (Gate)
            {
                if (!Open()) return;
                try
                {
                    _steps.WriteLine(line);
                    if (++_stepsSinceFlush >= 200) { _steps.Flush(); _stepsSinceFlush = 0; }
                }
                catch { }
            }
        }

        private static void Flush()
        {
            lock (Gate)
            {
                try { _steps?.Flush(); _timeline?.Flush(); _stepsSinceFlush = 0; } catch { }
            }
        }

        private static void WriteFile(string name, string text)
        {
            lock (Gate)
            {
                if (!Open()) return;
                try
                {
                    string safe = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                    File.WriteAllText(Path.Combine(_dir, safe), text ?? "", Encoding.UTF8);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Desync recorder: could not write " + name + ": " + e.Message); }
            }
        }

        private static void CopyPlayerLog(string suffix)
        {
            try
            {
                string log = Application.consoleLogPath;
                if (string.IsNullOrEmpty(log) || !File.Exists(log)) return;
                // Player.log is open for writing by the game: read it shared.
                using (var s = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(s))
                    WriteFile("Player-" + suffix + ".log", r.ReadToEnd());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Desync recorder: could not copy Player.log: " + e.Message); }
        }

        // ---- actions (scenario lockstep)

        private static string Describe(GameAction a)
        {
            if (a == null) return "?";
            string data = a.SupplementaryDataString;
            if (data != null && data.Length > 80) data = data.Substring(0, 77) + "...";
            return $"{a} actor={a.ActorID} data={a.SupplementaryDataIDMin},{a.SupplementaryDataIDMed},{a.SupplementaryDataIDMax},{a.SupplementaryDataBoolean}" +
                   (string.IsNullOrEmpty(data) ? "" : " \"" + data + "\"");
        }

        [HarmonyPatch(typeof(Synchronizer), "SendGameActionToPlayer")]
        [HarmonyPostfix]
        private static void Sent(GameAction action, NetworkPlayer targetPlayer) =>
            Note($"SENT {Describe(action)} -> player{targetPlayer?.PlayerID}");

        [HarmonyPatch(typeof(ActionProcessor), nameof(ActionProcessor.QueueUpAction))]
        [HarmonyPrefix]
        private static void Queued(GameAction action) => Note("QUEUED " + Describe(action));

        [HarmonyPatch(typeof(ActionProcessor), "ProcessAction")]
        [HarmonyPrefix]
        private static void Applied(GameAction action) => Note("APPLY " + Describe(action));

        [HarmonyPatch(typeof(ActionProcessor), nameof(ActionProcessor.ProcessSideAction))]
        [HarmonyPrefix]
        private static void Side(GameAction action) => Note("SIDE " + Describe(action));

        // ---- rules-engine messages, in the order this PC processes them

        [HarmonyPatch(typeof(Choreographer), "ProcessMessage")]
        [HarmonyPrefix]
        private static void Message(CMessageData message)
        {
            if (message == null || !Online) return;
            string actor = "";
            try
            {
                CActor a = message.m_ActorSpawningMessage;
                if (a != null) actor = $" {a.Class?.ID} {(a.ActorGuid ?? "").Substring(0, Math.Min(8, (a.ActorGuid ?? "").Length))}";
            }
            catch { }
            Step($"{Where()} {message.m_Type}{actor}");
        }

        // ---- checkpoint fingerprints

        [HarmonyPatch(typeof(DesyncDetector), "GenerateStateDumpAndFastAutoSave")]
        [HarmonyPostfix]
        private static void Checkpoint(StateHashToken __result)
        {
            if (__result == null) return;
            Note($"CHECKPOINT {Token(__result)} rng={Rng()}");
            Step($"{Where()} -- checkpoint important={__result.importantStateHash}");
        }

        private static string Token(StateHashToken t) =>
            t == null ? "-" : $"round={t.round} phase={t.phaseType} actor={t.playerID} action#{t.actionID} lastGameAction={t.lastGameActionID} important={t.importantStateHash} full={t.fullStateHash}";

        /// <summary>Where each of the scenario's random number generators is in its sequence (position + a checksum of
        /// its table). Read at checkpoints only: the rules engine runs on another thread, so between checkpoints the
        /// value depends on timing, not on the game.</summary>
        private static string Rng()
        {
            try
            {
                ScenarioState state = ScenarioManager.CurrentScenarioState;
                if (state == null) return "-";
                return string.Join(" ", new[] { "m_ScenarioRNG", "m_EnemyIDRNG", "m_EnemyAbilityCardRNG", "m_GuidRNG" }.Select(f =>
                {
                    var rng = Traverse.Create(state).Field(f);
                    int[] table = rng.Field("SeedArray").GetValue<int[]>();
                    uint sum = 2166136261;
                    if (table != null) foreach (int v in table) unchecked { sum = (sum ^ (uint)v) * 16777619; }
                    return $"{f.Substring(2).Replace("RNG", "")}={rng.Field("inext").GetValue<int>()}.{rng.Field("inextp").GetValue<int>()}.{sum:X8}";
                }));
            }
            catch { return "?"; }
        }

        // ---- desyncs

        private static StateHashToken _hostHash;

        [HarmonyPatch(typeof(DesyncDetector), "ClientSyncCheck")]
        [HarmonyPrefix]
        private static void BeforeClientCheck(StateHashToken hostHash) => _hostHash = hostHash;

        /// <summary>The client's comparison with the host's fingerprint (where the game detects desyncs).</summary>
        [HarmonyPatch(typeof(DesyncDetector), "ClientSyncCheck")]
        [HarmonyPostfix]
        private static void AfterClientCheck()
        {
            StateHashToken host = _hostHash, mine = DesyncDetector.s_StateHashHistory.LastOrDefault();
            if (host == null || mine == null) return;
            bool major = host.importantStateHash != mine.importantStateHash;
            bool minor = !major && host.fullStateHash != null && host.fullStateHash != mine.fullStateHash;
            Note($"SYNC CHECK {(major ? "MAJOR DESYNC" : minor ? "MINOR DESYNC" : "ok")} host: {Token(host)} | mine: {Token(mine)}");
            if (major || minor) Desync((major ? "MAJOR" : "minor") + $"-r{host.round}-a{host.actionID}", "this client found it");
        }

        /// <summary>Saves this PC's evidence once per desync, and tells the other players to do the same.</summary>
        private static void Desync(string id, string how, bool tellOthers = true)
        {
            lock (Gate)
            {
                if (!Handled.Add(id)) return;
                // After a desync most later checks mismatch too; the first few captures are the useful ones.
                if (Handled.Count > MaxCaptures) { Note($"DESYNC {id}: {how} (not saved: {MaxCaptures} captures already this session)"); return; }
            }
            Note($"DESYNC {id}: {how}; saving this PC's state");
            byte[] state = DesyncDetector.s_LastStateDump;
            WriteFile($"state-{Me()}-{id}.txt", state == null ? "(no state dump: the game made none)" : Encoding.UTF8.GetString(state));
            CopyPlayerLog(id);
            Flush();
            if (!tellOthers) return;
            try
            {
                Synchronizer.SendSideAction(GameActionType.LogReproStep, null, canBeUnreliable: false, sendToHostOnly: false, 0, 0, 0, dataBool: false,
                    $"{Signal} {id} from {Me()}");
            }
            catch (Exception e) { Note("could not tell the other players: " + e.Message); }
        }

        /// <summary>Another player's "desync now" signal (sent as a game repro-log text). The host passes it on so every
        /// client hears it.</summary>
        [HarmonyPatch(typeof(ReproStepsLogger), nameof(ReproStepsLogger.LogRemoteEvent))]
        [HarmonyPrefix]
        private static void SignalReceived(GameAction action)
        {
            string text = action?.SupplementaryDataString;
            int at = text?.IndexOf(Signal, StringComparison.Ordinal) ?? -1;
            if (at < 0) return;
            string[] parts = text.Substring(at + Signal.Length).Trim().Split(' ');
            string id = parts.Length > 0 ? parts[0] : "unknown";
            bool host = false;
            try { host = FFSNetwork.IsHost; } catch { }
            Desync(id, "another player found it (" + text.Substring(at) + ")", tellOthers: host);
        }

        /// <summary>A client asking for the host's state dump means it found a desync: the host's side of it.</summary>
        [HarmonyPatch(typeof(GHHostCallbacks), nameof(GHHostCallbacks.OnEvent), new[] { typeof(GameDataRequestToken), typeof(FFSNet.Backend.NetworkBackendPlayer) })]
        [HarmonyPrefix]
        private static void HostAskedForState(GameDataRequestToken request)
        {
            if (request == null || (DataActionType)request.DataActionID != DataActionType.SendStateDumpFromHost) return;
            Desync("asked-" + DateTime.Now.ToString("HHmmss"), "a client asked for the host's state dump (it found a desync)", tellOthers: false);
        }

        /// <summary>The game's empty dump saver, called with the host's and the client's dump ("host-MAJOR" ...).</summary>
        [HarmonyPatch(typeof(StateDumper), nameof(StateDumper.SaveStateDump), new[] { typeof(string), typeof(string) })]
        [HarmonyPrefix]
        private static void SaveDump(string stateDump, string fileName) =>
            WriteFile($"game-dump-{fileName}-{DateTime.Now:HHmmss}.txt", stateDump);

        [HarmonyPatch(typeof(DesyncDetector), nameof(DesyncDetector.OnHostStateDumpReceived))]
        [HarmonyPostfix]
        private static void DiffReady()
        {
            if (DesyncDetector.s_DiffTitle == null) return;
            Note("GAME'S DIFF: " + DesyncDetector.s_DiffTitle);
            WriteFile($"game-diff-{DateTime.Now:HHmmss}.txt", DesyncDetector.s_DiffTitle + "\n\n" + DesyncDetector.s_DiffDescription);
        }

        /// <summary>The game ending the session for a desync (the error screen): last chance to save everything.</summary>
        [HarmonyPatch(typeof(FFSNetwork), nameof(FFSNetwork.HandleDesync))]
        [HarmonyPrefix]
        private static void GameHandlesDesync(Exception ex)
        {
            Note("GAME DESYNC ERROR: " + ex?.Message);
            Desync("error-" + DateTime.Now.ToString("HHmmss"), "the game showed its desync error (" + ex?.Message + ")");
        }

        // ---- Ctrl+F7: zip for sending; also flushes the steps file now and then

        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Poll()
        {
            if (Time.frameCount == _lastPollFrame) return;
            _lastPollFrame = Time.frameCount;
            if (Time.unscaledTime - _lastFlush > 2f) { _lastFlush = Time.unscaledTime; Flush(); }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed || !kb.f7Key.wasPressedThisFrame) return;
            GiveResourcesPatches.Safe("make the desync zip", MakeZip);
        }

        private static void MakeZip()
        {
            Flush();
            Directory.CreateDirectory(Root);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string zip = Path.Combine(desktop, $"Frosthaven-desync-{Environment.UserName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var file = new FileStream(zip, FileMode.Create))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (string session in Directory.GetDirectories(Root).OrderByDescending(d => d).Take(ZipSessions))
                    foreach (string f in Directory.GetFiles(session))
                        Add(archive, f, "desync/" + Path.GetFileName(session) + "/" + Path.GetFileName(f));
                Add(archive, Path.Combine(Paths.GameRootPath, "FrosthavenMods", "coop-log.txt"), "coop-log.txt");
                string log = Application.consoleLogPath;
                if (!string.IsNullOrEmpty(log))
                {
                    Add(archive, log, "Player.log");
                    Add(archive, Path.Combine(Path.GetDirectoryName(log), "Player-prev.log"), "Player-prev.log");
                }
            }
            Plugin.Log.LogInfo("Desync recorder: zip written to " + zip);
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch { }
        }

        private static void Add(ZipArchive archive, string path, string name)
        {
            if (!File.Exists(path)) return;
            ZipArchiveEntry entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = entry.Open())
                src.CopyTo(dst);
        }
    }
}
