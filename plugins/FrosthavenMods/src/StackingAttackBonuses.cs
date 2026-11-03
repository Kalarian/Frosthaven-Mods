using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Stacking attack bonuses" (on by default): every bonus that applies to an attack adds up, the physical
    /// game's rule (rulebook p.25). The engine builds each target's attack numbers in CAttackSummary.TargetSummary's
    /// constructor: active bonuses with a melee/ranged AttackType are each added, but the default-type ones (a plain
    /// "+X Attack", the most common kind) are folded with Math.Max, so only the largest counts (Single Out +3 with
    /// Crippling Poison +1 gave +3, Enhancement Field top and bottom together +1). With the rule on, that one Math.Max
    /// call adds the positive strengths instead; negative ones stay ignored, as the engine's fold from 0 ignores them.
    /// Every card is covered, Frosthaven's own included (the user's decision A6, 2026-11-02).
    /// Logs "GHRule StackingAttackBonuses" when the sum differs from the old maximum.
    /// </summary>
    [HarmonyPatch]
    internal static class StackingAttackBonuses
    {
        [ThreadStatic] private static int _depth;
        [ThreadStatic] private static int _max, _sum;
        [ThreadStatic] private static CActiveBonus _last;
        [ThreadStatic] private static List<CActiveBonus> _added;
        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static readonly MethodInfo MathMax = AccessTools.Method(typeof(Math), nameof(Math.Max), new[] { typeof(int), typeof(int) });
        private static readonly MethodInfo Reference = AccessTools.Method(typeof(CActiveBonus), nameof(CActiveBonus.ReferenceStrength));

        private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(CAttackSummary.TargetSummary),
            new[] { typeof(CAttackSummary), typeof(CActor), typeof(CAbility), typeof(int) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            List<CodeInstruction> list = code.ToList();
            int at = list.FindIndex(c => c.Calls(Reference));
            int swapped = 0;
            // the bonus whose strength is read: the instruction that loads the instance for ReferenceStrength (3 before the
            // call: bonus, ability, target). Passed to the fold so the log names the bonuses even if the ReferenceStrength
            // postfix below never ran (the bench of 2026-11-02 21:15 saw no StackingAttackBonuses line; an offline trace
            // of this transpiler on the game's IL, rest-b, shows the right Math.Max swapped and +1 +1 = +2).
            CodeInstruction loadBonus = at >= 3 && list[at - 3].IsLdloc() ? list[at - 3] : null;
            if (at >= 0)
                for (int i = at + 1; i < list.Count; i++)
                {
                    if (!list[i].Calls(MathMax)) continue;
                    if (loadBonus != null)
                    {
                        list.Insert(i, new CodeInstruction(loadBonus.opcode, loadBonus.operand));
                        list[i + 1].operand = AccessTools.Method(typeof(StackingAttackBonuses), nameof(FoldWith));
                    }
                    else list[i].operand = AccessTools.Method(typeof(StackingAttackBonuses), nameof(Fold));
                    swapped++;
                    break;
                }
            if (swapped == 0) Plugin.Log.LogWarning("Stacking attack bonuses: not applied (the game's code changed)");
            return list;
        }

        [HarmonyPrefix]
        private static void Before(out (int, int, CActiveBonus, List<CActiveBonus>) __state)
        {
            __state = (_max, _sum, _last, _added);
            _depth++;
            _max = 0; _sum = 0; _last = null; _added = null;
        }

        [HarmonyFinalizer]
        private static Exception After(Exception __exception, (int, int, CActiveBonus, List<CActiveBonus>) __state, CActor target, CAbility ability)
        {
            try
            {
                if (__exception == null && _sum != _max && _added != null)
                {
                    CActiveBonus top = _added.OrderByDescending(b => b.BaseCard?.ID >= 20000 ? 1 : 0).First();
                    string names = string.Join(" + ", _added.Select(b => b.BaseCard?.Name ?? "?"));
                    string key = $"{ability?.ID} {target?.ActorGuid} {_sum}";
                    if (Logged.Add(key))
                    {
                        if (Logged.Count > 2000) { Logged.Clear(); Logged.Add(key); }
                        Plugin.Log.LogInfo($"GHRule StackingAttackBonuses card {top.BaseCard?.ID ?? 0} +{_sum} instead of +{_max} on {target?.GetPrefabName()} ({names})");
                    }
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("stacking log")) Plugin.Log.LogWarning("Stacking attack bonuses: log: " + e); }
            _depth--;
            (_max, _sum, _last, _added) = __state;
            return __exception;
        }

        /// <summary>The bonus whose strength the constructor just read (the next Math.Max folds it).</summary>
        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ReferenceStrength))]
        private static class ReadBonus
        {
            private static void Postfix(CActiveBonus __instance)
            {
                if (_depth > 0) _last = __instance;
            }
        }

        private static int FoldWith(int acc, int value, CActiveBonus bonus)
        {
            if (bonus != null && _depth > 0) _last = bonus;
            return Fold(acc, value);
        }

        private static int Fold(int acc, int value)
        {
            if (_depth <= 0 || !ModRules.StackingAttackBonuses) return Math.Max(acc, value);
            if (value > 0)
            {
                _max = Math.Max(_max, value);
                _sum += value;
                if (_last != null) (_added = _added ?? new List<CActiveBonus>()).Add(_last);
                return acc + value;
            }
            return acc;
        }
    }
}
