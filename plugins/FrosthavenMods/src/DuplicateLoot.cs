using System;
using System.Linq;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Share duplicate loot". Board game (p. 32): a character who loots a copy of an item they already own
    /// gives it to another character or sells it. The digital game always sells it: at the end of a scenario each
    /// looted item goes through CharacterController.Action_GainItem(..., "scenario_loot"), which turns an item the
    /// character can't own into its sell value in gold.
    ///
    /// With the rule on, such a duplicate goes into the game's own "items to gain" list instead, the same list event and
    /// quest item rewards use, so the outpost opens the usual item reward window and the party picks who gets it (each
    /// player for their own characters, the party leader confirms, all through the game's synced commands). This runs
    /// in the end-of-scenario bookkeeping, which every copy of the campaign replays the same way, and reads campaign
    /// state only. When nobody else in the party can take the item (or the reward window could not hand out every
    /// item), the game's auto-sell happens as before.
    /// </summary>
    [HarmonyPatch]
    internal static class DuplicateLoot
    {
        [HarmonyPatch(typeof(CharacterController), "Action_GainItem", new[] { typeof(CharacterState), typeof(ItemDef), typeof(string) })]
        [HarmonyPrefix]
        private static bool OfferDuplicate(CharacterController __instance, CharacterState character, ItemDef item, string source)
        {
            if (source != "scenario_loot" || item == null || character == null) return true;
            if (!ModRules.IsOn(ModRules.ShareDuplicateLoot)) return true;
            try
            {
                if (__instance.CanHaveItem(character, item)) return true; // not a duplicate
                CampaignController campaign = __instance.CampaignController;
                CampaignState state = campaign.State;
                string what = $"{GiveRules.Name(character)} looted a second {item.Name} (item {item.ID.ID})";
                string why = WhyNotOffer(campaign, state, character, item);
                if (why != null)
                {
                    CoopLog.Write($"DUPLICATE LOOT sold: {what}: {why}");
                    return true;
                }
                state.AddUniqueUnresolvedAction(CampaignInteractionType.GainItem);
                state.ItemToGainIDs.Add(item.ID);
                CoopLog.Write($"DUPLICATE LOOT offered to the party: {what}");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Share duplicate loot: falling back to the game's auto-sell: " + e);
                return true;
            }
        }

        /// <summary>Null when the party's item reward window can take this item, else why not.</summary>
        private static string WhyNotOffer(CampaignController campaign, CampaignState state, CharacterState looter, ItemDef item)
        {
            if (!campaign.IsItemValid(item)) return "not a valid item in this game";
            if (campaign.CountItemInstancesInCharacters(item) >= item.Quantity) return "every copy is already owned";
            if (state.ItemToGainIDs.Contains(item.ID)) return "already waiting in the reward window";
            if (!state.PartyCharacters.Any(c => c != looter && campaign.CharacterController.CanHaveItem(c, item)))
                return "nobody else in the party can own it";
            // The reward window gives each character at most one item and must hand out all of them, so make sure the
            // game's own default split still works with this item added (other rewards may be waiting too).
            var items = state.ItemsToGain.Concat(new[] { item }).ToList();
            var split = state.GenerateDefaultGainItemDistirubtion(items, campaign);
            if (split == null || split.Count(d => d.Item != null) < items.Count) return "the reward window could not hand it out";
            return null;
        }
    }
}
