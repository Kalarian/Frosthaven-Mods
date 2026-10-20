using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FHGUI;
using FHGUI.Common.Elements;
using FHGUI.DTO;
using FHGUI.Elements;
using FHGUI.Services;
using FHGUI.States;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>What can be given. Herbs are not here: the digital game keeps herbs only in the shared Frosthaven supply
    /// (CharacterController.Action_AddResources throws for herbs), so they are already shared.</summary>
    public enum GiveKind { Gold = 0, Lumber = 1, Metal = 2, Hide = 3 }

    /// <summary>
    /// House rule "Give resources": in the outpost, a character can hand gold or materials to another character.
    ///
    /// Sync: outpost actions are named multiplayer commands that every machine replays (FHMapChoreographer.ExecuteCommand
    /// → host validates and broadcasts → clients apply or roll back). We add one command, "FrosthavenMods.GiveResources",
    /// resolved by a prefix on MultiplayerGameController.GetExectionContext (permission checks and execution both go
    /// through it). The command only reads campaign state, so every machine reaches the same verdict. The rule switch and
    /// character ownership are checked in the UI, like the game's own ownership checks (they are not campaign state).
    /// </summary>
    public sealed class GiveResourcesCommands
    {
        public const string CommandPath = "FrosthavenMods.GiveResources";

        private static readonly MethodInfo BeginResult = AccessTools.Method(typeof(CampaignController), "BeginResult");
        private static readonly MethodInfo EndResult = AccessTools.Method(typeof(CampaignController), "EndResult");
        private static readonly MethodInfo AddGold = AccessTools.Method(typeof(CharacterController), "Action_AddGold");
        private static readonly MethodInfo AddResources = AccessTools.Method(typeof(CharacterController), "Action_AddResources");

        private readonly MultiplayerGameController _game;

        public GiveResourcesCommands(MultiplayerGameController game) { _game = game; }

        [MultiplayerCommand(MultiplayerCommandExecType.Everybody)]
        public CampaignController.Result GiveResources(CharacterID from, CharacterID to, int kind, int amount)
        {
            CampaignController campaign = _game.CampaignController;
            campaign.CharacterController.TryGetCharacter(from, out CharacterState giver);
            campaign.CharacterController.TryGetCharacter(to, out CharacterState receiver);
            string why = GiveRules.WhyNot(giver, receiver, (GiveKind)kind, amount);
            if (why != null) throw new FHGameException("Give resources: " + why);

            // One result bracket, like SellItem/BuyItem: both sides change together.
            BeginResult.Invoke(campaign, null);
            if ((GiveKind)kind == GiveKind.Gold)
            {
                AddGold.Invoke(campaign.CharacterController, new object[] { giver, -amount });
                AddGold.Invoke(campaign.CharacterController, new object[] { receiver, amount });
            }
            else
            {
                var r = new OutpostResources((OutpostMaterialType)kind, amount);
                AddResources.Invoke(campaign.CharacterController, new object[] { giver, r * -1 });
                AddResources.Invoke(campaign.CharacterController, new object[] { receiver, r });
            }
            var result = (CampaignController.Result)EndResult.Invoke(campaign, null);
            Plugin.Log.LogInfo($"Give resources: {GiveRules.Name(giver)} gave {amount} {GiveRules.Label((GiveKind)kind)} to {GiveRules.Name(receiver)} " +
                               $"(now {GiveRules.Amount(giver, (GiveKind)kind)} / {GiveRules.Amount(receiver, (GiveKind)kind)})");
            return result;
        }
    }

    internal static class GiveRules
    {
        public static string Name(CharacterState c) => string.IsNullOrEmpty(c?.Name) ? c?.ClassID.ID?.Replace("ID", "") ?? "?" : c.Name;

        public static string Label(GiveKind k) => k == GiveKind.Gold ? "gold" : k.ToString().ToLowerInvariant();

        public static int Amount(CharacterState c, GiveKind k)
        {
            switch (k)
            {
                case GiveKind.Gold: return (int)c.Gold;
                case GiveKind.Lumber: return c.Resources.Lumber;
                case GiveKind.Metal: return c.Resources.Metal;
                case GiveKind.Hide: return c.Resources.Hide;
                default: return 0;
            }
        }

        /// <summary>In the party, on the bench (set aside), or still being created. Not retired.</summary>
        public static bool CanReceive(CharacterState c) =>
            c.PartyState == CharacterPartyStateType.InParty || c.PartyState == CharacterPartyStateType.Removed ||
            c.PartyState == CharacterPartyStateType.CreationProcess;

        /// <summary>Why the giver can't give anything at all right now (null if they can).</summary>
        public static string WhyCantGive(CharacterState giver)
        {
            if (giver.PartyState != CharacterPartyStateType.InParty) return Name(giver) + " must be in the party to give.";
            if (giver.ScenariosStarted < 1) return Name(giver) + " can give after playing their first scenario.";
            return null;
        }

        /// <summary>Null if the gift is allowed, else the reason. Reads campaign state only (same answer on every machine).</summary>
        public static string WhyNot(CharacterState giver, CharacterState receiver, GiveKind kind, int amount)
        {
            if (giver == null || receiver == null) return "unknown character.";
            if (!Enum.IsDefined(typeof(GiveKind), kind)) return "unknown resource.";
            if (giver == receiver) return "a character can't give to themself.";
            if (amount <= 0) return "the amount must be at least 1.";
            string cant = WhyCantGive(giver);
            if (cant != null) return cant;
            if (!CanReceive(receiver)) return Name(receiver) + " can't receive (retired?).";
            if (Amount(giver, kind) < amount) return Name(giver) + $" only has {Amount(giver, kind)} {Label(kind)}.";
            return null;
        }
    }

    [HarmonyPatch]
    internal static class GiveResourcesPatches
    {
        private static readonly ConditionalWeakTable<MultiplayerGameController, GiveResourcesCommands> Handlers = new ConditionalWeakTable<MultiplayerGameController, GiveResourcesCommands>();
        private static readonly MethodInfo CommandMethod = typeof(GiveResourcesCommands).GetMethod(nameof(GiveResourcesCommands.GiveResources));

        /// <summary>Resolve our command name. Bound to this controller instance (the host runs two: server and client copy).</summary>
        [HarmonyPatch(typeof(MultiplayerGameController), "GetExectionContext")]
        [HarmonyPrefix]
        private static bool ResolveCommand(MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            if (path != GiveResourcesCommands.CommandPath) return true;
            __result = (Handlers.GetValue(__instance, g => new GiveResourcesCommands(g)), CommandMethod);
            return false;
        }

        /// <summary>Party column: Ctrl+left-click a character's portrait to give from them. Wraps the portrait's own
        /// left-click (Select) action, so a plain click still selects. (Middle-click is taken: the game opens the
        /// character summary on it, outside these hint actions.)</summary>
        [HarmonyPatch(typeof(PartyViewService), nameof(PartyViewService.GetPartyColumnSlotInteractions))]
        [HarmonyPostfix]
        private static void AddGiveAction(PartySlotDTO partySlotDto, ref IEnumerable<UIPartySlotHints.HintedActionParams> __result)
        {
            try
            {
                if (partySlotDto == null || partySlotDto.IsEmpty || partySlotDto.InCreation || !ModRules.IsOn(ModRules.GiveResources)) return;
                FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
                if (view == null || view.GameState?.ActiveScenario != null) return;
                if (!view.IsSoloGame && !partySlotDto.Owned) return; // only the controlling player gives
                CharacterID id = partySlotDto.CharacterID;
                var actions = __result.ToList();
                foreach (var action in actions.Where(a => a.InteractionType == FHInputEventElement.UserInteractionType.LeftClick))
                {
                    Action select = action.ActionCallback;
                    action.ActionCallback = () =>
                    {
                        if (UnityEngine.InputSystem.Keyboard.current?.ctrlKey.isPressed == true) Safe("start", () => Start(view, id));
                        else select?.Invoke();
                    };
                }
                __result = actions;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Give resources: could not add the portrait action: " + e);
            }
        }

        private static CharacterState Find(FHMapView view, CharacterID id) =>
            view.GameController.CharacterController.TryGetCharacter(id, out CharacterState c) ? c : null;

        private static void Start(FHMapView view, CharacterID giverId)
        {
            CharacterState giver = Find(view, giverId);
            if (giver == null) return;
            string cant = GiveRules.WhyCantGive(giver);
            if (cant != null) { Message(view, cant); return; }
            if (GiveRules.Amount(giver, GiveKind.Gold) + giver.Resources.TotalMaterials <= 0) { Message(view, GiveRules.Name(giver) + " has no gold or materials to give."); return; }

            var items = view.GameState.AllCharacters
                .Where(c => c != giver && GiveRules.CanReceive(c))
                .OrderBy(c => c.PartyState == CharacterPartyStateType.InParty ? 0 : c.PartyState == CharacterPartyStateType.CreationProcess ? 1 : 2)
                .Select(c => new GenericListModalViewState.ListItem
                {
                    Item = c.ID,
                    Text = $"{GiveRules.Name(c)}  ({c.Gold} gold)" +
                           (c.PartyState == CharacterPartyStateType.Removed ? "  - not in party" :
                            c.PartyState == CharacterPartyStateType.CreationProcess ? "  - being created: unspent gold is lost when creation finishes" : ""),
                })
                .ToList();
            if (items.Count == 0) { Message(view, "There is nobody to give to."); return; }

            view.PushState(new GenericListModalViewState("Give resources", $"{GiveRules.Name(giver)} gives to:", items, -1, null, "Cancel",
                pick => { if (pick?.Item is CharacterID to) ChooseWhat(view, giverId, to); }, backgroundEnabled: true));
        }

        /// <summary>What to give: gold and the three materials with the giver's amounts (none = greyed out). Uses the
        /// game's list window: its ResourcePickerModalViewState has no callers in the game and does nothing when pushed.
        /// After each gift this reopens for the same recipient; "Done" closes it.</summary>
        private static void ChooseWhat(FHMapView view, CharacterID giverId, CharacterID receiverId)
        {
            Safe("choose what", () =>
            {
                CharacterState giver = Find(view, giverId), receiver = Find(view, receiverId);
                if (giver == null || receiver == null) { Plugin.Log.LogWarning("Give resources: character not found"); return; }
                var kinds = new[] { GiveKind.Gold, GiveKind.Lumber, GiveKind.Metal, GiveKind.Hide };
                if (kinds.All(k => GiveRules.Amount(giver, k) <= 0)) { Message(view, GiveRules.Name(giver) + " has nothing left to give."); return; }
                var items = kinds.Select(k => new GenericListModalViewState.ListItem
                {
                    Item = k,
                    Text = $"{char.ToUpperInvariant(GiveRules.Label(k)[0])}{GiveRules.Label(k).Substring(1)}  ({GiveRules.Amount(giver, k)})",
                    Enabled = GiveRules.Amount(giver, k) > 0,
                }).ToList();
                view.PushState(new GenericListModalViewState("Give resources",
                    $"What does {GiveRules.Name(giver)} give to {GiveRules.Name(receiver)}?", items, -1, null, "Done",
                    pick => { if (pick?.Item is GiveKind kind) Safe("choose amount", () => ChooseAmount(view, giverId, receiverId, kind)); }, backgroundEnabled: true, columns: 2));
            });
        }

        private static void Safe(string step, Action action)
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError($"Give resources: {step} failed: {e}"); }
        }

        private static readonly int[] Presets = { 1, 2, 3, 4, 5, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200, 250, 300, 400, 500, 750, 1000 };

        private static void ChooseAmount(FHMapView view, CharacterID giverId, CharacterID receiverId, GiveKind kind)
        {
            CharacterState giver = Find(view, giverId), receiver = Find(view, receiverId);
            if (giver == null || receiver == null) return;
            int have = GiveRules.Amount(giver, kind);
            string label = GiveRules.Label(kind);
            var items = Presets.Where(a => a < have)
                .Select(a => new GenericListModalViewState.ListItem { Item = a, Text = $"{a} {label}" })
                .Concat(new[] { new GenericListModalViewState.ListItem { Item = have, Text = $"All ({have} {label})" } })
                .ToList();
            view.PushState(new GenericListModalViewState("Give resources",
                $"How much {label} does {GiveRules.Name(giver)} give to {GiveRules.Name(receiver)}? ({have} available)", items, -1, null, "Cancel",
                pick =>
                {
                    if (pick?.Item is int amount && Give(view, giver, receiver, kind, amount))
                        ChooseWhat(view, giverId, receiverId); // give more to the same character
                }, backgroundEnabled: true, columns: 4));
        }

        private static bool Give(FHMapView view, CharacterState giver, CharacterState receiver, GiveKind kind, int amount)
        {
            string why = GiveRules.WhyNot(giver, receiver, kind, amount);
            if (why != null) { Message(view, why); return false; }
            try
            {
                view.Choreographer.ExecuteCommand(GiveResourcesCommands.CommandPath, giver.ID, receiver.ID, (int)kind, amount);
                view.RefreshView();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Give resources: command failed: " + e);
                Message(view, "Could not give: " + (e.InnerException ?? e).Message);
                return false;
            }
        }

        private static void Message(FHMapView view, string text) =>
            view.PushState(new GenericModalViewState(text, "Give resources", null, canCancel: false));
    }
}
