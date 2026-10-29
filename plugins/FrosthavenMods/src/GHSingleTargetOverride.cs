using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A toggled single-target bonus that changes the attack (Behaviour: OverrideAbility with IsSingleTargetBonus: the
    /// Hatchet's The Favorite and The New Favorite: +3 Attack, and the Favorite goes to the target) applies to the target
    /// it's toggled on, as in Gloomhaven. Gloomhaven runs the bonus's override on the per-target copy of the attack when
    /// it's toggled (its ApplySingleTargetActiveBonus); Frosthaven dropped that call, and its per-target numbers
    /// (CAttackSummary.TargetSummary) only apply single-target items and songs. So the toggle did nothing: Power Pitch
    /// with the Favorite on still attacked at 6 and the mark never moved (the user's manual test, 2026-10-29). No
    /// Frosthaven card has such a bonus (its own single-target toggles are BuffSingleAttack); only pack cards are touched.
    /// The override is applied the way the same constructor applies a single-target item's overrides.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSingleTargetOverride
    {
        private static readonly AccessTools.FieldRef<CAttackSummary.TargetSummary, int> PreModifier =
            AccessTools.FieldRefAccess<CAttackSummary.TargetSummary, int>("m_PreModifierAttackStrength");
        private static readonly AccessTools.FieldRef<CAttackSummary.TargetSummary, List<SEventAttackModifier.OverrideBuffData>> LogData =
            AccessTools.FieldRefAccess<CAttackSummary.TargetSummary, List<SEventAttackModifier.OverrideBuffData>>("m_eventOverrideBuffData");
        private static readonly HashSet<string> Reported = new HashSet<string>();

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), MethodType.Constructor,
            new[] { typeof(CAttackSummary), typeof(CActor), typeof(CAbility), typeof(int) })]
        [HarmonyPostfix]
        private static void Apply(CAttackSummary.TargetSummary __instance, CActor target, CAbility ability)
        {
            try
            {
                if (ability?.ActiveSingleTargetActiveBonuses == null || !(__instance.AttackAbilityWithOverrides is CAbilityAttack copy)) return;
                foreach (CActiveBonus bonus in ability.ActiveSingleTargetActiveBonuses.Where(b => b.SingleTarget == target
                             && b.BaseCard != null && b.BaseCard.ID >= 20000
                             && b.HasBehaviour(CActiveBonus.EActiveBonusBehaviourType.OverrideAbility)).ToList())
                {
                    if (ability is CAbilityAttack attack && !bonus.IsValidAttackType(attack)) continue;   // the Favorite: ranged attacks only
                    List<CAbilityOverride> overrides = bonus.Ability?.ActiveBonusData?.ActiveBonusAbilityOverrides;
                    if (overrides == null || overrides.Count == 0) continue;
                    int before = copy.Strength;
                    var negative = new List<CCondition.ENegativeCondition>();
                    var positive = new List<CCondition.EPositiveCondition>();
                    foreach (CAbilityOverride o in overrides)
                    {
                        CAbilityOverride applied = copy.OverrideAbilityValues(o, perform: false, null, null, target);
                        if (applied?.NegativeConditions != null) negative.AddRange(applied.NegativeConditions);
                        if (applied?.PositiveConditions != null) positive.AddRange(applied.PositiveConditions);
                    }
                    int added = copy.Strength - before;
                    if (added > 0)
                    {
                        __instance.ItemStrengthBuffs.Add(added);
                        PreModifier(__instance) += added;
                    }
                    LogData(__instance)?.Add(new SEventAttackModifier.OverrideBuffData(SEventAttackModifier.OverrideBuffData.EOverrideBuffType.Item,
                        bonus.BaseCard.Name, added, negative, positive));
                    __instance.CacheFinalAttackStrength(initial: true, beforeModifiers: true);
                    if (Reported.Add(bonus.BaseCard.Name + " " + added))
                        Plugin.Log.LogInfo($"Gloomhaven single-target bonus: {bonus.BaseCard.Name} adds {added} to the attack on {target?.GetPrefabName()}");
                }
            }
            catch (Exception e) { if (Reported.Add("error")) Plugin.Log.LogError("Gloomhaven single-target bonus: " + e); }
        }
    }
}
