using System;
using System.Linq;
using FHGUI.Modules;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Adds a "Ctrl: Give" line under the party column's "Select / Edit" key hints, while the Give resources rule is on
    /// and outside scenarios. The hint box is a static part of the game's UI (PartyColumnModule._hintsContainer), so its
    /// left-click ("Select") row is cloned and relabelled. Its layout is logged once (UiDump) in case the line needs
    /// nudging.
    /// </summary>
    [HarmonyPatch]
    internal static class GiveHint
    {
        private const string CloneName = "FrosthavenMods_GiveHint";

        [HarmonyPatch(typeof(PartyColumnModule), nameof(PartyColumnModule.Init), new[] { typeof(PartyColumnModule.PartyColumnParams) })]
        [HarmonyPostfix]
        private static void AddHint(GameObject ____hintsContainer)
        {
            GiveResourcesPatches.Safe("add the Ctrl: Give hint", () =>
            {
                if (____hintsContainer == null) return;
                Transform box = ____hintsContainer.transform;
                UiDump.Once("Party column key hints", box);
                Transform clone = box.Find(CloneName);
                var view = Singleton<FHMapChoreographer>.InstanceFast?.View;
                bool show = ModRules.IsOn(ModRules.GiveResources) && view?.GameState?.ActiveScenario == null;
                if (clone == null)
                {
                    if (!show) return;
                    Transform select = FindSelectRow(box);
                    if (select == null) { Plugin.Log.LogWarning("Give resources: no hint row to copy"); return; }
                    clone = UnityEngine.Object.Instantiate(select.gameObject, box, false).transform;
                    clone.name = CloneName;
                    clone.SetAsLastSibling();
                    var listener = clone.GetComponentInChildren<TextLocalizedListener>(true);
                    if (listener != null) listener.SetTextKey("FHMODS_GIVE_HINT");
                    else
                    {
                        var text = clone.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
                        if (text != null) text.text = Text.Get("FHMODS_GIVE_HINT");
                    }
                }
                clone.gameObject.SetActive(show);
            });
        }

        /// <summary>The row whose text is the game's "Select" (left-click) hint; else the first row with text.</summary>
        private static Transform FindSelectRow(Transform box)
        {
            string select = GLOOM.LocalizationManager.GetTranslation("GUI_SELECT");
            Transform first = null;
            foreach (Transform row in box)
            {
                if (row.name == CloneName) continue;
                var text = row.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
                if (text == null) continue;
                if (first == null) first = row;
                if (!string.IsNullOrEmpty(select) && text.text.IndexOf(select, StringComparison.OrdinalIgnoreCase) >= 0) return row;
            }
            return first;
        }
    }
}
