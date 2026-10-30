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
    /// still carry Gloomhaven's look. After the game generates a card of a class in <see cref="Classes"/>:
    ///   - its text uses Frosthaven's card font (FH_Font_AbilityCards; the generator's is Sarala);
    ///   - its pictures are Frosthaven's: a consumed element is the element crossed in red (ConsumeX from the card icon
    ///     atlas, not Gloomhaven's arrow), an infused one the plain element with its white rim, the area hexes
    ///     Frosthaven's white-outlined ones with the white X (BSRedHex, BSYouHex, BSBlueHex);
    ///   - the rows the port marked get their backgrounds: &lt;link=fhpill&gt; the class-tinted rounded pill behind an
    ///     ability's modifiers and their enhancement slot, &lt;link=fhself&gt; Frosthaven's outlined box behind a
    ///     self-targeted value (FH_UI_CARDS_PositiveEffectBackground);
    ///   - pills and the dashed consume box take Frosthaven's translucent class tint;
    ///   - a half's infusions sit at its bottom, centred, with the abilities centred above them.
    /// The layout side (sizes, the pill rows, the dashed box, Frosthaven's words) is the port's (gh_layout.fh_style).
    /// </summary>
    [HarmonyPatch]
    internal static class GHCardStyle
    {
        internal static readonly HashSet<string> Classes = new HashSet<string> { "Brute" };
        private const string Atlas = "AbilityCardSpriteAtlas";
        private const float TintAlpha = 0x78 / 255f;
        private const float HexScale = 1.2f;
        private static readonly Regex Element = new Regex(@"^(Fire|Ice|Air|Earth|Light|Dark)$");
        private static readonly Regex Consume = new Regex(@"^Consume(Fire|Ice|Air|Earth|Light|Dark|Any|Wild)$");
        private static readonly Regex Create = new Regex(@"^Create(Fire|Ice|Air|Earth|Light|Dark|Any|Wild)(_Highlight)?$");
        private static readonly Dictionary<string, string> Hexes = new Dictionary<string, string> { ["Red"] = "BSRedHex", ["Grey"] = "BSYouHex", ["Blue"] = "BSBlueHex" };

        private static Dictionary<string, Sprite> _atlas;
        private static Sprite _pill, _self;
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
                if (!Pending.TryGetValue(__instance, out AbilityCardYMLData data) || data?.CharacterID == null) return;
                CCharacterClass cls = GHClasses.ClassById(data.CharacterID);
                if (cls == null || !GHClasses.IsGloomhavenClass(cls.AbilityCardArt, out ECharacter model) || !Classes.Contains(model.ToString())) return;
                Load();
                // the class's colour from its config (the card's own pieces are painted only later, SetVisualsFromConfig)
                Color tint = GHClasses.ClassColours.TryGetValue(model, out Color colour) ? colour : ClassTint(__instance.transform);
                tint.a = TintAlpha;
                foreach (FullAbilityCardAction half in new[] { __instance.topActionButton, __instance.bottomActionButton })
                {
                    if (half == null) continue;
                    Transform h = half.transform;
                    Fonts(h);
                    ConsumeSprites(h);
                    Pictures(h, tint);
                    Backgrounds(h, tint);
                    InfusionsAtBottom(h);
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
                    if (!Styled(img.GetComponentInParent<FullAbilityCard>())) continue;
                    Color c = img.color;
                    c.a = TintAlpha;
                    img.color = c;
                }
            }
            catch (Exception e) { if (Reported.Add("tint")) Plugin.Log.LogWarning("Gloomhaven card style: tint: " + e); }
        }

        private static bool Styled(FullAbilityCard card)
        {
            if (card == null || !Pending.TryGetValue(card, out AbilityCardYMLData data) || data?.CharacterID == null) return false;
            CCharacterClass cls = GHClasses.ClassById(data.CharacterID);
            return cls != null && GHClasses.IsGloomhavenClass(cls.AbilityCardArt, out ECharacter model) && Classes.Contains(model.ToString());
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

        /// <summary>The class colour the card's pieces are tinted with (the bonus bar's background carries it).</summary>
        private static Color ClassTint(Transform full)
        {
            Image bar = full.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null && (i.sprite.name == "horizontal_gradient" || i.sprite.name == "Divider"));
            Color c = bar != null ? bar.color : new Color(0.45f, 0.66f, 1f);
            c.a = TintAlpha;
            return c;
        }

        private static void Fonts(Transform half)
        {
            if (_font == null) return;
            foreach (TMP_Text t in half.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null && t.font.name.StartsWith("FH_Sarala", StringComparison.Ordinal) && t.font != _font)
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
                else if (Hexes.TryGetValue(name, out string hex)) want = hex;
                if (want != null && _atlas.TryGetValue(want, out Sprite s) && img.sprite != s)
                {
                    img.sprite = s;
                    img.preserveAspect = true;
                    // Frosthaven's hexes touch, their white outlines shared; its hex art fills less of the square
                    if (Hexes.ContainsKey(name)) img.rectTransform.localScale = Vector3.one * HexScale;
                }
                if (name == "AddedEffectRoundedBorder" || name == "ConditionalElement") img.color = tint;
            }
        }

        private static void Backgrounds(Transform half, Color tint)
        {
            foreach (TMP_Text t in half.GetComponentsInChildren<TMP_Text>(true))
            {
                string raw = t.text ?? "";
                if (raw.Contains("<link=fhpill>")) Behind(t, _pill, tint, 8f, 4f, "FHPill");
                if (raw.Contains("<link=fhself>")) Behind(t, _self, Color.white, 10f, 4f, "FHSelf");
            }
        }

        /// <summary>A sliced background behind a text and its enhancement slots (the row's EnhancementContainer), as
        /// Frosthaven draws a pill: measured from the drawn text and the slots, so nothing moves.</summary>
        private static void Behind(TMP_Text text, Sprite sprite, Color color, float padX, float padY, string name)
        {
            if (sprite == null || !(text.transform.parent is RectTransform row) || row.Find(name) != null) return;
            // the card is generated hidden, so the text has no mesh yet: its preferred size, centred in its rect as the
            // generator aligns these rows
            text.enableWordWrapping = false;   // a pill is one line, as Frosthaven's are
            text.overflowMode = TextOverflowModes.Overflow;
            Vector2 pref = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity);
            if (pref.x <= 0) return;
            var tr = (RectTransform)text.transform;
            Vector3 c = row.InverseTransformPoint(tr.TransformPoint(tr.rect.center));
            var corners = new List<Vector3> { c - new Vector3(pref.x / 2f, pref.y / 2f, 0), c + new Vector3(pref.x / 2f, pref.y / 2f, 0) };
            foreach (Transform sib in row)
                if (sib.name == "EnhancementContainer" && sib is RectTransform enh && enh.gameObject.activeSelf)
                {
                    var w = new Vector3[4];
                    enh.GetWorldCorners(w);
                    corners.Add(row.InverseTransformPoint(w[0]));
                    corners.Add(row.InverseTransformPoint(w[2]));
                }
            float xMin = corners.Min(c => c.x) - padX, xMax = corners.Max(c => c.x) + padX;
            float yMin = corners.Min(c => c.y) - padY, yMax = corners.Max(c => c.y) + padY;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.SetParent(row, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(xMax - xMin, Mathf.Max(yMax - yMin, 30f));
            rt.anchoredPosition = new Vector2((xMin + xMax) / 2f, (yMin + yMax) / 2f) - row.rect.center;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
        }

        /// <summary>Frosthaven keeps a half's infusions in a row of their own at its bottom; the generator puts them
        /// under the last ability. The top-level row holding them moves to the bottom, the rest is centred above.</summary>
        private static void InfusionsAtBottom(Transform half)
        {
            Transform parent = half.Find("Content/Layout Parent");
            if (!(parent is RectTransform lp)) return;
            var rows = lp.Cast<Transform>().OfType<RectTransform>().Where(r => r.gameObject.activeSelf).ToList();
            RectTransform infuse = rows.FirstOrDefault(r => r.GetComponentsInChildren<Transform>(true).Any(c => c.name == "InfuseContainer"));
            if (infuse == null) return;
            var others = rows.Where(r => r != infuse && !(r.rect.height < 10 && r.GetComponentInChildren<TMP_Text>() is TMP_Text sp && Regex.IsMatch(sp.text ?? "", @"^\s*<size=\d+%>\s*$"))).ToList();
            float Center(RectTransform r) => r.localPosition.y + (0.5f - r.pivot.y) * r.rect.height;
            Rect area = lp.rect;
            const float infuseBand = 60f;
            float h = infuse.rect.height;
            infuse.localPosition += new Vector3(0, area.yMin + infuseBand / 2f - Center(infuse), 0);
            if (others.Count == 0) return;
            float top = others.Max(r => Center(r) + r.rect.height / 2f), bottom = others.Min(r => Center(r) - r.rect.height / 2f);
            float shift = (area.yMax + area.yMin + infuseBand) / 2f - (top + bottom) / 2f;
            shift = Mathf.Min(shift, area.yMax - top);
            foreach (RectTransform r in others) r.localPosition += new Vector3(0, shift, 0);
        }
    }
}
