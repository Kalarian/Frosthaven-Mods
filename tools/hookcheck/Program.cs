// Checks a built frosthaven.mods.dll against the installed game's DLLs without running the game:
// every Harmony patch target resolves, injected ___fields and parameter names exist, transpilers still find their
// instructions, static reflection lookups are non-null, and the Traverse member names used at runtime exist.
// When the plugin starts using Traverse with a new member name, add it to the list at the end.
//
//     dotnet run --project tools/hookcheck -c Release -- [plugin dll] [--game PATH] [-v]
//
// The plugin dll defaults to plugins/FrosthavenMods/bin/Release/frosthaven.mods.dll (run from the repo root).
// Exit code 0 = every check passed.
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

var positional = args.Where((a, i) => !a.StartsWith("-") && (i == 0 || args[i - 1] != "--game")).ToList();
int gameArg = Array.IndexOf(args, "--game");
string Game = gameArg >= 0 && gameArg + 1 < args.Length ? args[gameArg + 1] : @"C:\Program Files (x86)\Steam\steamapps\common\Frosthaven";
string pluginPath = Path.GetFullPath(positional.FirstOrDefault() ?? @"plugins\FrosthavenMods\bin\Release\frosthaven.mods.dll");
// the game's Managed folder (Frosthaven_Data, or GH_Data for Gloomhaven digital when checking the test harness) and BepInEx
string[] dirs = Directory.GetDirectories(Game, "*_Data").Select(d => Path.Combine(d, "Managed")).Where(Directory.Exists)
    .Append(Game + @"\BepInEx\core").ToArray();
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    foreach (var d in dirs) { var p = Path.Combine(d, name.Name + ".dll"); if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p); }
    return null;
};
Console.WriteLine($"checking {pluginPath} against {Game}");
var plugin = Assembly.LoadFrom(pluginPath);
int ok = 0, bad = 0;
void Ok(string s) { ok++; if (args.Contains("-v")) Console.WriteLine("  ok   " + s); }
void Bad(string s) { bad++; Console.WriteLine("  FAIL " + s); }

// Plugin.Log, so the transpilers' "not applied" warnings are captured.
var warnings = new List<string>();
var log = new BepInEx.Logging.ManualLogSource("check");
log.LogEvent += (_, e) => { if (e.Level <= BepInEx.Logging.LogLevel.Warning) warnings.Add(e.Data?.ToString()); };
// (the mods plugin's, or any plugin type with a static ManualLogSource "Log" field: the test harness has one too)
var logField = (plugin.GetType("FrosthavenMods.Plugin") ?? plugin.GetTypes().FirstOrDefault(t => t.GetField("Log", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.FieldType == typeof(BepInEx.Logging.ManualLogSource)))
    ?.GetField("Log", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
logField?.SetValue(null, log);

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
string[] kinds = { "Prefix", "Postfix", "Transpiler", "Finalizer" };
string[] special = { "__instance", "__result", "__state", "__exception", "__args", "__originalMethod", "__runOriginal", "__resultRef" };

Type[] types;
try { types = plugin.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); foreach (var le in e.LoaderExceptions) Bad("type load: " + le.Message); }

MethodBase Resolve(HarmonyMethod h)
{
    var t = h.declaringType; var n = h.methodName; var a = h.argumentTypes;
    switch (h.methodType ?? MethodType.Normal)
    {
        case MethodType.Getter: return AccessTools.DeclaredPropertyGetter(t, n) ?? AccessTools.PropertyGetter(t, n);
        case MethodType.Setter: return AccessTools.DeclaredPropertySetter(t, n) ?? AccessTools.PropertySetter(t, n);
        case MethodType.Constructor: return AccessTools.DeclaredConstructor(t, a);
        case MethodType.StaticConstructor: return AccessTools.GetDeclaredConstructors(t).FirstOrDefault(c => c.IsStatic);
        case MethodType.Enumerator: return AccessTools.EnumeratorMoveNext(AccessTools.Method(t, n, a));
        default:
            if (a == null && t != null && t.GetMethods(All).Count(m => m.Name == n) > 1) throw new AmbiguousMatchException($"{t.Name}.{n} has overloads; give argument types");
            return AccessTools.DeclaredMethod(t, n, a) ?? AccessTools.Method(t, n, a);
    }
}

void CheckPatch(string label, MethodInfo patch, MethodBase original, string kind)
{
    if (original == null) { Bad($"{label}: target method not found"); return; }
    var orig = original.GetParameters();
    foreach (var p in patch.GetParameters())
    {
        string n = p.Name;
        if (kind == "Transpiler" || special.Contains(n) || (n.StartsWith("__") && int.TryParse(n.Substring(2), out _))) continue;
        if (n.StartsWith("___")) { if (AccessTools.Field(original.DeclaringType, n.Substring(3)) == null) Bad($"{label}: field {n.Substring(3)} not on {original.DeclaringType.Name}"); continue; }
        if (!orig.Any(o => o.Name == n)) Bad($"{label}: parameter '{n}' not on {original.DeclaringType.Name}.{original.Name}");
    }
    if (kind == "Transpiler")
    {
        try
        {
            var input = PatchProcessor.GetOriginalInstructions(original); var before = input.Select(i => (i.opcode, i.operand)).ToList();
            warnings.Clear();
            var output = ((IEnumerable<CodeInstruction>)patch.Invoke(null, patch.GetParameters().Select(p =>
                p.ParameterType == typeof(IEnumerable<CodeInstruction>) ? (object)input : p.ParameterType == typeof(MethodBase) ? original : null).ToArray())).ToList();
            if (warnings.Count > 0) { Bad($"{label}: {string.Join("; ", warnings)}"); return; }
            int changed = output.Count != before.Count ? 1 : output.Zip(before).Count(x => x.First.opcode != x.Second.opcode || !Equals(x.First.operand, x.Second.operand));
            if (changed == 0) { Bad($"{label}: transpiler changed nothing"); return; }
        }
        catch (Exception e) { Bad($"{label}: transpiler threw {e.GetBaseException().Message}"); return; }
    }
    Ok($"{label} -> {original.DeclaringType.Name}.{original.Name}");
}

foreach (var type in types.Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
{
    HarmonyMethod cls;
    try { cls = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(type)); }
    catch (Exception e) { Bad($"{type.Name}: attributes: {e.GetBaseException().Message}"); continue; }
    var targetMethod = type.GetMethod("TargetMethod", All);
    var targetMethods = type.GetMethod("TargetMethods", All);
    foreach (var m in type.GetMethods(All))
    {
        string kind = kinds.FirstOrDefault(k => m.Name == k || m.GetCustomAttributes(false).Any(a => a.GetType().Name == "Harmony" + k));
        if (kind == null) continue;
        string label = $"{type.Name}.{m.Name}";
        try
        {
            if (targetMethods != null)
                foreach (var o in (IEnumerable<MethodBase>)targetMethods.Invoke(null, null)) CheckPatch(label, m, o, kind);
            else if (targetMethod != null) CheckPatch(label, m, (MethodBase)targetMethod.Invoke(null, null), kind);
            else CheckPatch(label, m, Resolve(cls.Merge(HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromMethod(m)))), kind);
        }
        catch (Exception e) { Bad($"{label}: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
    }
}

// Static reflection lookups (static readonly MethodInfo / FieldRef fields).
foreach (var type in types)
{
    var fields = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(f => typeof(MemberInfo).IsAssignableFrom(f.FieldType) || f.FieldType.Name.StartsWith("FieldRef")).ToList();
    if (fields.Count == 0) continue;
    try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle); }
    catch (Exception e) { Bad($"{type.Name} static init: {e.GetBaseException().Message}"); continue; }
    foreach (var f in fields) { if (f.GetValue(null) == null) Bad($"{type.Name}.{f.Name} is null (member not found)"); else Ok($"{type.Name}.{f.Name}"); }
}

