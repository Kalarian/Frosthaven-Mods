#if DEV
using System;
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
    /// Ctrl+Shift+R, in the outpost with a character's sheet open, solo game: that character's personal quest is marked
    /// complete with the game's own cheat (the one behind its console command complete_personal_quest, processed the
    /// same way so the quest-complete notification shows) and their retirement starts, exactly as the roster's Retire
    /// button would. From there the game's own screens follow: rewards, confirmation, resource hand-out, the story.
    /// </summary>
    internal sealed class DevKeys : MonoBehaviour
    {
        internal const string Help = "Ctrl+Shift+R retires the selected character";

        private static readonly MethodInfo ProcessResult =
            AccessTools.Method(typeof(FHMapChoreographer), "ProcessResultFromCommand", new[] { typeof(CampaignController.Result), typeof(bool) });

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k == null || !k.ctrlKey.isPressed || !k.shiftKey.isPressed || !k.rKey.wasPressedThisFrame) return;
            try { Retire(); }
            catch (Exception e) { Plugin.Log.LogError("Dev keys: retire failed: " + e); }
        }

        private static void Retire()
        {
            FHMapChoreographer map = Singleton<FHMapChoreographer>.InstanceFast;
            FHMapView view = map?.View;
            if (view == null || view.GameState == null || map.GameController == null)
            {
                Plugin.Log.LogWarning("Dev keys: retire asked outside the outpost, ignored");
                return;
            }
            if (view.GameState.ActiveScenario != null) { GiveResourcesPatches.Message(view, "Dev keys: not during a scenario."); return; }
            if (!view.IsSoloGame) { GiveResourcesPatches.Message(view, "Dev keys: solo games only (the quest cheat is not sent to other players)."); return; }
            CharacterState c = view.SelectedCharacter;
            if (c == null) { GiveResourcesPatches.Message(view, "Dev keys: open a character's sheet first."); return; }
            if (c.PersonalQuest == null) { GiveResourcesPatches.Message(view, $"Dev keys: {GiveRules.Name(c)} has no personal quest."); return; }
            CharacterController characters = map.GameController.CharacterController;
            if (!characters.IsPersonalQuestCompleted(c))
            {
                if (ProcessResult == null) { Plugin.Log.LogError("Dev keys: the game's result processor is gone"); return; }
                ProcessResult.Invoke(null, new object[] { characters.Cheat_CompletePersonalQuest(c), false });
                Plugin.Log.LogInfo($"Dev keys: {GiveRules.Name(c)}'s personal quest marked complete");
            }
            if (c.PartyState != CharacterPartyStateType.RetireProcess)
            {
                view.GetService<RosterViewService>().StartCharacterRetirement(c.ID);
                Plugin.Log.LogInfo($"Dev keys: retirement started for {GiveRules.Name(c)} the {c.ClassID.ID}");
            }
        }
    }
}
#endif
