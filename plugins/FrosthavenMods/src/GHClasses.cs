using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven classes in Frosthaven (stage 1 spike, the author's PC only). tools/gh_port.py converts a class from the
    /// player's own Gloomhaven install into new ruleset files plus its English text; this side makes the game cope with
    /// a class it has no art for:
    ///   - 3D model: Frosthaven ships no hero bundle for Gloomhaven models, and asking for one is a null-reference crash
    ///     (AssetBundleManager.BundleConfigForPrefab finds no config). Until the real model loads from Gloomhaven's
    ///     bundles (stage 2), any hero model without a bundle borrows the Drifter's (an Inox, like the Brute).
    ///   - Text: keys Frosthaven lacks ($ABILITY_CARD_Trample$, PERK_Brute_01...) are answered from
    ///     FrosthavenMods\ghclasses\strings.tsv; Frosthaven's own translation wins wherever it has the key.
    /// Inert without converted data: no strings file means no text answers, and Frosthaven's own heroes all have
    /// bundles, so the stand-in never applies to them.
    /// </summary>
    [HarmonyPatch]
    internal static class GHClasses
    {
        private const string StandInModel = "Drifter";
        private static readonly string Folder = Path.Combine(Paths.GameRootPath, "FrosthavenMods", "ghclasses");
        private static Dictionary<string, string> _strings;
        private static readonly Dictionary<string, bool> GameHasKey = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static Dictionary<string, string> Strings
        {
            get
            {
                if (_strings != null) return _strings;
                _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string file = Path.Combine(Folder, "strings.tsv");
                try
                {
                    if (File.Exists(file))
                    {
                        foreach (string line in File.ReadAllLines(file))
                        {
                            int tab = line.IndexOf('\t');
                            if (tab > 0) _strings[line.Substring(0, tab)] = line.Substring(tab + 1).Replace("\\n", "\n");
                        }
                        Plugin.Log.LogInfo($"Gloomhaven classes: {_strings.Count} text keys from {file}");
                    }
                }
                catch (Exception e) { Plugin.Log.LogError("Gloomhaven classes: could not read " + file + ": " + e); }
                return _strings;
            }
        }

        // ------------------------------------------------------------------ 3D model stand-in

        /// <summary>The hero model to load instead of <paramref name="prefabName"/>, or null to load it as asked.</summary>
        private static string StandIn(string prefabName)
        {
            // Only Gloomhaven's and Jaws of the Lion's heroes (the enum entries before Frosthaven's first, the Infuser)
            if (string.IsNullOrEmpty(prefabName) || !Enum.TryParse(prefabName, out ECharacter model) ||
                model == ECharacter.None || model >= ECharacter.Infuser) return null;
            AssetBundleManager abm = AssetBundleManager.Instance;
            if (abm == null || abm.AssetBundleExists("hero_" + prefabName.ToLowerInvariant().Replace(" ", ""))) return null;
            if (Reported.Add(prefabName)) Plugin.Log.LogInfo($"Gloomhaven classes: no 3D model for {prefabName} yet, using the {StandInModel}'s");
            return StandInModel;
        }

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.GetCharacterPrefabFromBundle))]
        [HarmonyPrefix]
        private static void Prefab(CActor.EType type, ref string prefabName, ref string skinString)
        {
            if (type != CActor.EType.Player) return;
            string standIn = StandIn(prefabName);
            if (standIn == null) return;
            prefabName = standIn;
            skinString = null;
        }

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.GetCharacterPrefabFromBundleAsync))]
        [HarmonyPrefix]
        private static void PrefabAsync(CActor.EType type, ref string prefabName, ref string skinString) => Prefab(type, ref prefabName, ref skinString);

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.EnsureActorPrefabBundlesLoadedBeforeAction))]
        [HarmonyPrefix]
        private static void EnsureActor(ref string actorPrefabName) => actorPrefabName = StandIn(actorPrefabName) ?? actorPrefabName;

        [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.EnsureCharacterLoadedBeforeAction))]
        [HarmonyPrefix]
        private static void EnsureCharacter(ref ECharacter characterToBeLoaded)
        {
            string standIn = StandIn(characterToBeLoaded.ToString());
            if (standIn != null) characterToBeLoaded = (ECharacter)Enum.Parse(typeof(ECharacter), standIn);
        }

        /// <summary>Scenario loading asks for the bundles of every party member's model up front.</summary>
        [HarmonyPatch(typeof(BundleLoadSettings), nameof(BundleLoadSettings.GetBundlesNeededForRequirements))]
        [HarmonyPrefix]
        private static void Bundles(ref List<ECharacter> charactersNeeded)
        {
            if (charactersNeeded == null) return;
            List<ECharacter> swapped = null;
            for (int i = 0; i < charactersNeeded.Count; i++)
            {
                string standIn = StandIn(charactersNeeded[i].ToString());
                if (standIn == null) continue;
                swapped = swapped ?? new List<ECharacter>(charactersNeeded);
                swapped[i] = (ECharacter)Enum.Parse(typeof(ECharacter), standIn);
            }
            if (swapped != null) charactersNeeded = swapped;
        }

        // ------------------------------------------------------------------ text

        private static bool TryOurs(string term, out string text)
        {
            text = null;
            if (term == null || Strings.Count == 0) return false;
            string key = term.Length > 1 && term[0] == '$' && term[term.Length - 1] == '$' ? term.Substring(1, term.Length - 2) : term;
            if (!Strings.TryGetValue(key, out text)) return false;
            if (key.StartsWith("GH_")) return true;
            if (!GameHasKey.TryGetValue(key, out bool has))
            {
                has = I2.Loc.LocalizationManager.GetTermData(key) != null;
                if (I2.Loc.LocalizationManager.Sources.Count > 0) GameHasKey[key] = has;  // only trust a loaded table
            }
            return !has;
        }

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPrefix]
        private static bool Translate(string term, ref string __result)
        {
            if (!TryOurs(term, out string text)) return true;
            __result = GLOOM.LocalizationManager.ReplaceTextVariables(text);
            return false;
        }

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.TryGetTranslation))]
        [HarmonyPrefix]
        private static bool TryTranslate(string term, ref string translation, ref bool __result)
        {
            if (!TryOurs(term, out string text)) return true;
            translation = GLOOM.LocalizationManager.ReplaceTextVariables(text);
            __result = true;
            return false;
        }
    }
}
