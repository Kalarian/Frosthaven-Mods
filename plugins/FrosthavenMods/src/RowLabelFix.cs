using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// The distribution table's row labels ("110/110") sit 20 px left of their slot, and the table clips its left edge,
    /// so the first digit of 3-digit amounts was cut off. Fix() centres them and lets long ones shrink; Restore() puts
    /// them back, because the table is shared with the game's Building Donation screen.
    /// </summary>
    internal sealed class RowLabelFix
    {
        private readonly Dictionary<TextMeshProUGUI, (Vector2 Pos, bool AutoSize, float Min, float Max, bool Wrap)> _orig =
            new Dictionary<TextMeshProUGUI, (Vector2, bool, float, float, bool)>();

        public void Fix(Transform moduleRoot)
        {
            Transform headers = moduleRoot.Find("Panel/Content/Table/Viewport/Content/RowHeaders");
            if (headers == null) return;
            foreach (Transform header in headers)
            {
                var text = header.Find("Amount")?.GetComponent<TextMeshProUGUI>();
                if (text == null) continue;
                RectTransform rt = text.rectTransform;
                if (!_orig.ContainsKey(text))
                    _orig[text] = (rt.anchoredPosition, text.enableAutoSizing, text.fontSizeMin, text.fontSizeMax, text.enableWordWrapping);
                var o = _orig[text];
                rt.anchoredPosition = new Vector2(0f, o.Pos.y);
                text.enableWordWrapping = false;
                text.fontSizeMax = Mathf.Max(text.fontSize, o.Max);
                text.fontSizeMin = text.fontSizeMax * 0.6f;
                text.enableAutoSizing = true;
            }
        }

        public void Restore()
        {
            foreach (var kv in _orig)
            {
                if (kv.Key == null) continue;
                kv.Key.rectTransform.anchoredPosition = kv.Value.Pos;
                kv.Key.enableAutoSizing = kv.Value.AutoSize;
                kv.Key.fontSizeMin = kv.Value.Min;
                kv.Key.fontSizeMax = kv.Value.Max;
                kv.Key.enableWordWrapping = kv.Value.Wrap;
            }
            _orig.Clear();
        }
    }
}
