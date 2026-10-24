#if DEV
using System;
using System.Linq;
using System.Reflection;
using FHGUI;
using FHGUI.Services;
using FHRL;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrosthavenMods
{
    /// <summary>
    /// Test keys for the author's own install. Compiled in only by `dotnet build -c Release -p:Dev=true` (the DEV
    /// symbol, see the csproj); release builds never have them, and the dev build announces itself in the log.
    ///
    /// Ctrl+Shift+R, in the outpost with a character's sheet open, nobody else connected: that character's personal
    /// quest is marked complete with the game's own cheat (the one behind its console command complete_personal_quest,
    /// processed the same way so the quest-complete notification shows) and their retirement starts, exactly as the
    /// roster's Retire button would. From there the game's own screens follow: rewards, confirmation, resource
    /// hand-out, the story. Every press and every refusal is logged ("Dev keys:"), as is any other key pressed while
    /// Ctrl+Shift are held, so a key that does nothing can be traced in the BepInEx log.
    /// </summary>
    internal sealed class DevKeys : MonoBehaviour
    {
        internal const string Help = "Ctrl+Shift+R retires the selected character";

        private static readonly MethodInfo ProcessResult =
            AccessTools.Method(typeof(FHMapChoreographer), "ProcessResultFromCommand", new[] { typeof(CampaignController.Result), typeof(bool) });

        private bool _rDown;   // edge detection on the key's held state, independent of the input system's frame bookkeeping

        private void Start()
        {
            Keyboard k = Keyboard.current;
            Plugin.Log.LogInfo($"Dev keys: ready; keyboard {(k != null ? k.displayName : "NONE")}, input update mode {InputSystem.settings.updateMode}");
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k == null) return;
            bool chord = k.ctrlKey.isPressed && k.shiftKey.isPressed;
            bool r = chord && k.rKey.isPressed;
            bool pressed = r && !_rDown;
            _rDown = r;
            if (chord && !pressed && k.anyKey.wasPressedThisFrame)
            {
                string keys = string.Join("+", k.allKeys.Where(c => c != null && c.wasPressedThisFrame && !c.synthetic).Select(c => c.keyCode.ToString()));
                if (keys.Length > 0 && keys != "LeftCtrl" && keys != "RightCtrl" && keys != "LeftShift" && keys != "RightShift")
                    Plugin.Log.LogInfo($"Dev keys: Ctrl+Shift+{keys} (not a test key)");
            }
            if (!pressed) return;
            Plugin.Log.LogInfo("Dev keys: Ctrl+Shift+R");
            try { Retire(); }
            catch (Exception e) { Plugin.Log.LogError("Dev keys: retire failed: " + e); }
        }

        private static void Retire()
        {
            FHMapChoreographer map = Singleton<FHMapChoreographer>.InstanceFast;
            FHMapView view = map?.View;
            if (view == null || view.GameState == null || map.GameController == null) { Refuse(null, "not in the outpost"); return; }
            if (view.GameState.ActiveScenario != null) { Refuse(view, "not during a scenario"); return; }
            if (FFSNetwork.IsOnline && (!FFSNetwork.IsHost || FFSNet.PlayerRegistry.AllPlayers.Any(p => p != null && p.IsClient) || FFSNet.PlayerRegistry.OtherPlayersAreJoining()))
            {
                Refuse(view, "only while nobody else is connected (the quest cheat is not sent to other players)");
                return;
            }
            CharacterState c = view.SelectedCharacter;
            if (c == null) { Refuse(view, "open a character's sheet first"); return; }
            if (c.PersonalQuest == null) { Refuse(view, $"{GiveRules.Name(c)} has no personal quest"); return; }
            CharacterController characters = map.GameController.CharacterController;
            if (!characters.IsPersonalQuestCompleted(c))
            {
                if (ProcessResult == null) { Refuse(view, "the game's result processor is gone"); return; }
                ProcessResult.Invoke(null, new object[] { characters.Cheat_CompletePersonalQuest(c), false });
                Plugin.Log.LogInfo($"Dev keys: {GiveRules.Name(c)}'s personal quest marked complete");
            }
            if (c.PartyState != CharacterPartyStateType.RetireProcess)
            {
                view.GetService<RosterViewService>().StartCharacterRetirement(c.ID);
                Plugin.Log.LogInfo($"Dev keys: retirement started for {GiveRules.Name(c)} the {c.ClassID.ID}");
            }
            else Plugin.Log.LogInfo($"Dev keys: {GiveRules.Name(c)} is already retiring");
        }

        private static void Refuse(FHMapView view, string why)
        {
            Plugin.Log.LogWarning("Dev keys: retire refused: " + why);
            if (view != null) GiveResourcesPatches.Message(view, "Dev keys: " + why + ".");
        }
    }
}
#endif
