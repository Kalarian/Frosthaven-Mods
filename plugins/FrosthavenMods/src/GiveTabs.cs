using System;
using FHGUI;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// The "Resources" / "Items" buttons at the top of the Give window. From the UI dump, the window's Content column
    /// is: description text, a 45 px "Separator" row (a short line in the middle), then the table. The two buttons sit
    /// in the Separator row in place of its line, outside the layout. The window is the game's shared distribution
    /// table, so Hide() takes them away and puts the line back when our window closes.
    /// </summary>
    internal sealed class GiveTabs
    {
        private const string ResourcesName = "FrosthavenMods_TabResources", ItemsName = "FrosthavenMods_TabItems";
        private const float Scale = 0.7f, Gap = 8f, InactiveAlpha = 0.45f;

        private FHButton _resources, _items;
        private GameObject _line;

        public void Show(FHMapView view, Transform moduleRoot, bool itemsActive, string resourcesText, string itemsText, Action onResources, Action onItems)
        {
            Transform row = moduleRoot.Find("Panel/Content/Separator");
            if (row == null) { Plugin.Log.LogWarning("Give window: no separator row for the tabs"); return; }
            _line = row.Find("FH_Separator_Horizontal")?.gameObject;
            _line?.SetActive(false);
            _resources = Get(view, row, ResourcesName, right: true);
            _items = Get(view, row, ItemsName, right: false);
            Set(_resources, resourcesText, !itemsActive, onResources);
            Set(_items, itemsText, itemsActive, onItems);
        }

        public void Hide()
        {
            if (_resources != null) _resources.gameObject.SetActive(false);
            if (_items != null) _items.gameObject.SetActive(false);
            if (_line != null) _line.SetActive(true);
        }

        /// <summary>The tab buttons stay in the shared window between uses; made on first use.</summary>
        private static FHButton Get(FHMapView view, Transform row, string name, bool right)
        {
            Transform existing = row.Find(name);
            if (existing != null) return existing.GetComponent<FHButton>();
            FHButton button = ButtonCopy.Make(view, row, name, "", () => { });
            if (button == null) return null;
            var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            // Left button's right edge and right button's left edge meet in the middle, Gap apart.
            rt.pivot = new Vector2(right ? 1f : 0f, 0.5f);
            rt.anchoredPosition = new Vector2(right ? -Gap / 2 : Gap / 2, 0f);
            rt.localScale = new Vector3(Scale, Scale, 1f);
            UiDump.Once("Give window tab button (copy)", button.transform);
            return button;
        }

        private static void Set(FHButton button, string text, bool active, Action onClick)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);
            button.SetNonLocalizedText(text);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => GiveResourcesPatches.Safe("switch tab", onClick));
            button.interactable = true;
            // The open tab is bright, the other one dimmed.
            var group = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = active ? 1f : InactiveAlpha;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
    }
}
