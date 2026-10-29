using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The Red Guard's Brilliant Aegis perk (Gloomhaven crossover sheet): "Whenever you are attacked, you may consume Light
    /// to gain Shield 1 and have attackers gain disadvantage against you until the end of the round." Asked like Leather
    /// Armor (the user's choice, 2026-10-29): when an enemy's attack targets her and Light is available, before the
    /// modifiers are drawn. Perks have no "when attacked" trigger, so the perk only leaves an inert marker bonus
    /// (BrilliantAegis, gh_crossover.py) and this offers it through the game's own defender prompt
    /// (GameState.ShowToggleActiveBonusSelection, which the game uses for defenders during monster turns; the answer travels
    /// to the other clients as a normal game action). Used: Light is consumed and she gets one Shield 1 bonus with
    /// Frosthaven's ShieldAndDisadvantage behaviour for the round, which counts for this attack too (the same steps as a
    /// shield item at this moment, GameState.ShowItemSelection). It runs before her items are offered, so Leather Armor
    /// isn't asked for nothing once she has disadvantage.
    /// </summary>
    [HarmonyPatch]
    internal static class GHBrilliantAegis
    {
        private const string Marker = "BrilliantAegis";
        private const string ShieldName = "BrilliantAegisShield";
        internal const string HelpKey = "FHMODS_AEGIS_HELPBOX";
        private static readonly ConditionalWeakTable<CAbility, HashSet<CActor>> Asked = new ConditionalWeakTable<CAbility, HashSet<CActor>>();
        private static volatile bool _prompting;

        [HarmonyPatch(typeof(GameState), nameof(GameState.ShowItemSelection))]
        [HarmonyPrefix]
        private static void BeforeItems(CActor caster, CAbility ability, CItem.EItemTrigger trigger, CVars Vars)
        {
            if (trigger != CItem.EItemTrigger.OnAttackedBeforeAttackModifiers || !GHClassesRule.On) return;
            try { Offer(caster as CPlayerActor, ability, Vars); }
            catch (Exception e) { Plugin.Log.LogError("Brilliant Aegis: " + e); }
        }

        private static void Offer(CPlayerActor guard, CAbility ability, CVars vars)
        {
            if (guard == null || guard.IsDead || vars == null || !(PhaseManager.Phase is CPhaseAction)) return;
            if (vars.Get("ActorToAttack").GetObject<CActor>() != guard) return;
            CAbilityAttack attack = vars.Get("ParentAttack").GetObject<CAbilityAttack>() ?? ability as CAbilityAttack;
            CActor foe = attack?.TargetingActor;
            if (foe == null || !CActor.AreActorsEnemies(foe.Type, guard.Type, foe, guard)) return;
            CActiveBonus aegis = guard.ActiveBonuses.FirstOrDefault(b => b.Caster == guard && b.Ability?.Name == Marker);
            const ElementInfusionBoardManager.EElement light = ElementInfusionBoardManager.EElement.Light;
            if (aegis == null || !ElementInfusionBoardManager.GetAvailableElements(guard).Contains(light)) return;
            if (!Asked.GetOrCreateValue(attack).Add(guard)) return;   // once for each attack on her
            bool used;
            _prompting = true;
            try { used = GameState.ShowToggleActiveBonusSelection(guard, guard, ability ?? attack, new List<CActiveBonus> { aegis }); }
            finally { _prompting = false; }
            if (!used)
            {
                Plugin.Log.LogInfo($"Brilliant Aegis: {guard.GetPrefabName()} let {foe.GetPrefabName()}'s attack through");
                return;
            }
            ElementInfusionBoardManager.Consume(light, guard);
            ScenarioRuleClient.MessageHandler(new CElementsConsumed_MessageData(null, guard)
            {
                m_ChangedElements = new List<ElementInfusionBoardManager.EElement> { light },
                m_Card = aegis.BaseCard
            });
            string name = ShieldName + guard.ActiveBonuses.Count(b => b.Ability?.Name != null && b.Ability.Name.StartsWith(ShieldName, StringComparison.Ordinal));
            CAbility shield = CAbilityShield.CreateDefaultShield(1, 1, 1, isMonster: false, new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Self), name);
            shield.ActiveBonusData.AbilityData = shield;
            shield.ActiveBonusData.Duration = CActiveBonus.EActiveBonusDurationType.Round;
            shield.ActiveBonusData.Behaviour = CActiveBonus.EActiveBonusBehaviourType.ShieldAndDisadvantage;
            shield.ActiveBonusData.Hidden = true;
            shield.Start(guard, guard);
            shield.ApplyToActor(guard);
            if (aegis.ToggledBonus) aegis.ToggleActiveBonusFromVars(guard, false);   // off again for the next attack
            CAttackSummary.TargetSummary summary = vars.Get("TargetSummary").GetObject<CAttackSummary.TargetSummary>()
                                                   ?? attack.AttackSummary?.FindTarget(guard);
            if (summary != null)
            {
                summary.CalcOverallAdvantageDisadvantage();
                summary.CacheFinalAttackStrength(initial: true, beforeModifiers: true);
            }
            Plugin.Log.LogInfo($"Brilliant Aegis: {guard.GetPrefabName()} consumed Light against {foe.GetPrefabName()}: Shield 1 and attackers' disadvantage until the round ends");
        }

        /// <summary>The prompt's help line names the perk (the game's default reads "&lt;name&gt; will receive Attack").</summary>
        [HarmonyPatch(typeof(Choreographer), "Handle_ActorWantsAnActiveBonusConfirmation")]
        [HarmonyPrefix]
        private static void HelpText(CMessageData message)
        {
            if (_prompting && message is CActorWantsAnActiveBonusConfirmation_MessageData data) data.m_HelpTextKeyOverride = HelpKey;
        }
    }
}
