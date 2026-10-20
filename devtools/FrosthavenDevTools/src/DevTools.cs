using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace FrosthavenDevTools
{
    /// <summary>
    /// Testing shortcuts for the author's own machine. NOT part of the mod pack (lives in devtools/, never packed).
    /// Solo games only. They call the game's own debug console commands, which ship in the retail build but have no
    /// console to type them into:
    ///   Ctrl+F10  "win"            DebugMenu.WinScenarioCommand: wins the current scenario
    ///   Ctrl+F11  "skip_prologue"  FHMapChoreographer.SkipPrologue: ends the prologue (map only), lands in the outpost
    /// </summary>
    [BepInPlugin("frosthaven.devtools", "Frosthaven Dev Tools", "0.1.0")]
    public class DevTools : BaseUnityPlugin
    {
        private static ManualLogSource _log;
        private static readonly MethodInfo Win = AccessTools.Method("DebugMenu:WinScenarioCommand");
        private static readonly MethodInfo SkipPrologue = AccessTools.Method("FHMapChoreographer:SkipPrologue");

        private void Awake()
        {
            _log = Logger;
            _log.LogInfo($"Dev tools loaded (solo only): Ctrl+F10 = win scenario [{(Win != null ? "ok" : "MISSING")}], " +
                         $"Ctrl+F11 = skip prologue [{(SkipPrologue != null ? "ok" : "MISSING")}]");
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed) return;
            if (kb.f10Key.wasPressedThisFrame) Run("win scenario", () => Win.Invoke(null, new object[] { null, -1 }));
            if (kb.f11Key.wasPressedThisFrame) Run("skip prologue", DoSkipPrologue);
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
