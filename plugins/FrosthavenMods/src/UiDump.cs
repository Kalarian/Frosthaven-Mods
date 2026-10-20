using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// Development aid: writes a UI object's hierarchy (names, sizes, layout and scroll components) to the BepInEx log
    /// once per session, so a game window's layout can be read without seeing the screen. Window layouts live in the
    /// game's asset files, not in its code.
    /// </summary>
    internal static class UiDump
    {
        private static readonly HashSet<string> Done = new HashSet<string>();
        private const int MaxDepth = 9, MaxLines = 300;

        public static void Once(string label, Transform root)
        {
            if (root == null || !Done.Add(label)) return;
            var sb = new StringBuilder();
            int lines = 0;
            Walk(root, 0, sb, ref lines);
            Plugin.Log.LogInfo($"UI dump: {label} ({lines} lines)\n{sb}");
        }

        private static void Walk(Transform t, int depth, StringBuilder sb, ref int lines)
        {
            if (lines >= MaxLines) return;
            lines++;
            sb.Append(' ', depth * 2).Append(t.gameObject.activeSelf ? "" : "(off) ").Append(t.name);
            if (t is RectTransform rt)
                sb.Append($"  size={rt.rect.width:0}x{rt.rect.height:0} anchors=({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) pivot=({rt.pivot.x:0.##},{rt.pivot.y:0.##}) pos=({rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0})");
            var parts = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                switch (c)
                {
                    case RectTransform _: break;
                    case ScrollRect s:
                        parts.Add($"ScrollRect(h={s.horizontal},v={s.vertical},content={s.content?.name},viewport={s.viewport?.name},hbar={s.horizontalScrollbar?.name},vbar={s.verticalScrollbar?.name})");
                        break;
                    case LayoutElement le:
                        parts.Add($"LayoutElement(min={le.minWidth:0}x{le.minHeight:0},pref={le.preferredWidth:0}x{le.preferredHeight:0},flex={le.flexibleWidth:0.#}x{le.flexibleHeight:0.#},ignore={le.ignoreLayout})");
                        break;
                    case HorizontalOrVerticalLayoutGroup g:
                        parts.Add($"{c.GetType().Name}(spacing={g.spacing:0},ctrlW={g.childControlWidth},ctrlH={g.childControlHeight},expW={g.childForceExpandWidth})");
                        break;
                    case ContentSizeFitter f:
                        parts.Add($"ContentSizeFitter(h={f.horizontalFit},v={f.verticalFit})");
                        break;
                    case Component other when other != null:
                        parts.Add(other.GetType().Name);
                        break;
                }
            }
            if (parts.Count > 0) sb.Append("  [").Append(string.Join(", ", parts)).Append(']');
            sb.Append('\n');
            if (depth >= MaxDepth) return;
            // Many identical columns/cells: dump the first two children of a kind, then summarize.
            var groups = t.Cast<Transform>().GroupBy(c => c.name);
            foreach (var g in groups)
            {
                int shown = 0;
                foreach (Transform child in g)
                {
                    if (shown++ < 2) Walk(child, depth + 1, sb, ref lines);
                }
                if (shown > 2) { sb.Append(' ', (depth + 1) * 2).Append($"... {shown - 2} more '{g.Key}'\n"); lines++; }
            }
        }
    }
}
