using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary.YML;
using TMPro;

namespace FrosthavenMods
{
    /// <summary>
    /// Printed card levels the engine can't hold: the 2nd edition Sawbones' six Medical Packs (33230-33235) print "M".
    /// Their card files keep "Level: X" (a "Level: M" broke the outpost map: FHMapView.InitUI NullReferenceException,
    /// commit d771375), so only the card face's level label is rewritten. FullAbilityCard.InitCardLevel is the one place
    /// the game writes that label (the hand, the card list and the big card all call it); the card data is untouched.
    /// Inert without the 2nd edition pack (no card has those IDs).
    /// </summary>
    [HarmonyPatch]
    internal static class GHCardLevelText
    {
        private static readonly HashSet<int> Reported = new HashSet<int>();

        private static string PrintedLevel(int id) =>
            id >= GHPerks.FirstMedicalPack2e && id <= GHPerks.LastMedicalPack2e ? "M" : null;

        [HarmonyPatch(typeof(FullAbilityCard), nameof(FullAbilityCard.InitCardLevel))]
        [HarmonyPostfix]
        private static void ShowPrintedLevel(AbilityCardYMLData yml, TextMeshProUGUI ___levelText)
        {
            if (yml == null || ___levelText == null) return;
            string printed = PrintedLevel(yml.ID);
            if (printed == null || ___levelText.text == printed) return;
            ___levelText.text = printed;
            if (Reported.Add(yml.ID))
                Plugin.Log.LogInfo($"GHRule PrintedCardLevel card {yml.ID} level label shows {printed} (data level {yml.Level})");
        }
    }
}
