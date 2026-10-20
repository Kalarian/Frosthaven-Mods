using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Item trading": in the outpost, a character can hand an item they own to another party member (the
    /// Items tab of the Give window). Same sync model as GiveResources: one named multiplayer command that every copy of
    /// the campaign replays and that checks campaign state only.
    ///
    /// The move uses the game's own steps: Action_RemoveItem (the selling path: unequips it, puts it back in the shop or
    /// craftable supply, re-checks small item slots) then Action_GainItem (the buying/crafting path: takes it out of the
    /// supply again). Net effect: the item changes hands and the supplies are unchanged. The receiver equips it when a
    /// slot is free, like a bought or looted item.
    /// </summary>
    public sealed partial class GiveResourcesCommands
    {
        public const string ItemCommandPath = "FrosthavenMods.GiveItem";

        private static readonly MethodInfo RemoveItem = AccessTools.Method(typeof(CharacterController), "Action_RemoveItem", new[] { typeof(CharacterState), typeof(ItemDef), typeof(string) });
        private static readonly MethodInfo GainItem = AccessTools.Method(typeof(CharacterController), "Action_GainItem", new[] { typeof(CharacterState), typeof(ItemDef), typeof(string) });
        private static readonly MethodInfo EquipItem = AccessTools.Method(typeof(CharacterController), "Action_EquipItem", new[] { typeof(CharacterState), typeof(ItemDef) });

        [MultiplayerCommand(MultiplayerCommandExecType.Everybody)]
        public CampaignController.Result GiveItem(CharacterID from, CharacterID to, int itemId)
        {
            CampaignController campaign = _game.CampaignController;
            CharacterController characters = campaign.CharacterController;
            characters.TryGetCharacter(from, out CharacterState giver);
            characters.TryGetCharacter(to, out CharacterState receiver);
            ItemDef item = ItemRules.Find(campaign, itemId);
            string copy = CoopLog.Copy(_game);
            string what = $"{GiveRules.Name(giver)} -> {GiveRules.Name(receiver)}: item {itemId} {item?.Name}";
            string why = ItemRules.WhyNot(campaign, giver, receiver, item);
            if (why != null)
            {
                CoopLog.Write($"GIVE ITEM REJECTED on={copy} {what} ({why})");
                throw new FHGameException("Give item: " + why);
            }

            BeginResult.Invoke(campaign, null);
            RemoveItem.Invoke(characters, new object[] { giver, item, null });
            GainItem.Invoke(characters, new object[] { receiver, item, null });
            bool equipped = characters.CanEquipItem(receiver, item);
            if (equipped) EquipItem.Invoke(characters, new object[] { receiver, item });
            var result = (CampaignController.Result)EndResult.Invoke(campaign, null);
            CoopLog.Write($"GIVE ITEM APPLIED on={copy} {what}{(equipped ? " (equipped)" : "")}");
            CoopLog.Snapshot("after give item", _game);
            return result;
        }
    }

    internal static class ItemRules
    {
        public static ItemDef Find(CampaignController campaign, int itemId) =>
            campaign.DefRepository.Items.TryGetValue(new ItemID((uint)itemId), out ItemDef item) ? item : null;

        /// <summary>Items a character can give: everything they own, worn or not.</summary>
        public static List<ItemDef> Giveable(CampaignController campaign, CharacterState giver) =>
            campaign.CharacterController.GetOwnedItems(giver).Distinct().OrderBy(i => i.ID.ID).ToList();

        /// <summary>Party members only (not one being created: a new character could otherwise buy with their starting
        /// gold and pass it all on).</summary>
        public static bool CanReceive(CharacterState c) => c.PartyState == CharacterPartyStateType.InParty;

        /// <summary>Null if the receiver can take this item, else why not (for the table and the command alike).</summary>
        public static string WhyCantReceive(CampaignController campaign, CharacterState receiver, ItemDef item)
        {
            if (!CanReceive(receiver)) return GiveRules.Name(receiver) + " is not in the party.";
            if (!campaign.CharacterController.CanHaveItem(receiver, item)) return GiveRules.Name(receiver) + " already owns " + item.Name + ".";
            return null;
        }

        /// <summary>Null if the gift is allowed, else the reason. Reads campaign state only (same answer on every machine).</summary>
        public static string WhyNot(CampaignController campaign, CharacterState giver, CharacterState receiver, ItemDef item)
        {
            if (giver == null || receiver == null) return "unknown character.";
            if (item == null) return "unknown item.";
            if (giver == receiver) return "a character can't give to themself.";
            if (campaign.State.ActiveScenario != null) return "items can only be given in the outpost.";
            string cant = GiveRules.WhyCantGive(giver);
            if (cant != null) return cant;
            if (!campaign.CharacterController.GetOwnedItems(giver).Contains(item)) return GiveRules.Name(giver) + " doesn't own " + item.Name + ".";
            return WhyCantReceive(campaign, receiver, item);
        }
    }
}
