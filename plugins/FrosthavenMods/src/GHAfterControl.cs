using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// An augment's "After each of your control abilities, you may perform: ..." (the 2nd edition Mindthief's Phantasmal
    /// Killer: consume Dark: one controlled enemy suffers damage 3). Frosthaven's augments only add abilities to attacks,
    /// so the card writes the follow-up as one more augment ability marked MiscVars GHAfterControl. When the card is read
    /// (AbilityData.GetAction, pack cards only) the marked ability is taken out of the augment's per-attack list and kept
    /// for that card. After every ControlActor ability whose performer holds that augment and that controlled at least
    /// one enemy (the ability ends after the controlled figure has acted: the control is interrupted for the commanded
    /// abilities and resumes), the follow-up is stacked for the performer, with the enemies that ability controlled in
    /// the performer's temp var GHControlledEnemies (the card's Custom targeting reads it). Logs
    /// "GHRule AfterControl card N". Only pack cards carry the marker: inert without the content packs.
    /// </summary>
    [HarmonyPatch]
    internal static class GHAfterControl
    {
        internal const string Marker = "GHAfterControl";
        internal const string ControlledVar = "GHControlledEnemies";
        private static readonly Dictionary<int, CAbility> Templates = new Dictionary<int, CAbility>();
        private static CAbility _lastEnded;

        private static bool Marked(CAbility a)
        {
            try { return a?.MiscVars != null && a.GetMiscVar(Marker).GetBool(); }
            catch { return false; }
        }

        [HarmonyTargetMethod]
        private static System.Reflection.MethodBase TargetMethod() => typeof(ScenarioRuleLibrary.YML.AbilityData)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .First(m => m.Name == "GetAction" && m.GetParameters().Any(p => p.ParameterType == typeof(CAction).MakeByRefType()));

        [HarmonyPostfix]
        private static void Read(int cardID, ref CAction action)
        {
            try
            {
                if (action?.Abilities == null || cardID < 20000 || cardID >= 40000) return;
                foreach (CAbility a in action.Abilities)
                {
                    List<CAbility> list = a?.Augment?.Abilities;
                    if (list == null) continue;
                    CAbility marked = list.FirstOrDefault(Marked);
                    if (marked == null) continue;
                    list.Remove(marked);
                    Templates[cardID] = marked;
                    Plugin.Log.LogInfo($"Gloomhaven after-control augment: card {cardID} keeps {marked.Name} for after each control ability");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHAfterControl (read): " + e.Message); }
        }

        /// <summary>Called from the AbilityEnded postfix (GHAfterControlEnded) for every ended ability.</summary>
        internal static void Ended(CAbility ability)
        {
            try
            {
                if (Templates.Count == 0 || !(ability is CAbilityControlActor control) || ReferenceEquals(ability, _lastEnded)) return;
                if (!control.AbilityHasHappened || control.ActorsTargeted == null) return;
                CActor me = control.TargetingActor;
                if (me?.Augments == null || me.Augments.Count == 0) return;
                CAugment augment = me.Augments.FirstOrDefault(x => x?.RegisteredBaseCard != null && Templates.ContainsKey(x.RegisteredBaseCard.ID));
                if (augment == null) return;
                var controlled = control.ActorsTargeted
                    .Where(x => x != null && !x.IsDead && ScenarioManager.Scenario.HasActor(x) && CActor.AreActorsEnemies(x.Type, me.Type, x, me))
                    .Distinct().Cast<object>().ToList();
                if (controlled.Count == 0) return;
                int cardId = augment.RegisteredBaseCard.ID;
                if (!ElementInfusionBoardManager.GetAvailableElements(me).Contains(ElementInfusionBoardManager.EElement.Dark))
                {
                    Plugin.Log.LogInfo($"GHRule AfterControl card {cardId} Dark not available after {control.Name}: nothing offered");
                    return;
                }
                if (!(PhaseManager.CurrentPhase is CPhaseAction phase)) return;
                _lastEnded = ability;
                me.SetTempVar(ControlledVar, new TValue(controlled));
                CAbility copy = CAbility.CopyAbility(Templates[cardId], generateNewID: false);
                phase.StackNextAbilities(new List<CAbility> { copy }, me, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: true);
                Plugin.Log.LogInfo($"GHRule AfterControl card {cardId} after {control.Name}: consume Dark for damage 3 on one of {controlled.Count} controlled enemies offered");
            }
            catch (Exception e) { Plugin.Log.LogWarning("GHAfterControl (ended): " + e.Message); }
        }
    }

    [HarmonyPatch]
    internal static class GHAfterControlEnded
    {
        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void Ended(CAbility __instance) => GHAfterControl.Ended(__instance);
    }
}
