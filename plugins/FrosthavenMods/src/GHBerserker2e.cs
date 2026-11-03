using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Berserker's card rules Frosthaven's engine doesn't have (2e card-accuracy pass, task rest-a). Each
    /// is marked on the pack's cards (MiscVars) or gated on the pack's card / class IDs; Frosthaven's own cards never change.
    ///   - GH2E_SetHP / GH2E_SetHPHalf (Glass Hammer top "set your hit point value to 1", From the Brink top "to half your
    ///     maximum, rounded up"): at the end of the marked ability the hit points are set, which is not suffering damage
    ///     and not a heal (the approved reading): no damage triggers (Bathe in Blood, Vengeful Barrage), no prevents
    ///     (Spiked Armor's negate, Numb the Pain), no heal rules (poison, wound). Frosthaven's SetHPTo was damage.
    ///   - Final Fight top (card 32615): "When this card is moved from your active area to your lost pile, you become
    ///     exhausted": the move (the tracker running out or the card removed) exhausts the Berserker at the next phase
    ///     change (GameState.NextPhase), not inside the card move.
    ///   - GH2E_CannotPrevent on a Damage ability (Burning Hatred bottom): "This damage cannot be negated": no prevent
    ///     bonus and no lost card can stop it.
    ///   - Burning Hatred bottom (card 32627): "You may perform this action any number of times in succession, increasing
    ///     the damage suffered by 1 each time": after the half's attack the player is asked; yes repeats Suffer damage
    ///     N+1 (cannot be negated), Move 1, Attack 2, with no cap.
    ///   - Bone Breaker bottom (card 32625): "Place your character token on the target. The target loses Flying"
    ///     (persistent, the approved reading: for the scenario, until the card leaves the active area): the target gets
    ///     the Berserker2Token (a character resource on its status row) and StopFlying while the card's marker bonus
    ///     (GH2E_BoneBreaker) is active; both go when it ends.
    ///   - Defy Death perk (Berserker2 perk 10): after its prevent sets the hit points to 1, the Berserker performs Attack 2
    ///     targeting the attacker (the attack is the perk bonus's AbilityData, aimed through a temp var).
    ///   - Taunting Fury perk (Berserker2 perk 11): its end-of-turn control (MiscVars GH2E_TauntingFury) makes every
    ///     controlled enemy's Attack +0 target the Berserker (a temp var the commanded attack's custom targeting reads).
    ///   - Reckless Blows perk modifier (AMC_GH2E_Berserker2_Reckless_P1): "+1, you may suffer damage 2 to add +2 Attack":
    ///     asked for the card the Berserker's attack actually uses (with advantage/disadvantage after the pick); yes = the card counts +3 and the Berserker suffers 2
    ///     (offered only while that won't exhaust him).
    ///   - Careless Charge top: Frosthaven's MoveWasStraightLine requirement only looks at a move ability; on a consume
    ///     (checked against the attack) it now looks at the same action's last move ability.
    /// Every action logs "GHRule &lt;Rule&gt; card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHBerserker2e
    {
        internal const string ClassId = "Berserker2ID";
        internal const int FromTheBrinkCard = 32610, GlassHammerCard = 32611, FinalFightCard = 32615, BoneBreakerCard = 32625, BurningHatredCard = 32627;
        internal const string TokenResource = "Berserker2Token";
        private const string RecklessCard = "AMC_GH2E_Berserker2_Reckless_P1";
        private const string HatredSuffix = "_GHHatred";

        private static bool Marked(CAbility a, string key) => GHPlague.Marked(a, key);
        private static string Name(CActor a) => GHPlague.NameOf(a);
        private static bool IsBerserker(CActor a) => a is CPlayerActor p && p.Class?.ID == ClassId;

        private static int Int(CAbility a, string key)
        {
            try { return a?.MiscVars != null && a.MiscVars.Any(x => x.Item1 == key) ? a.GetMiscVar(key).GetInt() : 0; }
            catch { return 0; }
        }

        private static int CardOf(CAbility a) => GHPlague.CardOf(a);

        // ------------------------------------------------------------------ set hit points (not damage)

        private static readonly ConditionalWeakTable<CAbility, object> HPSet = new ConditionalWeakTable<CAbility, object>();

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void SetHP(CAbility __instance)
        {
            try
            {
                bool half = Marked(__instance, "GH2E_SetHPHalf");
                int to = Int(__instance, "GH2E_SetHP");
                if (!half && to <= 0) return;
                CActor me = __instance.TargetingActor;
                if (me == null || me.IsDead || HPSet.TryGetValue(__instance, out _)) return;
                HPSet.Add(__instance, new object());
                int target = half ? me.MaxHealthHalfRoundedUp : to;
                target = Math.Max(1, Math.Min(target, me.MaxHealth));
                int before = me.Health;
                if (before == target)
                {
                    Plugin.Log.LogInfo($"GHRule SetHP card {CardOf(__instance)} {Name(me)} already at {target}: hit points unchanged");
                    return;
                }
                me.Health = target;
                if (target < before)
                    ScenarioRuleClient.MessageHandler(new CActorBeenDamaged_MessageData(me) { m_ActorBeingDamaged = me, m_ActorOriginalHealth = before });
                else
                    ScenarioRuleClient.MessageHandler(new CActorBeenHealed_MessageData(me, me, target - before, before, false, false, false, false));
                me.CheckForCachedValuesAfterActiveBonusesUpdate();
                Plugin.Log.LogInfo($"GHRule SetHP card {CardOf(__instance)} {Name(me)} hit points set {before} -> {target} (not damage, not a heal)");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra sethp")) Plugin.Log.LogError("GHBerserker2e (set HP): " + e); }
        }

        // ------------------------------------------------------------------ Final Fight

        private static readonly List<CPlayerActor> PendingExhaust = new List<CPlayerActor>();

        [HarmonyPatch(typeof(CCharacterClass), nameof(CCharacterClass.MoveAbilityCard))]
        [HarmonyPostfix]
        private static void CardMoved(CCharacterClass __instance, CAbilityCard abilityCard, string fromCardPileName, string toCardPileName)
        {
            try
            {
                if (abilityCard == null || abilityCard.ID != FinalFightCard) return;
                if (fromCardPileName != "ActiveBonusCards" || (toCardPileName != "LostAbilityCards" && toCardPileName != "PermanentlyLostAbilityCards")) return;
                CPlayerActor me = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p != null && p.CharacterClass == __instance);
                if (me == null || me.IsDead) return;
                lock (PendingExhaust) if (!PendingExhaust.Contains(me)) PendingExhaust.Add(me);
                Plugin.Log.LogInfo($"GHRule ExhaustWhenLost card {FinalFightCard} Final Fight moved from {Name(me)}'s active area to the lost pile: {Name(me)} becomes exhausted");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra final fight")) Plugin.Log.LogError("GHBerserker2e (Final Fight): " + e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.NextPhase))]
        [HarmonyPrefix]
        private static void ExhaustPending()
        {
            List<CPlayerActor> due;
            lock (PendingExhaust)
            {
                if (PendingExhaust.Count == 0) return;
                due = PendingExhaust.ToList();
                PendingExhaust.Clear();
            }
            foreach (CPlayerActor me in due)
            {
                try
                {
                    if (me.IsDead || !ScenarioManager.Scenario.HasActor(me)) continue;
                    bool asleep = me.Tokens.HasKey(CCondition.ENegativeCondition.Sleep);
                    GameState.KillActor(me, me, CActor.ECauseOfDeath.Suicide, out _, null, asleep);
                    Plugin.Log.LogInfo($"GHRule ExhaustWhenLost card {FinalFightCard} {Name(me)} exhausted (Final Fight)");
                }
                catch (Exception e) { Plugin.Log.LogError("GHBerserker2e (Final Fight exhaust): " + e); }
            }
        }

        // ------------------------------------------------------------------ damage that cannot be negated

        [HarmonyPatch]
        private static class CannotPrevent
        {
            private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(GameState))
                .First(m => m.Name == nameof(GameState.ActorBeenDamaged) && m.GetParameters().Any(p => p.Name == "triggeredPreventDamage"));

            private static void Prefix(CActor actor, CAbility damageAbility, ref bool checkIfPlayerCanAvoidDamage, ref bool cannotPreventDamageWithActiveBonus)
            {
                try
                {
                    if (damageAbility == null || !Marked(damageAbility, "GH2E_CannotPrevent")) return;
                    checkIfPlayerCanAvoidDamage = false;
                    cannotPreventDamageWithActiveBonus = true;
                    Plugin.Log.LogInfo($"GHRule CannotPrevent card {CardOf(damageAbility)} {damageAbility.Name}: the damage to {Name(actor)} cannot be negated");
                }
                catch (Exception e) { if (GHClasses.Reported.Add("ra cannot prevent")) Plugin.Log.LogError("GHBerserker2e (cannot prevent): " + e); }
            }
        }

        // ------------------------------------------------------------------ Burning Hatred bottom, Bone Breaker bottom

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.AbilityEnded))]
        [HarmonyPostfix]
        private static void AttackEnded(CAbilityAttack __instance)
        {
            try
            {
                CActor me = __instance?.TargetingActor;
                if (!IsBerserker(me) || __instance.Name == null) return;
                int card = CardOf(__instance);
                if (card == BurningHatredCard && __instance.Name.StartsWith("BAttack", StringComparison.Ordinal)) Hatred(__instance, me);
                else if (card == BoneBreakerCard && __instance.Name == "BAttack" && __instance.AbilityHasHappened) BoneBreakerHit(__instance, me);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra attack ended")) Plugin.Log.LogError("GHBerserker2e (attack ended): " + e); }
        }

        private static void Hatred(CAbilityAttack attack, CActor me)
        {
            if (me.IsDead || !(PhaseManager.Phase is CPhaseAction action)) return;
            int done = 1;
            int at = attack.Name.IndexOf(HatredSuffix, StringComparison.Ordinal);
            if (at >= 0 && int.TryParse(attack.Name.Substring(at + HatredSuffix.Length), out int n)) done = n;
            int damage = done + 1;   // pass k suffers k: the first pass suffered 1
            List<CPhaseAction.CPhaseAbility> before = action.PreviousPhaseAbilities ?? new List<CPhaseAction.CPhaseAbility>();
            CAbility move = before.Select(p => p?.m_Ability).LastOrDefault(a => a is CAbilityMove && CardOf(a) == BurningHatredCard && a.Name.StartsWith("BMove", StringComparison.Ordinal));
            CAbility dmg = before.Select(p => p?.m_Ability).LastOrDefault(a => a is CAbilityDamage && CardOf(a) == BurningHatredCard && a.Name.StartsWith("BDamage", StringComparison.Ordinal));
            if (move == null || dmg == null)
            {
                Plugin.Log.LogWarning($"Burning Hatred: the half's move or damage was not found ({(move == null ? "move" : "damage")}); no repeat offered");
                return;
            }
            bool yes = GHPlague.Ask(me, false, "FHMODS_RA_HATRED", "FHMODS_RA_YES", "FHMODS_RA_NO", damage, me.Health);
            if (!yes)
            {
                Plugin.Log.LogInfo($"GHRule BurningHatred card {BurningHatredCard} {Name(me)} stops after {done} pass(es)");
                return;
            }
            CBaseCard baseCard = attack.GetAbilityBaseCard();
            CAbility a2 = Copy(attack, me, attack.Name.Split(new[] { HatredSuffix }, StringSplitOptions.None)[0] + HatredSuffix + damage);
            CAbility m2 = Copy(move, me, "BMove" + HatredSuffix + damage);
            CAbility d2 = Copy(dmg, me, "BDamage" + HatredSuffix + damage);
            d2.Strength = damage;
            action.InjectAbility(a2, me, baseCard);
            action.InjectAbility(m2, me, baseCard);
            action.InjectAbility(d2, me, baseCard);
            Plugin.Log.LogInfo($"GHRule BurningHatred card {BurningHatredCard} {Name(me)} repeats the action (pass {damage}): suffer {damage} (cannot be negated), Move 1, Attack 2");
        }

        private static CAbility Copy(CAbility from, CActor me, string name)
        {
            CAbility src = from.AbilityInitialState ?? from;
            CAbility copy = CAbility.CopyAbility(src, generateNewID: false);
            copy.ActiveBonusYML = from.ActiveBonusYML;
            copy.AbilityInitialState = from.AbilityInitialState;
            copy.Name = name;
            copy.TargetingActor = me;
            copy.ParentAbility = from.ParentAbility;
            return copy;
        }

        private static readonly Dictionary<CActor, CActor> BoneBreakerTargets = new Dictionary<CActor, CActor>();

        private static void BoneBreakerHit(CAbilityAttack attack, CActor me)
        {
            CActor target = attack.ActorsTargeted?.FirstOrDefault(x => x != null && !x.IsDead && ScenarioManager.Scenario.HasActor(x));
            if (target == null) return;
            if (BoneBreakerTargets.TryGetValue(me, out CActor old) && old != null && old != target) Release(old, me, "the token moves to a new target");
            BoneBreakerTargets[me] = target;
            if (target.GetCharacterResourceAmount(TokenResource) <= 0) target.AddCharacterResource(TokenResource, 1);
            target.ApplyCondition(me, CCondition.ENegativeCondition.StopFlying);
            // a flier that lands on an obstacle/trap/hazard is handled like Frosthaven's own StopFlying ability
            try { CAbilityStopFlying.CheckPostStopFlying(target); }
            catch (Exception e) { if (GHClasses.Reported.Add("ra bone breaker land")) Plugin.Log.LogError("GHBerserker2e (Bone Breaker landing): " + e); }
            Plugin.Log.LogInfo($"GHRule BoneBreaker card {BoneBreakerCard} {Name(me)}'s token placed on {Name(target)}: it loses Flying while the card is active");
        }

        private static void Release(CActor target, CActor me, string why)
        {
            if (target == null) return;
            if (target.GetCharacterResourceAmount(TokenResource) > 0) target.RemoveCharacterResource(TokenResource, target.GetCharacterResourceAmount(TokenResource), updateWorldSpaceUI: true);
            if (!target.IsDead) target.RemoveNegativeConditionToken(CCondition.ENegativeCondition.StopFlying, EConditionDecTrigger.None, me);
            Plugin.Log.LogInfo($"GHRule BoneBreaker card {BoneBreakerCard} token removed from {Name(target)} ({why}): Flying back");
        }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.Finish))]
        [HarmonyPostfix]
        private static void BonusFinished(CActiveBonus __instance)
        {
            try
            {
                if (!Marked(__instance?.Ability, "GH2E_BoneBreaker")) return;
                CActor me = __instance.Actor ?? __instance.Caster;
                if (me != null && BoneBreakerTargets.TryGetValue(me, out CActor target))
                {
                    BoneBreakerTargets.Remove(me);
                    Release(target, me, "Bone Breaker left the active area");
                }
                else if (me != null)
                {
                    // the target dictionary is memory only: after a save/reload or a rewind, find the target by the token
                    // (a character resource saved with the actor)
                    foreach (CActor t in ScenarioManager.Scenario?.AllAliveActors?.Where(x => x != null && x.GetCharacterResourceAmount(TokenResource) > 0).ToList() ?? new List<CActor>())
                        Release(t, me, "Bone Breaker left the active area; target found by its token after a reload");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra bone breaker")) Plugin.Log.LogError("GHBerserker2e (Bone Breaker): " + e); }
        }

        // ------------------------------------------------------------------ Defy Death perk

        private static readonly ConditionalWeakTable<CActiveBonus, object> DefyUsed = new ConditionalWeakTable<CActiveBonus, object>();

        [HarmonyPatch(typeof(CPreventDamageActiveBonus_PreventAndDamageActiveBonusCaster), nameof(CPreventDamageActiveBonus_PreventAndDamageActiveBonusCaster.OnPreventDamageTriggered))]
        [HarmonyPostfix]
        private static void DefyDeath(CPreventDamageActiveBonus_PreventAndDamageActiveBonusCaster __instance, CActor damageSource, CActor damagedActor, CActiveBonus preventedByBonus)
        {
            try
            {
                CActiveBonus b = __instance.ActiveBonus;
                if (b == null || preventedByBonus != b || !Marked(b.Ability, "GH2E_DefyDeath") || !IsBerserker(damagedActor)) return;
                if (DefyUsed.TryGetValue(b, out _)) return;
                DefyUsed.Add(b, new object());
                CAbility template = b.Ability?.ActiveBonusData?.AbilityData;
                if (damageSource == null || damageSource.IsDead || template == null || damagedActor.IsDead)
                {
                    Plugin.Log.LogInfo($"GHRule DefyDeath card 0 perk Defy Death: hit points set to 1; no attack ({(template == null ? "no attack data" : "the attacker is gone")})");
                    return;
                }
                damagedActor.SetTempVar("GH2EDefyDeathTarget", new TValue(damageSource));
                CAbility attack = CAbility.CopyAbility(template, generateNewID: true);
                attack.Name = "DefyDeathAttack";
                attack.SetMiscVar("InterruptCurrentAbility", true);
                PhaseManager.StartAbilities(attack, damagedActor, stackInsteadOfInject: true);
                Plugin.Log.LogInfo($"GHRule DefyDeath card 0 perk Defy Death: {Name(damagedActor)} at 1 hit point performs Attack 2 on the attacker {Name(damageSource)}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra defy death")) Plugin.Log.LogError("GHBerserker2e (Defy Death): " + e); }
        }

        // ------------------------------------------------------------------ Taunting Fury perk

        [HarmonyPatch(typeof(CAbilityControlActor), nameof(CAbilityControlActor.Start))]
        [HarmonyPrefix]
        private static void TauntStart(CAbilityControlActor __instance, CActor targetActor)
        {
            try
            {
                CActor targetingActor = targetActor is CPlayerActor ? targetActor : ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p?.Class?.ID == ClassId);
                if (!Marked(__instance, "GH2E_TauntingFury") || targetingActor == null) return;
                int n = 0;
                foreach (CActor foe in ScenarioManager.Scenario.AllAliveActors.Where(x => x != null && !x.IsDead && x.IsAdjacentTo(targetingActor)
                             && CActor.AreActorsEnemies(x.Type, targetingActor.Type, x, targetingActor)))
                {
                    foe.SetTempVar("GH2ETauntTarget", new TValue(targetingActor));
                    n++;
                }
                Plugin.Log.LogInfo($"GHRule TauntingFury card 0 perk Taunting Fury: {n} adjacent enemies may be controlled to attack {Name(targetingActor)}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra taunting fury")) Plugin.Log.LogError("GHBerserker2e (Taunting Fury): " + e); }
        }

        // ------------------------------------------------------------------ Reckless Blows perk modifier

        // Without advantage or disadvantage every drawn card is used: asked as the card is drawn (ChangeModifierValues, so
        // the modifier display shows +3). With advantage/disadvantage only the card the attack keeps may be paid for: the
        // ask waits for CActor.DrawAttackModifierCards to return the used list (the +1 is what the comparison sees).
        [HarmonyPatch(typeof(GameState), nameof(GameState.ChangeModifierValues))]
        [HarmonyPostfix]
        private static void Reckless(CActor actor, List<AttackModifierYMLData> modifiers, CAbilityAttack attackAbility, EAdvantageStatuses advantageStatus)
        {
            if (advantageStatus != EAdvantageStatuses.None) return;
            RecklessAsk(actor, modifiers, attackAbility, null);
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.DrawAttackModifierCards))]
        [HarmonyPostfix]
        private static void RecklessUsed(CActor actor, EAdvantageStatuses advStatus, CAbilityAttack attackAbility, List<AttackModifierYMLData> cardsDrawnTotal, List<AttackModifierYMLData> __result)
        {
            if (advStatus == EAdvantageStatuses.None) return;
            RecklessAsk(actor, __result, attackAbility, cardsDrawnTotal);
        }

        private static void RecklessAsk(CActor actor, List<AttackModifierYMLData> modifiers, CAbilityAttack attackAbility, List<AttackModifierYMLData> drawn)
        {
            try
            {
                if (modifiers == null || !IsBerserker(actor) || attackAbility?.TargetingActor != actor) return;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    AttackModifierYMLData card = modifiers[i];
                    if (card == null || card.Name != RecklessCard || card.MathModifier != "+1") continue;
                    if (actor.Health <= 2)
                    {
                        Plugin.Log.LogInfo($"GHRule RecklessBlows card 0 perk Reckless Blows: {Name(actor)} at {actor.Health} can't suffer 2 without exhausting: plain +1");
                        continue;
                    }
                    bool yes = GHPlague.Ask(actor, false, "FHMODS_RA_RECKLESS", "FHMODS_RA_YES", "FHMODS_RA_NO", Name(actor), actor.Health);
                    if (!yes) { Plugin.Log.LogInfo($"GHRule RecklessBlows card 0 perk Reckless Blows: declined, +1"); continue; }
                    AttackModifierYMLData copy = card.Copy(createCard: true);
                    copy.OriginalModifier = card.MathModifier;
                    copy.MathModifier = "+3";
                    copy.ModifiedFromModifier = card.Card;
                    ScenarioRuleClient.MessageHandler(new CChangeModifier_MessageData(GameState.InternalCurrentActor) { m_OriginalModifier = copy.OriginalModifier });
                    modifiers[i] = copy;
                    if (drawn != null) { int at = drawn.IndexOf(card); if (at >= 0) drawn[at] = copy; }
                    if (drawn != null && attackAbility.ModifierAbilities != null)
                        foreach (CAbilityAttack.AttackModifierAbilityInfo info in attackAbility.ModifierAbilities) if (info.Card == card) info.Card = copy;
                    actor.ApplyImmediateDamage(2);
                    Plugin.Log.LogInfo($"GHRule RecklessBlows card 0 perk Reckless Blows: {Name(actor)} suffers 2 for +2 Attack (the card counts +3)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra reckless")) Plugin.Log.LogError("GHBerserker2e (Reckless Blows): " + e); }
        }

        // ------------------------------------------------------------------ Tireless Retribution bottom

        // "If you short rest at the end of this round, gain all the benefits of a long rest." The card is Frosthaven's
        // improved short rest (the player picks the lost card); GameState.PlayerLongRested skips the rest of a long rest
        // on that path. Added here (before the rest runs, since the improved path ends with PhaseManager.StepComplete):
        // Heal 2 self (a card-less heal applied like a trap's heal: poison/wound rules and heal bonuses apply), spent
        // items refreshed (as the long-rest branch does) and the perks that act after a long rest (Rapid Recovery: their
        // heals). Bonuses that fire "on long rest" (DuringActionAbilityOnLongRest) need an action phase: logged, not run.
        internal const int TirelessRetributionCard = 32616;
        private static readonly Dictionary<CActor, int> TirelessRound = new Dictionary<CActor, int>();

        [HarmonyPatch(typeof(CAbilityImprovedShortRest), nameof(CAbilityImprovedShortRest.ApplyToActor))]
        [HarmonyPostfix]
        private static void TirelessPlayed(CAbilityImprovedShortRest __instance, CActor actor)
        {
            try
            {
                if (!IsBerserker(actor) || CardOf(__instance) != TirelessRetributionCard) return;
                lock (TirelessRound) TirelessRound[actor] = ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra tireless mark")) Plugin.Log.LogError("GHBerserker2e (Tireless Retribution mark): " + e); }
        }

        [HarmonyPatch(typeof(GameState), nameof(GameState.PlayerLongRested))]
        [HarmonyPrefix]
        private static void TirelessRest(CPlayerActor playerActor, bool improvedShortRestUsed)
        {
            try
            {
                CPlayerActor me = playerActor ?? GameState.InternalCurrentActor as CPlayerActor;
                if (!improvedShortRestUsed || !IsBerserker(me) || me.IsDead || me.CharacterClass?.ImprovedShortRest != true) return;
                int round = ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;
                bool marked;
                lock (TirelessRound) marked = TirelessRound.TryGetValue(me, out int r) && r == round;
                // after a reload the mark is gone: the card played this round is still among the round's cards
                if (!marked) marked = me.CharacterClass.RoundAbilityCards?.Any(c => c != null && c.ID == TirelessRetributionCard) == true;
                if (!marked) return;
                lock (TirelessRound) TirelessRound.Remove(me);
                int before = me.Health;
                HealCardless(me, 2);
                me.Inventory.RefreshItems(CItem.EItemSlotState.Spent);
                me.Inventory.RefreshItems(CItem.EItemSlotState.Locked, CItem.EItemSlot.None, CItem.EUsageType.Spent);
                me.ActivatePassiveItems(firstLoad: true);
                int perks = 0;
                foreach (CAbility a in me.CharacterClass.Perks.Where(p => (p.Flags & EPerkFlag.ExecuteAbilitiesAfterLongRest) != 0).SelectMany(p => p.Abilities))
                {
                    if (a is CAbilityHeal h && a.MeetsStartAbilityRequirements(me)) { HealCardless(me, h.Strength); perks++; }
                    else Plugin.Log.LogWarning($"Tireless Retribution: after-long-rest perk ability {a?.Name} ({a?.AbilityType}) is not a heal: not run");
                }
                int onRest = me.FindApplicableActiveBonuses(CAbility.EAbilityType.AddActiveBonus, CActiveBonus.EActiveBonusBehaviourType.DuringActionAbilityOnLongRest).Count();
                Plugin.Log.LogInfo($"GHRule LongRestBenefits card {TirelessRetributionCard} {Name(me)} short rests with the benefits of a long rest: Heal 2 ({before} -> {me.Health}), spent items refreshed, {perks} after-long-rest perk heal(s)" + (onRest > 0 ? $", {onRest} on-long-rest bonus(es) not run" : ""));
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra tireless")) Plugin.Log.LogError("GHBerserker2e (Tireless Retribution): " + e); }
        }

        private static void HealCardless(CActor me, int amount)
        {
            if (amount <= 0 || !GameState.ActorHealthCheck(me, me)) return;
            CAbilityHeal heal = CAbilityHeal.CreateDefaultHeal(amount, 1, 1, isMonster: false, new CAbilityFilterContainer(CAbilityFilter.EFilterTargetType.Self)) as CAbilityHeal;
            if (heal == null) return;
            heal.SkipAnim = true;
            heal.MiscAbilityData = new AbilityData.MiscAbilityData { AutotriggerAbility = true, IgnoreStun = true };
            heal.Start(me, me);
            heal.ActorIsApplying(me, new List<CActor> { me });
            heal.ApplyToActor(me);
        }

        // ------------------------------------------------------------------ MoveWasStraightLine on a consume


        [HarmonyPatch(typeof(CAbilityRequirements), nameof(CAbilityRequirements.MeetsAbilityRequirements), new[] { typeof(CActor), typeof(CAbility), typeof(CVarsContext), typeof(CItem), typeof(CActiveBonus) })]
        [HarmonyPrefix]
        private static bool StraightLineFromMove(CAbilityRequirements __instance, ref CAbility ability, ref bool __result)
        {
            try
            {
                if (__instance.AbilityMovementWasStraightLine != true || ability == null || ability is CAbilityMove) return true;
                if (CardOf(ability) < 20000 || CardOf(ability) >= 40000) return true;   // pack cards only
                if (!(PhaseManager.Phase is CPhaseAction action)) return true;
                CActor mover = ability.TargetingActor;
                CAbility move = action.PreviousPhaseAbilities?.Select(p => p?.m_Ability).LastOrDefault(a => a is CAbilityMove && a.TargetingActor == mover);
                // no move this action, or a move with no recorded path (the engine passes the path to List.AddRange: a null
                // path threw ArgumentNullException and the error screen came up, Careless Charge top 2026-11-02 22:15):
                // not a straight-line move, so the requirement fails instead
                if (!(move is CAbilityMove m) || m.AllArrayIndexOnPathIncludingRepeatsCopy == null) { __result = false; return false; }
                ability = move;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra straight")) Plugin.Log.LogError("GHBerserker2e (straight line): " + e); }
            return true;
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_RA_YES"] = "Yes",
            ["FHMODS_RA_NO"] = "No",
            ["FHMODS_RA_HATRED"] = "Burning Hatred: repeat the action? Suffer {0} damage (cannot be negated; you have {1}), then Move 1, Attack 2.",
            ["FHMODS_RA_RECKLESS"] = "Reckless Blows: {0} may suffer 2 damage (has {1}) to add +2 Attack to this attack.",
            ["FHMODS_RA_TAUNT"] = "Fearsome Taunt: {0} attacks {1}. Have the attack target {2} instead?",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_RA_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
