using HarmonyLib;
using UnityEngine;

namespace FrosthavenMods
{
    /// <summary>
    /// Esc closes the Give resources window, like the game's other windows. Esc normally opens the pause menu
    /// (ESCMenu.Show), so: a per-frame check (InputFHManager.LateUpdate runs every frame in every scene) closes our window
    /// when Esc is pressed, and ESCMenu.Show is skipped while our window is open or in the frame it closed - whichever
    /// of the two sees the key press first.
    /// </summary>
    [HarmonyPatch]
    internal static class GiveEscape
    {
        private static int _closedFrame = -10;

        private static bool EscPressed() =>
            UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true;

        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Poll()
        {
            GiveResourcesViewState open = GiveResourcesViewState.Open;
            if (open == null || !EscPressed()) return;
            _closedFrame = Time.frameCount;
            GiveResourcesPatches.Safe("close with Esc", open.Close);
        }

        [HarmonyPatch(typeof(ESCMenu), nameof(ESCMenu.Show))]
        [HarmonyPrefix]
        private static bool KeepPauseMenuClosed()
        {
            GiveResourcesViewState open = GiveResourcesViewState.Open;
            if (open != null)
            {
                _closedFrame = Time.frameCount;
                GiveResourcesPatches.Safe("close with Esc", open.Close);
                return false;
            }
            return Time.frameCount - _closedFrame > 1;
        }
    }
}
