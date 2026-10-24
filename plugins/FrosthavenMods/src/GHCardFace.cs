using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// The face of a Gloomhaven class's ability card, drawn the way Frosthaven draws its own. Frosthaven's cards are
    /// hand-built prefabs, one per card (hero_&lt;class&gt;cards), all with the same design: a 316 x 550 card, a picture
    /// banner with the level in a tag at its corner, the title on a class-tinted band, the top half in a gold frame
    /// with three corners cut and the basic Attack 2 pill attached under it, the initiative on a gold hex between
    /// chevrons, and the bottom half the same turned upside down with the Move 2 pill attached on top. A generated card
    /// (the AbilityCard-FH template GHClasses picks) has its own, older design: 294 x 450, one painting for the whole
    /// card, no banner. So for a Gloomhaven class:
    ///   - the card is laid out at the real cards' geometry (measured on screen from Frosthaven's own cards, in their
    ///     own units), before the game generates the card's text so the text is laid out in the new halves;
    ///   - the halves and the two basic-action buttons are painted by the card skin (the game swaps those sprites on
    ///     hover, select and disable), so the class gets its own skin, a copy of the stand-in's, whose sprites are the
    ///     real cards' pieces: the half background recoloured to the class's colour with the gold frame on it, the pill
    ///     fill with its frame, glows baked into the hover states, the bottom ones turned 180 degrees as on the real
    ///     cards. The pieces come from the stand-in's card bundle, which the game may unload, so their pixels are
    ///     copied into textures of our own once;
    ///   - the static pieces (card background, banner with the class's picture from the pack, title band and line,
    ///     level tag, initiative hex and chevrons) are plain images under the card.
    /// The class tint follows the real cards: the Drifter's half background is a neutral one (ActionBGR_Innactive) with
    /// a warm glow; the glow is measured against the neutral one and repainted in the class's colour.
    /// </summary>
    [HarmonyPatch]
    internal static class GHCardFace
    {
        // ------------------------------------------------------------------ geometry (real card units, from the top left)

        private const float CardW = 316f, CardH = 550f;
        private static readonly Rect Banner = new Rect(8, 8, 300, 79);
        private static readonly Rect TitleBand = new Rect(8, 87, 300, 30);
        private static readonly Rect TitleLine = new Rect(6, 84, 304, 18);
        private static readonly Rect LevelTag = new Rect(268, 69, 39, 17);
        private static readonly Rect TopHalf = new Rect(8, 117, 300, 191);
        private static readonly Rect BottomHalf = new Rect(8, 352, 300, 190);
        private static readonly Rect AttackPill = new Rect(8, 307, 118, 34);
        private static readonly Rect MovePill = new Rect(190, 319, 118, 34);
        private static readonly Vector2 Initiative = new Vector2(158, 330);   // centre
        private static readonly Vector2 HalfContent = new Vector2(292, 176);

        // the template's effect overlays, scaled with the card (template card 294 x 450)
        private static readonly (string name, Vector2 pos, Vector2 size)[] Overlays =
        {
            ("FX_Underneath_Full", new Vector2(0f, 70f), new Vector2(350f, 500f)),
            ("FX_Overlay_Full", new Vector2(0f, 23.5f), new Vector2(360f, 500f)),
            ("FX_Overlay_Bloom", new Vector2(0f, 230f), new Vector2(500f, 700f)),
            ("UIFX_Overlay", Vector2.zero, new Vector2(330f, 480f)),
        };

        // ------------------------------------------------------------------ hooks

        /// <summary>Before the game generates the card's text: the card's size and its two halves, so the rows are laid
        /// out in the halves they end up in; and the class's skin sprites, which the game applies during the call.</summary>
        [HarmonyPatch(typeof(AbilityCardUI), nameof(AbilityCardUI.MakeAbilityCard))]
        [HarmonyPrefix]
        private static void ShapeBeforeText(AbilityCardUI __instance, AbilityCardYMLData cardData)
        {
            if (cardData?.CharacterID == null) return;
            CCharacterClass cls = GHClasses.ClassById(cardData.CharacterID);
            if (cls == null || !GHClasses.IsGloomhavenClass(cls.AbilityCardArt, out ECharacter model)) return;
            Art.Ensure(model);
            Shape(__instance);
        }

        /// <summary>The character screen skins its cards by the class's name before the card knows its class, and the
        /// skin resets the initiative's colour: make sure the skin is ready, and dress the card again after.</summary>
        [HarmonyPatch(typeof(AbilityCardUI), "SetSkin", typeof(string), typeof(string))]
        [HarmonyPrefix]
        private static void SkinReady(string playerName)
        {
            if (playerName != null && GHClasses.IsGloomhavenClass(playerName, out ECharacter model)) Art.Ensure(model);
        }

        [HarmonyPatch(typeof(AbilityCardUI), "SetSkin", typeof(string), typeof(string))]
        [HarmonyPostfix]
        private static void DressAfterSkin(AbilityCardUI __instance, string playerName)
        {
            if (playerName != null && GHClasses.IsGloomhavenClass(playerName, out ECharacter model)) Dress(__instance, model);
        }

        // ------------------------------------------------------------------ layout

        private static void Shape(AbilityCardUI card)
        {
            if (!(card.transform.Find("Full") is RectTransform full)) return;
            full.sizeDelta = new Vector2(CardW, CardH);
            TopLeft(full.Find("Header"), new Rect(0, 0, CardW, CardH));
            HalfAt(full.Find("Top button"), TopHalf);
            HalfAt(full.Find("Bottom button"), BottomHalf);
            float sx = CardW / 294f, sy = CardH / 450f;
            foreach (var (name, pos, size) in Overlays)
                if (full.Find(name) is RectTransform fx)
                {
                    fx.anchoredPosition = new Vector2(pos.x * sx, pos.y * sy);
                    fx.sizeDelta = new Vector2(size.x * sx, size.y * sy);
                }
        }

        private static void HalfAt(Transform half, Rect at)
        {
            if (half == null) return;
            TopLeft(half, at);
            if (half.Find("Content") is RectTransform content)
            {
                content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = HalfContent;
            }
            Image image = half.GetComponent<Image>();
            if (image != null) image.color = Color.white;   // the template tints its own half art brown
        }

        /// <summary>Everything but the halves' text, after the game has made (or re-skinned) the card. Safe to repeat:
        /// the card pool reuses cards, and every piece is found again by name.</summary>
        internal static void Dress(AbilityCardUI card, ECharacter model)
        {
            if (!(card.transform.Find("Full") is RectTransform full)) return;
            Shape(card);
            Transform header = full.Find("Header");
            // the template's one-picture card: drawn clear (the game switches the image back on, but leaves its colour;
            // its title is a child and stays), and its shape no longer used for the unfocused-card dimming
            Image painting = header != null ? header.GetComponent<Image>() : null;
            if (painting != null) painting.color = Color.clear;
            if (full.Find("UnfocusedMask")?.GetComponent<Image>() is Image dimming && Art.Card != null) dimming.sprite = Art.Card;

            // under everything: card, banner, title band and line, level tag (in that order)
            Transform below = full.Find("FX_Underneath_Full");
            int at = below != null ? below.GetSiblingIndex() + 1 : 0;
            Picture(full, "GH card", Art.Card, new Rect(0, 0, CardW, CardH), at++);
            Masked(full, "GH banner", Art.Banner(model), Banner, at++);
            Picture(full, "GH title band", Art.TitleBandOf(model), TitleBand, at++);
            Picture(full, "GH title line", Art.TitleLine, TitleLine, at++);
            Picture(full, "GH level tag", Art.LevelTag, LevelTag, at++);

            if (header != null && header.Find("Title text") is RectTransform title)
            {
                TopLeft(title, new Rect(TitleBand.x + 4, TitleBand.y, TitleBand.width - 8, TitleBand.height));
                if (title.GetComponent<TMP_Text>() is TMP_Text text)
                {
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 11f;
                    text.fontSizeMax = 19f;
                    text.alignment = TextAlignmentOptions.Center;
                    text.color = new Color(0.96f, 0.93f, 0.86f);
                }
            }
            Level(full);
            Band(full, model);
        }

        /// <summary>"LVL 3" in the tag at the banner's corner: the template's own level number (the game writes it),
        /// and a small "LVL" before it.</summary>
        private static void Level(RectTransform full)
        {
            if (!(full.Find("LevelText") is RectTransform level) || !(level.GetComponent<TMP_Text>() is TMP_Text number)) return;
            TopLeft(level, new Rect(LevelTag.x + 20, LevelTag.y, 17, LevelTag.height));
            number.fontSize = 12f;
            number.alignment = TextAlignmentOptions.Center;
            number.color = new Color(1f, 0.85f, 0.35f);
            Transform t = full.Find("GH level label");
            if (t == null)
            {
                var go = new GameObject("GH level label", typeof(RectTransform));
                t = go.transform;
                t.SetParent(full, false);
                var label = go.AddComponent<TextMeshProUGUI>();
                label.font = number.font;
                label.text = "LVL";
                label.fontSize = 7f;
                label.alignment = TextAlignmentOptions.Right;
                label.color = new Color(0.86f, 0.86f, 0.82f);
                label.raycastTarget = false;
            }
            TopLeft(t, new Rect(LevelTag.x + 2, LevelTag.y, 18, LevelTag.height));
            t.SetSiblingIndex(level.GetSiblingIndex());
        }

        /// <summary>The middle band: the template's basic-action buttons as the real cards' pills (their sprites come
        /// from the class's skin), the initiative on its hex between the chevrons.</summary>
        private static void Band(RectTransform full, ECharacter model)
        {
            Transform group = full.Find("Default action buttons");
            if (group == null)
            {
                if (GHClasses.Reported.Add("no default action buttons"))
                    Plugin.Log.LogWarning("Gloomhaven classes: the card has no Full/Default action buttons; its basic actions stay where the template puts them");
            }
            else
            {
                TopLeft(group, new Rect(0, 0, CardW, CardH));
                Clear(group);
                Clear(group.Find("Default action text"));
                Pill(group.Find("Default action top"), AttackPill, -9f);
                Pill(group.Find("Default action button"), MovePill, 9f);
            }

            Transform initiative = full.Find("Initiative text");
            Transform ornament = Picture(full, "GH initiative ornament", Art.Ornament, Centred(Initiative, 118, 62), -1);
            Transform hex = Picture(full, "GH initiative hex", Art.Hex, Centred(Initiative, 72, 36), -1);
            if (initiative == null) return;
            TopLeft(initiative, Centred(Initiative, 60, 32));
            Beneath(ornament, initiative);
            Beneath(hex, initiative);
            if (initiative.GetComponent<TMP_Text>() is TMP_Text number)
            {
                number.color = Art.Ink(model);
                number.fontSize = 28f;
            }
        }

        /// <summary>A basic-action button at <paramref name="at"/>: its own image shows the skin's pill, its icon and
        /// number (switched off in the template) sit in the middle of the pill's straight part.</summary>
        private static void Pill(Transform pill, Rect at, float shift)
        {
            if (pill == null) return;
            TopLeft(pill, at);
            Image image = pill.GetComponent<Image>();
            if (image != null) { image.color = Color.white; image.type = Image.Type.Simple; image.preserveAspect = false; }
            var centre = new Vector2(0.5f, 0.5f);
            if (pill.Find("Image") is RectTransform icon)
            {
                icon.gameObject.SetActive(true);
                icon.anchorMin = icon.anchorMax = icon.pivot = centre;
                icon.anchoredPosition = new Vector2(shift - 10f, 0f);
                icon.sizeDelta = new Vector2(18f, 18f);
                if (icon.GetComponent<Image>() is Image picture) picture.color = Color.white;
            }
            if (pill.Find("TextMeshPro Text") is RectTransform number)
            {
                number.gameObject.SetActive(true);
                number.anchorMin = number.anchorMax = number.pivot = centre;
                number.anchoredPosition = new Vector2(shift + 10f, 0f);
                if (number.GetComponent<TMP_Text>() is TMP_Text text) { text.fontSize = 18f; text.color = Color.white; text.alignment = TextAlignmentOptions.Center; }
            }
            if (pill.Find("InteractabilityHighlight") is RectTransform highlight)
            {
                highlight.anchoredPosition = Vector2.zero;
                highlight.sizeDelta = Vector2.zero;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Rect Centred(Vector2 centre, float w, float h) => new Rect(centre.x - w / 2f, centre.y - h / 2f, w, h);

        /// <summary>Places <paramref name="t"/> at <paramref name="r"/> in its parent, measured from the parent's top left.</summary>
        private static void TopLeft(Transform t, Rect r)
        {
            if (!(t is RectTransform rect)) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(r.x, -r.y);
            rect.sizeDelta = new Vector2(r.width, r.height);
        }

        private static void Clear(Transform t)
        {
            Image image = t != null ? t.GetComponent<Image>() : null;
            if (image == null) return;
            image.color = Color.clear;
            image.raycastTarget = false;   // the group now covers the whole card
        }

        /// <summary>Moves <paramref name="t"/> to just before <paramref name="above"/> in draw order if it isn't already
        /// earlier (SetSiblingIndex counts after removing the moved child, so one already in front must stay put).</summary>
        private static void Beneath(Transform t, Transform above)
        {
            if (t != null && t.GetSiblingIndex() > above.GetSiblingIndex()) t.SetSiblingIndex(above.GetSiblingIndex());
        }

        /// <summary>A plain picture under <paramref name="parent"/>, found again by name, drawn at
        /// <paramref name="siblingIndex"/> (-1: where it is, or last when new). Nothing when the sprite is missing.</summary>
        private static Transform Picture(Transform parent, string name, Sprite sprite, Rect at, int siblingIndex)
        {
            if (sprite == null) return null;
            Transform t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                t = go.transform;
                t.SetParent(parent, false);
                go.GetComponent<Image>().raycastTarget = false;
            }
            t.GetComponent<Image>().sprite = sprite;
            TopLeft(t, at);
            if (siblingIndex >= 0) t.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
            return t;
        }

        /// <summary>A picture filling a clipped window (the banner).</summary>
        private static Transform Masked(Transform parent, string name, Sprite sprite, Rect at, int siblingIndex)
        {
            if (sprite == null) return null;
            Transform window = parent.Find(name);
            if (window == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(RectMask2D));
                window = go.transform;
                window.SetParent(parent, false);
                var picture = new GameObject("picture", typeof(RectTransform), typeof(Image));
                picture.transform.SetParent(window, false);
                picture.GetComponent<Image>().raycastTarget = false;
                var p = (RectTransform)picture.transform;
                p.anchorMin = Vector2.zero;
                p.anchorMax = Vector2.one;
                p.offsetMin = p.offsetMax = Vector2.zero;
            }
            window.GetChild(0).GetComponent<Image>().sprite = sprite;
            TopLeft(window, at);
            window.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
            return window;
        }

        // ------------------------------------------------------------------ the art

        /// <summary>The pieces of the real cards, copied once into textures of our own and recoloured per class.</summary>
        private static class Art
        {
            internal static Sprite Card, TitleLine, LevelTag, Ornament, Hex;
            private static readonly Dictionary<ECharacter, Sprite> TitleBands = new Dictionary<ECharacter, Sprite>();
            private static readonly HashSet<ECharacter> Skinned = new HashSet<ECharacter>();
            private static readonly Dictionary<ECharacter, Sprite> Banners = new Dictionary<ECharacter, Sprite>();
            private static bool _sourcesTried;
            private static Pixels _field, _fieldNeutral, _frame, _glow, _pill, _pillNeutral, _pillFrame, _pillGlow, _title;

            internal static Sprite TitleBandOf(ECharacter model) => TitleBands.TryGetValue(model, out Sprite s) ? s : null;

            internal static Color Ink(ECharacter model) =>
                GHClasses.ClassColours.TryGetValue(model, out Color c) ? new Color(c.r * 0.4f, c.g * 0.4f, c.b * 0.4f) : new Color(0.13f, 0.09f, 0.05f);

            /// <summary>The class's tint for recolouring: its colour scaled to the brightness of white, so multiplying a
            /// grey by it keeps the grey's brightness.</summary>
            private static Color TintOf(ECharacter model)
            {
                if (!GHClasses.ClassColours.TryGetValue(model, out Color c)) return Color.white;
                float lum = Lum(c);
                return lum <= 0.01f ? Color.white : new Color(c.r / lum, c.g / lum, c.b / lum, 1f);
            }

            internal static Sprite Banner(ECharacter model)
            {
                if (Banners.TryGetValue(model, out Sprite s)) return s;
                GHClasses.Pack pack = GHClasses.PackOf(model);
                string dir = pack == null ? null : Path.Combine(pack.Folder, "art", model.ToString());
                s = dir == null ? null : GHClasses.Picture(dir, "cardBanner", null);
                if (s == null && GHClasses.Reported.Add("no banner " + model))
                    Plugin.Log.LogWarning($"Gloomhaven classes: no cardBanner.png for {model}; its cards have no banner picture (rerun tools/gh_port.py)");
                Banners[model] = s;
                return s;
            }

            /// <summary>Copies the pieces (once) and gives the class's skin its sprites (once per class).</summary>
            internal static void Ensure(ECharacter model)
            {
                if (Skinned.Contains(model)) return;
                if (!Sources()) return;
                Color tint = TintOf(model);
                if (!TitleBands.ContainsKey(model))
                    TitleBands[model] = _title == null ? null : _title.Recolour(tint).ToSprite($"GH {model} title band");

                CharacterConfigUI cfg = UIInfoTools.Instance != null ? UIInfoTools.Instance.GetCharacterConfigUI(model.ToString(), useDefault: false) : null;
                AbilityCardUISkin skin = cfg?.cardSkin?.Skin;
                if (skin == null || cfg.cardSkin.name != GHClasses.SkinName(model))
                {
                    if (GHClasses.Reported.Add("no own skin " + model))
                        Plugin.Log.LogWarning($"Gloomhaven classes: {model} has no card skin of its own; its card halves keep the stand-in's");
                    Skinned.Add(model);
                    return;
                }
                if (_field != null && _fieldNeutral != null && _frame != null)
                {
                    Pixels half = Pixels.Glow(_field, _fieldNeutral, tint).Over(_frame.Slice(16, 12.8f));
                    Pixels lit = _glow != null ? half.Over(_glow.Fit(half.W, half.H)) : half;
                    Pixels dim = half.Darken(0.55f);
                    Sprite top = half.ToSprite($"GH {model} top half"), topLit = lit.ToSprite($"GH {model} top half lit"), topDim = dim.ToSprite($"GH {model} top half off");
                    Sprite bottom = half.Turned().ToSprite($"GH {model} bottom half"), bottomLit = lit.Turned().ToSprite($"GH {model} bottom half lit"), bottomDim = dim.Turned().ToSprite($"GH {model} bottom half off");
                    skin.topActionRegularSprite = top; skin.topActionHighlightSprite = topLit; skin.topActionSelectedSprite = topLit; skin.topActionDisabledSprite = topDim;
                    skin.bottomActionRegularSprite = bottom; skin.bottomActionHighlightSprite = bottomLit; skin.bottomActionSelectedSprite = bottomLit; skin.bottomActionDisabledSprite = bottomDim;
                }
                if (_pill != null && _pillNeutral != null && _pillFrame != null)
                {
                    Pixels pill = Pixels.Glow(_pill, _pillNeutral, tint).Over(_pillFrame.Fit(_pill.W, _pill.H));
                    Pixels lit = _pillGlow != null ? pill.Over(_pillGlow.Fit(pill.W, pill.H)) : pill;
                    Pixels dim = pill.Darken(0.55f);
                    skin.defaultTopActionRegularSprite = pill.ToSprite($"GH {model} attack pill");
                    skin.defaultTopActionHighlightSprite = lit.ToSprite($"GH {model} attack pill lit");
                    skin.defaultTopActionDisabledSprite = dim.ToSprite($"GH {model} attack pill off");
                    skin.defaultBottomActionRegularSprite = pill.Turned().ToSprite($"GH {model} move pill");
                    skin.defaultBottomActionHighlightSprite = lit.Turned().ToSprite($"GH {model} move pill lit");
                    skin.defaultBottomActionDisabledSprite = dim.Turned().ToSprite($"GH {model} move pill off");
                }
                skin.initiativeColor = Ink(model);
                Skinned.Add(model);
                Plugin.Log.LogInfo($"Gloomhaven classes: {model} card skin made from the {GHClasses.StandInModel}'s card pieces, in the class's colour");
            }

            /// <summary>The pieces, by the real card's object names (the same in every class's prefab), plus the
            /// neutral versions and the band pieces from misc_globalsettings. True once copied.</summary>
            private static bool Sources()
            {
                if (_sourcesTried) return Card != null;
                GameObject prefab = GHClasses.StandInCardPrefab();
                if (prefab == null) return false;   // try again on the next card
                _sourcesTried = true;
                var byObject = new Dictionary<string, Sprite>();
                foreach (Image image in prefab.GetComponentsInChildren<Image>(true))
                    if (image != null && image.sprite != null && !byObject.ContainsKey(image.name)) byObject[image.name] = image.sprite;
                Image level = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Level")?.Find("Image")?.GetComponent<Image>();
                Sprite Of(string obj) => byObject.TryGetValue(obj, out Sprite s) ? s : null;
                try
                {
                    Card = Pixels.Read(Of("cardBackground"))?.ToSprite("GH card");
                    TitleLine = Pixels.Read(Of("titleDecoration"))?.ToSprite("GH title line");
                    LevelTag = Pixels.Read(level != null ? level.sprite : null)?.ToSprite("GH level tag");
                    _title = Pixels.Read(Of("titleDarkeningBG"));
                    _field = Pixels.Read(Of("topFieldBG_base"));
                    _frame = Pixels.Read(Of("topFieldBG_frame"));
                    _pill = Pixels.Read(Of("defActionCardBG"));
                    _pillGlow = Pixels.Read(Of("defInteractabilityHighlight"));
                    _fieldNeutral = Pixels.Read(GlobalSprite("FH_UI_CARD_ActionBGR_Innactive"))?.Fit(_field?.W ?? 0, _field?.H ?? 0);
                    _glow = Pixels.Read(GlobalSprite("FH_UI_CARD_Selection"));
                    _pillNeutral = Pixels.Read(GlobalSprite("FH_UI_CARD_DEFAction_BGR_Inactive"))?.Fit(_pill?.W ?? 0, _pill?.H ?? 0);
                    _pillFrame = Pixels.Read(GlobalSprite("FH_UI_CARD_DEFAction_Frame_Active")) ?? Pixels.Read(Of("Frame"));
                    Ornament = GlobalSprite("FH_UI_CARD_Initiative_Ornament");
                    Hex = GlobalSprite("FH_UI_CARD_Initiative");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Gloomhaven classes: could not copy the card pieces from {prefab.name}: {e}");
                }
                Plugin.Log.LogInfo($"Gloomhaven classes: card pieces copied from {prefab.name}" + (Card == null ? " (card background missing: cards keep the template's look)" : ""));
                return Card != null;
            }

            private static readonly Dictionary<string, Sprite> GlobalSprites = new Dictionary<string, Sprite>();

            /// <summary>A sprite of misc_globalsettings (loaded for the whole session) by name.</summary>
            internal static Sprite GlobalSprite(string name)
            {
                if (GlobalSprites.TryGetValue(name, out Sprite cached)) return cached;
                Sprite sprite = null;
                foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle == null || bundle.name != "misc_globalsettings") continue;
                    sprite = bundle.LoadAsset<Sprite>(name);
                    break;
                }
                if (sprite == null)
                    sprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && s.name == name);
                if (sprite == null && GHClasses.Reported.Add("no sprite " + name))
                    Plugin.Log.LogWarning($"Gloomhaven classes: the game has no {name} sprite loaded; that piece of the card is left out");
                GlobalSprites[name] = sprite;
                return sprite;
            }
        }

        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>A sprite's pixels on the CPU, and the few operations the card pieces need.</summary>
        private sealed class Pixels
        {
            internal readonly int W, H;
            internal readonly Color[] Px;
            private Pixels(int w, int h, Color[] px) { W = w; H = h; Px = px; }

            /// <summary>The sprite's pixels, read back from the GPU (the game's textures aren't CPU-readable): drawn
            /// into a render target the size of its texture, the sprite's rectangle read out. sRGB in, sRGB out.</summary>
            internal static Pixels Read(Sprite sprite)
            {
                if (sprite == null) return null;
                Texture2D source = sprite.texture;
                Rect r = sprite.textureRect;
                RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    var copy = new Texture2D(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), TextureFormat.RGBA32, false, false);
                    copy.ReadPixels(new Rect(r.x, r.y, copy.width, copy.height), 0, 0);
                    copy.Apply();
                    var pixels = new Pixels(copy.width, copy.height, copy.GetPixels());
                    UnityEngine.Object.Destroy(copy);
                    return pixels;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            internal Sprite ToSprite(string name)
            {
                var texture = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
                texture.SetPixels(Px);
                texture.Apply(false, true);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return sprite;
            }

            private Color At(float x, float y)
            {
                x = Mathf.Clamp(x, 0, W - 1); y = Mathf.Clamp(y, 0, H - 1);
                int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, W - 1), y1 = Math.Min(y0 + 1, H - 1);
                float fx = x - x0, fy = y - y0;
                Color a = Color.Lerp(Px[y0 * W + x0], Px[y0 * W + x1], fx), b = Color.Lerp(Px[y1 * W + x0], Px[y1 * W + x1], fx);
                return Color.Lerp(a, b, fy);
            }

            /// <summary>Resampled to <paramref name="w"/> x <paramref name="h"/> (unchanged when already that size).</summary>
            internal Pixels Fit(int w, int h)
            {
                if (w <= 0 || h <= 0 || (w == W && h == H)) return this;
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = At((x + 0.5f) * W / w - 0.5f, (y + 0.5f) * H / h - 0.5f);
                return new Pixels(w, h, px);
            }

            /// <summary>A 9-sliced frame drawn at its own size with thinner borders: <paramref name="border"/> source
            /// pixels become <paramref name="target"/> (how the real card draws the frame, at 2.5 pixels per unit, over
            /// a background drawn at 2).</summary>
            internal Pixels Slice(int border, float target)
            {
                float Map(float d, int size)
                {
                    if (d < target) return d * border / target;
                    if (d > size - target) return size - (size - d) * border / target;
                    return border + (d - target) * (size - 2f * border) / (size - 2f * target);
                }
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = At(Map(x + 0.5f, W) - 0.5f, Map(y + 0.5f, H) - 0.5f);
                return new Pixels(W, H, px);
            }

            internal Pixels Over(Pixels top)
            {
                var px = new Color[Px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    Color b = Px[i], t = top.Px[i];
                    float a = t.a + b.a * (1f - t.a);
                    px[i] = a <= 0f ? Color.clear : new Color((t.r * t.a + b.r * b.a * (1f - t.a)) / a, (t.g * t.a + b.g * b.a * (1f - t.a)) / a, (t.b * t.a + b.b * b.a * (1f - t.a)) / a, a);
                }
                return new Pixels(W, H, px);
            }

            internal Pixels Darken(float f) => new Pixels(W, H, Px.Select(c => new Color(c.r * f, c.g * f, c.b * f, c.a)).ToArray());

            internal Pixels Turned() => new Pixels(W, H, Enumerable.Reverse(Px).ToArray());

            /// <summary>Same brightness, the class's hue (the title band).</summary>
            internal Pixels Recolour(Color tint) =>
                new Pixels(W, H, Px.Select(c => { float l = Lum(c); return new Color(Mathf.Clamp01(l * tint.r), Mathf.Clamp01(l * tint.g), Mathf.Clamp01(l * tint.b), c.a); }).ToArray());

            /// <summary>The neutral background plus the stand-in's glow (how much brighter its version is) in the
            /// class's hue.</summary>
            internal static Pixels Glow(Pixels classed, Pixels neutral, Color tint)
            {
                var px = new Color[classed.Px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    Color c = classed.Px[i], n = neutral.Px[i];
                    float g = Mathf.Max(0f, Lum(c) - Lum(n)) * 1.3f;
                    px[i] = new Color(Mathf.Clamp01(n.r + g * tint.r), Mathf.Clamp01(n.g + g * tint.g), Mathf.Clamp01(n.b + g * tint.b), c.a);
                }
                return new Pixels(classed.W, classed.H, px);
            }
        }
    }
}
