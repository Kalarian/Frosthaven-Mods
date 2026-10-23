using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrosthavenDevTools
{
    /// <summary>
    /// Testing shortcuts for the author's own machine. NOT part of the mod pack (lives in devtools/, never packed).
    /// Solo games only. They call the game's own debug console commands, which ship in the retail build but have no
    /// console to type them into:
    ///   Ctrl+F4   unlock every Gloomhaven class tools/gh_port.py added to the ruleset (the Brute...) in the loaded
    ///             campaign, outpost only. THROWAWAY CAMPAIGNS ONLY: once a save has a class the game doesn't know,
    ///             Frosthaven without that data can't load it (and its error button moves the save to the corrupted
    ///             saves folder).
    ///   Ctrl+F5   write every event's Reveal event outcomes preview to <game>\FrosthavenMods\event-previews.txt
    ///   Ctrl+F6   draw the next town event of the current season (to test the mod's Reveal event outcomes)
    ///   Ctrl+F8   the next won scenario (Ctrl+F10) also completes every party member's uncompleted masteries: they're
    ///             added to the win's result, which the game then awards its normal way (to test the mod's Masteries
    ///             carry over). Topping up the in-scenario trackers doesn't work: the game recounts them from the
    ///             scenario's stats when it ends.
    ///   Ctrl+F9   +100 gold and +10 lumber/metal/hide per party member, +10 of each herb to the Frosthaven supply
    ///   Ctrl+F10  "win"            DebugMenu.WinScenarioCommand: wins the current scenario
    ///   Ctrl+F11  "skip_prologue"  FHMapChoreographer.SkipPrologue: ends the prologue (map only), lands in the outpost
    ///   Ctrl+F12  fake duplicate loot: the selected character "loots" a second copy of one of their items (one that
    ///             could really drop: not every copy owned, someone else could take it), as at the end of a
    ///             scenario. With the mod's Share duplicate loot rule on, the item reward window opens; off, it is sold.
    ///             (Test campaigns only: the copy is conjured, not taken from the supply.)
    /// The game destroys BepInEx's plugin object soon after startup (as in Stolen Realm), so the plugin's own Update
    /// never runs; the keys are polled from postfixes on the game's own per-frame methods instead.
    /// </summary>
    [BepInPlugin("frosthaven.devtools", "Frosthaven Dev Tools", "0.2.0")]
    public class DevTools : BaseUnityPlugin
    {
        internal static ManualLogSource _log;
        private static readonly MethodInfo Win = AccessTools.Method("DebugMenu:WinScenarioCommand");
        private static readonly MethodInfo SkipPrologue = AccessTools.Method("FHMapChoreographer:SkipPrologue");
        private static int _lastFrame = -1;
        private static string _listeningVia;

        private void Awake()
        {
            _log = Logger;
            var harmony = new Harmony("frosthaven.devtools");
            int hooks = 0;
            foreach (string target in new[] { "InputFHManager:LateUpdate", "Choreographer:Update" })
            {
                MethodInfo m = AccessTools.Method(target);
                if (m == null) { _log.LogWarning("Dev tools: no " + target + " to hook"); continue; }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(DevTools), nameof(Poll)));
                hooks++;
            }
            harmony.Patch(AccessTools.Method(typeof(FHRL.MultiplayerGameController), "GetExectionContext"),
                prefix: new HarmonyMethod(typeof(DevCommands), nameof(DevCommands.Resolve)));
            harmony.Patch(AccessTools.Method(typeof(FHRL.ScenarioController), nameof(FHRL.ScenarioController.CompleteActiveScenario)),
                prefix: new HarmonyMethod(typeof(DevCommands), nameof(DevCommands.AddMasteries)));
            _log.LogInfo($"Dev tools loaded (solo only, {hooks} frame hooks): Ctrl+F10 = win scenario [{(Win != null ? "ok" : "MISSING")}], " +
                         $"Ctrl+F11 = skip prologue [{(SkipPrologue != null ? "ok" : "MISSING")}]");
        }

        private static void Poll(MethodBase __originalMethod)
        {
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            if (_listeningVia == null)
            {
                _listeningVia = __originalMethod.DeclaringType?.Name;
                _log.LogInfo("Dev tools: listening for keys (via " + _listeningVia + ")");
            }
            if (DevCommands.ForceMasteries && DevCommands.ForcedFrame >= 0 && Time.frameCount > DevCommands.ForcedFrame)
            {
                DevCommands.ForceMasteries = false;
                DevCommands.ForcedFrame = -1;
            }
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed) return;
            if (kb.f4Key.wasPressedThisFrame) Run("unlock the Gloomhaven classes", UnlockGHClasses);
            if (kb.f5Key.wasPressedThisFrame) Run("write all event previews", DumpEventPreviews);
            if (kb.f6Key.wasPressedThisFrame) Run("draw a town event", DrawEvent);
            if (kb.f8Key.wasPressedThisFrame) Run("the next win completes every mastery", () => DevCommands.ForceMasteries = true);
            if (kb.f9Key.wasPressedThisFrame) Run("+100 gold, +10 of each material and herb", AddGold);
            if (kb.f10Key.wasPressedThisFrame) Run("win scenario", () => Win.Invoke(null, new object[] { null, -1 }));
            if (kb.f11Key.wasPressedThisFrame) Run("skip prologue", DoSkipPrologue);
            if (kb.f12Key.wasPressedThisFrame) Run("fake duplicate loot", FakeDuplicateLoot);
        }

        /// <summary>Ctrl+F9: 100 gold and 10 lumber/metal/hide to each party member, 10 of each herb to the Frosthaven
        /// supply (the digital game keeps herbs there, and materials on characters). One synced command of our own
        /// (resolved by a prefix on MultiplayerGameController.GetExectionContext, like the mod's GiveResources), so the
        /// host's checking copy sees it too - the give_gold console cheat edits only this machine's copy.</summary>
        private static void AddGold()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works in the outpost, not inside a scenario");
            map.ExecuteCommand(DevCommands.Path, 100, 10);
            map.View.RefreshView();
        }

        /// <summary>Ctrl+F4: the game's own unlock_class console command, for each Gloomhaven / Jaws of the Lion class the
        /// ruleset has (only ported ones are there). A local state edit like the console's: solo only.</summary>
        private static void UnlockGHClasses()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works in the outpost, not inside a scenario");
            var unlocked = new System.Collections.Generic.List<string>();
            foreach (FHRL.CharacterClassDef def in map.DefRepo.CharacterClasses.Values)
            {
                string model = def.ID.ID.EndsWith("ID") ? def.ID.ID.Substring(0, def.ID.ID.Length - 2) : def.ID.ID;
                if (!Enum.TryParse(model, out ScenarioRuleLibrary.ECharacter e) || e == ScenarioRuleLibrary.ECharacter.None ||
                    e >= ScenarioRuleLibrary.ECharacter.Infuser) continue;
                if (System.Linq.Enumerable.Any(map.GameState.UnlockedClassIDs, id => id == def.ID)) continue;
                map.GameState.UnlockedClassIDs.Add(def.ID);
                unlocked.Add(def.ID.ID);
            }
            string msg = unlocked.Count > 0
                ? "Unlocked " + string.Join(", ", unlocked) + ". Create the character from the party screen as usual. " +
                  "This campaign now needs the Gloomhaven class data to load: use it for testing only."
                : "No Gloomhaven classes to unlock: none are in the ruleset (run tools/gh_port.py and fh_mods.py install --gh), " +
                  "or they're already unlocked.";
            map.View.PushState(new FHGUI.States.GenericModalViewState(msg, "Dev tools: Gloomhaven classes", null, canCancel: false));
            map.View.RefreshView();
            _log.LogInfo("Dev tools: " + msg);
        }

        private static readonly MethodInfo PushActiveEvent = AccessTools.Method(typeof(FHGUI.FHMapView), "TryPushActiveEvent");

        private static void FakeDuplicateLoot()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works in the outpost, not inside a scenario");
            FHRL.CharacterState c = map.View.SelectedCharacter ?? throw new InvalidOperationException("select a character first");
            FHRL.CampaignController campaign = map.View.GameController;
            var owned = System.Linq.Enumerable.ToList(campaign.CharacterController.GetOwnedItems(c));
            // Only an item a second copy of could really drop (not every copy owned) that someone else could take.
            FHRL.ItemDef item = System.Linq.Enumerable.FirstOrDefault(owned, i =>
                (i.IsUnlimitedQuantity || campaign.CountItemInstancesInCharacters(i) < i.Quantity) &&
                System.Linq.Enumerable.Any(campaign.State.PartyCharacters, p => p != c && campaign.CharacterController.CanHaveItem(p, i)));
            if (item == null)
            {
                string why = owned.Count == 0
                    ? $"{c.Name} owns no items."
                    : $"Every copy of each of {c.Name}'s items is already owned (bench characters count), or nobody else in the party " +
                      "could take it, so no second copy could drop.\n\nBuy an item that's still in the shop, one nobody else in the " +
                      "party owns, and try again.";
                map.View.PushState(new FHGUI.States.GenericModalViewState(why, "Dev tools: fake duplicate loot", null, canCancel: false));
                _log.LogInfo("Dev tools: fake duplicate loot skipped: " + why);
                return;
            }
            _log.LogInfo($"Dev tools: {c.Name} 'loots' a second {item.Name} (item {item.ID.ID}).");
            map.ExecuteCommand(DevCommands.FakeLootPath, c.ID, (int)item.ID.ID);
            // The outpost shows pending rewards when it loads; ask it to look now.
            PushActiveEvent?.Invoke(map.View, new object[] { false });
            map.View.RefreshView();
        }

        /// <summary>Ctrl+F5: the mod's preview of every event option, for checking the wording (the mod does the work).
        /// Looked up on the key press: the dev tools load before the mod.</summary>
        private static void DumpEventPreviews()
        {
            MethodInfo DumpPreviews = AccessTools.Method("FrosthavenMods.EventPreview:DumpAll");
            if (DumpPreviews == null) throw new InvalidOperationException("the installed Frosthaven Mods has no event preview dump");
            _log.LogInfo("Dev tools: event previews written to " + DumpPreviews.Invoke(null, null));
        }

        private static void DrawEvent()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works in the outpost");
            if (map.GameController.OutpostEventController.HasEventToResolve) throw new InvalidOperationException("an event is already open");
            map.ExecuteCommand(DevCommands.DrawEventPath);
            PushActiveEvent?.Invoke(map.View, new object[] { false });
            map.View.RefreshView();
        }

        private static void DoSkipPrologue()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works on the map screen between missions, not inside a scenario");
            if (!map.GameState.IsInPrologue) throw new InvalidOperationException("the campaign is not in the prologue");
            SkipPrologue.Invoke(null, new object[] { null });
        }

        private static void Run(string what, Action action)
        {
            if (FFSNetwork.IsOnline) { _log.LogWarning($"Dev tools: '{what}' is for solo games only; ignored."); return; }
            try
            {
                action();
                _log.LogInfo($"Dev tools: {what} done.");
            }
            catch (Exception e)
            {
                _log.LogWarning($"Dev tools: {what} failed: {(e.InnerException ?? e).Message}");
            }
        }
    }

    /// <summary>The dev tools' own synced command (see AddGold).</summary>
    public sealed class DevCommands
    {
        public const string Path = "FrosthavenDevTools.Grant";
        public const string FakeLootPath = "FrosthavenDevTools.FakeDuplicateLoot";
        public const string DrawEventPath = "FrosthavenDevTools.DrawEvent";
        private static readonly MethodInfo ActivateOutpostEvent = AccessTools.Method(typeof(FHRL.OutpostEventController), "Action_ActivateEventForOutpost");
        private static readonly MethodInfo GainItem = AccessTools.Method(typeof(FHRL.CharacterController), "Action_GainItem",
            new[] { typeof(FHRL.CharacterState), typeof(FHRL.ItemDef), typeof(string) });
        private static readonly MethodInfo BeginResult = AccessTools.Method(typeof(FHRL.CampaignController), "BeginResult");
        private static readonly MethodInfo EndResult = AccessTools.Method(typeof(FHRL.CampaignController), "EndResult");
        private static readonly MethodInfo AddGold = AccessTools.Method(typeof(FHRL.CharacterController), "Action_AddGold");
        private static readonly MethodInfo AddResources = AccessTools.Method(typeof(FHRL.CharacterController), "Action_AddResources");
        private static readonly MethodInfo AddOutpostResources = AccessTools.Method(typeof(FHRL.OutpostController), "Action_AddResources");
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FHRL.MultiplayerGameController, DevCommands> Handlers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<FHRL.MultiplayerGameController, DevCommands>();
        private static readonly MethodInfo GrantMethod = typeof(DevCommands).GetMethod(nameof(Grant));
        private static readonly MethodInfo FakeLootMethod = typeof(DevCommands).GetMethod(nameof(FakeDuplicateLoot));
        private static readonly MethodInfo DrawEventMethod = typeof(DevCommands).GetMethod(nameof(DrawEvent));

        private readonly FHRL.MultiplayerGameController _game;
        public DevCommands(FHRL.MultiplayerGameController game) { _game = game; }

        /// <summary>Armed by Ctrl+F8 (solo only), used up by the next won scenario.</summary>
        internal static bool ForceMasteries;
        internal static int ForcedFrame = -1;

        /// <summary>Prefix on the win's campaign command: each party member's uncompleted masteries join the result, so
        /// the game checks and awards them (perk marks, completed list) as if earned in play.</summary>
        public static void AddMasteries(FHRL.ScenarioController __instance, FHRL.ScenarioResult result)
        {
            if (!ForceMasteries || result == null || !result.ScenarioWon || result.isRestarting) return;
            // disarmed by the next frame's key poll, not here: the command may run on more than one copy of the campaign
            ForcedFrame = Time.frameCount;
            FHRL.CampaignController campaign = __instance.CampaignController;
            foreach (FHRL.ScenarioResult.CharacterResult r in result.Characters)
            {
                FHRL.CharacterState c = r.GetCharacter(campaign.State);
                if (c == null) continue;
                foreach (FHRL.MasteryDef m in campaign.CharacterController.GetUncompletedMasteries(c))
                {
                    if (r.MasteriesCompleted.Contains(m.ID)) continue;
                    r.MasteriesCompleted.Add(m.ID);
                    DevTools._log.LogInfo($"Dev tools: {c.Name} completes {m.ID.ID} with this win.");
                }
            }
        }

        public static bool Resolve(FHRL.MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            MethodInfo method = path == Path ? GrantMethod : path == FakeLootPath ? FakeLootMethod : path == DrawEventPath ? DrawEventMethod : null;
            if (method == null) return true;
            __result = (Handlers.GetValue(__instance, g => new DevCommands(g)), method);
            return false;
        }

        [FHRL.MultiplayerCommand(FHRL.MultiplayerCommandExecType.Everybody)]
        public FHRL.CampaignController.Result Grant(int gold, int each)
        {
            FHRL.CampaignController campaign = _game.CampaignController;
            var materials = new FHRL.OutpostResources(FHRL.OutpostResources.AllMaterialTypes()) * each;
            var herbs = new FHRL.OutpostResources(FHRL.OutpostResources.AllHerbTypes()) * each;
            BeginResult.Invoke(campaign, null);
            foreach (FHRL.CharacterState c in System.Linq.Enumerable.ToList(campaign.State.PartyCharacters))
            {
                AddGold.Invoke(campaign.CharacterController, new object[] { c, gold });
                AddResources.Invoke(campaign.CharacterController, new object[] { c, materials });
            }
            AddOutpostResources.Invoke(campaign.OutpostController, new object[] { herbs });
            return (FHRL.CampaignController.Result)EndResult.Invoke(campaign, null);
        }

        [FHRL.MultiplayerCommand(FHRL.MultiplayerCommandExecType.Everybody)]
        public FHRL.CampaignController.Result FakeDuplicateLoot(FHRL.CharacterID id, int itemId)
        {
            FHRL.CampaignController campaign = _game.CampaignController;
            campaign.CharacterController.TryGetCharacter(id, out FHRL.CharacterState c);
            if (c == null || !campaign.DefRepository.Items.TryGetValue(new FHRL.ItemID((uint)itemId), out FHRL.ItemDef item))
                throw new FHRL.FHGameException("unknown character or item");
            BeginResult.Invoke(campaign, null);
            GainItem.Invoke(campaign.CharacterController, new object[] { c, item, "scenario_loot" });
            return (FHRL.CampaignController.Result)EndResult.Invoke(campaign, null);
        }

        [FHRL.MultiplayerCommand(FHRL.MultiplayerCommandExecType.Everybody)]
        public FHRL.CampaignController.Result DrawEvent()
        {
            FHRL.CampaignController campaign = _game.CampaignController;
            BeginResult.Invoke(campaign, null);
            ActivateOutpostEvent.Invoke(campaign.OutpostEventController, new object[] { !campaign.State.Outpost.Calendar.IsSummer });
            return (FHRL.CampaignController.Result)EndResult.Invoke(campaign, null);
        }
    }
}
