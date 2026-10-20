using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// The Frosthaven mod pack's code side. Each feature is a campaign house rule (see ModRules), so it is saved with the
    /// campaign and synced to every co-op player by the game's own house-rule network action.
    /// </summary>
    [BepInPlugin(Guid, "Frosthaven Mods", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "frosthaven.mods";
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            CoopLog.SessionStart();
            var harmony = new Harmony(Guid);
            // One patch class at a time: if a game update breaks one feature's patch, the others still load.
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { harmony.CreateClassProcessor(type).Patch(); }
                catch (System.Exception e) { Log.LogError($"Could not patch {type.Name} (that feature is off): {e}"); }
            }
            int n = 0;
            foreach (var _ in harmony.GetPatchedMethods()) n++;
            Log.LogInfo($"Frosthaven Mods {Version} loaded: {n} game methods patched. House rules: {ModRules.Describe()}");
        }
    }
}
