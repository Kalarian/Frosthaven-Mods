using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FrosthavenInstaller
{
    /// <summary>
    /// One-file installer for the Frosthaven mod pack (the pack zip is an embedded resource; a newer one is fetched
    /// from the latest GitHub release). A mod is a set of unified diffs / new files against the game's
    /// Frosthaven_Data/StreamingAssets/Rulebase/Base.ruleset. Install rebuilds that file from a vanilla backup plus
    /// every mod in the pack, so no game data is ever shipped and a Steam update is picked up automatically.
    /// The backup and state live in &lt;game&gt;\FrosthavenMods\ (shared with the author's tools/fh_mods.py).
    /// Double-click: a small window. Command line: "&lt;gameFolder&gt; install [offline]" or "&lt;gameFolder&gt; uninstall"
    /// runs without a window and writes FrosthavenMods\install-log.txt.
    /// </summary>
    internal static class Program
    {
        internal const string GameExe = "Frosthaven.exe";
        internal const string ProcessName = "Frosthaven";
        internal const string SteamFolder = "Frosthaven";
        internal const string AppId = "2347080";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && Directory.Exists(args[0]))
            {
                string dir = args[0];
                bool offline = args.Any(a => a.Equals("offline", StringComparison.OrdinalIgnoreCase));
                var log = new StringBuilder();
                int code;
                try
                {
                    if (!File.Exists(Path.Combine(dir, GameExe))) { log.AppendLine("No " + GameExe + " in " + dir); code = 1; }
                    else if (args[1].Equals("uninstall", StringComparison.OrdinalIgnoreCase)) code = Pack.Uninstall(dir, log);
                    else
                    {
                        string zip = offline ? null : Updater.FetchNewerZip(dir, log);
                        code = Pack.Install(dir, log, zip);
                    }
                }
                catch (Exception e) { log.AppendLine("ERROR: " + e); code = 2; }
                try
                {
                    Directory.CreateDirectory(State.Folder(dir));
                    File.WriteAllText(Path.Combine(State.Folder(dir), "install-log.txt"), log.ToString(), new UTF8Encoding(false));
                }
                catch { }
                return code;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    // ================================================================== the .ruleset archive

    internal sealed class RsEntry
    {
        public string Name;
        public int Parser;
        public int Binary;
        public byte[] Data;
    }

    /// <summary>Format from FH.YML.RulebaseFile: header char[12] "02392fe1" + uint32 count; per entry char[128] name,
    /// int32 parser type, int32 isBinary, uint32 offset (from the end of the index), uint32 size; then the data.</summary>
    internal static class Ruleset
    {
        private const string Magic = "02392fe1";
        private const int HeaderSize = 16, EntrySize = 144, NameSize = 128;
        public const int CustomLevel = 255;

        private static readonly Dictionary<string, int> ParserIds = new Dictionary<string, int>
        {
            {"RemoveYML",1},{"HeroSummon",2},{"AttackModifier",3},{"AttackModifierDeck",4},{"Character",5},{"AbilityCard",6},
            {"ItemCard",7},{"Perk",8},{"Enhancements",9},{"IconGlossary",10},{"EnemyCard",11},{"Enemy",12},{"MonsterData",13},
            {"TreasureTable",14},{"Rooms",15},{"ScenarioAbility",16},{"Scenario",17},{"Difficulty",18},{"RoadEvent",19},
            {"VisibilitySphere",20},{"Temple",21},{"Village",22},{"Quest",23},{"Message",24},{"Headquarters",25},{"CityEvent",26},
            {"InitialEvents",27},{"BattleGoal",28},{"PersonalQuest",29},{"StoreLocation",30},{"ItemConfig",31},
            {"AbilityCardConfig",32},{"CharacterConfig",33},{"MonsterConfig",34},{"MapConfig",35},{"CharacterResources",36},
            {"GameMode",37},{"FHScenario",38},{"FHItem",39},{"CampaignSection",40},{"FHEnhancement",41},{"Mastery",42},{"PetCard",43},
        };

        public static List<RsEntry> Read(byte[] d)
        {
            if (d.Length < HeaderSize) throw new InvalidDataException("Base.ruleset is too short");
            if (CString(d, 0, 12) != Magic) throw new InvalidDataException("Base.ruleset is not a Rulebase file");
            uint count = BitConverter.ToUInt32(d, 12);
            long dataStart = HeaderSize + (long)count * EntrySize;
            if (dataStart > d.Length) throw new InvalidDataException("Base.ruleset index is truncated");
            var list = new List<RsEntry>((int)count);
            for (int i = 0; i < count; i++)
            {
                int o = HeaderSize + i * EntrySize;
                uint off = BitConverter.ToUInt32(d, o + 136), size = BitConverter.ToUInt32(d, o + 140);
                long start = dataStart + off;
                if (start + size > d.Length) throw new InvalidDataException("Base.ruleset entry " + i + " runs past the end");
                var data = new byte[size];
                Buffer.BlockCopy(d, (int)start, data, 0, (int)size);
                list.Add(new RsEntry { Name = CString(d, o, NameSize), Parser = BitConverter.ToInt32(d, o + 128), Binary = BitConverter.ToInt32(d, o + 132), Data = data });
            }
            return list;
        }

        public static byte[] Write(List<RsEntry> entries)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Fixed(Encoding.ASCII.GetBytes(Magic), 12));
                w.Write((uint)entries.Count);
                uint offset = 0;
                foreach (RsEntry e in entries)
                {
                    byte[] name = Encoding.UTF8.GetBytes(e.Name);
                    if (name.Length >= NameSize) throw new InvalidDataException("file name too long for the ruleset: " + e.Name);
                    w.Write(Fixed(name, NameSize));
                    w.Write(e.Parser); w.Write(e.Binary); w.Write(offset); w.Write((uint)e.Data.Length);
                    offset += (uint)e.Data.Length;
                }
                foreach (RsEntry e in entries) w.Write(e.Data);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>After the last entry of the same parser type, else before the first higher type (the game loads
        /// runs of one parser type as a batch, in enum order).</summary>
        public static void InsertByParser(List<RsEntry> entries, RsEntry n)
        {
            int idx = -1;
            for (int i = 0; i < entries.Count; i++) if (entries[i].Parser == n.Parser) idx = i + 1;
            if (idx < 0) { idx = entries.FindIndex(e => e.Parser > n.Parser); if (idx < 0) idx = entries.Count; }
            entries.Insert(idx, n);
        }

        public static int ParserFromFile(string name, byte[] data)
        {
            if (name.EndsWith(".lvldat", StringComparison.OrdinalIgnoreCase)) return CustomLevel;
            string text = Encoding.UTF8.GetString(data);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            string first = text.Split('\n')[0];
            int colon = first.IndexOf(':');
            int id;
            if (colon < 0 || !ParserIds.TryGetValue(first.Substring(colon + 1).Trim(), out id))
                throw new InvalidDataException(name + ": first line must be 'Parser: <Type>'");
            return id;
        }

        private static string CString(byte[] d, int offset, int max)
        {
            int n = 0;
            while (n < max && d[offset + n] != 0) n++;
            return Encoding.UTF8.GetString(d, offset, n);
        }

        private static byte[] Fixed(byte[] src, int size)
        {
            var b = new byte[size];
            Buffer.BlockCopy(src, 0, b, 0, Math.Min(src.Length, size));
            return b;
        }
    }

    // ================================================================== unified diffs on raw bytes (CRLF-safe)

    internal sealed class PatchFailed : Exception { public PatchFailed(string m) : base(m) { } }

    internal static class Patch
    {
        private static readonly byte[] NoEol = Encoding.ASCII.GetBytes("\\ No newline at end of file\n");

        private sealed class Hunk { public int OldStart, OldLen; public List<byte[]> Old = new List<byte[]>(), New = new List<byte[]>(); public bool LastOld, LastNew; }

        public static List<byte[]> SplitLines(byte[] d)
        {
            var lines = new List<byte[]>();
            int start = 0;
            for (int i = 0; i < d.Length; i++)
                if (d[i] == (byte)'\n') { lines.Add(Slice(d, start, i + 1 - start)); start = i + 1; }
            if (start < d.Length) lines.Add(Slice(d, start, d.Length - start));
            return lines;
        }

        private static List<Hunk> Parse(byte[] diff)
        {
            var hunks = new List<Hunk>();
            Hunk cur = null;
            var segments = new List<byte[]>();
            int s = 0;
            for (int i = 0; i <= diff.Length; i++)
                if (i == diff.Length || diff[i] == (byte)'\n') { segments.Add(Slice(diff, s, i - s)); s = i + 1; }
            for (int k = 2; k < segments.Count; k++)   // skip the ---/+++ header lines
            {
                byte[] seg = segments[k];
                if (seg.Length == 0) continue;
                byte[] line = new byte[seg.Length + 1];
                Buffer.BlockCopy(seg, 0, line, 0, seg.Length);
                line[seg.Length] = (byte)'\n';                // split on \n only, so a CRLF line keeps its \r
                if (seg.Length >= 2 && seg[0] == '@' && seg[1] == '@')
                {
                    var m = Regex.Match(Encoding.ASCII.GetString(seg), @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
                    if (!m.Success) throw new PatchFailed("bad hunk header");
                    cur = new Hunk { OldStart = int.Parse(m.Groups[1].Value), OldLen = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1 };
                    hunks.Add(cur);
                }
                else if (cur != null && Same(line, NoEol))
                {
                    if (cur.LastOld) cur.Old[cur.Old.Count - 1] = Slice(cur.Old[cur.Old.Count - 1], 0, cur.Old[cur.Old.Count - 1].Length - 1);
                    if (cur.LastNew) cur.New[cur.New.Count - 1] = Slice(cur.New[cur.New.Count - 1], 0, cur.New[cur.New.Count - 1].Length - 1);
                }
                else if (cur != null && (line[0] == ' ' || line[0] == '-' || line[0] == '+'))
                {
                    byte[] body = Slice(line, 1, line.Length - 1);
                    cur.LastOld = line[0] != '+'; cur.LastNew = line[0] != '-';
                    if (cur.LastOld) cur.Old.Add(body);
                    if (cur.LastNew) cur.New.Add(body);
                }
            }
            return hunks;
        }

        /// <summary>Apply a diff; like GNU patch's default fuzz 2, a hunk whose exact context is gone is retried with
        /// up to 2 outer context lines per side dropped (and a warning logged).</summary>
        public static byte[] Apply(byte[] data, byte[] diff, string label, StringBuilder log)
        {
            List<byte[]> lines = SplitLines(data);
            int shift = 0, n = 0;
            foreach (Hunk h in Parse(diff))
            {
                n++;
                int expected = (h.OldLen > 0 ? h.OldStart - 1 : h.OldStart) + shift;
                int lead = 0, trail = 0, min = Math.Min(h.Old.Count, h.New.Count);
                while (lead < min && Same(h.Old[lead], h.New[lead])) lead++;
                while (trail < min - lead && Same(h.Old[h.Old.Count - 1 - trail], h.New[h.New.Count - 1 - trail])) trail++;
                int pos = -1, usedFuzz = 0;
                List<byte[]> oldPart = null, newPart = null;
                for (int fuzz = 0; fuzz <= 2 && pos < 0; fuzz++)
                {
                    int a = Math.Min(fuzz, lead), b = Math.Min(fuzz, trail);
                    oldPart = h.Old.GetRange(a, h.Old.Count - a - b);
                    newPart = h.New.GetRange(a, h.New.Count - a - b);
                    if (oldPart.Count == 0 && fuzz > 0) break;
                    int best = int.MaxValue;
                    for (int i = 0; i + oldPart.Count <= lines.Count; i++)
                    {
                        if (!Matches(lines, i, oldPart)) continue;
                        int dist = Math.Abs(i - (expected + a));
                        if (dist < best) { best = dist; pos = i; }
                    }
                    usedFuzz = fuzz;
                }
                if (pos < 0) throw new PatchFailed(label + ": change " + n + " no longer matches the game file (game update?)");
                if (usedFuzz > 0) log.AppendLine("  note: " + label + ": change " + n + " applied with fuzz " + usedFuzz + " (the game changed nearby lines)");
                lines.RemoveRange(pos, oldPart.Count);
                lines.InsertRange(pos, newPart);
                shift += newPart.Count - oldPart.Count;
            }
            using (var ms = new MemoryStream())
            {
                foreach (byte[] l in lines) ms.Write(l, 0, l.Length);
                return ms.ToArray();
            }
        }

        private static bool Matches(List<byte[]> lines, int at, List<byte[]> part)
        {
            for (int j = 0; j < part.Count; j++) if (!Same(lines[at + j], part[j])) return false;
            return true;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static byte[] Slice(byte[] d, int start, int len)
        {
            var b = new byte[len];
            Buffer.BlockCopy(d, start, b, 0, len);
            return b;
        }
    }

    // ================================================================== backup + state (<game>\FrosthavenMods\state.json)

    internal sealed class State
    {
        public string BuildId, VanillaSha, InstalledSha, Version;
        public List<string> Mods = new List<string>();
        /// <summary>Game-folder files this installer placed (paths relative to the game folder, '/' separated).</summary>
        public List<string> Files = new List<string>();
        /// <summary>True when the BepInEx loader in the game folder was put there by us (so uninstall removes it).</summary>
        public bool LoaderOwned;

        public static string Folder(string gameDir) { return Path.Combine(gameDir, "FrosthavenMods"); }
        private static string FilePath(string gameDir) { return Path.Combine(Folder(gameDir), "state.json"); }
        public string BackupPath(string gameDir) { return Path.Combine(Folder(gameDir), "Base.ruleset." + BuildId); }

        public static State Load(string gameDir)
        {
            string p = FilePath(gameDir);
            if (!File.Exists(p)) return null;
            string j = File.ReadAllText(p, Encoding.UTF8);
            var s = new State { BuildId = Str(j, "buildid"), VanillaSha = Str(j, "vanilla_sha"), InstalledSha = Str(j, "installed_sha"), Version = Str(j, "version") };
            s.Mods = List(j, "mods");
            s.Files = List(j, "files");
            s.LoaderOwned = Regex.IsMatch(j, "\"loader_owned\"\\s*:\\s*true");
            return s;
        }

        public void Save(string gameDir)
        {
            Directory.CreateDirectory(Folder(gameDir));
            string j = "{\n  \"buildid\": " + Q(BuildId) + ",\n  \"vanilla_sha\": " + Q(VanillaSha) + ",\n  \"installed_sha\": " + Q(InstalledSha)
                     + ",\n  \"mods\": [" + string.Join(", ", Mods.Select(Q).ToArray()) + "],\n  \"version\": " + Q(Version)
                     + ",\n  \"files\": [" + string.Join(", ", Files.Select(Q).ToArray()) + "],\n  \"loader_owned\": " + (LoaderOwned ? "true" : "false") + "\n}";
            File.WriteAllText(FilePath(gameDir), j, new UTF8Encoding(false));
        }

        private static List<string> List(string json, string key)
        {
            var list = new List<string>();
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (m.Success) foreach (Match x in Regex.Matches(m.Groups[1].Value, "\"([^\"]*)\"")) list.Add(x.Groups[1].Value);
            return list;
        }

        private static string Str(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(null|\"([^\"]*)\")");
            return m.Success && m.Groups[2].Success ? m.Groups[2].Value : null;
        }

        private static string Q(string v) { return v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    }

    // ================================================================== the work

    internal static class Pack
    {
        public static string RulesetPath(string gameDir) { return Path.Combine(gameDir, "Frosthaven_Data", "StreamingAssets", "Rulebase", "Base.ruleset"); }

        public static string EmbeddedVersion()
        {
            try
            {
                using (Stream zs = Assembly.GetExecutingAssembly().GetManifestResourceStream("pack.zip"))
                using (var zip = new ZipArchive(zs, ZipArchiveMode.Read))
                    return ZipVersion(zip);
            }
            catch { return "0"; }
        }

        public static string ZipVersion(ZipArchive zip)
        {
            ZipArchiveEntry e = zip.GetEntry("version.txt");
            if (e == null) return "0";
            using (var r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd().Trim();
        }

        public static string InstalledVersion(string gameDir)
        {
            try { State s = State.Load(gameDir); return s != null && s.InstalledSha != null && s.Version != null ? s.Version : "none"; } catch { return "none"; }
        }

        public static string SteamBuildId(string gameDir)
        {
            try
            {
                string acf = Path.GetFullPath(Path.Combine(gameDir, "..", "..", "appmanifest_" + Program.AppId + ".acf"));
                Match m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s+\"(\\d+)\"");
                return m.Success ? m.Groups[1].Value : "unknown";
            }
            catch { return "unknown"; }
        }

        /// <summary>The vanilla Base.ruleset bytes. A file Steam has just replaced (new build, or Verify) becomes the new backup;
        /// our own modded file is answered from the backup; anything else is refused (null + a message).</summary>
        private static byte[] Vanilla(string gameDir, StringBuilder log, out State state)
        {
            state = State.Load(gameDir);
            byte[] game = File.ReadAllBytes(RulesetPath(gameDir));
            string gameSha = Sha256(game);
            string build = SteamBuildId(gameDir);
            if (state != null && gameSha == state.InstalledSha)
            {
                byte[] b = File.Exists(state.BackupPath(gameDir)) ? File.ReadAllBytes(state.BackupPath(gameDir)) : null;
                if (b == null || Sha256(b) != state.VanillaSha)
                {
                    log.AppendLine("The saved copy of the original game file is missing or damaged.");
                    log.AppendLine("Fix: in Steam, right-click Frosthaven > Properties > Installed Files > Verify integrity of game files, then run this again.");
                    return null;
                }
                return b;
            }
            if (state != null && gameSha != state.VanillaSha && (build == state.BuildId || build == "unknown"))
            {
                log.AppendLine("The game's Base.ruleset has been changed by something other than this installer.");
                log.AppendLine("Fix: in Steam, right-click Frosthaven > Properties > Installed Files > Verify integrity of game files, then run this again.");
                return null;
            }
            Ruleset.Read(game); // it is a real ruleset
            if (state != null && state.BuildId != build) log.AppendLine("The game was updated by Steam (build " + state.BuildId + " -> " + build + "); saving the new original.");
            // the game files we placed are still there after a Steam update: keep tracking them
            var fresh = new State { BuildId = build, VanillaSha = gameSha, Files = state != null ? state.Files : new List<string>(), LoaderOwned = state != null && state.LoaderOwned };
            Directory.CreateDirectory(State.Folder(gameDir));
            foreach (string old in Directory.GetFiles(State.Folder(gameDir), "Base.ruleset.*")) File.Delete(old);
            File.WriteAllBytes(fresh.BackupPath(gameDir), game);
            fresh.Save(gameDir);
            log.AppendLine("Saved a copy of the original game file (Steam build " + build + ").");
            state = fresh;
            return game;
        }

        private sealed class ModInZip { public string Folder, Name; public List<ZipArchiveEntry> Patches = new List<ZipArchiveEntry>(), Files = new List<ZipArchiveEntry>(); }

        private static List<ModInZip> ReadMods(ZipArchive zip)
        {
            var mods = new Dictionary<string, ModInZip>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string[] p = e.FullName.Replace('\\', '/').Split('/');
                if (p.Length < 3 || p[0] != "mods" || e.FullName.EndsWith("/")) continue;
                ModInZip m;
                if (!mods.TryGetValue(p[1], out m)) mods[p[1]] = m = new ModInZip { Folder = p[1], Name = p[1] };
                if (p.Length == 3 && p[2] == "mod.json")
                {
                    using (var r = new StreamReader(e.Open(), Encoding.UTF8))
                    {
                        Match nm = Regex.Match(r.ReadToEnd(), "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                        if (nm.Success) m.Name = Regex.Unescape(nm.Groups[1].Value);
                    }
                }
                else if (p.Length > 3 && p[2] == "patches" && e.Name.EndsWith(".diff")) m.Patches.Add(e);
                else if (p.Length > 3 && p[2] == "files") m.Files.Add(e);
            }
            // same order as tools/fh_mods.py: folder names, then paths, compared lowercased
            Comparison<string> cmp = (a, b) => string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant());
            var list = mods.Values.ToList();
            list.Sort((a, b) => cmp(a.Folder, b.Folder));
            foreach (ModInZip m in list)
            {
                m.Patches.Sort((a, b) => cmp(RelPath(a, "patches"), RelPath(b, "patches")));
                m.Files.Sort((a, b) => cmp(RelPath(a, "files"), RelPath(b, "files")));
            }
            return list;
        }

        private static string RelPath(ZipArchiveEntry e, string kind)
        {
            string[] p = e.FullName.Replace('\\', '/').Split('/');
            return string.Join("/", p.Skip(3).ToArray());
        }

        private static byte[] ReadAll(ZipArchiveEntry e)
        {
            using (Stream s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }

        public static int Install(string gameDir, StringBuilder log, string zipPath = null)
        {
            if (IsGameRunning()) { log.AppendLine("Frosthaven is running. Close the game, then try again."); return 1; }
            if (!File.Exists(RulesetPath(gameDir))) { log.AppendLine("Can't find " + RulesetPath(gameDir)); return 1; }
            using (Stream zs = zipPath != null ? (Stream)File.OpenRead(zipPath) : Assembly.GetExecutingAssembly().GetManifestResourceStream("pack.zip"))
            {
                if (zs == null) { log.AppendLine("The mod pack is missing from this installer (build error)."); return 2; }
                using (var zip = new ZipArchive(zs, ZipArchiveMode.Read))
                {
                    string version = ZipVersion(zip);
                    log.AppendLine("Installing mod pack v" + version + (zipPath != null ? " (downloaded)" : " (built into this installer)") + ", replacing: " + InstalledVersion(gameDir) + ".");
                    State state;
                    byte[] vanilla = Vanilla(gameDir, log, out state);
                    if (vanilla == null) return 1;

                    List<RsEntry> entries = Ruleset.Read(vanilla);
                    var byName = new Dictionary<string, RsEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (RsEntry e in entries) byName[e.Name] = e;
                    List<ModInZip> mods = ReadMods(zip);
                    try
                    {
                        foreach (ModInZip m in mods)
                        {
                            foreach (ZipArchiveEntry p in m.Patches)
                            {
                                string rel = RelPath(p, "patches");
                                rel = rel.Substring(0, rel.Length - ".diff".Length);
                                RsEntry target;
                                if (!byName.TryGetValue(rel, out target)) throw new PatchFailed(m.Name + ": " + rel + " is not in the game any more (game update?)");
                                target.Data = Patch.Apply(target.Data, ReadAll(p), m.Name + ": " + rel, log);
                            }
                            foreach (ZipArchiveEntry f in m.Files)
                            {
                                string rel = RelPath(f, "files");
                                byte[] data = ReadAll(f);
                                RsEntry existing;
                                if (byName.TryGetValue(rel, out existing)) { existing.Data = data; continue; }
                                var n = new RsEntry { Name = rel, Parser = Ruleset.ParserFromFile(rel, data), Data = data };
                                Ruleset.InsertByParser(entries, n);
                                byName[rel] = n;
                            }
                            log.AppendLine("  + " + m.Name);
                        }
                    }
                    catch (PatchFailed e)
                    {
                        log.AppendLine("A mod does not fit this version of the game: " + e.Message);
                        log.AppendLine("Nothing was changed. Tell whoever runs the mod pack, they need to update that mod.");
                        return 1;
                    }
                    byte[] result = Ruleset.Write(entries);
                    Ruleset.Read(result); // sanity: reads back
                    string dest = RulesetPath(gameDir), tmp = dest + ".tmp";
                    File.WriteAllBytes(tmp, result);
                    File.Copy(tmp, dest, true);
                    File.Delete(tmp);
                    state.InstalledSha = Sha256(result);
                    state.Mods = mods.Select(m => m.Folder).ToList();
                    state.Version = version;
                    InstallGameFiles(gameDir, zip, state, log);
                    state.Save(gameDir);
                    log.AppendLine("Installed " + mods.Count + " rules mod(s)" + (state.Files.Count > 0 ? " and the code plugin(s)." : "."));
                }
            }
            log.AppendLine("Done. Launch the game from Steam as usual.");
            log.AppendLine("Playing online: everyone in the group needs the same mod pack version, or the game will desync mid-scenario.");
            return 0;
        }

        private const string LoaderMarker = "winhttp.dll";

        /// <summary>Copy the pack's game/ files into the game folder. A BepInEx loader that someone else installed is left
        /// alone (only our plugins are added). Files we placed last time that are no longer in the pack are removed.</summary>
        private static void InstallGameFiles(string gameDir, ZipArchive zip, State state, StringBuilder log)
        {
            var entries = zip.Entries.Where(e => e.FullName.StartsWith("game/") && !e.FullName.EndsWith("/")).ToList();
            var previous = new HashSet<string>(state.Files, StringComparer.OrdinalIgnoreCase);
            bool foreignLoader = File.Exists(Path.Combine(gameDir, LoaderMarker)) && !state.LoaderOwned;
            if (foreignLoader && entries.Count > 0)
                log.AppendLine("  A mod loader (BepInEx) was already installed; keeping it and adding our plugin to it.");
            var placed = new List<string>();
            foreach (ZipArchiveEntry e in entries)
            {
                string rel = e.FullName.Substring("game/".Length);
                bool isPlugin = rel.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase);
                if (foreignLoader && !isPlugin) continue;
                if (rel.Split('/').Any(p => p == ".." || p.Length == 0) || Path.IsPathRooted(rel)) throw new InvalidDataException("bad path in pack: " + rel);
                string dest = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (Stream src = e.Open())
                using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write)) src.CopyTo(dst);
                placed.Add(rel);
            }
            if (!foreignLoader && placed.Contains(LoaderMarker)) state.LoaderOwned = true;
            var now = new HashSet<string>(placed, StringComparer.OrdinalIgnoreCase);
            foreach (string old in previous.Where(p => !now.Contains(p)))   // e.g. a plugin that left the pack
            {
                string path = Path.Combine(gameDir, old.Replace('/', Path.DirectorySeparatorChar));
                TryDelete(path, log);
                try { string dir = Path.GetDirectoryName(path); if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
            }
            if (placed.Count == 0 && state.LoaderOwned) RemoveLoaderLeftovers(gameDir, state, log);
            state.Files = now.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            int plugins = placed.Count(p => p.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase));
            if (placed.Count > 0) log.AppendLine("  + " + (foreignLoader ? "" : "mod loader and ") + plugins + " plugin file(s)");
        }

        private static void TryDelete(string path, StringBuilder log)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { log.AppendLine("  Could not remove " + path + ": " + e.Message); }
        }

        /// <summary>Remove the files we placed. If the loader is ours, also the files BepInEx writes itself (cache, its
        /// config, logs), then any folders left empty. Someone else's plugins are never touched.</summary>
        private static void RemoveGameFiles(string gameDir, State state, StringBuilder log)
        {
            foreach (string rel in state.Files) TryDelete(Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar)), log);
            if (state.LoaderOwned) RemoveLoaderLeftovers(gameDir, state, log);
            state.Files = new List<string>();
        }

        private static void RemoveLoaderLeftovers(string gameDir, State state, StringBuilder log)
        {
            string bep = Path.Combine(gameDir, "BepInEx");
            foreach (string f in new[] { LoaderMarker, "doorstop_config.ini", ".doorstop_version" }) TryDelete(Path.Combine(gameDir, f), log);
            if (Directory.Exists(bep))
            {
                try { if (Directory.Exists(Path.Combine(bep, "cache"))) Directory.Delete(Path.Combine(bep, "cache"), true); } catch { }
                TryDelete(Path.Combine(bep, "config", "BepInEx.cfg"), log);
                foreach (string f in Directory.GetFiles(bep, "LogOutput*.log")) TryDelete(f, log);
                foreach (string d in Directory.GetDirectories(bep, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
                try { if (!Directory.EnumerateFileSystemEntries(bep).Any()) Directory.Delete(bep); } catch { }
                if (Directory.Exists(bep)) log.AppendLine("  Left the BepInEx folder in place: it has other files in it (another mod?).");
            }
            state.LoaderOwned = false;
        }

        public static int Uninstall(string gameDir, StringBuilder log)
        {
            if (IsGameRunning()) { log.AppendLine("Frosthaven is running. Close the game, then try again."); return 1; }
            State state = State.Load(gameDir);
            if (state == null) { log.AppendLine("No mods are installed (nothing to remove)."); return 0; }
            if (state.Files.Count > 0 || state.LoaderOwned)
            {
                bool owned = state.LoaderOwned;
                RemoveGameFiles(gameDir, state, log);
                state.Save(gameDir);
                log.AppendLine(owned ? "Removed the mod loader and plugins." : "Removed our plugin (the mod loader that was already there is kept).");
            }
            string gameSha = Sha256(File.ReadAllBytes(RulesetPath(gameDir)));
            if (gameSha == state.InstalledSha)
            {
                byte[] b = File.Exists(state.BackupPath(gameDir)) ? File.ReadAllBytes(state.BackupPath(gameDir)) : null;
                if (b == null || Sha256(b) != state.VanillaSha)
                {
                    log.AppendLine("The saved original is missing or damaged. In Steam: right-click Frosthaven > Properties > Installed Files > Verify integrity of game files.");
                    return 1;
                }
                File.WriteAllBytes(RulesetPath(gameDir), b);
            }
            else if (gameSha != state.VanillaSha)
            {
                log.AppendLine("The game file was changed by something else; nothing was touched. In Steam: right-click Frosthaven > Properties > Installed Files > Verify integrity of game files.");
                return 1;
            }
            foreach (string f in Directory.GetFiles(State.Folder(gameDir)))
                if (!Path.GetFileName(f).Equals("install-log.txt", StringComparison.OrdinalIgnoreCase)) File.Delete(f);
            log.AppendLine("Mods removed. The game is back to its original rules. Saves were not touched.");
            return 0;
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        public static bool IsGameRunning()
        {
            try { return Process.GetProcessesByName(Program.ProcessName).Length > 0; } catch { return false; }
        }

        // ---------- finding the game ----------

        public static string FindGameFolder()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(here, Program.GameExe))) return here;
            foreach (string lib in SteamLibraries())
            {
                string cand = Path.Combine(lib, "steamapps", "common", Program.SteamFolder);
                if (File.Exists(Path.Combine(cand, Program.GameExe))) return cand;
            }
            foreach (DriveInfo d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
                foreach (string rel in new[] { @"Program Files (x86)\Steam", @"Program Files\Steam", "Steam", "SteamLibrary", @"Games\Steam", @"Games\SteamLibrary" })
                {
                    string cand = Path.Combine(d.RootDirectory.FullName, rel, "steamapps", "common", Program.SteamFolder);
                    if (File.Exists(Path.Combine(cand, Program.GameExe))) return cand;
                }
            return null;
        }

        private static IEnumerable<string> SteamLibraries()
        {
            var libs = new List<string>();
            string steam = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) steam = k != null ? k.GetValue("SteamPath") as string : null;
                if (steam == null) using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")) steam = k != null ? k.GetValue("InstallPath") as string : null;
            }
            catch { }
            if (string.IsNullOrEmpty(steam)) return libs;
            steam = steam.Replace('/', Path.DirectorySeparatorChar);
            libs.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p) && !libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                }
            return libs;
        }
    }

    // ================================================================== GitHub Releases

    /// <summary>Looks for a newer pack on GitHub (public repo, no login): the latest release's tag is the version, its
    /// assets carry the pack zip and a SHA256SUMS.txt. The zip is downloaded to a temp file and verified before use.</summary>
    internal static class Updater
    {
        public const string Repo = "Kalarian/Frosthaven-Mods";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        private const string Api = "https://api.github.com/repos/" + Repo + "/releases/latest";
        private const string ZipName = "Frosthaven-Mods-pack.zip";

        public sealed class Latest { public string Version; public string ZipUrl; public string SumsUrl; }

        private static System.Net.WebClient Client()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            var wc = new System.Net.WebClient();
            // never answer from the WinINet cache: "latest" must be current
            wc.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            wc.Headers.Add("User-Agent", "FrosthavenModsInstaller");
            return wc;
        }

        public static Latest Check(StringBuilder log)
        {
            try
            {
                string json;
                using (var wc = Client())
                {
                    wc.Headers.Add("Accept", "application/vnd.github+json");
                    wc.Headers.Add("Cache-Control", "no-cache");
                    json = wc.DownloadString(Api + "?t=" + DateTime.UtcNow.Ticks);
                }
                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                if (!m.Success) { log.AppendLine("Online check: no release found."); return null; }
                var l = new Latest { Version = m.Groups[1].Value };
                foreach (Match a in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string u = a.Groups[1].Value;
                    if (u.EndsWith("/" + ZipName, StringComparison.OrdinalIgnoreCase)) l.ZipUrl = u;
                    else if (u.EndsWith("/SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) l.SumsUrl = u;
                }
                return l;
            }
            catch (Exception e) { log.AppendLine("Online check failed (" + e.Message + "); using the pack built into this installer."); return null; }
        }

        public static bool IsNewer(string a, string b)
        {
            Version va, vb;
            if (!Version.TryParse(Pad(a), out va) || !Version.TryParse(Pad(b), out vb)) return false;
            return va > vb;
        }
        private static string Pad(string v) { v = (v ?? "0").Trim().TrimStart('v', 'V'); return v.Contains(".") ? v : v + ".0"; }

        /// <summary>Path of a downloaded, verified zip when GitHub has a newer pack than this exe carries; null otherwise.</summary>
        public static string FetchNewerZip(string gameDir, StringBuilder log)
        {
            Latest l = Check(log);
            if (l == null) return null;
            string embedded = Pack.EmbeddedVersion();
            log.AppendLine("Latest online: v" + l.Version + " | in this installer: v" + embedded + " | installed: " + Pack.InstalledVersion(gameDir));
            if (!IsNewer(l.Version, embedded)) return null;
            if (l.ZipUrl == null || l.SumsUrl == null) { log.AppendLine("The newer release is missing its pack or checksum file; using the built-in pack."); return null; }
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "Frosthaven-Mods-" + l.Version + ".zip");
                using (var wc = Client())
                {
                    wc.DownloadFile(l.ZipUrl, tmp);
                    string sums = wc.DownloadString(l.SumsUrl);
                    var sm = Regex.Match(sums, "([0-9a-fA-F]{64})\\s+\\*?" + Regex.Escape(ZipName), RegexOptions.IgnoreCase);
                    if (!sm.Success || !Pack.Sha256(File.ReadAllBytes(tmp)).Equals(sm.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    {
                        log.AppendLine("Downloaded pack failed its checksum; using the built-in pack instead.");
                        try { File.Delete(tmp); } catch { }
                        return null;
                    }
                    log.AppendLine("Downloaded v" + l.Version + " and verified its checksum.");
                }
                using (var zip = ZipFile.OpenRead(tmp)) { if (zip.GetEntry("version.txt") == null) { log.AppendLine("Downloaded pack has no version.txt; using the built-in pack."); return null; } }
                return tmp;
            }
            catch (Exception e) { log.AppendLine("Download failed (" + e.Message + "); using the built-in pack."); return null; }
        }
    }

    // ================================================================== the window

    internal sealed class MainForm : Form
    {
        private readonly TextBox _path = new TextBox();
        private readonly TextBox _log = new TextBox();
        private readonly Button _install = new Button();
        private readonly Button _remove = new Button();
        private readonly Label _version;

        public MainForm()
        {
            Text = "Frosthaven Mods";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(560, 380);
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label { Text = "Frosthaven mod pack", Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 12) };
            var sub = new Label { Text = "Installs or updates the mods. Close the game first.", AutoSize = true, Location = new Point(18, 44), ForeColor = Color.DimGray };
            _version = new Label { Text = "Pack in this installer: v" + Pack.EmbeddedVersion() + "  -  checking GitHub for a newer one...", AutoSize = true, Location = new Point(18, 64), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f) };
            var checker = new System.Threading.Thread(() =>
            {
                Updater.Latest l = Updater.Check(new StringBuilder());
                string embedded = Pack.EmbeddedVersion();
                string text = l == null ? "Pack in this installer: v" + embedded + "  -  GitHub not reachable, this pack will be used."
                    : Updater.IsNewer(l.Version, embedded) ? "Pack in this installer: v" + embedded + "  -  v" + l.Version + " is on GitHub and will be downloaded on Install."
                    : "Pack in this installer: v" + embedded + "  -  up to date (GitHub: v" + l.Version + ").";
                try { BeginInvoke((Action)(() => _version.Text = text)); } catch { }
            });
            checker.IsBackground = true; checker.Start();

            var pathLabel = new Label { Text = "Game folder", AutoSize = true, Location = new Point(18, 92) };
            _path.SetBounds(18, 112, 440, 26);
            var browse = new Button { Text = "Browse...", Location = new Point(464, 110), Size = new Size(78, 28) };
            browse.Click += (s, e) => Browse();

            _install.Text = "Install / Update"; _install.SetBounds(18, 152, 150, 34); _install.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _install.Click += (s, e) => Run(false);
            _remove.Text = "Remove mods"; _remove.SetBounds(176, 152, 120, 34);
            _remove.Click += (s, e) => Run(true);
            var close = new Button { Text = "Close", Location = new Point(464, 152), Size = new Size(78, 34) };
            close.Click += (s, e) => Close();

            _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
            _log.SetBounds(18, 198, 524, 166); _log.Font = new Font("Consolas", 9f); _log.BackColor = Color.White;

            Controls.AddRange(new Control[] { title, sub, _version, pathLabel, _path, browse, _install, _remove, close, _log });

            string found = null;
            try { found = Pack.FindGameFolder(); } catch { }
            if (found != null) { _path.Text = found; SetResult(Installed(found)); }
            else SetResult("Could not find the game on its own. Use Browse... to pick the folder that contains 'Frosthaven.exe'\r\n(Steam: right-click the game -> Manage -> Browse local files).");
        }

        private static string V(string version) => version == "none" ? version : "v" + version;

        private static string Installed(string dir)
        {
            string v = Pack.InstalledVersion(dir);
            return v == "none" ? "No mods installed." : "Installed: " + V(v);
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pick the Frosthaven game folder (it contains Frosthaven.exe)", ShowNewFolderButton = false })
            {
                if (Directory.Exists(_path.Text)) dlg.SelectedPath = _path.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
            }
        }

        private void Run(bool uninstall)
        {
            string dir = _path.Text.Trim().Trim('"');
            if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, Program.GameExe)))
            {
                MessageBox.Show(this, "That folder does not contain 'Frosthaven.exe'. Use Browse... to pick the game folder.", "Frosthaven Mods", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Pack.IsGameRunning())
            {
                MessageBox.Show(this, "Frosthaven is running. Close the game, then try again.", "Frosthaven Mods", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (uninstall && MessageBox.Show(this, "Remove the mods and put the game's original rules back?\n\nSaves are not touched.", "Remove mods", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _install.Enabled = _remove.Enabled = false;
            Cursor = Cursors.WaitCursor;
            var log = new StringBuilder();
            try
            {
                string before = Pack.InstalledVersion(dir);
                string zip = uninstall ? null : Updater.FetchNewerZip(dir, log);
                int code = uninstall ? Pack.Uninstall(dir, log) : Pack.Install(dir, log, zip);
                string after = Pack.InstalledVersion(dir);
                // just the outcome; the step-by-step log only when something went wrong
                if (code != 0) SetResult("Didn't work:\r\n" + log.ToString().TrimEnd());
                else if (uninstall) SetResult(before == "none" ? "No mods were installed." : "Mods removed.");
                else if (before == "none") SetResult("Installed " + V(after) + ".");
                else if (before == after) SetResult("Already up to date (" + V(after) + "). No update needed.");
                else SetResult("Updated from " + V(before) + " to " + V(after) + ".");
            }
            catch (Exception e)
            {
                SetResult("Something went wrong: " + e.Message + "\r\n" + log.ToString().TrimEnd());
                MessageBox.Show(this, "Something went wrong:\n" + e.Message + "\n\nSend a screenshot of this window.", "Frosthaven Mods", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _install.Enabled = _remove.Enabled = true; Cursor = Cursors.Default; }
        }

        /// <summary>Replaces the box's text: it always shows the result of the last thing done, not a running log.</summary>
        private void SetResult(string s)
        {
            _log.Text = s.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
    }
}
