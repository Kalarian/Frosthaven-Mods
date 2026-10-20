using System.Collections.Generic;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// Our own text for game UI that only takes translation keys (window titles, button labels): any key starting with
    /// FHMODS_ is answered from this table by a postfix on the game's translation lookup.
    /// </summary>
    [HarmonyPatch]
    internal static class Text
    {
        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_GIVE_TITLE"] = "Give Resources",
            ["FHMODS_GIVE"] = "Give",
            ["FHMODS_GIVE_HINT"] = "Ctrl: Give",
        };

        public static string Get(string key) => Strings.TryGetValue(key, out string text) ? text : key;

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_") && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
