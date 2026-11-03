using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Abilities on the 2nd edition pack's attack modifier cards that Frosthaven's modifier code doesn't run.
    /// CAbilityAttack.ProcessModifierAbilities knows a fixed list of ability kinds (attack, heal, push, a granted
    /// ControlActor, ...); any other kind on a drawn card was "Unsupported attackAbility modifier ability type" and the
    /// game's error screen (2e passes, 2026-11-01: Cragheart's Rock Spray obstacle, Nightshroud's Tear, Sawbones' Medical
    /// Pack, Sunkeeper's recover). For a pack card (named AMC_GH2E_...), such an ability runs the way the same method runs
    /// its Kill / ControlActor kinds: after the attack, as the next ability of the figure that performs the card's
    /// abilities (the drawer, or its summoner with TryToPerformAsSummoner), with the attack's target as TargetActor (so a
    /// card's targeting can say "adjacent to the target"). Kinds: Create (obstacle), CreateShadow (Tear),
    /// GiveSupplyCard, RecoverDiscardedCards, Ward (the Safeguard carrier, GHSafeguard.cs), Bless / Invisible.
    /// Frosthaven's own cards are never touched. Logs "GHRule ModifierAbility card 0 {modifier} {kind}" per ability run.
    /// </summary>
    [HarmonyPatch]
    internal static class GHModifierAbilities
    {
        private static readonly HashSet<CAbility.EAbilityType> Stacked = new HashSet<CAbility.EAbilityType>
        {
            CAbility.EAbilityType.Create, CAbility.EAbilityType.CreateShadow, CAbility.EAbilityType.GiveSupplyCard,
            CAbility.EAbilityType.RecoverDiscardedCards, CAbility.EAbilityType.Ward, CAbility.EAbilityType.Bless,
            CAbility.EAbilityType.Invisible,
        };

        private static readonly FieldInfo AttackState = AccessTools.Field(typeof(CAbilityAttack), "m_State");

        public static bool IsPackCard(CAbilityAttack.AttackModifierAbilityInfo info) =>
            info?.Card?.Name != null && info.Card.Name.StartsWith("AMC_GH2E_", StringComparison.Ordinal);

        [HarmonyPatch(typeof(CAbilityAttack), "ProcessModifierAbilities")]
        [HarmonyPrefix]
        private static void Before(CAbilityAttack __instance, List<CAbilityAttack.AttackModifierAbilityInfo> modifierAbilities, CActor targetActor,
            CAttackSummary.TargetSummary targetSummary)
        {
            try
            {
                if (modifierAbilities == null || modifierAbilities.Count == 0) return;
                var ours = modifierAbilities.Where(i => IsPackCard(i) && i.Ability != null && Stacked.Contains(i.Ability.AbilityType)).ToList();
                if (ours.Count == 0) return;
                // all out of the engine's list first: one left in hits the engine's "Unsupported ... modifier ability type"
                foreach (CAbilityAttack.AttackModifierAbilityInfo info in ours) modifierAbilities.Remove(info);
                foreach (CAbilityAttack.AttackModifierAbilityInfo info in ours)
                {
                    try
                    {
                        if (__instance.TargetingActor == null || __instance.TargetingActor.IsMirrorActor()) continue;
                        CPhaseAction phase = PhaseManager.CurrentPhaseAction;
                        if (phase == null)
                        {
                            if (GHClasses.Reported.Add("modifier abilities no phase"))
                                Plugin.Log.LogWarning($"Gloomhaven modifier abilities: {info.Card?.Name} {info.Ability.AbilityType} drawn outside an action phase: skipped");
                            continue;
                        }
                        CAbility ability = CAbility.CopyAbility(info.Ability, generateNewID: false);
                        if (ability == null) continue;
                        if (targetSummary != null && ability.AddTargetSummaryContext(targetSummary).obj is CVars vars)
                        {
                            vars.Set("AttackSummaryModInfo", new TValue(info));
                            vars.Set("TargetActor", new TValue(targetActor));
                        }
                        ability.ParentAbility = __instance;
                        AttackState?.SetValue(__instance, CAbilityAttack.EAttackState.CheckForPlayerIdle);
                        CActor performer = info.TargetingActor ?? __instance.TargetingActor;
                        phase.StackNextAbilities(new List<CAbility> { ability }, performer, killAfter: false,
                            stackToNextCurrent: true, copyCurrentActionID: false, interruptCurrent: true);
                        Plugin.Log.LogInfo($"GHRule ModifierAbility card 0 {info.Card?.Name} {ability.AbilityType} by {performer?.GetPrefabName()} (target {targetActor?.GetPrefabName()})");
                    }
                    catch (Exception e) { string n = info?.Card?.Name ?? "?"; if (GHClasses.Reported.Add("modifier abilities " + n)) Plugin.Log.LogError("Gloomhaven modifier abilities (" + n + "): " + e); }
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("modifier abilities")) Plugin.Log.LogError("Gloomhaven modifier abilities: " + e); }
        }
    }
}