// Traverse lookups by name at runtime.
Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Select(a => { try { return a.GetTypes().FirstOrDefault(t => t.Name == n || t.FullName == n); } catch { return null; } }).FirstOrDefault(t => t != null);
Type ElementOf(Type t) => t.IsGenericType ? t.GetGenericArguments()[0] : t.GetElementType();
void Member(string label, Type t, string name, bool property = false)
{
    if (t == null) { Bad($"{label}: type not found"); return; }
    var m = property ? (MemberInfo)AccessTools.Property(t, name) : AccessTools.Field(t, name);
    if (m == null) Bad($"{label}: {t.Name}.{name} not found"); else Ok($"{label}: {t.Name}.{name}");
}
if (plugin.GetType("FrosthavenMods.Plugin") != null)   // the mods plugin's reflection-only members; not for other plugins
{
    var rowHeaders = AccessTools.Field(T("UIDebriefingTable") ?? T("DebriefingTable"), "m_RowHeaders");
    if (rowHeaders == null) Bad("BattleStats: m_RowHeaders owner not found (checked via its patch above)"); else Member("BattleStats Icon", ElementOf(rowHeaders.FieldType), "Icon");
    var modules = AccessTools.Property(T("FHMapView"), "Modules")?.PropertyType ?? AccessTools.Field(T("FHMapView"), "Modules")?.FieldType;
    var dist = modules == null ? null : (AccessTools.Property(modules, "DistributionModule")?.PropertyType ?? AccessTools.Field(modules, "DistributionModule")?.FieldType);
    Member("ButtonCopy DoneButton", dist, "DoneButton");
    foreach (var f in new[] { "m_ScenarioRNG", "m_EnemyIDRNG", "m_EnemyAbilityCardRNG", "m_GuidRNG" })
    {
        var fi = AccessTools.Field(T("ScenarioRuleLibrary.ScenarioState"), f);
        if (fi == null) { Bad($"DesyncRecorder: ScenarioState.{f} not found"); continue; }
        foreach (var g in new[] { "SeedArray", "inext", "inextp" }) Member($"DesyncRecorder {f}", fi.FieldType, g);
    }
    foreach (var f in new[] { "_bSpawner", "_cSpawner1", "_cSpawner2", "_eSpawner1", "_eSpawner2", "_damagedThisRound" })
        Member("DwarfTweaks FH014", T("ScenarioRuleLibrary.ScenarioScript_FH014"), f);
    Member("EventPreview Data", T("OutpostEventModule"), "Data", property: true);
}

Console.WriteLine($"{ok} ok, {bad} failed");
return bad == 0 ? 0 : 1;
