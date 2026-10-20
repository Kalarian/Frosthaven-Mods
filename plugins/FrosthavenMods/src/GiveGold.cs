using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FHGUI;
using FHGUI.Elements;
using FHGUI.Services;
using FHGUI.States;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Give gold": in the outpost, a character can hand some of their gold to another character.
    ///
    /// Sync: outpost actions are named multiplayer commands that every machine replays (FHMapChoreographer.ExecuteCommand
    /// → host validates and broadcasts → clients apply or roll back). We add one command, "FrosthavenMods.GiveGold",
    /// resolved by a prefix on MultiplayerGameController.GetExectionContext (permission checks and execution both go
    /// through it). The command only reads campaign state, so every machine reaches the same verdict. The rule switch and
    /// character ownership are checked in the UI, like the game's own ownership checks (they are not campaign state).
    /// </summary>
    public sealed class GiveGoldCommands
    {
        public const string CommandPath = "FrosthavenMods.GiveGold";

        private static readonly MethodInfo BeginResult = AccessTools.Method(typeof(CampaignController), "BeginResult");
        private static readonly MethodInfo EndResult = AccessTools.Method(typeof(CampaignController), "EndResult");
        private static readonly MethodInfo AddGold = AccessTools.Method(typeof(CharacterController), "Action_AddGold");

        private readonly MultiplayerGameController _game;

        public GiveGoldCommands(MultiplayerGameController game) { _game = game; }

        [MultiplayerCommand(MultiplayerCommandExecType.Everybody)]
        public CampaignController.Result GiveGold(CharacterID from, CharacterID to, int amount)
        {
            CampaignController campaign = _game.CampaignController;
            campaign.CharacterController.TryGetCharacter(from, out CharacterState giver);
            campaign.CharacterController.TryGetCharacter(to, out CharacterState receiver);
            string why = GiveGoldRules.WhyNot(giver, receiver, amount);
            if (why != null) throw new FHGameException("Give gold: " + why);

            // One result bracket, like SellItem/BuyItem: both sides change together.
            BeginResult.Invoke(campaign, null);
            AddGold.Invoke(campaign.CharacterController, new object[] { giver, -amount });
            AddGold.Invoke(campaign.CharacterController, new object[] { receiver, amount });
            var result = (CampaignController.Result)EndResult.Invoke(campaign, null);
            Plugin.Log.LogInfo($"Give gold: {Describe(giver)} gave {amount} gold to {Describe(receiver)} ({giver.Gold} / {receiver.Gold} left)");
            return result;
        }

        internal static string Describe(CharacterState c) => string.IsNullOrEmpty(c?.Name) ? c?.ClassID.ID?.Replace("ID", "") ?? "?" : c.Name;
    }

    internal static class GiveGoldRules
    {
        /// <summary>Null if the gift is allowed, else the reason. Reads campaign state only (same answer on every machine).</summary>
        public static string WhyNot(CharacterState giver, CharacterState receiver, int amount)
        {
            if (giver == null || receiver == null) return "unknown character.";
            if (giver == receiver) return "a character can't give gold to themself.";
            if (amount <= 0) return "the amount must be at least 1.";
            if (giver.PartyState != CharacterPartyStateType.InParty) return GiveGoldCommands.Describe(giver) + " must be in the party to give gold.";
            if (giver.ScenariosStarted < 1) return GiveGoldCommands.Describe(giver) + " can give gold after playing their first scenario.";
            if (receiver.PartyState != CharacterPartyStateType.InParty && receiver.PartyState != CharacterPartyStateType.CreationProcess)
                return GiveGoldCommands.Describe(receiver) + " is not in the party.";
            if (giver.Gold < amount) return GiveGoldCommands.Describe(giver) + $" only has {giver.Gold} gold.";
            return null;
        }

        public static bool CanReceive(CharacterState c) =>
            c.PartyState == CharacterPartyStateType.InParty || c.PartyState == CharacterPartyStateType.CreationProcess;
    }

    [HarmonyPatch]
    internal static class GiveGoldPatches
    {
        private static readonly ConditionalWeakTable<MultiplayerGameController, GiveGoldCommands> Handlers = new ConditionalWeakTable<MultiplayerGameController, GiveGoldCommands>();
        private static readonly MethodInfo GiveGoldMethod = typeof(GiveGoldCommands).GetMethod(nameof(GiveGoldCommands.GiveGold));
        private static readonly MethodInfo PopThisState = AccessTools.Method(typeof(FHMapViewState), "PopThisState");

        /// <summary>Resolve our command name. Bound to this controller instance (the host runs two: server and client copy).</summary>
        [HarmonyPatch(typeof(MultiplayerGameController), "GetExectionContext")]
        [HarmonyPrefix]
        private static bool ResolveCommand(MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            if (path != GiveGoldCommands.CommandPath) return true;
            __result = (Handlers.GetValue(__instance, g => new GiveGoldCommands(g)), GiveGoldMethod);
            return false;
        }

        // null = OnShow returned before setting up the header (invalid selection); true = the simple header with a Back
        // bar (prologue, or a character outside the party); false = the normal outpost screen, which has no button bar.
        private static bool? _simpleHeader;

        [HarmonyPatch(typeof(HeaderViewService), nameof(HeaderViewService.SetupCharacterDetailsHeader))]
        [HarmonyPostfix]
        private static void RememberHeaderMode(bool __result) => _simpleHeader = __result;

        [HarmonyPatch(typeof(CharacterEquipmentViewState), nameof(CharacterEquipmentViewState.OnShow))]
        [HarmonyPrefix]
        private static void ForgetHeaderMode() => _simpleHeader = null;

        /// <summary>Add a "Give gold" button to a character's Equipment screen in the outpost (next to Back when the game
        /// shows one; on its own otherwise).</summary>
        [HarmonyPatch(typeof(CharacterEquipmentViewState), nameof(CharacterEquipmentViewState.OnShow))]
        [HarmonyPostfix]
        private static void AddButton(CharacterEquipmentViewState __instance)
        {
            try
            {
                if (_simpleHeader == null || !ModRules.IsOn(ModRules.GiveGold)) return;
                FHMapView view = __instance.View;
                CharacterState giver = view?.SelectedCharacter;
                if (giver == null || view.GameState.ActiveScenario != null) return;
                if (!view.IsSoloGame && !view.PlayerCharacters.Contains(giver)) return; // only the controlling player
                var screen = view.Modules.Characters.MainScreen;
                var give = new ButtonsBarParam { LabelKey = "@Give gold", Callback = () => ChooseRecipient(view, giver) };
                if (_simpleHeader == true)
                    screen.SetButtonsBar(screen.Button_Back(() => PopThisState.Invoke(__instance, null)), give);
                else
                    screen.SetButtonsBar(give);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Give gold: could not add the button: " + e);
            }
        }

        private static void ChooseRecipient(FHMapView view, CharacterState giver)
        {
            if (giver.PartyState != CharacterPartyStateType.InParty) { Message(view, GiveGoldCommands.Describe(giver) + " must be in the party to give gold."); return; }
            if (giver.Gold == 0) { Message(view, GiveGoldCommands.Describe(giver) + " has no gold to give."); return; }
            if (giver.ScenariosStarted < 1) { Message(view, GiveGoldCommands.Describe(giver) + " can give gold after playing their first scenario."); return; }

            var items = view.GameState.AllCharacters
                .Where(c => c != giver && GiveGoldRules.CanReceive(c))
                .Select(c => new GenericListModalViewState.ListItem
                {
                    Item = c,
                    Text = GiveGoldCommands.Describe(c) + $"  ({c.Gold} gold)" +
                           (c.PartyState == CharacterPartyStateType.CreationProcess ? "  - being created: gold not spent before finishing is lost" : ""),
                })
                .ToList();
            if (items.Count == 0) { Message(view, "There is nobody else in the party to give gold to."); return; }

            view.PushState(new GenericListModalViewState("Give gold",
                $"{GiveGoldCommands.Describe(giver)} has {giver.Gold} gold. Give to:", items, -1, null, "Cancel",
                pick => { if (pick?.Item is CharacterState receiver) ChooseAmount(view, giver, receiver); }, backgroundEnabled: true));
        }

        private static readonly int[] Amounts = { 1, 2, 5, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200, 250, 300, 400, 500, 750, 1000 };

        private static void ChooseAmount(FHMapView view, CharacterState giver, CharacterState receiver)
        {
            int gold = (int)giver.Gold;
            var items = Amounts.Where(a => a < gold)
                .Select(a => new GenericListModalViewState.ListItem { Item = a, Text = a + " gold" })
                .Concat(new[] { new GenericListModalViewState.ListItem { Item = gold, Text = $"All ({gold} gold)" } })
                .ToList();
            view.PushState(new GenericListModalViewState("Give gold",
                $"How much gold does {GiveGoldCommands.Describe(giver)} give to {GiveGoldCommands.Describe(receiver)}?", items, -1, null, "Cancel",
                pick => { if (pick?.Item is int amount) Give(view, giver, receiver, amount); }, backgroundEnabled: true, columns: 4));
        }

        private static void Give(FHMapView view, CharacterState giver, CharacterState receiver, int amount)
        {
            string why = GiveGoldRules.WhyNot(giver, receiver, amount);
            if (why != null) { Message(view, why); return; }
            try
            {
                view.Choreographer.ExecuteCommand(GiveGoldCommands.CommandPath, giver.ID, receiver.ID, amount);
                view.RefreshView();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Give gold: command failed: " + e);
                Message(view, "The gold could not be given: " + (e.InnerException ?? e).Message);
            }
        }

        private static void Message(FHMapView view, string text) =>
            view.PushState(new GenericModalViewState(text, "Give gold", null, canCancel: false));
    }
}
