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
    public sealed partial class GiveResourcesCommands
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
            string copy = CoopLog.Copy(_game);
            string what = $"{GiveRules.Name(giver)} -> {GiveRules.Name(receiver)}: {amount} {GiveRules.Label((GiveKind)kind)}";
            string why = GiveRules.WhyNot(giver, receiver, (GiveKind)kind, amount);
            if (why != null)
            {
                CoopLog.Write($"GIVE REJECTED on={copy} {what} ({why})");
                throw new FHGameException("Give resources: " + why);
            }

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
            CoopLog.Write($"GIVE APPLIED on={copy} {what} (now {GiveRules.Amount(giver, (GiveKind)kind)} / {GiveRules.Amount(receiver, (GiveKind)kind)})");
            CoopLog.Snapshot("after give", _game);
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

        /// <summary>The party: in it, or being created into a party slot. (The bench was tried and dropped: the table
        /// only fits the party, and giving to the bench is rare.)</summary>
        public static bool CanReceive(CharacterState c) =>
            c.PartyState == CharacterPartyStateType.InParty || c.PartyState == CharacterPartyStateType.CreationProcess;

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
            if (!CanReceive(receiver)) return Name(receiver) + " is not in the party.";
            if (Amount(giver, kind) < amount) return Name(giver) + $" only has {Amount(giver, kind)} {Label(kind)}.";
            return null;
        }
    }

    [HarmonyPatch]
    internal static class GiveResourcesPatches
    {
        private static readonly ConditionalWeakTable<MultiplayerGameController, GiveResourcesCommands> Handlers = new ConditionalWeakTable<MultiplayerGameController, GiveResourcesCommands>();
        private static readonly Dictionary<string, MethodInfo> Commands = new Dictionary<string, MethodInfo>
        {
            [GiveResourcesCommands.CommandPath] = typeof(GiveResourcesCommands).GetMethod(nameof(GiveResourcesCommands.GiveResources)),
            [GiveResourcesCommands.ItemCommandPath] = typeof(GiveResourcesCommands).GetMethod(nameof(GiveResourcesCommands.GiveItem)),
        };

        /// <summary>Resolve our command names. Bound to this controller instance (the host runs two: server and client copy).</summary>
        [HarmonyPatch(typeof(MultiplayerGameController), "GetExectionContext")]
        [HarmonyPrefix]
        private static bool ResolveCommand(MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            if (path == null || !Commands.TryGetValue(path, out MethodInfo method)) return true;
            __result = (Handlers.GetValue(__instance, g => new GiveResourcesCommands(g)), method);
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
                if (partySlotDto == null || partySlotDto.IsEmpty || !ModRules.GiveOn) return;
                FHMapView view = Singleton<FHMapChoreographer>.InstanceFast?.View;
                if (view == null || view.GameState?.ActiveScenario != null) return;
                bool controlled = view.IsSoloGame || partySlotDto.Owned; // only the controlling player gives
                CharacterID id = partySlotDto.CharacterID;
                var actions = __result.ToList();
                foreach (var action in actions.Where(a => a.InteractionType == FHInputEventElement.UserInteractionType.LeftClick))
                {
                    Action select = action.ActionCallback;
                    action.ActionCallback = () =>
                    {
                        if (UnityEngine.InputSystem.Keyboard.current?.ctrlKey.isPressed == true)
                        {
                            if (controlled) Safe("start", () => Start(view, id));
                            else Message(view, "You can only give from characters you control.");
                        }
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

        internal static CharacterState Find(FHMapView view, CharacterID id) =>
            view.GameController.CharacterController.TryGetCharacter(id, out CharacterState c) ? c : null;

        internal static void StartFrom(FHMapView view, CharacterID giverId) => Safe("start", () => Start(view, giverId));

        private static void Start(FHMapView view, CharacterID giverId)
        {
            CharacterState giver = Find(view, giverId);
            if (giver == null) return;
            string cant = GiveRules.WhyCantGive(giver);
            if (cant != null) { Message(view, cant); return; }
            bool resources = ModRules.IsOn(ModRules.GiveResources), items = ModRules.IsOn(ModRules.ItemTrading);
            bool hasResources = resources && GiveResourcesViewState.Kinds.Any(k => GiveRules.Amount(giver, k) > 0);
            bool hasItems = items && ItemRules.Giveable(view.GameController, giver).Count > 0;
            if (!hasResources && !hasItems)
            {
                string what = resources && items ? "gold, materials or items" : resources ? "gold or materials" : "items";
                Message(view, GiveRules.Name(giver) + " has no " + what + " to give.");
                return;
            }
            var receivers = view.GameState.AllCharacters
                .Where(c => c != giver && (resources ? GiveRules.CanReceive(c) : ItemRules.CanReceive(c)))
                .OrderBy(c => c.PartyState == CharacterPartyStateType.InParty ? 0 : 1)
                .Select(c => c.ID)
                .ToList();
            if (receivers.Count == 0) { Message(view, "There is nobody else in the party to give to."); return; }
            // Opens on Resources, or on Items when there are no resources to give (or that rule is off).
            view.PushState(new GiveResourcesViewState(giverId, receivers, resources, items, startOnItems: !hasResources));
        }

        internal static void Safe(string step, Action action)
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError($"Give resources: {step} failed: {e}"); }
        }

        internal static bool Give(FHMapView view, CharacterState giver, CharacterState receiver, GiveKind kind, int amount)
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

        internal static bool GiveItem(FHMapView view, CharacterState giver, CharacterState receiver, ItemDef item)
        {
            string why = ItemRules.WhyNot(view.GameController, giver, receiver, item);
            if (why != null) { Message(view, why); return false; }
            try
            {
                view.Choreographer.ExecuteCommand(GiveResourcesCommands.ItemCommandPath, giver.ID, receiver.ID, (int)item.ID.ID);
                view.RefreshView();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Give item: command failed: " + e);
                Message(view, "Could not give: " + (e.InnerException ?? e).Message);
                return false;
            }
        }

        internal static void Message(FHMapView view, string text) =>
            view.PushState(new GenericModalViewState(text, "Give", null, canCancel: false));
    }
}
