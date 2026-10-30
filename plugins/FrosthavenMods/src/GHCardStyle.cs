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
    /// still carry Gloomhaven's look. After the game generates a card of a class in <see cref="Classes"/>, each half is
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
        internal static readonly HashSet<string> Classes = new HashSet<string> { "Brute" };
        private const string Atlas = "AbilityCardSpriteAtlas";
        private const float TintAlpha = 0x78 / 255f;
        private const float HalfWidthPx = 478f;   // a card half, in the pixels the numbers below are measured in

        // Frosthaven's pill (Sodden Soil) and self box (Launch Pod)
        private const float EdgeMargin = 14f, PillHeight = 39f, PillPad = 9.5f, PillGap = 7f, PillText = 18f, PillPpum = 2f;
        private const float SelfHeight = 41f, SelfPadLeft = 13f, SelfPadRight = 12f, SelfPpum = 2.4f;
        // infusions (Call of Doom, Primal Bellow)
        private const float BadgeBottom = 6f;
        private const float GeneratorElement = 51f, GeneratorSlot = 26f, SlotSize = 32f, SlotGap = 5f;
        private const float InfuseSize = 48f, InfuseBottom = 11.5f, InfuseBand = 72f, ContentTop = 9f;
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
                    Slots(rows, f);
                    ConsumeSprites(h);
                    Pictures(h, tint);
                    Areas(rows, f);
                    Pills(rows, tint, f);
                    ConsumeBoxes(rows, f);
                    SelfBoxes(rows, f);
                    Infusions(h, rows as RectTransform, f);
                    Badges(h, f);
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
            return cls != null && GHClasses.IsGloomhavenClass(cls.AbilityCardArt, out model) && Classes.Contains(model.ToString());
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
                var parts = pillRows.Select(r => Drawn(r, line)).ToList();
                float pillW = parts.Sum(p => p.width) + (parts.Count - 1) * 4f * f + 2 * PillPad * f;
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
                    pillW = parts.Sum(p => p.width) + (parts.Count - 1) * 4f * f + 2 * PillPad * f;
                    total = main.width + PillGap * f + pillW;
                }
                float left = line.rect.center.x - total / 2f;
                float midY = main.center.y;
                Shift(mainRow, line, left - main.xMin, 0);
                float x = left + main.width + PillGap * f + PillPad * f;
                for (int i = 0; i < pillRows.Count; i++)
                {
                    Shift(pillRows[i], line, x - parts[i].xMin, midY - parts[i].center.y);
                    x += parts[i].width + 4f * f;
                }
                Image bg = Background(pillRows[0], "FHPill", _pill, Image.Type.Tiled, PillPpum, tint);
                float pillLeft = left + main.width + PillGap * f;
                Place(bg.rectTransform, line, new Rect(pillLeft, midY - PillHeight * f / 2f, pillW, PillHeight * f));
            }
        }

        /// <summary>A consume in Frosthaven's dashed box (White Winds, Call of Doom): 61 px tall, 8 px, the crossed
        /// element (51), the tall colon (size 40, 16 wide), the reward on one line, 12 px. The generator balances the
        /// reward over two lines in a narrow column; it is laid out again here, centred where the box was.</summary>
        private static void ConsumeBoxes(Transform rows, float f)
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
                float w = (8f + 51f + 16f + 2f + 12f) * u + tw, h = 61f * u;
                Rect old = new Rect(box.rect.center.x - w / 2f, box.rect.center.y - h / 2f, w, h);
                Image back = box.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null && i.sprite.name == "ConditionalElement");
                if (back != null) Place(back.rectTransform, box, old);
                float x = old.xMin + 8f * u;
                Place(button, box, old);
                Place(element, box, new Rect(x, old.center.y - 25.5f * u, 51f * u, 51f * u));
                x += 51f * u;
                Place((RectTransform)colon.transform, box, new Rect(x, old.center.y - 25.5f * u, 16f * u, 51f * u));
                x += 18f * u;
                Place(desc, box, new Rect(x, old.center.y - 17.5f * u, tw, 35f * u));
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
            var top = rows.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf).ToList();
            RectTransform infuseRow = top.FirstOrDefault(r => r.GetComponentsInChildren<Transform>(true).Any(c => c.name == "InfuseContainer"));
            if (infuseRow == null) return;
            var container = (RectTransform)infuseRow.GetComponentsInChildren<Transform>(true).First(c => c.name == "InfuseContainer");
            var element = container.Cast<Transform>().OfType<RectTransform>().FirstOrDefault();
            // the generator's element is 51 px, Frosthaven's 48 (absolute: a pooled card is generated again)
            container.localScale = new Vector3(InfuseSize / GeneratorElement, InfuseSize / GeneratorElement, 1f);
            Rect halfRect = half.rect;
            Rect cr = In(container, half);
            Shift(infuseRow, half, halfRect.center.x - cr.center.x, halfRect.yMin + InfuseBottom * f - cr.yMin);
            var others = top.Where(r => r != infuseRow && Drawn(r, half).height > 0 || (r != infuseRow && r.childCount > 0 && In(r, half).height > 12 * f)).ToList();
            if (others.Count == 0) return;
            float hi = others.Max(r => In(r, half).yMax), lo = others.Min(r => In(r, half).yMin);
            float regionTop = halfRect.yMax - ContentTop * f, regionBottom = halfRect.yMin + InfuseBand * f;
            float shift = (regionTop + regionBottom) / 2f - (hi + lo) / 2f;
            shift = Mathf.Min(shift, regionTop - hi);
            foreach (RectTransform r in others) Shift(r, half, 0, shift);
        }

        /// <summary>The XP / persistent / lost badges: Frosthaven's bar ends flush with the half's right edge, its bottom
        /// 6 px above the half's (Sodden Soil: the bar at 434..491 x, 448..486 y in a half 13..491 x, 188..492 y). The bar
        /// sizes itself (ContentSizeFitter), so it is anchored bottom-right and grows leftwards from there.</summary>
        private static void Badges(RectTransform half, float f)
        {
            var bar = half.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FH_UI_HUD_CARD_A_ExperienceActiveBonuses") as RectTransform;
            if (bar == null || !(bar.parent is RectTransform parent)) return;
            Rect p = In(parent, half);   // the parent's rect in the half's space (the half's units are the parent's: no scale)
            bar.anchorMin = bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(1f, 0f);
            bar.anchoredPosition = new Vector2(half.rect.xMax - p.xMax, half.rect.yMin + BadgeBottom * f - p.yMin);
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
