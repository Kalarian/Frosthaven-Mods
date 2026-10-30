using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// A Gloomhaven card drawn in Frosthaven's style (the user's layout review, 2026-10-29). Frosthaven's own cards are
    /// hand-made prefabs; ours come from the card generator Frosthaven inherited from Gloomhaven digital, whose pieces
    /// still carry Gloomhaven's look. After the game generates a Gloomhaven class's card (every class since 2026-10-30), each half is
    /// redrawn to Frosthaven's measured geometry (carddump of Sodden Soil, Launch Pod, Primal Bellow, Call of Doom,
    /// Overwhelming Wave; numbers below are Frosthaven's, in pixels of a card 504 wide, scaled to the card):
    ///   - text in Frosthaven's card font (FH_Font_AbilityCards), the ability rows only (the XP badge keeps Sarala-Bold);
    ///   - an ability with modifiers is one centred line: value, its slots, 7 px, then the pill: AddedEffectRoundedBorder
    ///     tiled at 2x density, 39 tall, 9.5 padding, its text at 18, the class colour at alpha 0x78 (Sodden Soil);
    ///   - a self-targeted value sits in FH_UI_CARDS_PositiveEffectBackground sliced at 2.4x density, 41 tall, 13 / 12
    ///     padding, the value at 20 (Launch Pod);
    ///   - consumed and infused elements are Frosthaven's art (the card icon atlas; a consume's own sprite list too);
    ///   - infusions are 48 px, their bottom 11.5 px above the half's edge, the abilities centred above (Call of Doom);
    ///   - area hexes are drawn here to Frosthaven's: pointy-topped, 53 wide, 2 px apart on a regular grid, a 3.2 px white
    ///     rim, a thin pale X on the red ones, red 199/32/38, grey 59, blue 32/148/198 (Overwhelming Wave; Frosthaven's
    ///     areas are one picture per card, so its hex art isn't a sprite to reuse).
    /// The layout side (sizes, the pill rows, the dashed box, Frosthaven's words) is the port's (gh_layout.fh_style).
    /// </summary>
    [HarmonyPatch]
    internal static class GHCardStyle
    {
        private const string Atlas = "AbilityCardSpriteAtlas";
        private const float TintAlpha = 0x78 / 255f;
        private const float HalfWidthPx = 478f;   // a card half, in the pixels the numbers below are measured in

        // Frosthaven's pill (Sodden Soil) and self box (Launch Pod)
        private const float EdgeMargin = 14f, PillHeight = 39f, PillPad = 9.5f, PillGap = 7f, PillText = 18f, PillPpum = 2f;
        private const float SelfHeight = 41f, SelfPadLeft = 13f, SelfPadRight = 12f, SelfPpum = 2.4f;
        // infusions (Call of Doom, Primal Bellow)
        private const float BadgeBottom = 6f;
        private const float GeneratorElement = 51f, GeneratorSlot = 26f, SlotSize = 32f, SlotGap = 5f;
        private const float PillSlotPull = 3f, PillSlotAfter = 21f, PillSlotEnd = 2f;
        private const float InfuseSize = 48f, InfuseBottom = 11.5f, InfuseBand = 72f, ContentTop = 9f;
        // wrapped text keeps 24 px from the half's sides (Against All Odds: 26..27; the generator's rows run to 11); a
        // half that doesn't fit is scaled to keep FitEdge px from the sides, clear of the badges and the infusion band
        // an area beside its line keeps 20 px from the rim and 24 from the line, or goes under it (Frosthaven's 51 area halves)
        private const float AreaRim = 20f, AreaGap = 24f, AreaBelow = 12f;
        private const float TextMargin = 24f, FitEdge = 10f, FitMin = 0.6f, ConsumeMinText = 0.8f;
        private const bool FitTrace = false;   // true: log every half's measured content (the fit review)
        // hexes (Overwhelming Wave)
        private const float HexDot = 54f, HexColumn = 54.5f, HexWidth = 53f, HexRim = 3.2f, HexXHalf = 10.3f, HexXStroke = 2.8f;

        private static readonly Regex Consume = new Regex(@"^Consume(Fire|Ice|Air|Earth|Light|Dark|Any|Wild)$");
        private static readonly Regex Create = new Regex(@"^Create(Fire|Ice|Air|Earth|Light|Dark|Any|Wild)(_Highlight)?$");

        private static Dictionary<string, Sprite> _atlas;
        private static Sprite _pill, _self;
        private static readonly Dictionary<string, Sprite> HexSprites = new Dictionary<string, Sprite>();
        private static TMP_FontAsset _font;
        private static readonly HashSet<string> Reported = new HashSet<string>();

        // the card being generated: MakeFullCard knows its data, MakeFullCardContinued generates the halves' rows (the
        // card's own AbilityCard is set only later, by SetupClassElements)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FullAbilityCard, AbilityCardYMLData> Pending =
            new System.Runtime.CompilerServices.ConditionalWeakTable<FullAbilityCard, AbilityCardYMLData>();

        [HarmonyPatch(typeof(FullAbilityCard), nameof(FullAbilityCard.MakeFullCard))]
        [HarmonyPostfix]
        private static void Remember(FullAbilityCard __instance, AbilityCardYMLData cardData)
        {
            Pending.Remove(__instance);
            if (cardData != null) Pending.Add(__instance, cardData);
        }

        [HarmonyPatch(typeof(FullAbilityCard), nameof(FullAbilityCard.MakeFullCardContinued))]
        [HarmonyPostfix]
        private static void AfterHalves(FullAbilityCard __instance)
        {
            try
            {
                if (!Styled(__instance, out ECharacter model)) return;
                Load();
                Color tint = GHClasses.ClassColours.TryGetValue(model, out Color colour) ? colour : new Color(0.45f, 0.66f, 1f);
                tint.a = TintAlpha;
                foreach (FullAbilityCardAction half in new[] { __instance.topActionButton, __instance.bottomActionButton })
                {
                    if (half == null || !(half.transform is RectTransform h)) continue;
                    float f = h.rect.width / HalfWidthPx;   // card units per measured pixel
                    Transform rows = h.Find("Content/Layout Parent");
                    if (rows == null) continue;
                    Fonts(rows);
                    // an Augment half (Mindthief) has its own stacked layout: the fit passes slid it under the top rim
                    bool augment = rows.GetComponentsInChildren<Transform>(true).Any(c => c.name == "AugmentRow");
                    if (!augment) Margins(h, (RectTransform)rows, f);
                    Slots(rows, f);
                    ConsumeSprites(h);
                    Pictures(h, tint);
                    Areas(rows, f);
                    Pills(rows, tint, f);
                    ConsumeBoxes(rows, h, f);
                    if (!augment) Relayout(rows);
                    SelfBoxes(rows, f);
                    AreaClear(h, (RectTransform)rows, f);
                    Trackers(h, (RectTransform)rows, f);
                    Infusions(h, rows as RectTransform, f);
                    Badges(h, f);
                    if (!augment) Fit(h, (RectTransform)rows, f);
                }
            }
            catch (Exception e) { if (Reported.Add("style")) Plugin.Log.LogWarning("Gloomhaven card style: " + e); }
        }

        /// <summary>The generator paints its pills and dashed boxes in the class colour after the card is built
        /// (CreateLayout.SetVisualsFromConfig); Frosthaven's are that colour, translucent.</summary>
        [HarmonyPatch(typeof(CreateLayout), nameof(CreateLayout.SetVisualsFromConfig))]
        [HarmonyPostfix]
        private static void Translucent(CreateLayout __instance)
        {
            try
            {
                if (!(Traverse.Create(__instance).Field("_graphicElements").GetValue() is System.Collections.IList list)) return;
                foreach (object o in list)
                {
                    if (!(Traverse.Create(o).Field("Item1").GetValue() is Image img) || img == null || img.sprite == null) continue;
                    if (img.sprite.name != "AddedEffectRoundedBorder" && img.sprite.name != "ConditionalElement") continue;
                    if (!Styled(img.GetComponentInParent<FullAbilityCard>(), out _)) continue;
                    Color c = img.color;
                    c.a = TintAlpha;
                    img.color = c;
                }
            }
            catch (Exception e) { if (Reported.Add("tint")) Plugin.Log.LogWarning("Gloomhaven card style: tint: " + e); }
        }

        private static bool Styled(FullAbilityCard card, out ECharacter model)
        {
            model = default;
            if (card == null || !Pending.TryGetValue(card, out AbilityCardYMLData data) || data?.CharacterID == null) return false;
            CCharacterClass cls = GHClasses.ClassById(data.CharacterID);
            return cls != null && GHClasses.IsGloomhavenClass(cls.AbilityCardArt, out model);
        }

        private static void Load()
        {
            if (_atlas != null) return;
            _atlas = new Dictionary<string, Sprite>();
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s == null) continue;
                if (s.texture != null && s.texture.name == Atlas && !_atlas.ContainsKey(s.name)) _atlas[s.name] = s;
                else if (s.name == "AddedEffectRoundedBorder" && _pill == null) _pill = s;
                else if (s.name == "FH_UI_CARDS_PositiveEffectBackground" && _self == null) _self = s;
            }
            _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name == "FH_Font_AbilityCards");
            Plugin.Log.LogInfo($"Gloomhaven card style: {_atlas.Count} card icons, pill {(_pill != null ? "ok" : "MISSING")}, self box {(_self != null ? "ok" : "MISSING")}, font {(_font != null ? "ok" : "MISSING")}");
        }

        // ------------------------------------------------------------------ text and pictures

        private static void Fonts(Transform rows)
        {
            if (_font == null) return;
            foreach (TMP_Text t in rows.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.name == "FH_Sarala-Regular")
                {
                    t.font = _font;
                    t.ForceMeshUpdate();
                }
        }

        /// <summary>Text that runs closer than 24 px to the half's sides gets the difference as its margin, so it wraps where
        /// Frosthaven's does; the rows are then laid out again (the generator's layout groups size each row by its text).
        /// The dashed box, the pills and the self box are left alone: they are one line, placed below.</summary>
        private static void Margins(RectTransform half, RectTransform rows, float f)
        {
            foreach (TMP_Text t in rows.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!t.enableWordWrapping || !Shown(t.transform, rows) || string.IsNullOrWhiteSpace(t.text)) continue;
                if (t.text.Contains("<link=fhpill>") || t.text.Contains("<link=fhself>")) continue;
                if (t.GetComponentsInParent<Transform>(true).Any(p => p.name == "ConditionalElement" || p.name == "Consume Button")) continue;
                var rt = (RectTransform)t.transform;
                Rect r = In(rt, half);
                // a narrow column (Beast Tyrant's "Command" beside its ally's attack) would only break its word
                if (r.width < 0.6f * half.rect.width) continue;
                float want = TextMargin * f;
                float left = Mathf.Max(0f, half.rect.xMin + want - r.xMin), right = Mathf.Max(0f, r.xMax - (half.rect.xMax - want));
                if (left < 0.5f * f && right < 0.5f * f) continue;
                float k = rt.rect.width / Mathf.Max(r.width, 0.01f);   // the text's units per half unit
                t.margin = new Vector4(left * k, t.margin.y, right * k, t.margin.w);
                // a new margin doesn't mark the text's preferred height stale: without this the rows kept their one-line
                // heights and a wrapped line ran into the next row (Rumbling Advance, Rocky End)
                t.SetLayoutDirty();
                t.ForceMeshUpdate(true, true);
            }
            // always: the generator sized its rows for Gloomhaven's font, and Frosthaven's is taller (a use tracker under a
            // three-line text sat on its last line: Shrapnel, Heart Seeker)
            Relayout(rows);
        }

        private static readonly HashSet<string> FixedHeight = new HashSet<string> { "Consume Button", "InfuseContainer", "SummonContainer",
            "XPContainer", "IconContainer", "DividerCardElement", "MandatoryTrigger", "ConditionalElement", "BlinkbladeState_Fast",
            "BlinkbladeState_Slow", "AddedEffect" };

        /// <summary>The generator's own row sizing (CreateLayout.GetTotalHeight with andSet), run again for the new font and
        /// margins: its rows are sized and stacked by hand, not by layout groups, so a wrapped line didn't grow its row
        /// (Rumbling Advance, Rocky End). The generator's pass also reverses the children once (it builds them bottom-up);
        /// that's done already, so this one keeps their order.</summary>
        private static float Relayout(Transform parent)
        {
            return RelayoutRows(parent);
        }

        private static float RelayoutRows(Transform parent)
        {
            float spacing = GlobalSettings.Instance.m_AbilityCardSettings.RowSpacing;
            var kids = parent.Cast<Transform>().ToList();
            float total = 0f;
            foreach (Transform child in kids)
            {
                float h = 0f;
                if (child.name == "Column Container" || child.name == "Row Container")
                {
                    h = RelayoutRows(child);
                    if (child.name == "Row Container" && child.Cast<Transform>().All(x => x.name == "Text Container")) h += spacing;
                }
                else if (child.name == "Text Container" && child.GetComponent<TextMeshProUGUI>() is TextMeshProUGUI t)
                {
                    t.SetLayoutDirty();   // the height cached for Gloomhaven's font is stale (Shrapnel: a line short)
                    t.ForceMeshUpdate(true);
                    h = t.preferredHeight;
                }
                else if (child.name == "AreaEffectContainer" && child.GetComponent<LayoutElement>() is LayoutElement le) h = le.preferredHeight;
                else if (FixedHeight.Contains(child.name)) h = ((RectTransform)child).rect.height;
                total = child.name == "Column Container" ? Mathf.Max(total, h) : total + h;
            }
            if (parent.name == "Row Container" && parent is RectTransform row)
                row.sizeDelta = new Vector2(row.sizeDelta.x, total + (kids.All(x => x.name == "Text Container") ? spacing : 0f));
            if (kids.Any(x => x.name == "Row Container"))
            {
                float y = total / 2f;
                foreach (RectTransform c in kids.OfType<RectTransform>())
                {
                    c.anchoredPosition = new Vector2(c.anchoredPosition.x, y);
                    y -= c.rect.height;
                }
            }
            return total;
        }

        /// <summary>Active itself and up to (not including) the root: the card is built hidden, so activeInHierarchy is false.</summary>
        private static bool Shown(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent)
                if (!p.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>A consume's element keeps its own list of pictures (ConsumeElement.elementSprites, Gloomhaven
        /// digital's arrows) and sets one again whenever the element is chosen: the list itself takes Frosthaven's.</summary>
        private static void ConsumeSprites(Transform half)
        {
            foreach (ConsumeElement ce in half.GetComponentsInChildren<ConsumeElement>(true))
            {
                var field = Traverse.Create(ce).Field("elementSprites");
                if (!(field.GetValue() is Sprite[] list)) continue;
                var copy = (Sprite[])list.Clone();
                for (int i = 0; i < copy.Length; i++)
                    if (copy[i] != null && Consume.IsMatch(copy[i].name) && _atlas.TryGetValue(copy[i].name, out Sprite fh)) copy[i] = fh;
                field.SetValue(copy);
            }
        }

        private static void Pictures(Transform half, Color tint)
        {
            foreach (Image img in half.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null) continue;
                string name = img.sprite.name.Replace("(Clone)", "");
                string want = null;
                Match m;
                if (Consume.IsMatch(name) && img.sprite.texture != null && img.sprite.texture.name != Atlas) want = name;
                else if ((m = Create.Match(name)).Success) want = m.Groups[1].Value;
                if (want != null && _atlas.TryGetValue(want, out Sprite s) && img.sprite != s)
                {
                    img.sprite = s;
                    img.preserveAspect = true;
                }
                if (name == "AddedEffectRoundedBorder" || name == "ConditionalElement") img.color = tint;
                // Frosthaven's dashed consume box (Call of Doom: CBackground tiled at 4.9x density)
                if (name == "ConditionalElement") { img.type = Image.Type.Tiled; img.pixelsPerUnitMultiplier = 4.9f; }
            }
        }

        // ------------------------------------------------------------------ geometry helpers

        /// <summary>A rect's corners in another transform's space: (xMin, yMin, xMax, yMax).</summary>
        private static Rect In(RectTransform rt, Transform space)
        {
            var w = new Vector3[4];
            rt.GetWorldCorners(w);
            Vector3 a = space.InverseTransformPoint(w[0]), b = space.InverseTransformPoint(w[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>What a row container draws, in another transform's space: its text (centred in its rect, at its
        /// preferred size; the card is built hidden, so there is no mesh to measure) and its enhancement slots.</summary>
        private static Rect Drawn(RectTransform row, Transform space)
        {
            Rect r = Rect.zero;
            bool any = false;
            foreach (Transform c in row)
            {
                if (!c.gameObject.activeSelf || !(c is RectTransform crt)) continue;
                Rect cr;
                if (c.GetComponent<TMP_Text>() is TMP_Text t)
                {
                    Vector2 pref = t.GetPreferredValues(t.text, float.PositiveInfinity, float.PositiveInfinity);
                    if (pref.x <= 0.01f) continue;
                    Rect box = In(crt, space);
                    float k = box.width / Mathf.Max(crt.rect.width, 0.01f);
                    cr = new Rect(box.center.x - pref.x * k / 2f, box.center.y - pref.y * k / 2f, pref.x * k, pref.y * k);
                }
                else if (c.name == "EnhancementContainer" && c.childCount > 0) cr = In(crt, space);
                else continue;
                r = any ? Rect.MinMaxRect(Mathf.Min(r.xMin, cr.xMin), Mathf.Min(r.yMin, cr.yMin), Mathf.Max(r.xMax, cr.xMax), Mathf.Max(r.yMax, cr.yMax)) : cr;
                any = true;
            }
            return any ? r : Rect.zero;
        }

        private static void Shift(Transform t, Transform space, float dx, float dy)
        {
            Vector3 d = t.parent.InverseTransformVector(space.TransformVector(new Vector3(dx, dy, 0)));
            t.localPosition += d;
        }

        private static Image Background(Transform parent, string name, Sprite sprite, Image.Type type, float ppum, Color color)
        {
            Transform old = parent.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.pixelsPerUnitMultiplier = ppum;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void Place(RectTransform rt, Transform space, Rect where)
        {
            // where is in space's coordinates; rt's parent may differ
            // centre-anchored first: on a stretched rect, sizeDelta adds to the parent's size
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            Vector3 c = rt.parent.InverseTransformPoint(space.TransformPoint(where.center));
            Vector3 size = rt.parent.InverseTransformVector(space.TransformVector(new Vector3(where.width, where.height, 0)));
            rt.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
            rt.localPosition = new Vector3(c.x, c.y, rt.localPosition.z);
        }

        /// <summary>Frosthaven's enhancement slots are 32 px (the generator's 26), set a little further from the value.
        /// Scaled about the container's left edge, so the value keeps its place.</summary>
        private static void Slots(Transform rows, float f)
        {
            foreach (RectTransform c in rows.GetComponentsInChildren<Transform>(true).Where(t => t.name == "EnhancementContainer").OfType<RectTransform>().ToList())
            {
                if (c.childCount == 0) continue;
                Rect before = In(c, c.parent);
                float k = SlotSize / GeneratorSlot;
                c.localScale = new Vector3(k, k, 1f);
                Rect after = In(c, c.parent);
                float gap = c.parent.InverseTransformVector(rows.TransformVector(new Vector3(SlotGap * f, 0, 0))).magnitude;
                c.localPosition += new Vector3(before.xMin - after.xMin + gap, (before.center.y - after.center.y), 0);
            }
        }

        // ------------------------------------------------------------------ pills and self boxes

        /// <summary>An ability with a pill: the value with its slots, 7 px, then the pill, the whole line centred in its row
        /// (the port's 4-column row: an empty column, the value's, the pill's, an empty one).</summary>
        private static void Pills(Transform rows, Color tint, float f)
        {
            foreach (TMP_Text t in rows.GetComponentsInChildren<TMP_Text>(true).Where(x => (x.text ?? "").Contains("<link=fhpill>")).ToList())
            {
                if (!(t.transform.parent is RectTransform pillRow) || !(pillRow.parent is RectTransform pillCol) || !(pillCol.parent is RectTransform line)) continue;
                if (pillCol.GetSiblingIndex() < 1) continue;
                // the pill's text: Frosthaven's 18, one line
                t.fontSize = PillText;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
                t.ForceMeshUpdate();
                // the value's row: the column before the pill's (several pill rows of one ability share a column)
                RectTransform mainRow = line.GetChild(pillCol.GetSiblingIndex() - 1).Cast<Transform>().OfType<RectTransform>().FirstOrDefault(r => r.gameObject.activeSelf);
                if (mainRow == null) continue;
                var pillRows = pillCol.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf && Drawn(r, line).width > 0).ToList();
                if (pillRows.IndexOf(pillRow) > 0) continue;   // handled with the first
                Rect main = Drawn(mainRow, line);
                if (main.width <= 0) continue;
                // a slot in the pill (Shard Launch, Exploding Decoy): 7 px after its number, not a value's 10; 15 px
                // before the next icon; 9 px before the pill's end
                var slotted = pillRows.Select(r => r.Cast<Transform>().FirstOrDefault(c => c.gameObject.activeSelf && c.name == "EnhancementContainer" && c.childCount > 0)).ToList();
                foreach (Transform s in slotted.Where(s => s != null)) Shift(s, line, -PillSlotPull * f, 0);
                float Gaps() => Enumerable.Range(0, pillRows.Count - 1).Sum(i => (slotted[i] != null ? PillSlotAfter : 4f) * f)
                                + (slotted[pillRows.Count - 1] != null ? PillSlotEnd * f : 0f);
                var parts = pillRows.Select(r => Drawn(r, line)).ToList();
                float pillW = parts.Sum(p => p.width) + Gaps() + 2 * PillPad * f;
                float total = main.width + PillGap * f + pillW;
                // too long for the half: the pill's text shrinks, as Frosthaven's pill text does (auto 5-18)
                float avail = line.rect.width - 2 * EdgeMargin * f;
                if (total > avail)
                {
                    float textW = parts.Sum(p => p.width);
                    float k = Mathf.Max(0.55f, (textW - (total - avail)) / textW);
                    foreach (TMP_Text pt in pillRows.SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)))
                    {
                        pt.fontSize = PillText * k;
                        pt.ForceMeshUpdate();
                    }
                    parts = pillRows.Select(r => Drawn(r, line)).ToList();
                    pillW = parts.Sum(p => p.width) + Gaps() + 2 * PillPad * f;
                    total = main.width + PillGap * f + pillW;
                }
                float left = line.rect.center.x - total / 2f;
                float midY = main.center.y;
                Shift(mainRow, line, left - main.xMin, 0);
                float x = left + main.width + PillGap * f + PillPad * f;
                for (int i = 0; i < pillRows.Count; i++)
                {
                    Shift(pillRows[i], line, x - parts[i].xMin, midY - parts[i].center.y);
                    x += parts[i].width + (slotted[i] != null ? PillSlotAfter : 4f) * f;
                }
                Image bg = Background(pillRows[0], "FHPill", _pill, Image.Type.Tiled, PillPpum, tint);
                float pillLeft = left + main.width + PillGap * f;
                Place(bg.rectTransform, line, new Rect(pillLeft, midY - PillHeight * f / 2f, pillW, PillHeight * f));
            }
        }

        /// <summary>A consume in Frosthaven's dashed box (White Winds, Call of Doom): 61 px tall, 8 px, the crossed
        /// element (51), the tall colon (size 40, 16 wide), the reward on one line, 12 px. The generator balances the
        /// reward over two lines in a narrow column; it is laid out again here, centred where the box was.</summary>
        private static void ConsumeBoxes(Transform rows, RectTransform half, float f)
        {
            foreach (RectTransform box in rows.GetComponentsInChildren<Transform>(true).Where(t => t.name == "ConditionalElement").OfType<RectTransform>().ToList())
            {
                var button = box.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Consume Button") as RectTransform;
                if (button == null) continue;
                var element = button.Find("Element2") as RectTransform;
                var colon = button.Find("Colon")?.GetComponent<TMP_Text>();
                var desc = button.Find("Description") as RectTransform;
                var text = desc != null ? desc.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.text)) : null;
                if (element == null || colon == null || text == null) continue;
                foreach (var g in desc.GetComponentsInChildren<LayoutGroup>(true)) g.enabled = false;
                if (button.GetComponent<LayoutGroup>() is LayoutGroup bg) bg.enabled = false;
                text.text = Regex.Replace(text.text, @"\s*\n\s*", " ");
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                colon.fontSize = 40f;
                colon.enableAutoSizing = false;
                Vector2 pref = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity);
                // the box's own units per measured pixel
                float u = box.InverseTransformVector(rows.TransformVector(new Vector3(f, 0, 0))).magnitude;
                float tw = pref.x * (text.transform as RectTransform).lossyScale.x / box.lossyScale.x;
                // the box stays inside the half: a long reward is set smaller (to 80%), then on two lines
                // a consume of two elements: the second (ConsumeButton.Element1) sits after the first, not on it (Obsidian Shards)
                var second = button.Find("Element1") as RectTransform;
                if (second != null && !second.gameObject.activeSelf) second = null;
                float fixedW = (8f + 51f + 16f + 2f + 12f + (second != null ? 51f : 0f)) * u, th = 35f * u;
                float maxW = box.InverseTransformVector(half.TransformVector(new Vector3(half.rect.width, 0, 0))).magnitude - 2f * EdgeMargin * u;
                if (fixedW + tw > maxW)
                {
                    text.fontSize *= Mathf.Max(ConsumeMinText, (maxW - fixedW) / tw);
                    pref = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity);
                    tw = pref.x * text.transform.lossyScale.x / box.lossyScale.x;
                    if (fixedW + tw > maxW)
                    {
                        tw = maxW - fixedW;
                        text.enableWordWrapping = true;
                        Vector2 two = text.GetPreferredValues(text.text, tw * box.lossyScale.x / text.transform.lossyScale.x, float.PositiveInfinity);
                        th = Mathf.Max(th, two.y * text.transform.lossyScale.y / box.lossyScale.y);
                    }
                }
                float w = fixedW + tw, h = Mathf.Max(61f * u, th + 26f * u);
                // the generator sized the box for its own (stacked, taller) layout: it takes the new height, centred, so
                // Relayout closes the gap it left (Wretched Creature, Concealed Dominance: an empty middle)
                if (Mathf.Approximately(box.anchorMin.y, box.anchorMax.y) && Mathf.Approximately(box.pivot.y, 0.5f) && box.rect.height > h + 8f * u)
                    box.sizeDelta = new Vector2(box.sizeDelta.x, h + 8f * u);
                Rect old = new Rect(box.rect.center.x - w / 2f, box.rect.center.y - h / 2f, w, h);
                // a box in a column (beside an area) may be centred too near the side: move it in
                float lo = half.InverseTransformPoint(box.TransformPoint(new Vector3(old.xMin, old.center.y, 0))).x;
                float hi = half.InverseTransformPoint(box.TransformPoint(new Vector3(old.xMax, old.center.y, 0))).x;
                float dx = lo < half.rect.xMin + EdgeMargin * f ? half.rect.xMin + EdgeMargin * f - lo
                         : hi > half.rect.xMax - EdgeMargin * f ? half.rect.xMax - EdgeMargin * f - hi : 0f;
                old.x += dx * u / f;
                Image back = box.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null && i.sprite.name == "ConditionalElement");
                if (back != null) Place(back.rectTransform, box, old);
                float x = old.xMin + 8f * u;
                Place(button, box, old);
                Place(element, box, new Rect(x, old.center.y - 25.5f * u, 51f * u, 51f * u));
                x += 51f * u;
                if (second != null)
                {
                    Place(second, box, new Rect(x, old.center.y - 25.5f * u, 51f * u, 51f * u));
                    x += 51f * u;
                }
                Place((RectTransform)colon.transform, box, new Rect(x, old.center.y - 25.5f * u, 16f * u, 51f * u));
                x += 18f * u;
                Place(desc, box, new Rect(x, old.center.y - th / 2f, tw, th));
                foreach (RectTransform r in desc.GetComponentsInChildren<RectTransform>(true).Where(r => r != desc && r.name != "secondaryFX"))
                {
                    r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
                }
            }
        }

        /// <summary>A self-targeted value in Frosthaven's outlined box (Launch Pod's Heal 2).</summary>
        private static void SelfBoxes(Transform rows, float f)
        {
            foreach (TMP_Text t in rows.GetComponentsInChildren<TMP_Text>(true).Where(x => (x.text ?? "").Contains("<link=fhself>")).ToList())
            {
                if (!(t.transform.parent is RectTransform row)) continue;
                Rect d = Drawn(row, row);
                if (d.width <= 0) continue;
                Image bg = Background(row, "FHSelf", _self, Image.Type.Sliced, SelfPpum, Color.white);
                Place(bg.rectTransform, row, new Rect(d.xMin - SelfPadLeft * f, d.center.y - SelfHeight * f / 2f, d.width + (SelfPadLeft + SelfPadRight) * f, SelfHeight * f));
            }
        }

        // ------------------------------------------------------------------ infusions

        /// <summary>Frosthaven keeps a half's infusions in a band of their own at its bottom; the generator puts them under
        /// the last ability. The infusion row moves to the band (48 px, 11.5 px from the edge), the rest is centred above.</summary>
        private static void Infusions(RectTransform half, RectTransform rows, float f)
        {
            if (rows == null) return;
            RectTransform infuseRow = InfuseRow(rows, half, out List<RectTransform> containers);
            if (infuseRow == null) return;
            // the generator's element is 51 px, Frosthaven's 48 (absolute: a pooled card is generated again)
            foreach (RectTransform container in containers)
                container.localScale = new Vector3(InfuseSize / GeneratorElement, InfuseSize / GeneratorElement, 1f);
            Rect halfRect = half.rect;
            float band = Band(InfuseBottom * f + Union(containers.SelectMany(c => Icons(c, half))).height, f);
            // the rest, centred above the band: whole top-level rows move (the infusions' own row is placed after)
            var parts = Parts(rows, half, infuseRow);
            if (parts.Count > 0)
            {
                Rect b = Union(parts);
                float regionTop = halfRect.yMax - ContentTop * f, regionBottom = halfRect.yMin + band;
                float shift = (regionTop + regionBottom) / 2f - b.center.y;
                shift = Mathf.Min(shift, regionTop - b.yMax);
                foreach (RectTransform r in rows.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf && r != infuseRow))
                    Shift(r, half, 0, shift);
            }
            PlaceInfusions(half, infuseRow, containers, f);
        }

        /// <summary>The row of a half's own infusions (not a consume's reward): the highest container around them that holds
        /// nothing else, so an infusion row inside a column beside an area moves alone (Frigid Torrent: the attack came
        /// down with it).</summary>
        private static RectTransform InfuseRow(RectTransform rows, RectTransform half, out List<RectTransform> containers)
        {
            containers = rows.GetComponentsInChildren<Transform>(true).Where(c => c.name == "InfuseContainer" && Shown(c, rows)
                    && !c.GetComponentsInParent<Transform>(true).Any(p => p.name == "Consume Button" || p.name == "ConditionalElement"))
                .OfType<RectTransform>().ToList();
            if (containers.Count == 0) return null;
            var all = containers;
            // the lowest container around all of them (two infusions in columns of their own: Frigid Torrent's Ice, Air) ...
            RectTransform row = containers[0];
            while (!all.All(c => c.IsChildOf(row)) && row.parent is RectTransform up && row != rows) row = up;
            if (row == rows) return null;
            // ... then up while it holds nothing else
            while (row.parent is RectTransform p && p != rows)
            {
                RectTransform mine = row;
                if (p.Cast<Transform>().OfType<RectTransform>().Any(c => c != mine && c.gameObject.activeSelf && Extent(c, half).Any())) break;
                row = p;
            }
            return row;
        }

        /// <summary>The infusions centred at the half's bottom, 11.5 px up (Call of Doom).</summary>
        private static void PlaceInfusions(RectTransform half, RectTransform infuseRow, List<RectTransform> containers, float f)
        {
            Rect cr = Union(containers.SelectMany(c => Icons(c, half)));
            Shift(infuseRow, half, half.rect.center.x - cr.center.x, half.rect.yMin + InfuseBottom * f - cr.yMin);
        }

        /// <summary>The elements themselves: six of them wrap to a second row below the container's own rect (Chromatic Explosion).</summary>
        private static List<Rect> Icons(RectTransform container, RectTransform half)
        {
            // the container's rect is where Frosthaven's 11.5 px is measured from; an element's own rect sits lower than
            // its circle, so only a wrapped row (wholly below the container's middle) is added
            Rect box = In(container, half);
            var rects = new List<Rect> { box };
            rects.AddRange(container.Cast<Transform>().OfType<RectTransform>().Where(c => c.gameObject.activeSelf)
                .Select(c => In(c, half)).Where(r => r.yMax < box.center.y));
            return rects;
        }

        /// <summary>Everything the half draws except one subtree (its infusion row).</summary>
        private static List<Rect> Parts(RectTransform rows, RectTransform half, Transform except)
        {
            var parts = new List<Rect>();
            foreach (RectTransform r in rows.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf))
                foreach (Graphic g in r.GetComponentsInChildren<Graphic>(true))
                    if ((except == null || !g.transform.IsChildOf(except)) && Extent1(g, r, half) is Rect e) parts.Add(e);
            return parts;
        }

        private static Rect Union(IEnumerable<Rect> rects) =>
            rects.Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));

        /// <summary>The band the infusions keep at the bottom: Frosthaven's 72 px for one row, more for elements that wrap to a
        /// second (their top, given from the half's bottom, plus the same 12.5 px gap as one row has).</summary>
        private static float Band(float infusionTop, float f) => Mathf.Max(InfuseBand * f, infusionTop + (InfuseBand - InfuseBottom - InfuseSize) * f);

        /// <summary>The XP / persistent / lost badges: Frosthaven's bar ends flush with the half's right edge, its bottom
        /// 6 px above the half's (Sodden Soil: the bar at 434..491 x, 448..486 y in a half 13..491 x, 188..492 y). The bar
        /// sizes itself (ContentSizeFitter), so it is anchored bottom-right and grows leftwards from there.</summary>
        private static void Badges(RectTransform half, float f)
        {
            var bar = half.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FH_UI_HUD_CARD_A_ExperienceActiveBonuses") as RectTransform;
            if (bar == null || !(bar.parent is RectTransform parent)) return;
            // its badges are laid out side by side; laid out now, not on the next frame (the X drawn over the infinity)
            LayoutRebuilder.ForceRebuildLayoutImmediate(bar);
            Rect p = In(parent, half);   // the parent's rect in the half's space (the half's units are the parent's: no scale)
            bar.anchorMin = bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(1f, 0f);
            bar.anchoredPosition = new Vector2(half.rect.xMax - p.xMax, half.rect.yMin + BadgeBottom * f - p.yMin);
        }

        /// <summary>A half whose content is taller or wider than the half (long Gloomhaven texts, an area beside text) is
        /// scaled down around its centre, never below 60%, and moved inside: 10 px from the sides, under the top, above the
        /// infusion band, and above the badges when it reaches into them. Frosthaven's writers shortened such texts; ours
        /// are Gloomhaven's words, so the font gives instead.</summary>
        private static void Fit(RectTransform half, RectTransform rows, float f)
        {
            RectTransform infuseRow = InfuseRow(rows, half, out List<RectTransform> containers);
            var others = rows.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf && r != infuseRow).ToList();
            var bar = half.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FH_UI_HUD_CARD_A_ExperienceActiveBonuses") as RectTransform;
            Rect badge = bar != null && Shown(bar, half) && bar.rect.width > 1f ? In(bar, half) : Rect.zero;
            var parts = Parts(rows, half, infuseRow);
            if (parts.Count == 0) return;
            Rect b = Union(parts);
            Rect hr = half.rect;
            float bottom = infuseRow != null ? hr.yMin + Band(Union(containers.SelectMany(c => Icons(c, half))).yMax - hr.yMin, f)
                         : badge.width > 0 && parts.Any(p => p.Overlaps(badge)) ? badge.yMax + 2f * f : hr.yMin + FitEdge * f;
            Rect region = Rect.MinMaxRect(hr.xMin + FitEdge * f, bottom, hr.xMax - FitEdge * f, hr.yMax - ContentTop * f);
            if (Plugin.Log != null && FitTrace)
            {
                var card = half.GetComponentInParent<FullAbilityCard>();
                string name = card != null && Pending.TryGetValue(card, out AbilityCardYMLData d) ? d.Name : "?";
                Plugin.Log.LogInfo($"Gloomhaven card style: check {name}/{half.name} parts {parts.Count} content {b.xMin:0},{b.yMin:0}..{b.xMax:0},{b.yMax:0} region {region.xMin:0},{region.yMin:0}..{region.xMax:0},{region.yMax:0} half {hr.xMin:0},{hr.yMin:0}..{hr.xMax:0},{hr.yMax:0}");
            }
            if (region.xMin <= b.xMin && b.xMax <= region.xMax && region.yMin <= b.yMin && b.yMax <= region.yMax) return;
            float k = Mathf.Clamp(Mathf.Min(region.width / b.width, region.height / b.height), FitMin, 1f);
            // after scaling around b's centre, the content's size is b * k; centre it in the region where it overflows
            Vector2 c = b.center;
            Vector2 size = b.size * k;
            float cx = Mathf.Clamp(c.x, region.xMin + size.x / 2f, Mathf.Max(region.xMin + size.x / 2f, region.xMax - size.x / 2f));
            float cy = Mathf.Clamp(c.y, region.yMin + size.y / 2f, Mathf.Max(region.yMin + size.y / 2f, region.yMax - size.y / 2f));
            foreach (RectTransform r in others)
            {
                Vector2 rc = In(r, half).center;
                Vector2 to = new Vector2(cx, cy) + (rc - c) * k;
                r.localScale = new Vector3(r.localScale.x * k, r.localScale.y * k, 1f);
                Shift(r, half, to.x - In(r, half).center.x, to.y - In(r, half).center.y);
            }
            // an infusion row inside a scaled column keeps Frosthaven's size and place
            if (infuseRow != null && others.Any(r => infuseRow.IsChildOf(r)))
            {
                infuseRow.localScale = new Vector3(infuseRow.localScale.x / k, infuseRow.localScale.y / k, 1f);
                PlaceInfusions(half, infuseRow, containers, f);
            }
            if (Reported.Add("fit:" + half.GetInstanceID()))
                Plugin.Log.LogInfo($"Gloomhaven card style: fit {half.name} x{k:0.00} content {b.width:0}x{b.height:0} region {region.width:0}x{region.height:0}");
        }

        /// <summary>What a row draws, in the half's space, piece by piece: each text at its wrapped size (no mesh: the card
        /// is built hidden), each visible picture (pills, boxes, hexes, slots, icons).</summary>
        private static IEnumerable<Rect> Extent(RectTransform row, RectTransform half)
        {
            foreach (Graphic g in row.GetComponentsInChildren<Graphic>(true))
                if (Extent1(g, row, half) is Rect r) yield return r;
        }

        private static Rect? Extent1(Graphic g, RectTransform row, RectTransform half)
        {
            if (!g.enabled || !Shown(g.transform, row.parent) || g.color.a < 0.05f) return null;
            Rect box = In(g.rectTransform, half);
            if (box.width <= 0.01f || box.height <= 0.01f) return null;
            if (g is TMP_Text t)
            {
                // the text at its wrapped width and its row's height (Relayout sizes each row to its text), joined with
                // the mesh's bounds where the hidden card has a mesh (often it has none); the width is capped to the rect
                // (an uncapped preferred width read as twice the half's, 2026-10-30)
                if (string.IsNullOrWhiteSpace(t.text)) return null;
                float k = box.width / Mathf.Max(t.rectTransform.rect.width, 0.01f);
                float inner = Mathf.Max(1f, t.rectTransform.rect.width - t.margin.x - t.margin.z);
                Vector2 pref = t.GetPreferredValues(t.text, t.enableWordWrapping ? inner : float.PositiveInfinity, float.PositiveInfinity);
                if (pref.x <= 0.01f) return null;
                float w = Mathf.Min(pref.x, inner) * k, hgt = Mathf.Max(pref.y * k, box.height);
                float mid = box.center.x + (t.margin.x - t.margin.z) * k / 2f;
                float x0 = ((int)t.alignment & 0x1) != 0   /* a left alignment */ ? box.xMin + t.margin.x * k : mid - w / 2f;
                Rect est = new Rect(x0, box.center.y - hgt / 2f, w, hgt);
                t.ForceMeshUpdate(true);
                Bounds tb = t.textBounds;
                if (tb.size.x <= 0.01f || tb.size.y <= 0.01f) return est;
                Vector3 a = half.InverseTransformPoint(t.transform.TransformPoint(tb.min)), c = half.InverseTransformPoint(t.transform.TransformPoint(tb.max));
                return Union(new[] { est, Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y)) });
            }
            return g is Image img && img.sprite != null ? box : (Rect?)null;
        }

        /// <summary>A use tracker under an active bonus's text: the bonus's box keeps the height the generator gave it, and a
        /// longer text in Frosthaven's font ran over the tracker (Shrapnel, Heart Seeker). The tracker's row and the rows
        /// after it move down under the text; Fit brings the half back inside if that overflows.</summary>
        private static void Trackers(RectTransform half, RectTransform rows, float f)
        {
            var top = rows.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf).ToList();
            for (int i = 1; i < top.Count; i++)
            {
                if (!top[i].GetComponentsInChildren<Transform>(true).Any(c => c.name == "XPContainer" && Shown(c, rows))) continue;
                var mine = Extent(top[i], half).ToList();
                if (mine.Count == 0) continue;
                float yTop = mine.Max(p => p.yMax), x0 = mine.Min(p => p.xMin), x1 = mine.Max(p => p.xMax);
                var above = top.Take(i).SelectMany(r => Extent(r, half)).Where(p => p.xMax > x0 && p.xMin < x1).ToList();
                if (above.Count == 0) continue;
                float dy = yTop + 3f * f - above.Min(p => p.yMin);
                if (dy <= 0f) continue;
                for (int j = i; j < top.Count; j++) Shift(top[j], half, 0f, -dy);
            }
        }

        /// <summary>An area beside an ability: the pill drawn above is wider than the generator's column, and ran under the
        /// hexes (Ink Bomb, Cold Front, Crystallizing Blast). The area moves right as far as the half allows, then the
        /// columns beside it move left, then the area is drawn smaller (not below 70%).</summary>
        private static void AreaClear(RectTransform half, RectTransform rows, float f)
        {
            foreach (RectTransform area in rows.GetComponentsInChildren<Transform>(true).Where(c => c.name == "AreaEffectContainer" && Shown(c, rows)).OfType<RectTransform>().ToList())
            {
                // the level where something sits beside the area: the area may be wrapped in a column or two
                RectTransform col = area, line = null;
                while (col.parent is RectTransform p)
                {
                    if (p.Cast<Transform>().OfType<RectTransform>().Any(c => c != col && c.gameObject.activeSelf && Extent(c, half).Any())) { line = p; break; }
                    if (p == rows) break;
                    col = p;
                }
                if (line == null) continue;
                var hexParts = Extent(area, half).ToList();
                if (hexParts.Count == 0) continue;
                Rect hex = hexParts.Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                var beside = line.Cast<Transform>().OfType<RectTransform>().Where(c => c != col && c.gameObject.activeSelf).ToList();
                var all = beside.SelectMany(c => Extent(c, half)).ToList();
                if (all.Count == 0) continue;
                Rect block = Union(all);
                float lo = half.rect.xMin + FitEdge * f, hi = half.rect.xMax - AreaRim * f;
                // Frosthaven puts the area under a line whose pill carries a condition or element after a comma (Blizzard
                // "range 3, muddle", Dark Fog, Sweeping Aid), beside one with a range only (Tornado, Inevitable Conclusion)
                bool rider = beside.SelectMany(c => c.GetComponentsInChildren<TMP_Text>(true))
                    .Any(t => (t.text ?? "").Contains("<link=fhpill>") && Regex.IsMatch(t.text, @",\s*(<nobr>)?<sprite"));
                if (!rider && block.width + AreaGap * f + hex.width <= hi - lo)
                {
                    // beside: the area at least AreaGap after the line and AreaRim from the rim; the line moves left
                    // when the two don't fit where they are
                    float x = Mathf.Min(Mathf.Max(hex.xMin, block.xMax + AreaGap * f), hi - hex.width);
                    float push = block.xMax + AreaGap * f - x;   // > 0: the line moves left (it fits: checked above)
                    if (push > 0f) foreach (RectTransform c in beside) Shift(c, half, -push, 0f);
                    Shift(area, half, x - hex.xMin, 0f);
                }
                else
                {
                    // Frosthaven draws the area centred under a line too wide to leave room beside it (Blizzard, Dark Fog,
                    // Sweeping Aid): the line is centred on the half and the area goes under it
                    foreach (RectTransform c in beside) Shift(c, half, half.rect.center.x - block.center.x, 0f);
                    Shift(area, half, half.rect.center.x - hex.center.x, block.yMin - AreaBelow * f - hex.yMax);
                }
            }
        }

        // ------------------------------------------------------------------ area hexes

        /// <summary>The area's hexes on Frosthaven's grid with Frosthaven's look. The generator's grid is squashed (rows
        /// 0.75 of a column apart); Frosthaven's is regular (0.866), its hexes 53 wide, 54.5 apart.</summary>
        private static void Areas(Transform rows, float f)
        {
            foreach (Transform area in rows.GetComponentsInChildren<Transform>(true).Where(c => c.name == "AreaEffectContainer").ToList())
            {
                var hexes = area.Cast<Transform>().OfType<RectTransform>().Where(c => c.GetComponent<Image>() is Image i && i.sprite != null && HexKind(i.sprite.name) != null).ToList();
                if (hexes.Count == 0) continue;
                var dots = area.Cast<Transform>().OfType<RectTransform>().Where(c => c.GetComponent<EnhancedAreaHex>() != null).ToList();
                float cell = hexes[0].rect.width;   // the generator's column spacing, in the area's units
                Vector2 centre = new Vector2(hexes.Average(h => h.anchoredPosition.x), hexes.Average(h => h.anchoredPosition.y));
                // the area's units per measured pixel: the half's f, carried into the area's space
                float unit = area.InverseTransformVector(rows.TransformVector(new Vector3(f, 0, 0))).magnitude;
                float kx = HexColumn * unit / cell, ky = kx * (0.8660254f / 0.75f);
                Vector2 Map(Vector2 p) => centre + new Vector2((p.x - centre.x) * kx, (p.y - centre.y) * ky);
                foreach (RectTransform h in hexes)
                {
                    var img = h.GetComponent<Image>();
                    img.sprite = Hex(HexKind(img.sprite.name));
                    img.preserveAspect = false;
                    h.localScale = Vector3.one;
                    h.anchoredPosition = Map(h.anchoredPosition);
                    h.sizeDelta = new Vector2(HexWidth * unit * HexPadW, HexWidth * unit * 2f / 1.7320508f * HexPadH);
                }
                foreach (RectTransform d in dots)
                {
                    d.anchoredPosition = Map(d.anchoredPosition);
                    d.sizeDelta = new Vector2(59f, 69f) * unit;   // Frosthaven's dot cell (Reaper Function)
                    if (d.GetComponent<Image>() is Image di) { di.color = new Color(1f, 1f, 1f, 0.8f); di.preserveAspect = true; }
                }
            }
        }

        private static string HexKind(string sprite)
        {
            string n = sprite.Replace("(Clone)", "");
            return n == "Red" || n == "BSRedHex" || n == "GHHexRed" ? "Red" : n == "Grey" || n == "BSYouHex" || n == "GHHexGrey" ? "Grey" : n == "Blue" || n == "BSBlueHex" || n == "GHHexBlue" ? "Blue" : null;
        }

        // the hex texture's margin round the hex (for the soft edge), as factors of the hex's size
        private const float HexPadW = 1.08f, HexPadH = 1.07f;

        /// <summary>A hex drawn once per kind, 4x the measured size, anti-aliased by its distance to the edges.</summary>
        private static Sprite Hex(string kind)
        {
            if (HexSprites.TryGetValue(kind, out Sprite s) && s != null) return s;
            const float k = 4f;                       // texture pixels per measured pixel
            float w = HexWidth * k, rim = HexRim * k;
            float h = w * 2f / 1.7320508f;
            int tw = Mathf.CeilToInt(w * HexPadW), th = Mathf.CeilToInt(h * HexPadH);
            Color fill = kind == "Red" ? new Color32(199, 32, 38, 255) : kind == "Blue" ? new Color32(32, 148, 198, 255) : new Color32(59, 59, 59, 255);
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true) { name = "GHHex" + kind, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[tw * th];
            float cx = tw / 2f, cy = th / 2f, half = w / 2f;
            float xh = HexXHalf * k, xs = HexXStroke * k / 2f;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - cx), qy = Mathf.Abs(y + 0.5f - cy);
                    float d = Mathf.Max(qx, 0.5f * qx + 0.8660254f * qy) - half;   // pointy-topped hex, inradius half
                    float outside = Mathf.Clamp01(d + 0.5f * k) / k;               // 0 inside, ~1 one pixel out
                    float cover = Mathf.Clamp01(0.5f - d / k);                     // edge anti-aliased over a measured pixel
                    if (cover <= 0f) { px[y * tw + x] = new Color32(0, 0, 0, 0); continue; }
                    float inRim = Mathf.Clamp01((d + rim) / k + 0.5f);            // 1 in the rim, 0 in the fill
                    Color c = Color.Lerp(fill, Color.white, inRim);
                    if (kind == "Red")
                    {
                        float ux = x + 0.5f - cx, uy = y + 0.5f - cy;
                        float best = float.MaxValue;
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            // distance to the segment from (-xh, -sgn xh) to (xh, sgn xh)
                            float t = Mathf.Clamp((ux * xh + uy * sgn * xh) / (2 * xh * xh), -1f, 1f);
                            float ddx = ux - t * xh, ddy = uy - t * sgn * xh;
                            best = Mathf.Min(best, Mathf.Sqrt(ddx * ddx + ddy * ddy));
                        }
                        float xa = Mathf.Clamp01((xs - best) / k + 0.5f) * 0.88f;
                        c = Color.Lerp(c, Color.white, xa * (1f - inRim));
                    }
                    c.a = cover;
                    px[y * tw + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            UnityEngine.Object.DontDestroyOnLoad(tex);
            s = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f * k);
            s.name = "GHHex" + kind;
            UnityEngine.Object.DontDestroyOnLoad(s);
            HexSprites[kind] = s;
            return s;
        }
    }
}
