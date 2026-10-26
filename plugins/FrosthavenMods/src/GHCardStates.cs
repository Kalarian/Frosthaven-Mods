using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// A Gloomhaven card's halves light up when hovered and dim once spent, as Frosthaven's do. Frosthaven's card halves
    /// swap their backdrop through a state controller the Gloomhaven cards' template doesn't have (actionBackground is
    /// empty there); the class skin's half sprites (normal, lit, off: GHCardFace.Art.Ensure) sat in the half button's
    /// sprite-swap slots, which the button never uses (no transition). So each refresh of a half's look sets the sprite
    /// for its state here: lit while hovered or selected, off while it can't be played, normal otherwise; and the half's
    /// text fades as far as on Frosthaven's cards. Only halves showing a Gloomhaven class's sprites are touched.
    /// </summary>
    [HarmonyPatch]
    internal static class GHCardStates
    {
        private static readonly FieldInfo ActionBackground = AccessTools.Field(typeof(FullAbilityCardAction), "actionBackground");
        private static readonly FieldInfo ActionButton = AccessTools.Field(typeof(FullAbilityCardAction), "actionButton");
        private static readonly FieldInfo IsSelected = AccessTools.Field(typeof(FullAbilityCardAction), "isSelected");
        private static readonly FieldInfo InactiveAlpha = AccessTools.Field(typeof(FullAbilityCardAction), "inactiveAlpha");
        private static readonly PropertyInfo Active = AccessTools.Property(typeof(FullAbilityCardAction), "ActiveAbilityAction");

        /// <summary>Each Gloomhaven half's own normal sprite (the button's image as the card was built).</summary>
        private static readonly Dictionary<Image, Sprite> Normal = new Dictionary<Image, Sprite>();
        private static readonly HashSet<FullAbilityCardAction> Hovered = new HashSet<FullAbilityCardAction>();

        private static Image GhImage(FullAbilityCardAction half, out Selectable button)
        {
            button = ActionButton?.GetValue(half) as Selectable;
            if (button == null || ActionBackground?.GetValue(half) != null) return null;   // a Frosthaven card: its own controller
            if (!(button.targetGraphic is Image image) || image.sprite == null) return null;
            if (!Normal.ContainsKey(image))
            {
                if (!image.sprite.name.StartsWith("GH ")) return null;
                Normal[image] = image.sprite;
                if (InactiveAlpha != null) InactiveAlpha.SetValue(half, 0.3f);   // Frosthaven's cards fade spent halves to 0.3
            }
            return image;
        }

        private static void Show(FullAbilityCardAction half)
        {
            Image image = GhImage(half, out Selectable button);
            if (image == null) return;
            bool selected = IsSelected?.GetValue(half) is bool s && s;
            bool active = Active?.GetValue(half) is bool a && a;
            SpriteState st = button.spriteState;
            Sprite sprite = selected ? st.selectedSprite
                : !active ? st.disabledSprite
                : Hovered.Contains(half) ? st.highlightedSprite
                : Normal[image];
            if (sprite != null && image.sprite != sprite) image.sprite = sprite;
        }

        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.RefreshInteractable))]
        [HarmonyPostfix]
        private static void AfterRefresh(FullAbilityCardAction __instance, bool defaultAction)
        {
            if (!defaultAction) Show(__instance);
        }

        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.ToggleSelect))]
        [HarmonyPostfix]
        private static void AfterSelect(FullAbilityCardAction __instance) => Show(__instance);

        [HarmonyPatch(typeof(FullAbilityCardAction), nameof(FullAbilityCardAction.ToggleHighlightHover))]
        [HarmonyPostfix]
        private static void AfterHover(FullAbilityCardAction __instance, bool active, bool isDefaultAbility)
        {
            if (isDefaultAbility) return;
            if (active) Hovered.Add(__instance); else Hovered.Remove(__instance);
            Show(__instance);
        }
    }
}
