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
    ///   Sunkeeper10 (1st edition) / Sunkeeper211 (2nd edition), Shielding Light: whenever one of her heals would raise
    ///     an ally's hit points above their maximum, that ally gains Ward. Checked after each heal application: the hit
    ///     points before, the heal's strength and the maximum say whether it overflowed (for the 2nd edition perk a
    ///     poisoned target's heal only removes the Poison, so it never overflows); the Ward is applied the way the game's
    ///     own heals add conditions. The 2nd edition Lay on Hands (card 33305, "Heal X, X = the target's maximum hit
    ///     points", written as a heal to full) counts as a heal of the target's maximum hit points.
    ///     Logs "GHRule ShieldingLight card N ...".
    ///   Sawbones09, Revitalizing Medicine: whenever an ally performs the heal of a Medical Pack or Large Medical Pack
    ///     (the Sawbones' supply cards, pack IDs 20417-20427), that character first loses one negative condition.
    ///     Done just before the heal applies, picking the worst condition the heal itself would not clear (stun,
    ///     disarm, immobilize, muddle, impair), while a Sawbones with the perk is alive in the scenario.
    ///   Sawbones207 (2nd edition), Potent Remedies: +1 Heal on every heal ability of a 2nd edition Medical Pack (33230-
    ///     33235), whoever performs it, while a Sawbones with the perk is in the scenario. The +1 is added for the heal's
    ///     announcement and its application only (the ability object is reused). Logs "GHRule PotentRemedies card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHPerks
    {
        internal const string ShieldingLight = "Sunkeeper10";
        internal const string ShieldingLight2e = "Sunkeeper211";
        internal const string RevitalizingMedicine = "Sawbones09";
        internal const string PotentRemedies = "Sawbones207";
        private const int FirstMedicalPack = 20417, LastMedicalPack = 20427;
        internal const int FirstMedicalPack2e = 33230, LastMedicalPack2e = 33235;
        internal const int LayOnHands2e = 33305;

        private static readonly CCondition.ENegativeCondition[] WorstFirst =
        {
            CCondition.ENegativeCondition.Stun, CCondition.ENegativeCondition.Disarm, CCondition.ENegativeCondition.Immobilize,
            CCondition.ENegativeCondition.Muddle, CCondition.ENegativeCondition.Impair,
        };

        internal static bool IsMedicalPack2e(CAbility ability)
        {
            try
            {
                int id = ability?.GetAbilityBaseCard()?.ID ?? 0;
                return id >= FirstMedicalPack2e && id <= LastMedicalPack2e;
            }
            catch (Exception) { return false; }
        }

        internal static CPlayerActor WithPerk(string perkId) =>
            ScenarioManager.Scenario?.AllAliveActors?.OfType<CPlayerActor>().FirstOrDefault(p => HasPerk(p, perkId));

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyPrefix]
        private static void BeforeHeal(CAbilityHeal __instance, CActor actor, out (int health, bool poisoned, int plus) __state)
        {
            __state = (0, false, 0);
            try { __state = (actor != null ? actor.Health : 0, actor != null && actor.Tokens != null && actor.Tokens.HasKey(CCondition.ENegativeCondition.Poison), 0); }
            catch (Exception e) { Report("Shielding Light state", e); }
            try { RemoveConditionFirst(__instance, actor); }
            catch (Exception e) { Report("Revitalizing Medicine", e); }
            try { __state.plus = PotentPlus(__instance, actor, true); }
            catch (Exception e) { Report("Potent Remedies", e); }
        }

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyPostfix]
        private static void AfterHeal(CAbilityHeal __instance, CActor actor, (int health, bool poisoned, int plus) __state)
        {
            try { WardOnOverheal(__instance, actor, __state.health, __state.poisoned); }
            catch (Exception e) { Report("Shielding Light", e); }
        }

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyFinalizer]
        private static Exception HealDone(Exception __exception, CAbilityHeal __instance, (int health, bool poisoned, int plus) __state)
        {
            if (__state.plus != 0 && __instance != null) __instance.Strength -= __state.plus;
            return __exception;
        }

        // the heal's announcement (the numbers shown over the figures) builds its own strengths from Strength
        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ActorIsApplying))]
        [HarmonyPrefix]
        private static void BeforeAnnounce(CAbilityHeal __instance, out int __state)
        {
            __state = 0;
            try { __state = PotentPlus(__instance, null, false); }
            catch (Exception e) { Report("Potent Remedies", e); }
        }

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ActorIsApplying))]
        [HarmonyFinalizer]
        private static Exception AnnounceDone(Exception __exception, CAbilityHeal __instance, int __state)
        {
            if (__state != 0 && __instance != null) __instance.Strength -= __state;
            return __exception;
        }

        private static int PotentPlus(CAbilityHeal heal, CActor actor, bool log)
        {
            if (heal == null || !IsMedicalPack2e(heal)) return 0;
            CPlayerActor saw = WithPerk(PotentRemedies);
            if (saw == null) return 0;
            heal.Strength += 1;
            // the +1 must be reported back even if the log line throws, or the finalizer never takes it off again
            if (log)
                try { Plugin.Log.LogInfo($"GHRule PotentRemedies card {heal.GetAbilityBaseCard()?.ID} +1 Heal on the Medical Pack's heal ({heal.TargetingActor?.GetPrefabName()} heals {actor?.GetPrefabName()} for {heal.Strength}; perk of {saw.GetPrefabName()})"); }
                catch (Exception e) { Report("Potent Remedies log", e); }
            return 1;
        }

        private static void WardOnOverheal(CAbilityHeal heal, CActor actor, int healthBefore, bool wasPoisoned)
        {
            var healer = heal?.TargetingActor as CPlayerActor;
            if (healer == null || actor == null || ReferenceEquals(actor, healer)) return;
            if (actor.Type != CActor.EType.Player && actor.Type != CActor.EType.HeroSummon && actor.Type != CActor.EType.Ally) return;
            bool first = HasPerk(healer, ShieldingLight), second = HasPerk(healer, ShieldingLight2e);
            if (!first && !second) return;
            if (actor.IsDead || !ScenarioManager.Scenario.HasActor(actor)) return;
            if (second && wasPoisoned) return;                                                   // the heal only removed the Poison
            int strength = heal.Strength;
            int card = 0;
            try { card = heal.GetAbilityBaseCard()?.ID ?? 0; } catch (Exception) { }
            if (card == LayOnHands2e) strength = Math.Max(strength, actor.MaxHealth);           // "Heal X, X = the target's maximum"
            if (healthBefore + strength <= actor.MaxHealth) return;                              // no overflow
            if (actor.Tokens.HasKey(CCondition.EPositiveCondition.Ward)) return;
            CAbility ward = CAbility.CreateAbility(CAbility.EAbilityType.Ward, CAbilityFilterContainer.CreateDefaultFilter(), healer.IsOriginalMonsterType, true, 1);
            ward.Start(healer, heal.FilterActor);
            (ward as CAbilityTargeting)?.ApplyToActor(actor);
            Plugin.Log.LogInfo($"GHRule ShieldingLight card {card} {healer.CharacterClass?.ID}'s heal of {strength} on {actor.GetPrefabName()} ({healthBefore}/{actor.MaxHealth} hit points) overflowed; it gains Ward");
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

        internal static bool HasPerk(CPlayerActor player, string perkId) =>
            player?.CharacterClass?.Perks != null && player.CharacterClass.Perks.Any(p => p != null && p.ID == perkId);

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string perk, Exception e)
        {
            if (Reported.Add(perk)) Plugin.Log.LogError($"Gloomhaven perks: {perk} failed: {e}");
        }
    }
}
