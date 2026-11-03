// Values a pack's loaders accept but the game can't use (the Sawbones' "Level: M" passed rulecheck, then FHMapView.InitUI
// threw; data-validity review 2026-11-03). Loads Frosthaven's own rule files (extracted/Base) and then a pack's with the
// game's own loaders, masteries and battle goals too (MapRuleLibrary), then repeats on the loaded objects what
// YMLDefRepository.ToRuleLibraryDefRepo does when the outpost or campaign starts:
//   - ClassCardDef: uint.Parse(Level), (uint)Initiative, the infusions as ScenarioElement, area hexes, every enhancement
//     slot; a hex slot's price divides by the action's red hexes (top-level abilities only, plus what the plugin's
//     GHEnhancerInnerHexes adds for pack cards): 0 = a division by 0 and buying zeroes the gold
//   - CharacterAttackModifierDef: every Card.* property; PerkDef: the modifiers a perk names (unknown names are dropped)
//   - CharacterClassDef: health table, hero stats, companion, masteries; CharacterYMLData.Validate (a Role, ...) and the
//     class screen fields (Role, Difficulty, Strengths, Weaknesses, Adventure_Description, Description)
//   - items: ItemCardYMLData.GetItem
// Prints one line per problem; "note" lines are facts worth knowing that the game handles (1 level-5 card, a perk with 4
// boxes). Exit 1 on any problem.
//
//   dotnet run --project tools/defcheck -c Release -- <pack files folder> [<another pack files folder> ...]
//   dotnet run --project tools/defcheck -c Release -- frosthaven      (Frosthaven's own files as the checked set)
using System.Reflection;
using MapRuleLibrary.Client;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using SharedLibrary.Client;

string root = FindRoot();
string vanilla = Path.Combine(root, @"extracted\Base");
if (args.Length == 0) { Console.WriteLine("usage: defcheck <pack files folder>... | frosthaven"); return 2; }
typeof(UnityEngine.Debug).GetField("s_Logger", BindingFlags.NonPublic | BindingFlags.Static)
    .SetValue(null, new UnityEngine.Logger(new ConsoleLog(Environment.GetEnvironmentVariable("DC_VERBOSE") == null)));
int total = 0;
foreach (var a in args)
{
    bool fh = a.Equals("frosthaven", StringComparison.OrdinalIgnoreCase);
    total += Check(fh ? vanilla : Path.GetFullPath(a), fh);
}
return total == 0 ? 0 : 1;

string FindRoot()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, @"extracted\Base"))) return d.FullName;
    return @"C:\Claude\Frosthaven";
}

