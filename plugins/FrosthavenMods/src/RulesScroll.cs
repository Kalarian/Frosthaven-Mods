using System;
using System.Collections.Generic;
using FHGUI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// The Custom Rules window's scroll view is pointed at the wrong object: its content is Content_CampaignSpesific (one
    /// of the two lists, empty in game) instead of Content, the sized parent of both lists. The game's list was short
    /// enough for that not to show; with our rules the last rows were cut off, with no scrollbar and no mouse wheel.
    /// Each time the window builds its rows (on Init, on Show, and twice per switch change), the scroll view is pointed
    /// at Content, so the game's own wheel, drag and scrollbar work; the rows keep clear of the scrollbar; and their
    /// tooltips stay on screen (the long descriptions ran off the bottom). Showing the window starts it at the top.
    /// Not a house rule: it's how the rule switches themselves are reached.
    /// </summary>
    [HarmonyPatch]
    internal static class RulesScroll
    {
        private const int ScrollbarGap = 32;
        private static readonly HashSet<int> Padded = new HashSet<int>();

        private static readonly AccessTools.FieldRef<UICampaignSettingsWindow, RectTransform> GeneralContainer =
            AccessTools.FieldRefAccess<UICampaignSettingsWindow, RectTransform>("m_GeneralSettingsContainer");

        private static readonly AccessTools.FieldRef<UICampaignSettingsWindow, List<GameObject>> Rows =
            AccessTools.FieldRefAccess<UICampaignSettingsWindow, List<GameObject>>("m_SettingRows");

        private static readonly AccessTools.FieldRef<UITooltipTarget, bool> ScreenBound =
            AccessTools.FieldRefAccess<UITooltipTarget, bool>("screenBound");

        [HarmonyPatch(typeof(UICampaignSettingsWindow), "PopulateSettings")]
        [HarmonyPostfix]
        private static void FixRows(UICampaignSettingsWindow __instance)
        {
            try
            {
                ScrollRect scroll = Scroll(__instance, out RectTransform content);
                if (scroll == null) return;
                if (scroll.content != content)
                {
                    Plugin.Log.LogInfo($"Rules list scrolling: scroll view content '{scroll.content?.name}' -> '{content.name}'");
                    scroll.content = content;
                }
                // keep the rows' switches clear of the scrollbar (once per window: the padding outlives a rebuild)
                var listLayout = content.GetComponent<VerticalLayoutGroup>();
                if (listLayout != null && Padded.Add(content.GetInstanceID()))
                {
                    RectOffset p = listLayout.padding; // a new object, so the layout is marked for a rebuild
                    listLayout.padding = new RectOffset(p.left, p.right + ScrollbarGap, p.top, p.bottom);
                }
                foreach (GameObject row in Rows(__instance))
                {
                    if (row == null) continue;
                    foreach (UITooltipTarget tip in row.GetComponentsInChildren<UITooltipTarget>(true))
                        ScreenBound(tip) = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Rules list scrolling: not fixed: " + e);
            }
        }

        [HarmonyPatch(typeof(UICampaignSettingsWindow), nameof(UICampaignSettingsWindow.Show))]
        [HarmonyPostfix]
        private static void StartAtTop(UICampaignSettingsWindow __instance)
        {
            try
            {
                if (Scroll(__instance, out RectTransform content) != null)
                    content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Rules list scrolling: not reset: " + e);
            }
        }

        /// <summary>The window's scroll view, and the viewport's child holding both lists (Content).</summary>
        private static ScrollRect Scroll(UICampaignSettingsWindow window, out RectTransform content)
        {
            content = null;
            RectTransform container = GeneralContainer(window);
            if (container == null) return null;
            UiDump.Once("Custom rules window", window.transform);
            ScrollRect scroll = container.GetComponentInParent<ScrollRect>(true);
            if (scroll == null || scroll.viewport == null || !container.IsChildOf(scroll.viewport))
            {
                Plugin.Log.LogWarning("Rules list scrolling: no scroll view around the rule list; left as is.");
                return null;
            }
            Transform t = container;
            while (t.parent != scroll.viewport) t = t.parent;
            content = (RectTransform)t;
            return scroll;
        }
    }
}
