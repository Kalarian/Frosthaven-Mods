using System.Collections.Generic;
using System.Linq;
using FH.YML;
using FHRL;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// Enhancer price of a hex slot inside a command ability (Beast Tyrant / Wildfury Forceful Swipe and Unstoppable Beast
    /// tops: the bear's attack area has an enhanceable hex). The game's price for a hex enhancement is the cost divided by
    /// the action's red (enemy) area hexes, and it counts those only on the action's top-level abilities
    /// (YMLDefRepository.ToRuleLibraryDefRepo), while the slots come from every ability including the commanded one. So
    /// the action has 0 red hexes: the price divides by 0, shows as a huge negative number, and buying it sets the
    /// character's gold to 0. After the def repository is built, a pack card's action with a hex slot and 0 counted red
    /// hexes gets the most red hexes of any of its abilities, inner ones included (what the game would count had the area
    /// been top level). The cards play the same; only the Enhancer's numbers change.
    /// Inert without a content pack: only cards 20000-39999 are looked at, and Frosthaven's own have no such slot.
    /// packcheck flags a hex slot this fix can't price (no ability of the action has a red area hex).
    /// </summary>
    [HarmonyPatch]
    internal static class GHEnhancerInnerHexes
    {
        [HarmonyPatch(typeof(YMLDefRepository), "ToRuleLibraryDefRepo")]
        [HarmonyPostfix]
        private static void CountInnerHexes(DefRepository __result)
        {
            if (__result?.ClassCards == null || ScenarioRuleClient.SRLYML?.AbilityCards == null) return;
            foreach (AbilityCardYMLData card in ScenarioRuleClient.SRLYML.AbilityCards)
            {
                if (card == null || card.ID < 20000 || card.ID >= 40000 || card.CharacterID == "All") continue;
                if (!__result.ClassCards.TryGetValue(new ClassCardID((uint)card.ID), out ClassCardDef def) || def?.Actions == null) continue;
                // the same action list, in the same order, as ToRuleLibraryDefRepo builds def.Actions
                var actions = new List<CAction>();
                if (card.TopActionCardData?.Abilities != null) actions.Add(card.TopActionCardData);
                if (card.BottomActionCardData?.Abilities != null) actions.Add(card.BottomActionCardData);
                for (int i = 0; i < actions.Count && i < def.Actions.Count; i++)
                {
                    ClassCardDef.Ability ability = def.Actions[i];
                    if (ability.EffectHexes != 0 || !ability.EnhancementSlots.Any(s => s.Mark == CardEnhancementMarkType.Hex)) continue;
                    int red = CAbilityCard.GetActionAbilities(actions[i]).Where(a => a?.AreaEffect != null)
                        .Select(a => a.AreaEffect.EnabledHexes.Count(x => x.Flags.HasFlag(CAreaEffect.EHexFlag.Enemy)))
                        .DefaultIfEmpty(0).Max();
                    if (red <= 0) continue;
                    ability.EffectHexes = red;
                    Plugin.Log.LogInfo($"GHRule EnhancerInnerHexes card {card.ID} {(ability.IsTopAction ? "top" : "bottom")}: hex slot priced over {red} red hexes of a commanded ability (the game counted 0)");
                }
            }
        }
    }
}
