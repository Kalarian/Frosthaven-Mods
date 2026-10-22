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
    /// When the window opens, the scroll view is pointed at Content, so the game's own wheel, drag and scrollbar work.
    /// In game (only our rules listed), our "Mod pack rules" heading and its note are also pinned above the list, so only
    /// the switches scroll. Not a house rule: it's how the rule switches themselves are reached.
    /// </summary>
    [HarmonyPatch(typeof(UICampaignSettingsWindow), nameof(UICampaignSettingsWindow.Init))]
    internal static class RulesScroll
    {
        private const string PinnedName = "FrosthavenMods_PinnedHeader";
        private const int ScrollbarGap = 32;
        private static readonly HashSet<int> Padded = new HashSet<int>();

        private static readonly AccessTools.FieldRef<UICampaignSettingsWindow, RectTransform> GeneralContainer =
            AccessTools.FieldRefAccess<UICampaignSettingsWindow, RectTransform>("m_GeneralSettingsContainer");

        private static readonly AccessTools.FieldRef<UICampaignSettingsWindow, List<GameObject>> Rows =
            AccessTools.FieldRefAccess<UICampaignSettingsWindow, List<GameObject>>("m_SettingRows");

        [HarmonyPostfix]
        private static void FixScroll(UICampaignSettingsWindow __instance, UICampaignSettingsWindow.Params @params)
        {
            try
            {
                RectTransform container = GeneralContainer(__instance);
                if (container == null) return;
                UiDump.Once("Custom rules window", __instance.transform);
                ScrollRect scroll = container.GetComponentInParent<ScrollRect>(true);
                if (scroll == null || scroll.viewport == null || !container.IsChildOf(scroll.viewport))
                {
                    Plugin.Log.LogWarning("Rules list scrolling: no scroll view around the rule list; left as is.");
                    return;
                }
                // the viewport's child holding the list (Content: both lists, sized to fit them)
                Transform t = container;
                while (t.parent != scroll.viewport) t = t.parent;
                var content = (RectTransform)t;
                if (scroll.content != content)
                {
                    Plugin.Log.LogInfo($"Rules list scrolling: scroll view content '{scroll.content?.name}' -> '{content.name}'");
                    scroll.content = content;
                }
                // keep the rows' switches clear of the scrollbar (once per window: the padding outlives a reopen)
                var listLayout = content.GetComponent<VerticalLayoutGroup>();
                if (listLayout != null && Padded.Add(content.GetInstanceID()))
                {
                    RectOffset p = listLayout.padding; // a new object, so the layout is marked for a rebuild
                    listLayout.padding = new RectOffset(p.left, p.right + ScrollbarGap, p.top, p.bottom);
                }

                bool onlyOurs = @params.CampaignSpecific == null || @params.CampaignSpecific.Count == 0;
                PinHeader(scroll, onlyOurs ? Rows(__instance) : null);
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 1f;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Rules list scrolling: not fixed: " + e);
            }
        }

        /// <summary>Moves the first two rows (our heading and its note) into a panel above the scroll view, and makes the
        /// scroll view start below it. With no rows to pin, the scroll view gets its full height back.</summary>
        private static void PinHeader(ScrollRect scroll, List<GameObject> rows)
        {
            var view = (RectTransform)scroll.transform;
            Transform pinned = view.parent.Find(PinnedName);
            bool pin = rows != null && rows.Count >= 3 && rows[0] != null && rows[1] != null;
            if (!pin)
            {
                view.offsetMax = new Vector2(view.offsetMax.x, 0f);
                return;
            }
            if (pinned == null)
            {
                var go = new GameObject(PinnedName, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(view.parent, false);
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
                var layout = go.AddComponent<VerticalLayoutGroup>();
                // same sideways padding as the list, so the heading lines up with the rows
                var listLayout = scroll.content.GetComponent<VerticalLayoutGroup>();
                var innerLayout = rows[0].transform.parent.GetComponent<VerticalLayoutGroup>();
                int left = (listLayout?.padding.left ?? 0) + (innerLayout?.padding.left ?? 0);
                int right = (listLayout?.padding.right ?? 0) + (innerLayout?.padding.right ?? 0);
                layout.padding = new RectOffset(left, right, listLayout?.padding.top ?? 0, 8);
                layout.spacing = innerLayout?.spacing ?? 8f;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                pinned = rt;
            }
            rows[0].transform.SetParent(pinned, false);
            rows[1].transform.SetParent(pinned, false);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)pinned);
            float height = ((RectTransform)pinned).rect.height;
            view.offsetMax = new Vector2(view.offsetMax.x, -height);
            Plugin.Log.LogInfo($"Rules list scrolling: heading pinned ({height:0} high)");
        }
    }
}
