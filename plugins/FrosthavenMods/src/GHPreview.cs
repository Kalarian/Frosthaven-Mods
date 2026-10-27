using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Preview-effect configs for the pack's classes. Frosthaven's UIInfoTools carries one PreviewEffectConfigUI per
    /// class of its own, looked up by the class name; code that survived from Gloomhaven still asks for Gloomhaven's
    /// (WorldspaceUI.EffectsBar.ToggleDoomEffect asks for "Doomstalker" to draw the doom marker and read its icon off
    /// the answer: null, NullReferenceException, the game's error screen; the system probes, 2026-10-27). A missing
    /// config for a pack class is answered with the class's own active-ability icon and no text, the way the hero's
    /// perk icons are filled in (GHClasses.PerkIcon).
    ///   - Frosthaven's own classes are never touched: the postfix only acts on a null answer for a pack class.
    /// </summary>
    [HarmonyPatch]
    internal static class GHPreview
    {
        [HarmonyPatch(typeof(UIInfoTools), nameof(UIInfoTools.GetPreviewEffectConfig))]
        [HarmonyPostfix]
        private static void ClassConfig(UIInfoTools __instance, string id, ref PreviewEffectInfo __result)
        {
            if (__result != null || string.IsNullOrEmpty(id)) return;
            try
            {
                if (!GHClasses.PackClasses().Any(c => string.Equals(c.CharacterModel.ToString(), id, StringComparison.OrdinalIgnoreCase))) return;
                Sprite icon = __instance.GetCharacterActiveAbilityIcon(id);
                __result = new PreviewEffectInfo { previewEffectIcon = icon, previewEffectText = "" };
                Plugin.Log.LogInfo($"Gloomhaven classes: preview-effect config for {id} answered with the class icon ({(icon == null ? "none" : icon.name)})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Gloomhaven classes: preview-effect config: " + e.Message);
            }
        }
    }
}
