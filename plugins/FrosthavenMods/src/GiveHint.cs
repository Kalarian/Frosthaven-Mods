using System;
using System.Linq;
using FHGUI.Modules;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// Adds a "Ctrl + [left-click icon] Give" line under the party column's "Select / Edit" key hints, while the Give
    /// resources rule is on and outside scenarios. From the UI dump, the hint box (PartyColumnModule._hintsContainer,
    /// "Hints", LayoutElement pref height 60) has no layout group: each "Hint" row (88x20: Icon 20x20 at x=0, a label
    /// filling the rest) is placed by hand, Select at y=0 and Edit at y=-25. So the Select row is cloned, moved to
    /// y=-50, given a "Ctrl +" label in front of its mouse icon, and the box is made 25 px taller while the line shows.
    /// </summary>
    [HarmonyPatch]
    internal static class GiveHint
    {
        private const string CloneName = "FrosthavenMods_GiveHint";
        private const float RowStep = 25f, CtrlWidth = 36f, IconWidth = 20f, Gap = 2f;
        private static float? _origBoxHeight;

        [HarmonyPatch(typeof(PartyColumnModule), nameof(PartyColumnModule.Init), new[] { typeof(PartyColumnModule.PartyColumnParams) })]
        [HarmonyPostfix]
        private static void AddHint(GameObject ____hintsContainer)
        {
            GiveResourcesPatches.Safe("add the Ctrl + click Give hint", () =>
            {
                if (____hintsContainer == null) return;
                Transform box = ____hintsContainer.transform;
                Transform clone = box.Find(CloneName);
                var view = Singleton<FHMapChoreographer>.InstanceFast?.View;
                bool show = ModRules.GiveOn && view?.GameState?.ActiveScenario == null;
                if (clone == null)
                {
                    if (!show) return;
                    clone = Build(box);
                    if (clone == null) return;
                }
                clone.gameObject.SetActive(show);
                var boxLayout = box.GetComponent<LayoutElement>();
                if (boxLayout != null)
                {
                    if (_origBoxHeight == null) _origBoxHeight = boxLayout.preferredHeight;
                    boxLayout.preferredHeight = _origBoxHeight.Value + (show ? RowStep : 0f);
                }
            });
        }

        private static Transform Build(Transform box)
        {
            Transform select = FindSelectRow(box);
            if (select == null) { Plugin.Log.LogWarning("Give resources: no hint row to copy"); return null; }
            float lowest = box.Cast<Transform>().Where(t => t.name != CloneName).Select(t => ((RectTransform)t).anchoredPosition.y).DefaultIfEmpty(0f).Min();

            var row = (RectTransform)UnityEngine.Object.Instantiate(select.gameObject, box, false).transform;
            row.name = CloneName;
            row.SetAsLastSibling();
            row.anchoredPosition = new Vector2(row.anchoredPosition.x, lowest - RowStep);
            row.sizeDelta = new Vector2(row.sizeDelta.x + CtrlWidth, row.sizeDelta.y);

            var label = row.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            var icon = row.Find("Icon") as RectTransform;
            if (label == null || icon == null) { Plugin.Log.LogWarning("Give resources: hint row has no label/icon"); return row; }

            // "Ctrl +" in front: a copy of the label without its translation listener.
            var ctrl = (RectTransform)UnityEngine.Object.Instantiate(label.gameObject, row, false).transform;
            ctrl.name = "Label_Ctrl";
            var ctrlListener = ctrl.GetComponent<TextLocalizedListener>();
            if (ctrlListener != null) UnityEngine.Object.DestroyImmediate(ctrlListener);
            var ctrlText = ctrl.GetComponent<TMP_Text>();
            ctrlText.text = "Ctrl +";
            ctrlText.alignment = TextAlignmentOptions.MidlineLeft;
            ctrlText.enableWordWrapping = false;
            ctrl.anchorMin = new Vector2(0f, 0f);
            ctrl.anchorMax = new Vector2(0f, 1f);
            ctrl.pivot = new Vector2(0f, 0.5f);
            ctrl.sizeDelta = new Vector2(CtrlWidth, 0f);
            ctrl.anchoredPosition = Vector2.zero;

            // Mouse icon after it, then "Give".
            icon.anchoredPosition = new Vector2(CtrlWidth + Gap, icon.anchoredPosition.y);
            var labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2(CtrlWidth + Gap + IconWidth + Gap, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(0f, labelRect.offsetMax.y);
            label.enableWordWrapping = false;
            var listener = label.GetComponent<TextLocalizedListener>();
            if (listener != null) listener.SetTextKey("FHMODS_GIVE_HINT");
            else label.text = Text.Get("FHMODS_GIVE_HINT");
            return row;
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
