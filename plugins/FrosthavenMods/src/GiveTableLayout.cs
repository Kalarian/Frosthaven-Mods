using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// Layout tweaks for the Give Resources window, applied to the game's distribution table after each refresh:
    /// - More than MaxColumns columns: the table is capped at MaxColumns wide and scrolls sideways (mouse wheel, drag,
    ///   or a horizontal scrollbar cloned from the table's own vertical one). From the UI dump: Panel/Content/Table has
    ///   a ScrollRect (vertical only) over Viewport/Content, which holds RowHeaders + ColumnContainer; the module sets the
    ///   table's preferred width to fit every column.
    /// - Each portrait gets a hover tooltip with the character's name.
    /// The table is shared with the game's Building Donation screen, so Restore() puts everything back.
    /// </summary>
    internal sealed class GiveTableLayout
    {
        private const int MaxColumns = 6;
        private const string HBarName = "FrosthavenMods_HorizontalScrollbar";

        private ScrollRect _scroll;
        private bool _saved, _origHorizontal, _origVertical;
        private Vector2 _origAnchorMin, _origAnchorMax, _origPivot;
        private ContentSizeFitter.FitMode _origFit;
        private Scrollbar _hbar;
        private readonly List<UITextTooltipTarget> _tooltips = new List<UITextTooltipTarget>();
        private Transform _root;

        public void Apply(Transform moduleRoot, int columns)
        {
            _root = moduleRoot;
            Transform table = moduleRoot.Find("Panel/Content/Table");
            _scroll = table != null ? table.GetComponent<ScrollRect>() : null;
            if (_scroll == null || _scroll.content == null) { Plugin.Log.LogWarning("Give resources: table scroll area not found; no scrolling"); return; }
            RectTransform content = _scroll.content;
            var fitter = content.GetComponent<ContentSizeFitter>();
            if (!_saved)
            {
                _origHorizontal = _scroll.horizontal;
                _origVertical = _scroll.vertical;
                _origAnchorMin = content.anchorMin; _origAnchorMax = content.anchorMax; _origPivot = content.pivot;
                _origFit = fitter != null ? fitter.horizontalFit : ContentSizeFitter.FitMode.Unconstrained;
                _saved = true;
            }
            if (columns <= MaxColumns) { RestoreScroll(); return; }

            var headers = content.Find("RowHeaders") as RectTransform;
            var firstColumn = content.Find("ColumnContainer")?.Cast<Transform>().FirstOrDefault(t => t.gameObject.activeSelf) as RectTransform;
            float headerWidth = headers != null ? headers.rect.width : 82f;
            float columnWidth = firstColumn != null && firstColumn.rect.width > 0 ? firstColumn.rect.width : 170f;
            var le = table.GetComponent<LayoutElement>();
            if (le != null) le.preferredWidth = headerWidth + columnWidth * MaxColumns;

            // Content grows to its full width from the left edge; the viewport shows a window onto it.
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            if (fitter != null) fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.horizontal = true;
            _scroll.vertical = false; // the 4 rows fit; with vertical off the mouse wheel scrolls sideways
            _scroll.horizontalScrollbar = EnsureHorizontalBar(table);
            _scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private Scrollbar EnsureHorizontalBar(Transform table)
        {
            if (_hbar == null)
            {
                Transform existing = table.Find(HBarName);
                if (existing != null) _hbar = existing.GetComponent<Scrollbar>();
            }
            if (_hbar == null)
            {
                Scrollbar vbar = _scroll.verticalScrollbar;
                if (vbar == null) return null;
                GameObject go = Object.Instantiate(vbar.gameObject, table, false);
                go.name = HBarName;
                _hbar = go.GetComponent<Scrollbar>();
                _hbar.direction = Scrollbar.Direction.LeftToRight;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 1f);
                float thickness = ((RectTransform)vbar.transform).rect.width > 0 ? ((RectTransform)vbar.transform).rect.width : 8f;
                rt.sizeDelta = new Vector2(0f, thickness);
                rt.anchoredPosition = new Vector2(0f, -6f);
                if (_hbar.handleRect != null)
                {
                    _hbar.handleRect.anchorMin = new Vector2(_hbar.handleRect.anchorMin.x, 0f);
                    _hbar.handleRect.anchorMax = new Vector2(_hbar.handleRect.anchorMax.x, 1f);
                }
            }
            _hbar.gameObject.SetActive(true);
            var group = _hbar.GetComponent<CanvasGroup>();
            if (group != null) { group.alpha = 1f; group.interactable = true; group.blocksRaycasts = true; }
            return _hbar;
        }

        /// <summary>Hover text on each column's portrait, in column order.</summary>
        public void SetPortraitTooltips(IList<string> texts)
        {
            Transform columns = _root?.Find("Panel/Content/Table/Viewport/Content/ColumnContainer");
            if (columns == null) return;
            int i = 0;
            foreach (Transform column in columns)
            {
                if (!column.gameObject.activeSelf) continue;
                if (i >= texts.Count) break;
                Transform portrait = column.Find("ColumnHeader/FH_UI_OUT_HeroSelectionSlot_Distribution");
                if (portrait == null) { i++; continue; }
                var tip = portrait.GetComponent<UITextTooltipTarget>();
                if (tip == null)
                {
                    tip = portrait.gameObject.AddComponent<UITextTooltipTarget>();
                    tip.Initialize(UITooltipManager.Corner.Auto, Vector2.zero, false, 260f, 50f, true, false);
                }
                if (!_tooltips.Contains(tip)) _tooltips.Add(tip);
                tip.SetText(texts[i++]);
            }
        }

        private void RestoreScroll()
        {
            if (_scroll == null || !_saved) return;
            RectTransform content = _scroll.content;
            _scroll.horizontal = _origHorizontal;
            _scroll.vertical = _origVertical;
            _scroll.horizontalScrollbar = null;
            if (content != null)
            {
                content.anchorMin = _origAnchorMin; content.anchorMax = _origAnchorMax; content.pivot = _origPivot;
                content.anchoredPosition = new Vector2(0f, content.anchoredPosition.y);
                var fitter = content.GetComponent<ContentSizeFitter>();
                if (fitter != null) fitter.horizontalFit = _origFit;
            }
            if (_hbar != null) _hbar.gameObject.SetActive(false);
        }

        public void Restore()
        {
            RestoreScroll();
            foreach (var tip in _tooltips)
                if (tip != null) { tip.HideTooltip(0f); Object.Destroy(tip); }
            _tooltips.Clear();
        }
    }
}
