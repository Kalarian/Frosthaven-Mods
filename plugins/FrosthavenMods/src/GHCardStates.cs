using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// A Gloomhaven card's halves and basic-action pills light up when hovered and dim once spent, as Frosthaven's do.
    /// Frosthaven's card halves swap their backdrop through a state controller the Gloomhaven cards' template doesn't
    /// have (actionBackground is empty there); the class skin's sprites (normal, lit, off: GHCardFace.Art.Ensure) sat in
    /// the buttons' sprite-swap slots, which the buttons never use (no transition). So each refresh of a half's look sets
    /// the sprites for its state here:
    ///   - the half: lit while it or its basic action is hovered or selected, off while it can't be played;
    ///   - the pill (Basic Attack on the top half, Basic Move on the bottom): lit while hovered or selected, off and
    ///     faded while it can't be played;
    ///   - spent halves' text fades as far as on Frosthaven's cards.
    /// Only halves showing a Gloomhaven class's sprites are touched.
    /// </summary>
    [HarmonyPatch]
    internal static class GHCardStates
    {
        private static readonly FieldInfo ActionBackground = AccessTools.Field(typeof(FullAbilityCardAction), "actionBackground");
        private static readonly FieldInfo ActionButton = AccessTools.Field(typeof(FullAbilityCardAction), "actionButton");
        private static readonly FieldInfo DefaultButton = AccessTools.Field(typeof(FullAbilityCardAction), "defaultActionButton");
        private static readonly FieldInfo DefCanvasGroup = AccessTools.Field(typeof(FullAbilityCardAction), "defCanvasGroup");
        private static readonly FieldInfo IsSelected = AccessTools.Field(typeof(FullAbilityCardAction), "isSelected");
        private static readonly FieldInfo IsSelectedDefault = AccessTools.Field(typeof(FullAbilityCardAction), "isSelectedDefaultAction");
        private static readonly FieldInfo IsInPreview = AccessTools.Field(typeof(FullAbilityCardAction), "isInPreview");
        private static readonly FieldInfo InactiveAlpha = AccessTools.Field(typeof(FullAbilityCardAction), "inactiveAlpha");
        private static readonly PropertyInfo Active = AccessTools.Property(typeof(FullAbilityCardAction), "ActiveAbilityAction");
        private static readonly PropertyInfo ActiveDefault = AccessTools.Property(typeof(FullAbilityCardAction), "ActiveDefaultAction");

        /// <summary>Each Gloomhaven half's and pill's own normal sprite (the button's image as the card was built).</summary>
        private static readonly Dictionary<Image, Sprite> Normal = new Dictionary<Image, Sprite>();
        private static readonly HashSet<FullAbilityCardAction> Hovered = new HashSet<FullAbilityCardAction>();
        private static readonly HashSet<FullAbilityCardAction> HoveredDefault = new HashSet<FullAbilityCardAction>();

        private static Image GhImage(Selectable button)
        {
            if (button == null || !(button.targetGraphic is Image image) || image.sprite == null) return null;
            if (!Normal.ContainsKey(image))
            {
                string name = image.sprite.name;
                if (!name.StartsWith("GH ") || name.EndsWith(" lit") || name.EndsWith(" off")) return null;   // learnt from its normal look
                Normal[image] = image.sprite;
            }
            return image;
        }

        private static bool Flag(FieldInfo field, FullAbilityCardAction half) => field?.GetValue(half) is bool b && b;
        private static bool Flag(PropertyInfo prop, FullAbilityCardAction half) => prop?.GetValue(half) is bool b && b;

        private static void Set(Image image, Selectable button, bool lit, bool off)
        {
            SpriteState st = button.spriteState;
            Sprite sprite = lit ? st.highlightedSprite : off ? st.disabledSprite : Normal[image];
            if (sprite != null && image.sprite != sprite) image.sprite = sprite;
        }

        private static void Show(FullAbilityCardAction half)
        {
            if (ActionBackground?.GetValue(half) != null) return;   // a Frosthaven card: its own controller
            var button = ActionButton?.GetValue(half) as Selectable;
            Image image = GhImage(button);
            if (image == null) return;
            if (InactiveAlpha != null && !(InactiveAlpha.GetValue(half) is float a && a == 0.3f))
                InactiveAlpha.SetValue(half, 0.3f);   // Frosthaven's cards fade spent halves to 0.3

            bool active = Flag(Active, half), activeDefault = Flag(ActiveDefault, half);
            bool selectedDefault = Flag(IsSelectedDefault, half);
            bool defaultLit = selectedDefault || (activeDefault && HoveredDefault.Contains(half));
            Set(image, button, Flag(IsSelected, half) || defaultLit || (active && Hovered.Contains(half)), !active);

            var pillButton = DefaultButton?.GetValue(half) as Selectable;
            Image pill = GhImage(pillButton);
            if (pill == null) return;
            Set(pill, pillButton, defaultLit, !activeDefault);
            // the template has no fading group for the pill (Frosthaven's cards fade it with defCanvasGroup)
            CanvasGroup group = DefCanvasGroup?.GetValue(half) as CanvasGroup;
            if (group == null) group = pill.GetComponent<CanvasGroup>() ?? pill.gameObject.AddComponent<CanvasGroup>();
            group.alpha = activeDefault || selectedDefault || Flag(IsInPreview, half) ? 1f : 0.3f;
        }

        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.RefreshInteractable))]
        [HarmonyPostfix]
        private static void AfterRefresh(FullAbilityCardAction __instance) => Show(__instance);

        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.ToggleSelect))]
        [HarmonyPostfix]
        private static void AfterSelect(FullAbilityCardAction __instance) => Show(__instance);

        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.ToggleHighlightHover))]
        [HarmonyPostfix]
        private static void AfterHover(FullAbilityCardAction __instance, bool active, bool isDefaultAbility)
        {
            HashSet<FullAbilityCardAction> set = isDefaultAbility ? HoveredDefault : Hovered;
            if (active) set.Add(__instance); else set.Remove(__instance);
            Show(__instance);
        }
    }
}
