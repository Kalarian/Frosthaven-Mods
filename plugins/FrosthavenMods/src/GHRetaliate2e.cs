using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// Retaliate rules of the 2nd edition Berserker and Bruiser (2e card-accuracy pass, task rest-a), all marked on the
    /// pack's cards (MiscVars), so Frosthaven's own cards never change:
    ///   - GH2E_ProcXPOnce (Resolute Stand, Unbridled Power, Tireless Retribution, Eye for an Eye, Provoking Roar, Selfish
    ///     Retribution): "Gain XP 1 the next time you retaliate this round": the engine pays a retaliate bonus's ProcXP on
    ///     every retaliate (CActor.ApplyRetaliateToAttack asks CActiveBonus.ReferenceXP per used bonus); a marked bonus
    ///     pays only its first time.
    ///   - GH2E_RetaliatePerAttack (Careless Charge bottom): "Retaliate X for the attack, X = enemies adjacent to you when
    ///     the attack is performed": the bonus's strength is recounted at every ask (the engine kept the count from when
    ///     the card was played).
    ///   - GH2E_GrowingRage (Growing Rage top, a persistent marker): "Whenever you retaliate, perform Heal 1, self; if,
    ///     after the heal, your hit points are half your maximum or less, repeat once": after each of your retaliates.
    ///   - GH2E_SpikedArmorWound (Spiked Armor top's round retaliate): "Whenever an enemy suffers damage from your retaliate
    ///     this round, they gain Wound": after any of your retaliates that damaged the attacker (not only this card's).
    ///   - GH2E_RetaliateAfterDamage (Warding Strength bottom, Shield 1 with Frosthaven's ShieldAndRetaliate behaviour):
    ///     the Retaliate 1 and the use slot come after the attack's damage (OnBeingAttackedPostDamage) and only when the
    ///     attack made the Bruiser suffer damage (the approved reading: a 0-damage attack uses no slot), never when the
    ///     attack killed him; the engine did both before the damage, on any attack with a value above 0.
    ///   - Spiked Hide (Bruiser perk, AMC_GH2E_Bruiser_SpikedHide_P0_R): the drawn card goes "to the active area" as the
    ///     Bruiser's BruiserSpikedHide token (a character resource: saved, shown on his status row); on the next attack on
    ///     him by an adjacent enemy every held card gives Retaliate 2 for that attack and is discarded.
    /// Kills by retaliate are reported to GHMasteriesRestA (Berserker mastery 1). Every action logs "GHRule &lt;Rule&gt; card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHRetaliate2e
    {
        internal const int GrowingRageCard = 32612, SpikedArmorCard = 32626, CarelessChargeCard = 32623, WardingStrengthCard = 32007;
        internal const string SpikedHideCard = "AMC_GH2E_Bruiser_SpikedHide_P0_R", SpikedHideResource = "BruiserSpikedHide";

        internal static bool Marked(CAbility a, string key) => GHPlague.Marked(a, key);

        private static int CardOf(CActiveBonus b)
        {
            try { return b?.BaseCard?.ID ?? GHPlague.CardOf(b?.Ability); } catch { return 0; }
        }

        internal static string Name(CActor a) => GHPlague.NameOf(a);

        private static IEnumerable<CActiveBonus> Bonuses(CActor a, string marker) =>
            (a?.ActiveBonuses ?? new List<CActiveBonus>()).Where(b => b != null && !b.Finished() && Marked(b.Ability, marker));

        // ------------------------------------------------------------------ the retaliate scope

        [ThreadStatic] private static int s_Depth;
        [ThreadStatic] private static int s_LastCalc;
        [ThreadStatic] private static int s_SpikedHideUsed;
        [ThreadStatic] private static CActor s_SpikedHideHolder;

        private struct Scope { public bool On; public CActor Retaliator; public int AttackerHealth; public bool AttackerWasDead; public int? Override; }

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyRetaliateToAttack))]
        [HarmonyPrefix]
        private static void RetaliateStart(CActor __instance, CActor actorBeingAttacked, int? retaliateBuffOverride, out Scope __state)
        {
            s_Depth++;
            s_LastCalc = 0;
            s_SpikedHideUsed = 0;
            s_SpikedHideHolder = null;
            __state = new Scope { On = true, Retaliator = actorBeingAttacked, AttackerHealth = __instance.Health, AttackerWasDead = __instance.IsDead, Override = retaliateBuffOverride };
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyRetaliateToAttack))]
        [HarmonyPostfix]
        private static void RetaliateDone(CActor __instance, Scope __state)
        {
            if (!__state.On) return;
            try
            {
                CActor me = __state.Retaliator, attacker = __instance;
                if (s_SpikedHideUsed > 0 && s_SpikedHideHolder != null)
                {
                    s_SpikedHideHolder.RemoveCharacterResource(SpikedHideResource, s_SpikedHideUsed, updateWorldSpaceUI: true);
                    Plugin.Log.LogInfo($"GHRule SpikedHide card 0 perk Spiked Hide: {s_SpikedHideUsed} card(s) discarded for Retaliate {2 * s_SpikedHideUsed} against {Name(attacker)}");
                }
                bool retaliated = __state.Override.HasValue ? __state.Override.Value > 0 : s_LastCalc > 0;
                if (!retaliated || me == null) return;
                bool died = !__state.AttackerWasDead && (attacker.IsDead || attacker.Health <= 0);
                int dealt = died ? __state.AttackerHealth : Math.Max(0, __state.AttackerHealth - attacker.Health);
                AfterRetaliate(me, attacker, dealt, died);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra retaliate")) Plugin.Log.LogError("GHRetaliate2e: " + e); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyRetaliateToAttack))]
        [HarmonyFinalizer]
        private static Exception RetaliateEnd(Exception __exception)
        {
            s_Depth = Math.Max(0, s_Depth - 1);
            return __exception;
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.CalculateRetaliate), new[] { typeof(CAbility), typeof(int), typeof(List<CActiveBonus>) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
        [HarmonyPostfix]
        private static void Calculated(CActor __instance, CAbility ability, ref int __result)
        {
            if (s_Depth <= 0) return;
            try
            {
                // Spiked Hide: the held cards answer an attack by an adjacent enemy
                CActor attacker = ability?.TargetingActor;
                int held = __instance.GetCharacterResourceAmount(SpikedHideResource);
                if (held > 0 && attacker != null && s_SpikedHideHolder == null && __instance.IsAdjacentTo(attacker)
                    && CActor.AreActorsEnemies(attacker.Type, __instance.Type, attacker, __instance))
                {
                    __result += 2 * held;
                    s_SpikedHideUsed = held;
                    s_SpikedHideHolder = __instance;
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra spiked hide")) Plugin.Log.LogError("GHRetaliate2e (Spiked Hide): " + e); }
            s_LastCalc = __result;
        }

        private static void AfterRetaliate(CActor me, CActor attacker, int dealt, bool died)
        {
            // Growing Rage
            CActiveBonus rage = Bonuses(me, "GH2E_GrowingRage").FirstOrDefault();
            if (rage != null && !me.IsDead)
            {
                int card = CardOf(rage);
                int healed = HealSelf(me, 1);
                string more = "";
                if (me.Health <= me.MaxHealthHalfRoundedUp && !me.IsDead)
                {
                    int again = HealSelf(me, 1);
                    more = $"; at {me.Health - again} of {me.MaxHealth} (half or less) the heal is repeated: +{again}";
                }
                Plugin.Log.LogInfo($"GHRule GrowingRage card {(card != 0 ? card : GrowingRageCard)} {Name(me)} retaliated: Heal 1 (+{healed}){more}, now {me.Health}");
            }
            // Spiked Armor
            CActiveBonus spikes = Bonuses(me, "GH2E_SpikedArmorWound").FirstOrDefault();
            if (spikes != null && dealt > 0 && !died && !attacker.IsDead)
            {
                attacker.ApplyCondition(me, CCondition.ENegativeCondition.Wound);
                int card = CardOf(spikes);
                Plugin.Log.LogInfo($"GHRule SpikedArmor card {(card != 0 ? card : SpikedArmorCard)} {Name(attacker)} suffered {dealt} from {Name(me)}'s retaliate: gains Wound");
            }
            if (died) GHMasteriesRestA.NoteRetaliateKill(me, attacker);
        }

        /// <summary>A heal of the figure on itself with the heal rules (poison, wound), shown like a heal ability's.</summary>
        internal static int HealSelf(CActor a, int amount)
        {
            int before = a.Health;
            a.Healed(amount, null, a, out bool bane, out bool wound, out bool poison, out bool brittle, out int healed);
            ScenarioRuleClient.MessageHandler(new CActorBeenHealed_MessageData(a, a, poison ? 0 : healed, before, poison, wound, bane, brittle));
            return Math.Max(0, a.Health - before);
        }

        // ------------------------------------------------------------------ XP once

        private static readonly ConditionalWeakTable<CActiveBonus, object> XPPaid = new ConditionalWeakTable<CActiveBonus, object>();

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ReferenceXP))]
        [HarmonyPostfix]
        private static void XPOnce(CActiveBonus __instance, ref int __result)
        {
            if (s_Depth <= 0 || __result <= 0) return;
            try
            {
                if (!Marked(__instance.Ability, "GH2E_ProcXPOnce")) return;
                if (XPPaid.TryGetValue(__instance, out _)) { __result = 0; return; }
                XPPaid.Add(__instance, new object());
                Plugin.Log.LogInfo($"GHRule RetaliateXPOnce card {CardOf(__instance)} {Name(__instance.Actor)} retaliated: XP {__result} (only this once this round)");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra xp once")) Plugin.Log.LogError("GHRetaliate2e (XP once): " + e); }
        }

        // ------------------------------------------------------------------ Careless Charge bottom

        private static readonly HashSet<string> PerAttackLogged = new HashSet<string>();

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ReferenceStrength))]
        [HarmonyPostfix]
        private static void RetaliatePerAttack(CActiveBonus __instance, CAbility ability, ref int __result)
        {
            try
            {
                if (!(__instance.Ability is CAbilityRetaliate) || !Marked(__instance.Ability, "GH2E_RetaliatePerAttack")) return;
                CActor me = __instance.Actor;
                if (me == null || me.IsDead) return;
                int n = ScenarioManager.Scenario.AllAliveActors.Count(x => x != null && x != me && !x.IsDead && me.IsAdjacentTo(x)
                    && CActor.AreActorsEnemies(x.Type, me.Type, x, me));
                __result = n;
                if (s_Depth > 0 && ability?.TargetingActor != null)
                {
                    string key = ability.ID + "/" + me.ActorGuid;
                    if (PerAttackLogged.Add(key))
                    {
                        if (PerAttackLogged.Count > 500) { PerAttackLogged.Clear(); PerAttackLogged.Add(key); }
                        Plugin.Log.LogInfo($"GHRule RetaliatePerAttack card {CarelessChargeCard} {Name(me)} attacked by {Name(ability.TargetingActor)}: Retaliate {n} ({n} adjacent enemies now)");
                    }
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra per attack")) Plugin.Log.LogError("GHRetaliate2e (per attack): " + e); }
        }

        // ------------------------------------------------------------------ Warding Strength bottom

        [HarmonyPatch(typeof(CShieldActiveBonus_ShieldAndRetaliate), nameof(CShieldActiveBonus_ShieldAndRetaliate.OnBeingAttacked))]
        [HarmonyPrefix]
        private static bool NotBeforeDamage(CShieldActiveBonus_ShieldAndRetaliate __instance)
        {
            try { return !Marked(__instance.ActiveBonus?.Ability, "GH2E_RetaliateAfterDamage"); }
            catch { return true; }
        }

        [HarmonyPatch(typeof(CBespokeBehaviour), nameof(CBespokeBehaviour.OnBeingAttackedPostDamage))]
        [HarmonyPostfix]
        private static void AfterDamage(CBespokeBehaviour __instance, CAbilityAttack attackAbility, CAttackSummary.TargetSummary targetSummary)
        {
            try
            {
                if (!(__instance is CShieldActiveBonus_ShieldAndRetaliate shield)) return;
                CActiveBonus b = shield.ActiveBonus;
                if (b == null || b.Finished() || !Marked(b.Ability, "GH2E_RetaliateAfterDamage")) return;
                CActor me = b.Actor, attacker = attackAbility?.TargetingActor;
                if (me == null || attacker == null || targetSummary == null || targetSummary.Actor != me) return;
                int card = CardOf(b);
                if (me.IsDead || me.Health <= 0)
                {
                    Plugin.Log.LogInfo($"GHRule WardingStrength card {card} {Name(me)} fell to {Name(attacker)}'s attack: no retaliate, no slot used");
                    return;
                }
                if (targetSummary.ActualDamageTaken <= 0)
                {
                    Plugin.Log.LogInfo($"GHRule WardingStrength card {card} {Name(attacker)}'s attack made {Name(me)} suffer no damage: no retaliate, no slot used");
                    return;
                }
                shield.Retaliate(attacker, attackAbility);
                b.RestrictActiveBonus(attacker);
                if (b.HasTracker)
                {
                    shield.OnBehaviourTriggered();
                    b.UpdateXPTracker();
                    b.FinishIfNoRemaining();
                }
                Plugin.Log.LogInfo($"GHRule WardingStrength card {card} {Name(me)} suffered {targetSummary.ActualDamageTaken} from {Name(attacker)}: Retaliate 1 after the damage, slot used ({(b.HasTracker ? b.Remaining : 0)} left)");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra warding")) Plugin.Log.LogError("GHRetaliate2e (Warding Strength): " + e); }
        }
    }
}
