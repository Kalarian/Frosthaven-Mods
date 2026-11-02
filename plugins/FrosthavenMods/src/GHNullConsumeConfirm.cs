using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A consume that turns the current step into nothing (the 1st edition Voidwarden's Withering Conviction, Freeze the
    /// Soul and Commanding Presence bottoms: "Curse yourself; consume Dark: don't"). Gloomhaven marks the override
    /// AllowContinueForNullAbility and lets the player go on; Frosthaven never reads that flag. On switching Dark on, the
    /// game shows "Confirm Action" for a moment and then leaves the ready button grey, the skip button grey too (a
    /// mandatory step): the turn can't go on (the user's manual tests 41 and 42, 2026-11-02; the harness stalled the same
    /// way, "ready True/False"). While a pack card's step that a consume has emptied is the current one (its card writes
    /// a real ability under that name, the running copy is Null) on the local player's turn, the ready button is kept lit
    /// as the game's own "Confirm Action": pressing it completes the step as usual (the element is taken then), and
    /// switching the consume off gives the step back, so the game sets the button itself again. Polled each frame from
    /// InputFHManager.LateUpdate (as the plugin's other per-frame checks). Only pack cards (IDs 20000-39999): inert
    /// without the content packs.
    /// </summary>
    [HarmonyPatch]
    internal static class GHNullConsumeConfirm
    {
        private static bool _reported;

        private static readonly System.Reflection.FieldInfo InnerButton = AccessTools.Field(typeof(ReadyButton), "readyButton");

        /// <summary>The current step is a pack card's step a consume emptied, on the local player's turn.</summary>
        private static bool Emptied(out CAbility ability, out CAbilityCard card)
        {
            ability = null; card = null;
            if (PhaseManager.PhaseType != CPhase.PhaseType.Action) return false;
            ability = PhaseManager.CurrentPhaseAction?.CurrentPhaseAbility?.m_Ability;
            if (ability == null || ability.AbilityType != CAbility.EAbilityType.Null) return false;
            if (!(ability.TargetingActor is CPlayerActor player) || !player.IsUnderMyControl()) return false;
            card = ability.GetAbilityBaseCard() as CAbilityCard;
            if (card == null || card.ID < 20000 || card.ID >= 40000) return false;
            // the card's own step of that name is a real ability: a consume emptied it (a step the card leaves empty on
            // purpose, the empty steps that carry consumes, is the game's own business)
            string name = ability.Name;
            return new[] { card.TopAction, card.BottomAction }.Where(a => a?.Abilities != null)
                .SelectMany(a => a.Abilities).Any(a => a != null && a.Name == name && a.AbilityType != CAbility.EAbilityType.Null);
        }

        // the game's ready button stays grey while "a cancelled ability is about to end" (CPhaseAction
        // AboutToEndACancelledAbility): it waits for the engine to end it, which never happens for a step the player
        // emptied mid-turn. For that step only, the button counts as usable (a click with no queued action is the game's
        // own ScenarioRuleClient.StepComplete for a Confirm state)
        [HarmonyPatch(typeof(ReadyButton), "CheckButtonInteractability", new[] { typeof(string) }, new[] { ArgumentType.Out })]
        [HarmonyPostfix]
        private static void Usable(ReadyButton __instance, ref bool __result)
        {
            try
            {
                if (__result || !Emptied(out _, out _)) return;
                if (InnerButton?.GetValue(__instance) is UnityEngine.UI.Button b && !b.interactable) b.interactable = true;
                __result = true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("GHNullConsumeConfirm: " + e.Message); }
        }

        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void Tick()
        {
            try
            {
                if (!Emptied(out CAbility ability, out CAbilityCard card)) return;
                ReadyButton ready = Choreographer.s_Choreographer?.readyButton;
                if (ready == null || ready.IsInteractable) return;
                ready.Toggle(active: true, ReadyButton.EButtonState.EREADYBUTTONCONFIRM,
                    GLOOM.LocalizationManager.GetTranslation("GUI_CONFIRM_ACTION"), hideOnClick: true, glowingEffect: true, interactable: true);
                ready.SetInteractable(true);
                if (!_reported)
                {
                    _reported = true;
                    Plugin.Log.LogInfo($"Gloomhaven consume that empties its step: {ability.Name} (card {card.ID}) can be confirmed and the turn goes on");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("GHNullConsumeConfirm: " + e.Message);
            }
        }
    }
}
