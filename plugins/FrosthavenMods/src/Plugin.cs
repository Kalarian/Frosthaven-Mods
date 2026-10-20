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
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            int n = 0;
            foreach (var _ in harmony.GetPatchedMethods()) n++;
            Log.LogInfo($"Frosthaven Mods {Version} loaded: {n} game methods patched. House rules: {ModRules.Describe()}");
        }
    }
}
