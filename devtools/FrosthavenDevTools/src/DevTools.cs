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
    ///   Ctrl+F9   +100 gold and +5 metal to each party member (synced CharacterGainGold/Metal effects)
    ///   Ctrl+F10  "win"            DebugMenu.WinScenarioCommand: wins the current scenario
    ///   Ctrl+F11  "skip_prologue"  FHMapChoreographer.SkipPrologue: ends the prologue (map only), lands in the outpost
    /// The game destroys BepInEx's plugin object soon after startup (as in Stolen Realm), so the plugin's own Update
    /// never runs; the keys are polled from postfixes on the game's own per-frame methods instead.
    /// </summary>
    [BepInPlugin("frosthaven.devtools", "Frosthaven Dev Tools", "0.1.3")]
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
            if (kb.f9Key.wasPressedThisFrame) Run("+100 gold and +5 metal to each party member", AddGold);
            if (kb.f10Key.wasPressedThisFrame) Run("win scenario", () => Win.Invoke(null, new object[] { null, -1 }));
            if (kb.f11Key.wasPressedThisFrame) Run("skip prologue", DoSkipPrologue);
        }

        /// <summary>Through the game's own synced command (CampaignController.ApplyEffect + CharacterGainGold), not the
        /// give_gold console cheat: that one edits only this machine's copy of the campaign, and the host's checking copy
        /// would then still see 0 gold and reject a Give gold test.</summary>
        private static void AddGold()
        {
            var map = Singleton<FHMapChoreographer>.InstanceFast;
            if (map == null || map.GameState == null) throw new InvalidOperationException("only works in the outpost, not inside a scenario");
            foreach (FHRL.CharacterState c in System.Linq.Enumerable.ToList(map.GameState.PartyCharacters))
            {
                map.ExecuteCommand(FHRPC.Campaign.ApplyEffect, new FHRL.CampaignEffect(FHRL.CampaignEffectType.CharacterGainGold, c.ClassID.ID, 100));
                map.ExecuteCommand(FHRPC.Campaign.ApplyEffect, new FHRL.CampaignEffect(FHRL.CampaignEffectType.CharacterGainMetal, c.ClassID.ID, 5));
            }
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
}
