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
        // the card background picture has a soft shadow round it: drawn 12 units past the card on every side, as the
        // real card draws it (cardBackground: stretched, size +24), its body fills the card exactly
        private static readonly Rect CardBody = new Rect(-12, -12, CardW + 24, CardH + 24);
        private static readonly Rect Banner = new Rect(8, 8, 300, 79);
        private static readonly Rect TitleBand = new Rect(8, 87, 300, 30);
        private static readonly Rect TitleLine = new Rect(6, 84, 304, 18);
        private static readonly Rect LevelTag = new Rect(270, 68, 37, 14);   // clear of the banner's bottom line (y 85)
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

        // ------------------------------------------------------------------ the hand list's row (mini card)

        /// <summary>Frosthaven's hand-list rows show the card's picture at their right end (Mask_CardArt, a window
        /// over the right half of the row). The template's is a Banner Spear painting; a Gloomhaven card shows the
        /// class's banner instead, fitted to the window (the slot is sized for the real cards' tall header pictures).
        /// False when there is no banner (the caller hides the template's painting).</summary>
        internal static bool MiniArt(AbilityCardUI card, ECharacter model)
        {
            Transform art = card.miniAbilityCard != null ? card.miniAbilityCard.transform.Find("Container_CardArt/Mask_CardArt/Image_CardArt") : null;
            Sprite banner = Art.Banner(model);
            if (!(art is RectTransform rect) || banner == null || !(art.GetComponent<Image>() is Image image)) return false;
            image.sprite = banner;
            image.preserveAspect = false;
            image.enabled = true;
            rect.anchorMin = new Vector2(-0.06f, -0.1f);
            rect.anchorMax = new Vector2(1.06f, 1.1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return true;
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
            Picture(full, "GH card", Art.Card, CardBody, at++);
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
                    text.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
                    text.color = Color.white;
                }
            }
            Level(full);
            Band(full, model);
            Locked(full);
        }

        /// <summary>An effect or highlight layer (or inside one), not card art.</summary>
        private static bool Effect(Transform t, Transform full)
        {
            for (; t != null && t != full; t = t.parent)
                if (t.name.StartsWith("FX_") || t.name.StartsWith("UIFX") || t.name.Contains("Highlight") || t.name == "UnfocusedMask") return true;
            return false;
        }

        private static readonly System.Reflection.FieldInfo EffectsInitiative = AccessTools.Field(typeof(CardEffects), "initiativeTxt");
        private static readonly System.Reflection.FieldInfo EffectsTexts = AccessTools.Field(typeof(CardEffects), "txtComp");
        private static readonly System.Reflection.FieldInfo EffectsOverlay = AccessTools.Field(typeof(CardEffects), "fgFx");
        private static readonly System.Reflection.FieldInfo EffectsSmoke = AccessTools.Field(typeof(CardEffects), "fx_Smoke");

        /// <summary>How a card the character can't take yet looks (the New Cards screen: UIOutpostCardSlot). The game
        /// greys a card through an ImageMaterialSwapper on the full card, which every real card prefab carries and the
        /// template lacks, so the card gets one with the real cards' greyscale material. And it plays the card's
        /// ghost-out effect, which on the template fades the initiative to white; the real cards keep theirs in the
        /// class's ink, so the initiative is taken out of the effect.</summary>
        private static void Locked(RectTransform full)
        {
            ImageMaterialSwapper swapper = full.GetComponent<ImageMaterialSwapper>();
            if (swapper == null && Art.Greyscale != null)
            {
                swapper = full.gameObject.AddComponent<ImageMaterialSwapper>();
                swapper.TargetMaterial = Art.Greyscale;
            }
            if (swapper != null)
            {
                // only the card's artwork: the template also holds effect layers drawn as plain white rectangles that their
                // own material keeps invisible (UIFX_Overlay, FX_*, highlights), and images drawn clear; in greyscale both
                // would show as a grey sheet over the card. The real cards' swapper takes all child images (they have none)
                swapper.UseChildImages = false;
                swapper.Images = full.GetComponentsInChildren<Image>(true)
                    .Where(i => i.sprite != null && i.color.a > 0f && !i.TryGetComponent<Mask>(out _) && !Effect(i.transform, full))
                    .ToArray();
            }
            CardEffects effects = full.GetComponent<CardEffects>();
            if (effects == null) return;
            EffectsInitiative?.SetValue(effects, null);
            // the template's ghost-out also rings the card with animated smoke (its overlay UIFX_Overlay and a smoke
            // particle system); the real cards' CardEffects have neither (every slot empty), so their locked and
            // discarded cards show none
            if (EffectsOverlay?.GetValue(effects) is Image overlay)
            {
                overlay.gameObject.SetActive(false);
                EffectsOverlay.SetValue(effects, null);
            }
            if (EffectsSmoke?.GetValue(effects) is Component smoke)   // a ParticleSystem (its module isn't referenced)
            {
                smoke.gameObject.SetActive(false);
                EffectsSmoke.SetValue(effects, null);
            }
            if (EffectsTexts?.GetValue(effects) is TextMeshProUGUI[] texts && texts.Length > 5) texts[5] = null;   // already initialised
        }

        private static readonly Color LevelYellow = new Color(1f, 0.87f, 0.3f);

        /// <summary>"LVL 3" in the tag at the banner's corner: the template's own level number (the game writes it),
        /// and a small "LVL" before it.</summary>
        private static void Level(RectTransform full)
        {
            if (!(full.Find("LevelText") is RectTransform level) || !(level.GetComponent<TMP_Text>() is TMP_Text number)) return;
            // the real tag writes both in the card's sans font (the template's level is in the title's serif), yellow
            TMP_FontAsset sans = full.Find("Default action buttons/Default action top/TextMeshPro Text")?.GetComponent<TMP_Text>()?.font;
            TopLeft(level, new Rect(LevelTag.x + 21, LevelTag.y, 15, LevelTag.height));
            if (sans != null) number.font = sans;
            number.fontSize = 12f;
            number.fontStyle = FontStyles.Bold;
            number.alignment = TextAlignmentOptions.Center;
            number.color = LevelYellow;
            Transform t = full.Find("GH level label");
            if (t == null)
            {
                var go = new GameObject("GH level label", typeof(RectTransform));
                t = go.transform;
                t.SetParent(full, false);
                var label = go.AddComponent<TextMeshProUGUI>();
                label.text = "LVL";
                label.raycastTarget = false;
            }
            if (t.GetComponent<TMP_Text>() is TMP_Text lvl)
            {
                lvl.font = number.font;
                lvl.fontSize = 8f;
                lvl.fontStyle = FontStyles.Bold;
                lvl.alignment = TextAlignmentOptions.Right;
                lvl.color = LevelYellow;
            }
            TopLeft(t, new Rect(LevelTag.x + 2, LevelTag.y + 1, 19, LevelTag.height - 1));
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
                Pill(group.Find("Default action top"), AttackPill, -9f, Art.GuiSprite("misc_gui", "Attack"));   // the template shows "Damage"
                Pill(group.Find("Default action button"), MovePill, 9f, null);
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
        private static void Pill(Transform pill, Rect at, float shift, Sprite iconSprite)
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
                if (icon.GetComponent<Image>() is Image picture)
                {
                    picture.color = Color.white;
                    if (iconSprite != null) picture.sprite = iconSprite;
                }
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
            private static Material _greyscale;

            /// <summary>The real cards' greyscale material for locked cards (a copy of ours), else the game's own
            /// disabled-greyscale material.</summary>
            internal static Material Greyscale => _greyscale != null ? _greyscale : UIInfoTools.Instance != null ? UIInfoTools.Instance.disabledGrayscaleMaterial : null;
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
                // the real banner's mask (FH_UI_CARD_ImageMask) cuts its two top corners at 45 degrees, about 3.5 units
                if (s != null) s = Pixels.Read(s).Bevel(Mathf.RoundToInt(3.5f * s.rect.width / GHCardFace.Banner.width)).ToSprite($"GH {model} banner");
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
                    TitleBands[model] = _title == null ? null : _title.Recolour(tint).Faint().ToSprite($"GH {model} title band");

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
                Plates(skin, model);
                Skinned.Add(model);
                Plugin.Log.LogInfo($"Gloomhaven classes: {model} card skin made from the {GHClasses.StandInModel}'s card pieces, in the class's colour");
            }

            /// <summary>The hand list's row plate and its initiative diamond are flat shapes in the class's colour
            /// (the Drifter's brown, the Boneshaper's green, all about as dark): the stand-in's, repainted in the class's
            /// colour at the same darkness.</summary>
            private static void Plates(AbilityCardUISkin skin, ECharacter model)
            {
                if (!GHClasses.ClassColours.TryGetValue(model, out Color colour)) return;
                var made = new Dictionary<Sprite, Sprite>();
                Sprite Repaint(Sprite source)
                {
                    if (source == null) return null;
                    if (made.TryGetValue(source, out Sprite done)) return done;
                    Pixels px = Pixels.Read(source);
                    Sprite sprite = px == null ? source : px.Plate(colour).ToSprite($"GH {model} {source.name}");
                    made[source] = sprite;
                    return sprite;
                }
                try
                {
                    foreach (var field in typeof(AbilityCardUISkin).GetFields())
                        if (field.FieldType == typeof(Sprite) && field.Name.EndsWith("PreviewBackground") && !field.Name.StartsWith("longRest"))
                            field.SetValue(skin, Repaint((Sprite)field.GetValue(skin)));
                    skin.initiativeBackground = Repaint(skin.initiativeBackground);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Gloomhaven classes: could not repaint {model}'s hand-list plates: {e.Message}");
                }
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
                    Material grey = prefab.GetComponentInChildren<ImageMaterialSwapper>(true)?.TargetMaterial;
                    if (grey != null) _greyscale = new Material(grey) { name = "GH card greyscale", hideFlags = HideFlags.DontUnloadUnusedAsset };
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
            internal static Sprite GlobalSprite(string name) => GuiSprite("misc_globalsettings", name);

            /// <summary>A sprite of one of the bundles the game keeps loaded for the whole session, by name.</summary>
            internal static Sprite GuiSprite(string bundleName, string name)
            {
                string key = bundleName + "/" + name;
                if (GlobalSprites.TryGetValue(key, out Sprite cached)) return cached;
                Sprite sprite = null;
                foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle == null || bundle.name != bundleName) continue;
                    sprite = bundle.LoadAsset<Sprite>(name);
                    break;
                }
                if (sprite == null)
                    sprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && s.name == name);
                if (sprite == null && GHClasses.Reported.Add("no sprite " + name))
                    Plugin.Log.LogWarning($"Gloomhaven classes: the game has no {name} sprite loaded; that piece of the card is left out");
                GlobalSprites[key] = sprite;
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

            /// <summary>The two top corners cut off at 45 degrees, <paramref name="cut"/> pixels deep (row 0 is the bottom).</summary>
            internal Pixels Bevel(int cut)
            {
                var px = (Color[])Px.Clone();
                for (int y = Math.Max(0, H - cut - 1); y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = Mathf.Min(H - 1 - y + x, H - 1 - y + (W - 1 - x));   // steps from the nearer top corner
                    float keep = Mathf.Clamp01(d - cut + 1f);
                    if (keep < 1f) px[y * W + x].a *= keep;
                }
                return new Pixels(W, H, px);
            }

            /// <summary>A flat-coloured shape repainted in <paramref name="colour"/>, at the shape's own darkness (its
            /// shading kept: each pixel's brightness relative to the shape's typical colour).</summary>
            internal Pixels Plate(Color colour)
            {
                // the shape's typical colour: the brightness most of its opaque pixels have
                float typical = Px.Where(c => c.a > 0.9f).Select(Lum).DefaultIfEmpty(0.33f).OrderBy(l => l).ElementAt(Px.Count(c => c.a > 0.9f) / 2);
                Color target = colour * (typical / Mathf.Max(0.01f, Lum(colour)));
                return new Pixels(W, H, Px.Select(c =>
                {
                    float k = Lum(c) / Mathf.Max(0.01f, typical);
                    return new Color(Mathf.Clamp01(target.r * k), Mathf.Clamp01(target.g * k), Mathf.Clamp01(target.b * k), c.a);
                }).ToArray());
            }

            internal Pixels Darken(float f) => new Pixels(W, H, Px.Select(c => new Color(c.r * f, c.g * f, c.b * f, c.a)).ToArray());

            internal Pixels Turned() => new Pixels(W, H, Enumerable.Reverse(Px).ToArray());

            /// <summary>Same brightness, half way to the class's hue (the title band; the real ones are only faintly
            /// tinted).</summary>
            internal Pixels Recolour(Color tint) =>
                new Pixels(W, H, Px.Select(c =>
                {
                    float l = Lum(c);
                    return new Color(Mathf.Clamp01(l * Mathf.Lerp(1f, tint.r, 0.5f)), Mathf.Clamp01(l * Mathf.Lerp(1f, tint.g, 0.5f)), Mathf.Clamp01(l * Mathf.Lerp(1f, tint.b, 0.5f)), c.a);
                }).ToArray());

            /// <summary>Only the lighter middle: transparent where the picture is at its darkest (its edges), opaque
            /// where it is lightest, smoothly in between. The real title band is opaque but its edges are the card's
            /// own colour, so it shows as a faint glow; recoloured, the edges would show as a box.</summary>
            internal Pixels Faint()
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Color c in Px) { float l = Lum(c); if (l < lo) lo = l; if (l > hi) hi = l; }
                float range = Mathf.Max(0.001f, hi - lo);
                return new Pixels(W, H, Px.Select(c => new Color(c.r, c.g, c.b, c.a * Mathf.SmoothStep(0f, 1f, (Lum(c) - lo) / range))).ToArray());
            }

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
