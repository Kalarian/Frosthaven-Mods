using System;
using System.Linq;
using System.Text.RegularExpressions;
using FHGUI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// A text button of our own, copied from the Give window's Give button (DistributionModule.DoneButton): the game's
    /// look without its keyboard shortcut, key hint icon or "which player suggests this" avatar circle.
    /// </summary>
    internal static class ButtonCopy
    {
        private static readonly Regex HintName = new Regex("hotkey|hint|glyph|prompt|input", RegexOptions.IgnoreCase);

        public static FHButton Make(FHMapView view, Transform parent, string name, string text, Action onClick)
        {
            var template = Traverse.Create(view.Modules.DistributionModule).Field("DoneButton").GetValue<FHButton>();
            if (template == null) { Plugin.Log.LogWarning("Frosthaven Mods: no button to copy"); return null; }
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;
            // No keyboard shortcut (the original answers Confirm/Enter).
            // (HotkeyClickableScopeElement lives in DevLocker.GFrame.Input; matched by name to avoid the reference.)
            foreach (Component c in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "HotkeyClickableScopeElement").ToList())
                UnityEngine.Object.DestroyImmediate(c);
            HideKeyHint(go);
            // The empty circle: the "which player suggests this" avatar list (from the UI dump).
            go.transform.Find("Container_SuggestingUsersList")?.gameObject.SetActive(false);

            var button = go.GetComponent<FHButton>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => GiveResourcesPatches.Safe(name, onClick));
            button.interactable = true;
            button.SetNonLocalizedText(text);
            var group = go.GetComponent<CanvasGroup>();
            if (group != null) { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
            return button;
        }

        /// <summary>The copied button keeps its key/controller hint icon (an empty circle once the hotkey is removed).
        /// Switch off every child that looks like one (by name or component type), never the label or the button's
        /// own frame.</summary>
        private static void HideKeyHint(GameObject button)
        {
            foreach (Transform t in button.GetComponentsInChildren<Transform>(true))
            {
                if (t == button.transform || t.GetComponent<TMP_Text>() != null) continue;
                bool hint = HintName.IsMatch(t.name) ||
                            t.GetComponents<Component>().Any(c => c != null && HintName.IsMatch(c.GetType().Name));
                if (hint && t.GetComponentInChildren<TMP_Text>(true) == null) t.gameObject.SetActive(false);
            }
        }
    }
}
