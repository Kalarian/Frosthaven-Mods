// Loads Frosthaven's own rule files (extracted/Base) and then a pack's files with the game's own loaders, in the order
// the game uses (YMLLoading.ProcessYMLFile: one loader per "Parser:" line), and lists every pack file a loader rejects
// with the validation messages the game would log ("Failed to parse file ..." = the start-up error screen).
//
//   dotnet run --project tools/rulecheck -c Release [-- <pack files folder>]
using System.Reflection;
using ScenarioRuleLibrary;
using SharedLibrary.Client;

string root = @"C:\Claude\Frosthaven";
string pack = args.Length > 0 ? args[0] : Path.Combine(root, @"mods\gloomhaven\files");
string vanilla = Path.Combine(root, @"extracted\Base");
// the loaders this check runs, in load order (YMLLoading); other parsers are skipped
string[] order = { "CharacterResources", "AttackModifier", "AttackModifierDeck", "HeroSummon", "Character", "AbilityCard", "ItemCard", "Perk" };
// ScenarioRuleClient.Initialise also starts the event log; only the rule data is wanted here
typeof(ScenarioRuleClient).GetField("s_SRLYML", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, new CSRLYML());
// Unity's Debug calls into the engine (ECall) outside the player: route its logger to this console instead
var quiet = Environment.GetEnvironmentVariable("RC_VERBOSE") == null;
typeof(UnityEngine.Debug).GetField("s_Logger", BindingFlags.NonPublic | BindingFlags.Static)
    .SetValue(null, new UnityEngine.Logger(new ConsoleLog(quiet)));
SharedClient.InitValidationRecord();
var data = ScenarioRuleClient.SRLYML.Data;
object Loader(string parser)
{
    foreach (var name in new[] { parser + "s", parser })
    {
        var p = data.GetType().GetProperty(name); if (p != null) return p.GetValue(data);
        var f = data.GetType().GetField(name); if (f != null) return f.GetValue(data);
    }
    return null;
}
string ParserOf(string file)
{
    using var r = new StreamReader(file);
    var first = (r.ReadLine() ?? "").Trim().TrimStart('\uFEFF');
    return first.StartsWith("Parser:") ? first.Substring(7).Trim() : null;
}
var files = new List<(string parser, string file, bool ours)>();
foreach (var (dir, ours) in new[] { (vanilla, false), (pack, true) })
    foreach (var f in Directory.EnumerateFiles(dir, "*.yml", SearchOption.AllDirectories))
    {
        var p = ParserOf(f);
        if (p != null && order.Contains(p)) files.Add((p, f, ours));
    }
int ok = 0, failed = 0, theirs = 0;
foreach (var parser in order)
{
    var loader = Loader(parser);
    if (loader == null) { Console.WriteLine($"(no loader for {parser})"); continue; }
    var process = loader.GetType().GetMethod("ProcessFile", new[] { typeof(StreamReader), typeof(string) });
    foreach (var (_, file, ours) in files.Where(x => x.parser == parser).OrderBy(x => x.ours).ThenBy(x => x.file, StringComparer.OrdinalIgnoreCase))
    {
        string rel = Path.GetRelativePath(ours ? pack : vanilla, file).Replace(Path.DirectorySeparatorChar, '/');
        bool good;
        try { using var r = new StreamReader(file); r.ReadLine(); /* the game reads the Parser line itself, then hands the loader the rest */ good = (bool)process.Invoke(loader, new object[] { r, rel }); }
        catch (TargetInvocationException e) { good = false; SharedClient.ValidationRecord.RecordedFailures.Add(null); Console.WriteLine($"  {rel}: threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); if (Environment.GetEnvironmentVariable("RC_STACK") != null) { Console.WriteLine(e.InnerException?.StackTrace); Environment.Exit(2); } }
        var msgs = SharedClient.ValidationRecord.RecordedFailures.Where(m => m != null).Select(m => m.Message).ToList();
        SharedClient.ValidationRecord.RecordedFailures.Clear();
        if (!ours) { if (!good) theirs++; continue; }
        if (good && msgs.Count == 0) { ok++; continue; }
        failed++;
        Console.WriteLine($"{(good ? "WARN" : "FAILED")} {rel}");
        foreach (var m in msgs) Console.WriteLine("    " + m.Replace("\n", " "));
    }
    // the loaders look earlier kinds up in SRLYML's lists (a card's CharacterID, a deck's modifiers), which the game
    // fills from the load queues between kinds
    ScenarioRuleClient.SRLYML.UploadNewData();
}
Console.WriteLine($"rulecheck: {ok} ok, {failed} with problems (Frosthaven's own files rejected offline: {theirs})");
return failed == 0 ? 0 : 1;

class ConsoleLog : UnityEngine.ILogHandler
{
    readonly bool quiet;
    public ConsoleLog(bool quiet) { this.quiet = quiet; }
    public void LogFormat(UnityEngine.LogType t, UnityEngine.Object c, string f, params object[] a) { if (!quiet) Console.WriteLine($"  [{t}] " + string.Format(f, a)); }
    public void LogException(Exception e, UnityEngine.Object c) { if (!quiet) Console.WriteLine($"  [Exception] {e}"); }
}
