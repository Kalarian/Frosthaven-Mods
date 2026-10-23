// Parses rule files with the game's own YAML reader, as every YML loader in the game does
// (new YamlParser().ParseYamlStream(new TextInput(text), out success)), and lists the ones it rejects with its errors.
// The reader is stricter than YAML: e.g. an empty list "[]" fails the whole file.
//
//   dotnet run --project tools/ymlcheck -c Release [-- <folder or .yml> ...]
//   (default: <game>\FrosthavenMods\ghclasses\ruleset, the Gloomhaven classes tools/gh_port.py converted)
using YamlFormats;

string game = @"C:\Program Files (x86)\Steam\steamapps\common\Frosthaven";
var targets = args.Length > 0 ? args : new[] { Path.Combine(game, "FrosthavenMods", "ghclasses", "ruleset") };
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
