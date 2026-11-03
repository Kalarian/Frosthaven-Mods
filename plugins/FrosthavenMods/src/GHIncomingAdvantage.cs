using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// "Gain advantage on all your attacks targeting this enemy" (Doomstalker 2e Dual Shot bottom's Doom; Expose bottom's
    /// Doom: "Your summons ... gain advantage"): a BuffIncomingAttacks bonus on the enemy with AttackHasAdvantage. The
    /// engine's CAttackSummary.TargetSummary.CalcAdvantage grants that advantage to ANY attacker, never checking the bonus's
    /// attacker filter (its Wound/+X parts do check it, in OnBeingAttackedPreDamage). Here, after CalcAdvantage: when the
    /// advantage came only from such incoming bonuses and every one of them is a pack bonus (card ID 20000-39999) whose
    /// filter rejects this attacker, the advantage is taken away. Frosthaven's own bonuses are untouched (a non-pack incoming
    /// advantage bonus keeps the engine's behaviour). Logs "GHRule IncomingAdvantageFilter card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHIncomingAdvantage
    {
        private static readonly MethodInfo IsValid = AccessTools.Method(typeof(CAttackActiveBonus_BuffIncomingAttacks), "IsValidTarget");
        private static readonly PropertyInfo AdvProp = AccessTools.Property(typeof(CAttackSummary.TargetSummary), nameof(CAttackSummary.TargetSummary.AttackersGainAdvantage));
        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static bool Pack(CActiveBonus b)
        {
            int id = 0;
            try { id = b?.BaseCard?.ID ?? 0; } catch { }
            return id >= 20000 && id < 40000;
        }

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), nameof(CAttackSummary.TargetSummary.CalcAdvantage))]
        [HarmonyPostfix]
        private static void Filter(CAttackSummary.TargetSummary __instance)
        {
            try
            {
                if (!__instance.AttackersGainAdvantage || IsValid == null || AdvProp == null) return;
                if (!(__instance.AttackAbilityWithOverrides is CAbilityAttack attack)) return;
                CActor target = __instance.Actor, attacker = attack.TargetingActor;
                if (target == null || attacker == null) return;
                List<CActiveBonus> incoming = CActiveBonus.FindApplicableActiveBonuses(target, CAbility.EAbilityType.Attack)
                    .Where(x => x != null && x.HasBehaviour<CAttackActiveBonus_BuffIncomingAttacks>() && x.Ability?.MiscAbilityData?.AttackHasAdvantage == true).ToList();
                if (incoming.Count == 0 || !incoming.All(Pack)) return;
                // the other sources CalcAdvantage adds: the attacker's own advantage bonuses and the attack's own advantage
                bool other = (__instance.ThisTargetAttackActiveBonuses ?? new List<CActiveBonus>()).Any(x => x.TryGetBehaviour(out CAttackActiveBonus_BuffAttack bh)
                                 && bh.ReferenceAdvantage(attack, target) && (!x.Ability.ActiveBonusData.IsSingleTargetBonus || x.SingleTarget == target))
                             || attack.MiscAbilityData?.AttackHasAdvantage == true;
                if (other) return;
                bool valid = incoming.Any(b => b.TryGetBehaviour(out CAttackActiveBonus_BuffIncomingAttacks bh)
                                               && (bool)IsValid.Invoke(bh, new object[] { attack, attacker, false }));
                if (valid) return;
                AdvProp.SetValue(__instance, false);
                string key = attack.ID + "/" + attacker.ActorGuid + "/" + target.ActorGuid;
                if (Logged.Add(key))
                {
                    if (Logged.Count > 300) { Logged.Clear(); Logged.Add(key); }
                    Plugin.Log.LogInfo($"GHRule IncomingAdvantageFilter card {incoming[0].BaseCard?.ID ?? 0} {GHPlague.NameOf(attacker)} attacks {GHPlague.NameOf(target)}: the Doom's advantage is not for this attacker");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("incoming advantage")) Plugin.Log.LogError("GHIncomingAdvantage: " + e); }
        }
    }
}