int Check(string pack, bool frosthaven)
{
    Console.WriteLine($"== {(frosthaven ? "Frosthaven's own files" : pack)}");
    // fresh rule data for each pack (ScenarioRuleClient.Initialise also starts the event log; only the data is wanted)
    typeof(ScenarioRuleClient).GetField("s_SRLYML", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, new CSRLYML());
    var mf = typeof(MapRuleLibraryClient).GetField("s_MRLYML", BindingFlags.NonPublic | BindingFlags.Static);
    mf.SetValue(null, Activator.CreateInstance(mf.FieldType));
    SharedClient.InitValidationRecord();
    var data = ScenarioRuleClient.SRLYML.Data;
    string[] order = { "CharacterResources", "AttackModifier", "AttackModifierDeck", "HeroSummon", "Character", "AbilityCard", "ItemCard", "Perk" };
    object Loader(string parser)
    {
        foreach (var name in new[] { parser + "s", parser })
        {
            var p = data.GetType().GetProperty(name); if (p != null) return p.GetValue(data);
            var f = data.GetType().GetField(name); if (f != null) return f.GetValue(data);
        }
        return null;
    }
    var files = new List<(string parser, string file, bool ours)>();
    foreach (var (dir, ours) in frosthaven ? new[] { (vanilla, true) } : new[] { (vanilla, false), (pack, true) })
        foreach (var f in Directory.EnumerateFiles(dir, "*.yml", SearchOption.AllDirectories))
        {
            var p = ParserOf(f);
            if (p != null) files.Add((p, f, ours));
        }
    var packFiles = new HashSet<string>(files.Where(x => x.ours).Select(x => Rel(pack, x.file)), StringComparer.OrdinalIgnoreCase);
    int problems = 0, notes = 0;
    void Problem(string what) { problems++; Console.WriteLine("PROBLEM " + what); }
    void Note(string what) { notes++; Console.WriteLine("note " + what); }

    void Run(object loader, IEnumerable<(string parser, string file, bool ours)> fs)
    {
        var process = loader.GetType().GetMethod("ProcessFile", new[] { typeof(StreamReader), typeof(string) });
        foreach (var (_, file, ours) in fs.OrderBy(x => x.ours).ThenBy(x => x.file, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Rel(ours ? pack : vanilla, file);
            bool good; string threw = null;
            try { using var r = new StreamReader(file); r.ReadLine(); good = (bool)process.Invoke(loader, new object[] { r, rel }); }
            catch (TargetInvocationException e) { good = false; threw = $"{e.InnerException?.GetType().Name}: {e.InnerException?.Message}"; }
            var msgs = SharedClient.ValidationRecord.RecordedFailures.Where(m => m != null).Select(m => m.Message).ToList();
            SharedClient.ValidationRecord.RecordedFailures.Clear();
            if (!ours) continue;
            msgs = msgs.Where(m => !PluginHandled(m, File.ReadAllText(file))).ToList();
            if (threw != null) Problem($"{rel}: the loader threw {threw}");
            else if (!good || msgs.Count > 0) Problem($"{rel}: {(good ? "loaded with" : "rejected:")} {string.Join(" | ", msgs.Select(m => m.Replace("\n", " ")))}");
        }
    }
    foreach (var parser in order)
    {
        var l = Loader(parser);
        if (l != null) Run(l, files.Where(x => x.parser == parser));
        // the loaders look earlier kinds up in SRLYML's lists, which the game fills between kinds
        ScenarioRuleClient.SRLYML.UploadNewData();
    }
    var md = MapRuleLibraryClient.MRLYML.Data;
    Run(md.BattleGoals, files.Where(x => x.parser == "BattleGoal"));
    Run(md.Masteries, files.Where(x => x.parser == "Mastery"));
    MapRuleLibraryClient.MRLYML.UploadNewData();
    MapRuleLibraryClient.MRLYML.FinishDataLoad();

    bool Ours(string fn) => fn != null && packFiles.Contains(fn.Replace('\\', '/'));
    var S = ScenarioRuleClient.SRLYML;
    var M = MapRuleLibraryClient.MRLYML;

    // ---------------------------------------------------------------- characters
    foreach (var c in S.Characters.Where(c => Ours(c.FileName)))
    {
        if (!c.Validate()) Problem($"{c.FileName}: CharacterYMLData.Validate: " + string.Join("; ", SharedClient.ValidationRecord.RecordedFailures.Where(m => m != null).Select(m => m.Message)));
        SharedClient.ValidationRecord.RecordedFailures.Clear();
        try
        {
            var h = c.HealthTable.Select(i => (uint)i).ToArray();
            if (h.Length < 9) Problem($"{c.ID}: HealthTable has {h.Length} entries (9 levels)");
            if (c.HeroStats == null) Problem($"{c.ID}: no HeroStats (the class screen's web)");
            if (!string.IsNullOrEmpty(c.CompanionSummonID) && !S.HeroSummons.Any(s => s.ID == c.CompanionSummonID)) Problem($"{c.ID}: companion summon {c.CompanionSummonID} has no HeroSummon");
            foreach (var n in new[] { "Role", "Difficulty", "Strengths", "Weaknesses", "Adventure_Description", "Description" })
                if (c.GetType().GetProperty(n)?.GetValue(c) == null) Problem($"{c.ID}: no {n} (blank on the class screens)");
            var masteryIds = new HashSet<string>(M.Masteries.Select(m => m.ID));
            foreach (var id in c.MasteryIDs ?? new string[0]) if (!masteryIds.Contains(id)) Problem($"{c.ID}: mastery {id} is not loaded (the class screens look it up)");
            var cards = S.AbilityCards.Where(a => a.CharacterID == c.ID).ToList();
            if (cards.Any(a => a.Level != "1" && a.Level != "X")) for (int L = 2; L <= 9; L++) { int k = cards.Count(a => a.Level == L.ToString()); if (k != 2) Note($"{c.ID}: level {L} has {k} cards (Frosthaven's classes have 2)"); }
        }
        catch (Exception ex) { Problem($"{c.ID}: the class def build throws {ex.GetType().Name}: {ex.Message}"); }
    }
    foreach (var m in M.Masteries.Where(m => Ours(m.FileName))) if (!m.Validate()) Problem($"{m.FileName}: mastery Validate fails");
    foreach (var g in M.BattleGoals.Where(g => Ours(g.FileName))) if (!g.Validate()) Problem($"{g.FileName}: battle goal Validate fails");
    SharedClient.ValidationRecord.RecordedFailures.Clear();

    // ---------------------------------------------------------------- attack modifiers and perks
    var amcNames = new HashSet<string>(S.AttackModifiers.Select(a => a.Name));
    foreach (var a in S.AttackModifiers.Where(a => Ours(a.FileName)))
    {
        try
        {
            var card = a.Card; object o = a.MathModifier; o = a.Rolling; o = a.Shuffle; o = a.IsPersistent;
            foreach (var p in card.GetType().GetProperties())
            {
                try { var v = p.GetValue(card); if (v is System.Collections.IEnumerable e && v is not string) foreach (var _ in e) { } }
                catch (Exception ex) { Problem($"modifier {a.Name}: Card.{p.Name} throws {ex.InnerException?.GetType().Name ?? ex.GetType().Name}: {ex.InnerException?.Message}"); }
            }
        }
        catch (Exception ex) { Problem($"modifier {a.Name}: throws {ex.GetType().Name}: {ex.Message}"); }
    }
    foreach (var p in S.Perks.Where(p => Ours(p.FileName)))
    {
        try
        {
            object o = p.Flags; o = p.Cost;
            if (p.Available > 3) Note($"perk {p.ID}: {p.Available} boxes (Frosthaven's most is 3; the perk sheet lays it out wider)");
            foreach (var d in p.CardsToAdd) if (!amcNames.Contains(d.Name)) Problem($"perk {p.ID}: adds unknown modifier {d.Name} (PerkDef drops it)");
            foreach (var d in p.CardsToRemove) if (!amcNames.Contains(d.Name)) Problem($"perk {p.ID}: removes unknown modifier {d.Name} (PerkDef drops it)");
            if (!S.Characters.Any(c => c.ID == p.CharacterID)) Problem($"perk {p.ID}: unknown class {p.CharacterID}");
        }
        catch (Exception ex) { Problem($"perk {p.ID}: throws {ex.GetType().Name}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- ability cards (ToRuleLibraryDefRepo's ClassCardDef)
    var noPlugin = Environment.GetEnvironmentVariable("DC_NO_PLUGIN");   // set: check as if the plugin weren't there
    int Red(CAbility a) => a?.AreaEffect == null ? 0 : a.AreaEffect.EnabledHexes.Count(x => x.Flags.HasFlag(CAreaEffect.EHexFlag.Enemy));
    foreach (var ac in S.AbilityCards.Where(c => Ours(c.FileName)))
    {
        string where = $"card {ac.ID} {ac.Name} ({ac.FileName})";
        try
        {
            if (ac.CharacterID == "All") continue;
            uint lvl = ac.Level == "X" ? 1u : uint.Parse(ac.Level);
            if (ac.Initiative < 0 || ac.Initiative > 99) Problem($"{where}: initiative {ac.Initiative}");
            bool mode = ac.TopActionCardData.ModeAbilities.Any() || ac.BottomActionCardData.ModeAbilities.Any();
            var list = new List<CAction>();
            if (ac.TopActionCardData?.Abilities != null) list.Add(ac.TopActionCardData);
            if (ac.BottomActionCardData?.Abilities != null) list.Add(ac.BottomActionCardData);
            if (list.Count < 2 && ac.SupplyCard != true) Problem($"{where}: only {list.Count} action(s) with abilities");
            foreach (var act in list)
            {
                string half = act == ac.BottomActionCardData ? "bottom" : "top";
                var aa = CAbilityCard.GetActionAbilities(act).ToList();
                int effHex = act.Abilities.Where(a => a.AreaEffect != null).Select(Red).DefaultIfEmpty(0).Max();
                bool hexSlot = aa.Any(ab => ab.AvailableEnhancementSlots.Any(e => e.MarkType == EEnhancementMarkType.Hex));
                // the plugin's GHEnhancerInnerHexes: a pack card's action with a hex slot and 0 counted red hexes gets the
                // most red hexes of any of its abilities, inner ones included
                if (effHex == 0 && hexSlot && noPlugin == null && ac.ID >= 20000 && ac.ID < 40000) effHex = aa.Select(Red).DefaultIfEmpty(0).Max();
                if (hexSlot && effHex == 0) Problem($"{where}: {half}: a hex enhancement slot but 0 red hexes to price it over (the Enhancer divides by 0; buying it zeroes the gold)");
                _ = act.Abilities.Where(x => x.AbilityType == CAbility.EAbilityType.Move).Sum(a => a.Strength);
                _ = act.Abilities.Any(a => a.ActiveBonusData.Duration == CActiveBonus.EActiveBonusDurationType.Persistent || a.ActiveBonusData.Duration == CActiveBonus.EActiveBonusDurationType.Summon);
                _ = act.Infusions?.Select(x => Enum.Parse(typeof(ElementInfusionBoardManager.EElement), x.ToString())).ToList();
                foreach (var ab in aa)
                    foreach (var e in ab.AvailableEnhancementSlots)
                    {
                        _ = ab.NumberTargets > 1 || ab.Targeting == CAbility.EAbilityTargeting.All || ab.StatIsBasedOnXEntries.Any(x => x.AbilityStatType == CAbility.EAbilityStatType.NumberOfTargets) || Red(ab) > 1;
                        _ = aa.Any(x => x is CAbilityControlActor c && c.ControlActorData.ControlAbilities.Any(y => y.ID == ab.ID) && c.NumberTargets != 1);
                        foreach (var _c in ab.GetAllPositiveConditions()) { }
                        foreach (var _c in ab.GetAllNegativeConditions()) { }
                        _ = ((int)e.MarkType, (uint)e.EnhancementSlot, (uint)e.EnhancementLine, ab is CAbilityMove mv && mv.Jump);
                    }
            }
        }
        catch (Exception ex) { Problem($"{where}: the card def build throws {ex.GetType().Name}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- items
    foreach (var it in S.ItemCards.Where(c => Ours(c.FileName)))
    {
        try { _ = it.GetItem; } catch (Exception ex) { Problem($"item {it.ID}: GetItem throws {ex.GetType().Name}: {ex.Message}"); }
    }

    Console.WriteLine($"defcheck: chars {S.Characters.Count(c => Ours(c.FileName))}, cards {S.AbilityCards.Count(c => Ours(c.FileName))}, " +
                      $"items {S.ItemCards.Count(c => Ours(c.FileName))}, perks {S.Perks.Count(c => Ours(c.FileName))}, modifiers {S.AttackModifiers.Count(c => Ours(c.FileName))}, " +
                      $"masteries {M.Masteries.Count(m => Ours(m.FileName))}: {problems} problem(s), {notes} note(s)");
    return problems;
}

static string Rel(string dir, string file) => Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/');

static string ParserOf(string file)
{
    using var r = new StreamReader(file);
    var first = (r.ReadLine() ?? "").Trim().TrimStart('\uFEFF');
    return first.StartsWith("Parser:") ? first.Substring(7).Trim() : null;
}

// the loader messages the mods plugin answers in the game (the same filter as rulecheck): a layout row for a Consume
// step of the same card (GHStepConsumeLayout), and an Active layout row for an ability inside a Consume step
static bool PluginHandled(string m, string text)
{
    var mm = System.Text.RegularExpressions.Regex.Match(m ?? "", @"Unable to find Consume data for entry (\w+)");
    if (mm.Success && System.Text.RegularExpressions.Regex.IsMatch(text, @"^ +" + mm.Groups[1].Value + @":\s*\r?\n +(MiscVars:\s*\r?\n(?: {20,}.*\r?\n)*)? +Consume:", System.Text.RegularExpressions.RegexOptions.Multiline))
        return true;
    var am = System.Text.RegularExpressions.Regex.Match(m ?? "", @"Active ability name (\w+) not found in linked abilities");
    return am.Success && System.Text.RegularExpressions.Regex.IsMatch(text, @"Consume:\s*\r?\n(?:(?: {20,}.*)?\r?\n)*? {24,}" + am.Groups[1].Value + @":\s*\r?\n");
}

class ConsoleLog : UnityEngine.ILogHandler
{
    readonly bool quiet;
    public ConsoleLog(bool quiet) { this.quiet = quiet; }
    public void LogFormat(UnityEngine.LogType t, UnityEngine.Object c, string f, params object[] a) { if (!quiet) Console.WriteLine($"  [{t}] " + string.Format(f, a)); }
    public void LogException(Exception e, UnityEngine.Object c) { if (!quiet) Console.WriteLine($"  [Exception] {e}"); }
}
