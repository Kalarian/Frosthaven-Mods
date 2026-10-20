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
    ///   Ctrl+F9   +100 gold and +10 lumber/metal/hide per party member, +10 of each herb to the Frosthaven supply
    ///   Ctrl+F10  "win"            DebugMenu.WinScenarioCommand: wins the current scenario
    ///   Ctrl+F11  "skip_prologue"  FHMapChoreographer.SkipPrologue: ends the prologue (map only), lands in the outpost
    ///   Ctrl+F12  fake duplicate loot: the selected character "loots" a copy of their first item, as at the end of a
    ///             scenario. With the mod's Share duplicate loot rule on, the item reward window opens; off, it is sold.
    ///             (Test campaigns only: the copy is conjured, not taken from the supply.)
    /// The game destroys BepInEx's plugin object soon after startup (as in Stolen Realm), so the plugin's own Update
    /// never runs; the keys are polled from postfixes on the game's own per-frame methods instead.
    /// </summary>
    [BepInPlugin("frosthaven.devtools", "Frosthaven Dev Tools", "0.1.5")]
    public class DevTools : BaseUnityPlugin
    {
        private static ManualLogSource _log;
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
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed) return;
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

        private static readonly MethodInfo PushActiveEvent = AccessTools.Method(typeof(FHGUI.FHMapView), "TryPushActiveEvent");

        private static void FakeDuplicateLoot()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works in the outpost, not inside a scenario");
            FHRL.CharacterState c = map.View.SelectedCharacter ?? throw new InvalidOperationException("select a character first");
            map.ExecuteCommand(DevCommands.FakeLootPath, c.ID);
            // The outpost shows pending rewards when it loads; ask it to look now.
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

        private readonly FHRL.MultiplayerGameController _game;
        public DevCommands(FHRL.MultiplayerGameController game) { _game = game; }

        public static bool Resolve(FHRL.MultiplayerGameController __instance, string path, ref ValueTuple<object, MethodInfo> __result)
        {
            MethodInfo method = path == Path ? GrantMethod : path == FakeLootPath ? FakeLootMethod : null;
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
        public FHRL.CampaignController.Result FakeDuplicateLoot(FHRL.CharacterID id)
        {
            FHRL.CampaignController campaign = _game.CampaignController;
            campaign.CharacterController.TryGetCharacter(id, out FHRL.CharacterState c);
            FHRL.ItemDef item = c == null ? null : System.Linq.Enumerable.FirstOrDefault(campaign.CharacterController.GetOwnedItems(c));
            if (item == null) throw new FHRL.FHGameException("the selected character owns no items");
            BeginResult.Invoke(campaign, null);
            GainItem.Invoke(campaign.CharacterController, new object[] { c, item, "scenario_loot" });
            return (FHRL.CampaignController.Result)EndResult.Invoke(campaign, null);
        }
    }
}
