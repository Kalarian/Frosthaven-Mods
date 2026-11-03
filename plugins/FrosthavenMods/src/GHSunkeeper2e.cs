using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Sunkeeper's printed rules Frosthaven's abilities can't express. Each is switched on by a misc var on
    /// the pack card's ability, so nothing changes without the 2nd edition pack:
    ///   - GH2E_PierceReduction N (Defensive Stance bottom, card 33311, on its persistent Shield): "reduce the Pierce value
    ///     of all attacks targeting you by N". When an attack's numbers are built against the holder
    ///     (CAttackSummary.TargetSummary.CalcShieldPierce), its Pierce drops by N (not below 0, an infinite pierce stays)
    ///     and the shield left after the pierce is worked out again; Pierce added later by the attacker's modifier card is
    ///     reduced by what is left of the N. Logs "GHRule PierceReduction card 33311 ...".
    ///   - GH2E_WardBonus N (Sanctify top, card 33313, on its attack aura): "+1 Attack; if they have Ward, +2 Attack
    ///     instead": the aura's strength for an attack is N more while the attacking ally has Ward
    ///     (CAttackActiveBonus_BuffAttack.ReferenceStrength). Logs "GHRule SanctifyWard card 33313 ...".
    ///   - GH2E_AdjacentAttackerOnly (Purifying Aura top, card 33309, on its persistent Retaliate): "on the next five
    ///     attacks targeting you performed by an adjacent enemy": the bonus counts an attack (Retaliate and a use slot) only
    ///     when the attacker stands next to the holder, melee or ranged (CActiveBonus.IsValidAttackType, which the
    ///     Retaliate's trigger and strength both ask). Logs "GHRule PurifyingAura card 33309 ...".
    ///   - GH2E_HealSplash N (Beacon of Light top, card 33314, a persistent marker bonus with three use slots): "on your
    ///     next three single-target heal abilities from your ability cards, additionally target all allies within Range N
    ///     of the primary target". After such a heal (one target, no area, from an ability card) is applied to its primary
    ///     target, the same heal is applied to every ally (the Sunkeeper herself included) within N hexes of that target
    ///     with line of sight to it, and a use slot moves (its XP with it); the card is lost after the third.
    ///     Logs "GHRule HealSplash card 33314 ...".
    ///   - Divine Ascension (card 33329): its Flying is a persistent Fly move bonus (the engine makes the figure flying while
    ///     it holds one, CActor.RefreshFlyingFlag); the attack bonus ends both (EndAllActiveBonusesOnBaseCardSimultaneously,
    ///     data). Logs "GHRule DivineAscension card 33329 ..." when the card's bonuses end.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSunkeeper2e
    {
        internal const string PierceReductionTag = "GH2E_PierceReduction";
        internal const string WardBonusTag = "GH2E_WardBonus";
        internal const string AdjacentAttackerTag = "GH2E_AdjacentAttackerOnly";
        internal const string HealSplashTag = "GH2E_HealSplash";
        private const int DivineAscensionCard = 33329;

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static void Log(string rule, int card, string detail, string onceKey = null)
        {
            if (onceKey != null)
            {
                if (Logged.Count > 4000) Logged.Clear();
                if (!Logged.Add(rule + "|" + onceKey)) return;
            }
            Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");
        }

        private static int MiscInt(CAbility a, string tag)
        {
            try
            {
                if (a?.MiscVars == null) return 0;
                TValue v = a.GetMiscVar(tag);
                if (v.isUnknown) return 0;
                return v.isBool ? (v.GetBool() ? 1 : 0) : v.GetInt();
            }
            catch (Exception) { return 0; }
        }

        private static int CardId(CActiveBonus b) => b?.BaseCard?.ID ?? 0;

        // ------------------------------------------------------------------ Defensive Stance: pierce reduction

        // Pierce and ShieldMinusPierce have private setters
        private static void SetPierce(CAttackSummary.TargetSummary s, int v) => Traverse.Create(s).Property("Pierce").SetValue(v);
        private static void SetShieldMinusPierce(CAttackSummary.TargetSummary s, int v) => Traverse.Create(s).Property("ShieldMinusPierce").SetValue(v);
        private static readonly ConditionalWeakTable<CAttackSummary.TargetSummary, StrongBox<int>> Reduced =
            new ConditionalWeakTable<CAttackSummary.TargetSummary, StrongBox<int>>();

        private static (int amount, CActiveBonus bonus) Reduction(CActor defender)
        {
            if (defender == null) return (0, null);
            int sum = 0;
            CActiveBonus first = null;
            foreach (CActiveBonus b in defender.ActiveBonuses ?? new List<CActiveBonus>())
            {
                if (b?.Ability == null || b.Finished() || b.Finishing()) continue;
                int n = MiscInt(b.Ability, PierceReductionTag);
                if (n <= 0) continue;
                sum += n;
                if (first == null) first = b;
            }
            return (sum, first);
        }

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), nameof(CAttackSummary.TargetSummary.CalcShieldPierce))]
        [HarmonyPostfix]
        private static void ReducePierce(CAttackSummary.TargetSummary __instance)
        {
            try
            {
                if (!(__instance?.AttackAbilityWithOverrides is CAbilityAttack)) return;
                (int amount, CActiveBonus bonus) = Reduction(__instance.Actor);
                if (amount <= 0) return;
                int pierce = __instance.Pierce;
                int used = 0;
                if (pierce > 0 && pierce < 99999)
                {
                    used = Math.Min(amount, pierce);
                    SetPierce(__instance, pierce - used);
                    SetShieldMinusPierce(__instance, Math.Max(0, __instance.Shield - __instance.Pierce));
                }
                Reduced.Remove(__instance);
                Reduced.Add(__instance, new StrongBox<int>(used));
                if (used > 0)
                    Log("PierceReduction", CardId(bonus), $"Pierce {pierce} -> {pierce - used} on the attack targeting {__instance.Actor?.GetPrefabName()} (shield {__instance.Shield})",
                        $"{__instance.OriginalAbility?.ID}/{__instance.Actor?.ActorGuid}/{pierce}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("pierce reduction")) Plugin.Log.LogError("GHSunkeeper2e (pierce reduction): " + e); }
        }

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), nameof(CAttackSummary.TargetSummary.DrawAttackModifiers))]
        [HarmonyPrefix]
        private static void BeforeModifiers(CAttackSummary.TargetSummary __instance, out int __state) => __state = __instance?.Pierce ?? 0;

        [HarmonyPatch(typeof(CAttackSummary.TargetSummary), nameof(CAttackSummary.TargetSummary.DrawAttackModifiers))]
        [HarmonyPostfix]
        private static void ReduceModifierPierce(CAttackSummary.TargetSummary __instance, int __state)
        {
            try
            {
                if (__instance == null) return;
                int added = __instance.Pierce - __state;
                if (added <= 0 || __instance.Pierce >= 99999) return;
                (int amount, CActiveBonus bonus) = Reduction(__instance.Actor);
                if (amount <= 0) return;
                int used = Reduced.TryGetValue(__instance, out StrongBox<int> box) ? box.Value : 0;
                int cut = Math.Min(Math.Max(0, amount - used), added);
                if (cut <= 0) return;
                SetPierce(__instance, __instance.Pierce - cut);
                if (box != null) box.Value += cut;
                Log("PierceReduction", CardId(bonus), $"modifier Pierce {added} -> {added - cut} on the attack targeting {__instance.Actor?.GetPrefabName()}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("pierce reduction mod")) Plugin.Log.LogError("GHSunkeeper2e (modifier pierce): " + e); }
        }

        // ------------------------------------------------------------------ Sanctify: +1 more for an attacker with Ward

        [HarmonyPatch(typeof(CAttackActiveBonus_BuffAttack), nameof(CAttackActiveBonus_BuffAttack.ReferenceStrength))]
        [HarmonyPostfix]
        private static void WardBonus(CAttackActiveBonus_BuffAttack __instance, CAbility ability, CActor target, ref int __result)
        {
            try
            {
                if (__result <= 0 || ability == null) return;
                CActiveBonus bonus = __instance.ActiveBonus;
                int more = MiscInt(bonus?.Ability, WardBonusTag);
                if (more <= 0) return;
                CActor attacker = ability.TargetingActor;
                if (attacker == null || !attacker.Tokens.HasKey(CCondition.EPositiveCondition.Ward)) return;
                __result += more;
                Log("SanctifyWard", CardId(bonus), $"{attacker.GetPrefabName()} has Ward: +{__result} Attack instead of +{__result - more} on {target?.GetPrefabName()}",
                    $"{ability.ID}/{target?.ActorGuid}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("sanctify")) Plugin.Log.LogError("GHSunkeeper2e (Sanctify): " + e); }
        }

        // ------------------------------------------------------------------ Purifying Aura: adjacent attackers only

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.IsValidAttackType))]
        [HarmonyPostfix]
        private static void AdjacentOnly(CActiveBonus __instance, CAbilityAttack attackAbility, ref bool __result)
        {
            try
            {
                if (!__result || attackAbility == null || MiscInt(__instance?.Ability, AdjacentAttackerTag) <= 0) return;
                CActor attacker = attackAbility.TargetingActor, holder = __instance.Actor;
                if (attacker == null || holder == null) return;
                bool adjacent = ScenarioManager.IsTileAdjacent(attacker.ArrayIndex.X, attacker.ArrayIndex.Y, holder.ArrayIndex.X, holder.ArrayIndex.Y);
                if (!adjacent) __result = false;
                Log("PurifyingAura", CardId(__instance), $"{attacker.GetPrefabName()} attacks {holder.GetPrefabName()} from {(adjacent ? "an adjacent hex: counts" : "a distance: no Retaliate, no slot")}",
                    $"{attackAbility.ID}/{attacker.ActorGuid}/{adjacent}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("purifying aura")) Plugin.Log.LogError("GHSunkeeper2e (Purifying Aura): " + e); }
        }

        // ------------------------------------------------------------------ Beacon of Light: heal splash

        [ThreadStatic] private static bool _splashing;

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyPostfix]
        private static void HealSplash(CAbilityHeal __instance, CActor actor)
        {
            if (_splashing) return;
            try
            {
                CActor healer = __instance?.TargetingActor;
                if (!(healer is CPlayerActor) || actor == null) return;
                if (__instance.ActorsTargeted == null || __instance.ActorsTargeted.Count != 1) return;     // the primary target only
                CActiveBonus bonus = healer.ActiveBonuses?.FirstOrDefault(b => b?.Ability != null && !b.Finished() && !b.Finishing()
                    && b.Actor == healer && MiscInt(b.Ability, HealSplashTag) > 0 && (!b.HasTracker || b.Remaining > 0));
                if (bonus == null) return;
                if (__instance.NumberTargets != 1 || __instance.AreaEffect != null || __instance.AllTargets) return;
                CBaseCard card = __instance.GetAbilityBaseCard();
                if (!(card is CAbilityCard) || card.CardType != CBaseCard.ECardType.CharacterAbility) return;  // "from your ability cards"
                int range = MiscInt(bonus.Ability, HealSplashTag);
                var extra = ScenarioManager.Scenario.AllAliveActors.Where(x => x != null && !x.IsDead && x != actor
                        && !CActor.AreActorsEnemies(healer.Type, x.Type, healer, x)
                        && (x.Type == CActor.EType.Player || x.Type == CActor.EType.HeroSummon || x.Type == CActor.EType.Ally)
                        && Point.HexDist(actor.ArrayIndex, x.ArrayIndex) <= range && x.Tile != null && actor.Tile != null
                        && CActor.HaveLOS(actor.Tile, x.Tile))
                    .ToList();
                _splashing = true;
                try
                {
                    foreach (CActor x in extra) __instance.ApplyToActor(x);
                }
                finally { _splashing = false; }
                bonus.UpdateXPTracker();
                Log("HealSplash", CardId(bonus), $"{card.Name}'s heal on {actor.GetPrefabName()} also heals {(extra.Count == 0 ? "nobody (no ally within range " + range + ")" : string.Join(", ", extra.Select(x => x.GetPrefabName())))}; slot used ({bonus.Remaining} left)");
                bonus.FinishIfNoRemaining();
            }
            catch (Exception e) { if (GHClasses.Reported.Add("heal splash")) Plugin.Log.LogError("GHSunkeeper2e (Beacon of Light): " + e); }
        }

        // ------------------------------------------------------------------ Divine Ascension: the card's end

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.Finish))]
        [HarmonyPostfix]
        private static void Ended(CActiveBonus __instance)
        {
            try
            {
                if (CardId(__instance) != DivineAscensionCard) return;
                Log("DivineAscension", DivineAscensionCard, $"{__instance.Ability?.Name} on {__instance.Actor?.GetPrefabName()} ended (flying now {__instance.Actor?.FlyingAfterConditions})");
            }
            catch (Exception) { }
        }
    }
}
