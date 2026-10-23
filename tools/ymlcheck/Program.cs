// Parses rule files with the game's own YAML reader, as every YML loader in the game does
// (new YamlParser().ParseYamlStream(new TextInput(text), out success)), and lists the ones it rejects with its errors.
// The reader is stricter than YAML: e.g. an empty list "[]" fails the whole file.
//
//   dotnet run --project tools/ymlcheck -c Release [-- <folder or .yml> ...]
//   (default: the files/ folder of every mod in the repo's mods\, e.g. the Gloomhaven classes tools/gh_port.py converted)
using YamlFormats;

string mods = @"C:\Claude\Frosthaven\mods";
var targets = args.Length > 0 ? args
    : Directory.Exists(mods) ? Directory.GetDirectories(mods).Select(p => Path.Combine(p, "files")).Where(Directory.Exists).ToArray()
    : new string[0];
if (targets.Length == 0) { Console.WriteLine($"nothing to check: no mod with a files folder under {mods}"); return 1; }
int ok = 0, failed = 0;
foreach (string target in targets)
{
    IEnumerable<string> files = Directory.Exists(target)
        ? Directory.EnumerateFiles(target, "*.yml", SearchOption.AllDirectories)
        : new[] { target };
    foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
    {
        var parser = new YamlParser();
        parser.ParseYamlStream(new TextInput(File.ReadAllText(file)), out bool success);
        if (success) { ok++; continue; }
        failed++;
        Console.WriteLine($"FAILED {file}");
        foreach (var e in parser.Errors ?? new List<Pair<int, string>>())
            Console.WriteLine($"    at {e.Left}: {e.Right}");
    }
}
Console.WriteLine($"{ok} ok, {failed} failed");
return failed == 0 ? 0 : 1;
