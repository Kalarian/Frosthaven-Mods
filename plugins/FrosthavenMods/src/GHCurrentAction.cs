using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The card action being performed, as the action phase holds it. GameState.CurrentAction is NOT it: the engine sets it
    /// when a half is picked (GameState.PlayerSelectedAbilityCardAction: a Copy() of the card's action, same ID), and the
    /// CPhaseAction constructor moves it into its private m_CurrentAction and sets GameState.CurrentAction to null
    /// (CPhaseAction.cs, end of the constructor). So in a CPhaseAction constructor postfix, and in every ability / bonus
    /// hook while the action runs, GameState.CurrentAction is null (2026-11-03 sweep: Spare Parts, Shared Supplies between
    /// the actions, Watch it Burn before the second action, Spent Power, Warm Up, Etheric Echo and the Elementalist's
    /// two-consumes bonus never acted; harness sessions never logged them). Compare the result's Action with a card's
    /// TopAction / BottomAction by ID (it is a copy). Not a rule: no GHRule line.
    /// </summary>
    internal static class GHCurrentAction
    {
        private static readonly AccessTools.FieldRef<CPhaseAction, GameState.CurrentActorAction> PhaseCurrent =
            AccessTools.FieldRefAccess<CPhaseAction, GameState.CurrentActorAction>("m_CurrentAction");

        /// <summary>The action <paramref name="phase"/> performs (or, without one, the running action phase's; outside an
        /// action phase GameState.CurrentAction, which the engine sets just before it builds the phase).</summary>
        internal static GameState.CurrentActorAction Get(CPhaseAction phase = null)
        {
            try
            {
                CPhaseAction pa = phase ?? PhaseManager.CurrentPhaseAction;
                GameState.CurrentActorAction held = pa != null ? PhaseCurrent(pa) : null;
                return held ?? GameState.CurrentAction;
            }
            catch { return GameState.CurrentAction; }
        }

        /// <summary>Which half of <paramref name="card"/> <paramref name="action"/> is (a copy of): by ID.</summary>
        internal static bool IsTop(CAction action, CAbilityCard card) =>
            action != null && card?.TopAction != null && (ReferenceEquals(action, card.TopAction) || action.ID == card.TopAction.ID);

        internal static bool IsBottom(CAction action, CAbilityCard card) =>
            action != null && card?.BottomAction != null && (ReferenceEquals(action, card.BottomAction) || action.ID == card.BottomAction.ID);
    }
}
