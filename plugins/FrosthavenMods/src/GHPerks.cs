using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The crossover perks the game's perk data can't express (tools/gh_crossover.py marks them Passive with no
    /// abilities; this code looks for the perk's ID on the character):
    ///   Sunkeeper10, Shielding Light: whenever one of her heals would raise an ally's hit points above their maximum,
    ///     that ally gains Ward. Checked after each heal application: the hit points before, the heal's strength and
    ///     the maximum say whether it overflowed; the Ward is applied the way the game's own heals add conditions.
    ///   Sawbones09, Revitalizing Medicine: whenever an ally performs the heal of a Medical Pack or Large Medical Pack
    ///     (the Sawbones' supply cards, pack IDs 20417-20427), that character first loses one negative condition.
    ///     Done just before the heal applies, picking the worst condition the heal itself would not clear (stun,
    ///     disarm, immobilize, muddle, impair), while a Sawbones with the perk is alive in the scenario.
    /// </summary>
    [HarmonyPatch]
    internal static class GHPerks
    {
        internal const string ShieldingLight = "Sunkeeper10";
        internal const string RevitalizingMedicine = "Sawbones09";
        private const int FirstMedicalPack = 20417, LastMedicalPack = 20427;

        private static readonly CCondition.ENegativeCondition[] WorstFirst =
        {
            CCondition.ENegativeCondition.Stun, CCondition.ENegativeCondition.Disarm, CCondition.ENegativeCondition.Immobilize,
            CCondition.ENegativeCondition.Muddle, CCondition.ENegativeCondition.Impair,
        };

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyPrefix]
        private static void BeforeHeal(CAbilityHeal __instance, CActor actor, out int __state)
        {
            __state = actor != null ? actor.Health : 0;
            try { RemoveConditionFirst(__instance, actor); }
            catch (Exception e) { Report("Revitalizing Medicine", e); }
        }

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyPostfix]
        private static void AfterHeal(CAbilityHeal __instance, CActor actor, int __state)
        {
            try { WardOnOverheal(__instance, actor, __state); }
            catch (Exception e) { Report("Shielding Light", e); }
        }

        private static void WardOnOverheal(CAbilityHeal heal, CActor actor, int healthBefore)
        {
            var healer = heal?.TargetingActor as CPlayerActor;
            if (healer == null || actor == null || ReferenceEquals(actor, healer)) return;
            if (actor.Type != CActor.EType.Player && actor.Type != CActor.EType.HeroSummon && actor.Type != CActor.EType.Ally) return;
            if (!HasPerk(healer, ShieldingLight)) return;
            if (healthBefore + heal.Strength <= actor.MaxHealth) return;                       // no overflow
            if (actor.Tokens.HasKey(CCondition.EPositiveCondition.Ward)) return;
            CAbility ward = CAbility.CreateAbility(CAbility.EAbilityType.Ward, CAbilityFilterContainer.CreateDefaultFilter(), healer.IsOriginalMonsterType, true, 1);
            ward.Start(healer, heal.FilterActor);
            (ward as CAbilityTargeting)?.ApplyToActor(actor);
            Plugin.Log.LogInfo($"Shielding Light: {healer.CharacterClass?.ID}'s heal overflowed; {actor.ID} gains Ward");
        }

        private static void RemoveConditionFirst(CAbilityHeal heal, CActor actor)
        {
            if (heal == null || actor == null) return;
            CBaseCard card = heal.GetAbilityBaseCard();
            if (card == null || card.ID < FirstMedicalPack || card.ID > LastMedicalPack) return;
            var performer = heal.TargetingActor as CPlayerActor;
            if (performer == null || HasPerk(performer, RevitalizingMedicine)) return;        // "an ally performs": not the Sawbones herself
            CScenario scenario = ScenarioManager.Scenario;
            if (scenario == null || !scenario.AllAliveActors.OfType<CPlayerActor>().Any(p => HasPerk(p, RevitalizingMedicine))) return;
            foreach (CCondition.ENegativeCondition condition in WorstFirst)
            {
                if (!actor.Tokens.HasKey(condition)) continue;
                actor.RemoveNegativeConditionToken(condition, EConditionDecTrigger.None, performer);
                Plugin.Log.LogInfo($"Revitalizing Medicine: {actor.ID} loses {condition} before the medical pack's heal");
                return;
            }
        }

        private static bool HasPerk(CPlayerActor player, string perkId) =>
            player?.CharacterClass?.Perks != null && player.CharacterClass.Perks.Any(p => p != null && p.ID == perkId);

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string perk, Exception e)
        {
            if (Reported.Add(perk)) Plugin.Log.LogError($"Gloomhaven perks: {perk} failed: {e}");
        }
    }
}
