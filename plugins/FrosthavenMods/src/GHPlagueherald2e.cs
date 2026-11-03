using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Plagueherald's card, perk and mastery rules that are not Plague tokens (GHPlague.cs):
    ///   - Heal through Poison (Gift of Rot top, Tainted Offering bottom: MiscVars GH2E_HealThroughPoisonChoice, "for each
    ///     target, you may heal that target as if they did not have Poison (if you do, their Poison is not removed)";
    ///     Xorn's Embrace top: GH2E_HealThroughPoison, always). For a poisoned target the Plagueherald's player is asked
    ///     (the choice) and on yes the Poison is set aside for the heal: the heal adds its hit points and removes Wound /
    ///     Bane / Brittle as a normal heal does, then the same Poison token is put back (no condition events fire).
    ///     "GHRule HealThroughPoison".
    ///   - Sickening Strength top (GH2E_SickeningStrength): after the Strengthen, each strengthened character in turn is
    ///     asked whether to suffer Damage 2 to play one card from their hand now; on yes they perform the card's templates
    ///     PH2_SickDamage then PH2_SickPlay (Frosthaven's SelectAndPlayCard from their own hand). "GHRule SickeningStrength".
    ///   - Nightmarish Affliction bottom (card 33016): each of the holder's next six ranged attack abilities uses a slot
    ///     whether or not its cost was paid: at the end of a ranged attack ability that did not move the tracker, the
    ///     plugin moves it (and gives its XP). "GHRule NightmarishSlot".
    ///   - Tainted Offering top (GH2E_FromPoisonedAlly): the bought attack is made "as if you were occupying the same hex
    ///     as that ally": the attack's TargetingOverrideArrayIndex (Frosthaven's own perform-from-another-hex, Shattersong
    ///     Resonant Frequency) is the hex of the ally the Plagueherald last poisoned (the cost). "GHRule FromAllyHex".
    ///   - Perk Purging Rest (Plagueherald211): when the long rest's own heal is about to apply, the Plagueherald may
    ///     remove Poison from one ally (asked ally by ally) to add +1 to that heal. "GHRule PurgingRest".
    ///   - Perk Xorn's Last Gift (Plagueherald212): once each scenario, when an enemy with Plague would die, the
    ///     Plagueherald may first control it: it performs the abilities of its monster ability card for this round, each
    ///     attack also giving Plague, driven by his player (switched to the player side as Frosthaven's player-driven
    ///     control is), then it dies through the engine's own on-death path (the kill stays credited as it was). The used
    ///     mark is the hidden resource GH2EXornUsed on him (saved with the scenario). "GHRule XornsLastGift".
    ///   - Masteries: FrosthavenMods_KillsWithoutModifierInTurn (a turn of his with Value kills and no modifier drawn by
    ///     him, from the kill and modifier records) and FrosthavenMods_PoisonEveryRound (in every round he took a turn he
    ///     gave or removed Poison on a figure: counted live in the hidden resources GH2EPoisonRounds / GH2EPoisonLastRound).
    /// Everything is gated on the pack's class Plagueherald2ID, its perk IDs, its card ID or misc vars only the pack's cards
    /// carry: without the 2nd edition pack this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHPlagueherald2e
    {
        internal const string PH = "Plagueherald2ID";
        internal const string PurgingRest = "Plagueherald211";
        internal const string XornsLastGift = "Plagueherald212";
        internal const int NightmarishCard = 33016;
        internal const string XornUsed = "GH2EXornUsed";
        internal const string PoisonRounds = "GH2EPoisonRounds";
        internal const string PoisonLast = "GH2EPoisonLastRound";
        internal const string KillsTag = "FrosthavenMods_KillsWithoutModifierInTurn";
        internal const string PoisonTag = "FrosthavenMods_PoisonEveryRound";

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Rule(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"GHPlagueherald2e ({what}): {e}");
        }

        private static bool IsPH(CActor a) => a != null && a.Class?.ID == PH;

        private static bool Allied(CActor a, CActor b) => a != null && b != null && CActor.AreActorsAllied(a.Type, b.Type, a, b);

        private static bool Ask(CActor asker, string key, params object[] args) =>
            GHPlague.Ask(asker, false, key, key + "_YES", key + "_NO", args);

        private static void SetAmount(CActor a, string id, int value)
        {
            int has = a.GetCharacterResourceAmount(id);
            if (has == value) return;
            if (has > 0) a.RemoveCharacterResource(id, has);
            if (value > 0) a.AddCharacterResource(id, value);
        }

        // ------------------------------------------------------------------ heal through Poison

        private static readonly AccessTools.FieldRef<CTokens, List<NegativeConditionPair>> NegativeTokens =
            AccessTools.FieldRefAccess<CTokens, List<NegativeConditionPair>>("NegativeTokens");

        [HarmonyPatch(typeof(CActor), nameof(CActor.Healed), new[] { typeof(int), typeof(CAbility), typeof(CActor), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool), typeof(bool), typeof(bool) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyPrefix]
        private static void BeforeHealed(CActor __instance, int strength, CAbility healAbility, bool ignoreTokens, out List<NegativeConditionPair> __state)
        {
            __state = null;
            try
            {
                if (ignoreTokens || healAbility == null || healAbility.MiscVars == null) return;
                bool always = GHPlague.Marked(healAbility, "GH2E_HealThroughPoison");
                bool choice = !always && GHPlague.Marked(healAbility, "GH2E_HealThroughPoisonChoice");
                if (!always && !choice) return;
                if (!__instance.Tokens.HasKey(CCondition.ENegativeCondition.Poison)) return;
                int card = GHPlague.CardOf(healAbility);
                CActor healer = healAbility.TargetingActor;
                if (choice && !Ask(healer, "FHMODS_PH2_THROUGH", GHPlague.NameOf(__instance), strength))
                {
                    Rule("HealThroughPoison", card, $"{GHPlague.NameOf(healer)} heals {GHPlague.NameOf(__instance)} normally (the heal removes the Poison)");
                    return;
                }
                List<NegativeConditionPair> list = NegativeTokens(__instance.Tokens);
                lock (list)
                {
                    __state = list.Where(p => p != null && p.NegativeCondition == CCondition.ENegativeCondition.Poison).ToList();
                    foreach (NegativeConditionPair p in __state) list.Remove(p);
                }
                Rule("HealThroughPoison", card, $"{GHPlague.NameOf(healer)} heals {GHPlague.NameOf(__instance)} for {strength} as if it had no Poison; the Poison stays ({(always ? "always" : "chosen")})");
            }
            catch (Exception e) { Report("heal through Poison", e); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.Healed), new[] { typeof(int), typeof(CAbility), typeof(CActor), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool), typeof(bool), typeof(bool) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
        [HarmonyFinalizer]
        private static Exception AfterHealed(Exception __exception, CActor __instance, List<NegativeConditionPair> __state)
        {
            try
            {
                if (__state != null && __state.Count > 0)
                {
                    List<NegativeConditionPair> list = NegativeTokens(__instance.Tokens);
                    lock (list) list.AddRange(__state);
                    ScenarioRuleClient.MessageHandler(new CUpdateWorldspaceConditionsUI_MessageData(__instance));
                }
            }
            catch (Exception e) { Report("heal through Poison (restore)", e); }
            return __exception;
        }

        // ------------------------------------------------------------------ Sickening Strength top

        private static readonly Queue<CPlayerActor> SickQueue = new Queue<CPlayerActor>();
        private static CAbility _sickDamage, _sickPlay;
        private static CPlayerActor _sickAlly;
        private static int _sickCard;

        private static void Stack(CActor performer, List<CAbility> abilities)
        {
            if (!(PhaseManager.CurrentPhase is CPhaseAction phase)) return;
            if (GameState.InternalCurrentActor != performer) GameState.OverrideCurrentActorForOneAction(performer);
            phase.StackNextAbilities(abilities, performer, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: false);
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void AbilityEnded(CAbility __instance)
        {
            if (__instance == null) return;
            try { SickeningEnded(__instance); } catch (Exception e) { Report("Sickening Strength", e); }
            try { NightmarishEnded(__instance); } catch (Exception e) { Report("Nightmarish Affliction", e); }
        }

        private static void SickeningEnded(CAbility a)
        {
            if (_sickPlay != null && ReferenceEquals(a, _sickPlay)) { _sickPlay = null; _sickDamage = null; NextSick(); return; }
            if (_sickDamage != null && ReferenceEquals(a, _sickDamage))
            {
                if (_sickAlly == null || _sickAlly.IsDead) { _sickPlay = null; _sickDamage = null; NextSick(); }
                return;
            }
            if (a.MiscVars == null || !GHPlague.Marked(a, "GH2E_SickeningStrength")) return;
            SickQueue.Clear();
            _sickCard = GHPlague.CardOf(a);
            var allies = (a.ActorsTargeted ?? new List<CActor>()).OfType<CPlayerActor>()
                .Where(p => !p.IsDead && p.CharacterClass?.HandAbilityCards?.Count > 0).Distinct().ToList();
            Rule("SickeningStrength", _sickCard, allies.Count == 0 ? "no strengthened character holds a card" : "strengthened characters may play a card for Damage 2: " + string.Join(", ", allies.Select(GHPlague.NameOf)));
            foreach (CPlayerActor p in allies) SickQueue.Enqueue(p);
            NextSick();
        }

        private static void NextSick()
        {
            while (SickQueue.Count > 0)
            {
                CPlayerActor ally = SickQueue.Dequeue();
                if (ally.IsDead || !(ally.CharacterClass?.HandAbilityCards?.Count > 0)) continue;
                if (!GHSawbones2e.Templates.TryGetValue("PH2_SickDamage", out CAbility damage) || !GHSawbones2e.Templates.TryGetValue("PH2_SickPlay", out CAbility play))
                {
                    if (Reported.Add("sick templates")) Plugin.Log.LogWarning("GHPlagueherald2e: Sickening Strength's templates were not read");
                    SickQueue.Clear();
                    return;
                }
                if (!Ask(ally, "FHMODS_PH2_SICK", GHPlague.NameOf(ally)))
                {
                    Rule("SickeningStrength", _sickCard, $"{GHPlague.NameOf(ally)} declines");
                    continue;
                }
                _sickAlly = ally;
                _sickDamage = CAbility.CopyAbility(damage, generateNewID: true);
                _sickPlay = CAbility.CopyAbility(play, generateNewID: true);
                Stack(ally, new List<CAbility> { _sickDamage, _sickPlay });
                Rule("SickeningStrength", _sickCard, $"{GHPlague.NameOf(ally)} suffers Damage 2 and plays one card from their hand now");
                return;
            }
            _sickAlly = null;
        }

        // ------------------------------------------------------------------ Nightmarish Affliction bottom; Tainted Offering top

        private static readonly Dictionary<CAbility, (CActiveBonus bonus, int index)> NightmarishAtStart = new Dictionary<CAbility, (CActiveBonus, int)>();

        private static CActiveBonus Nightmarish(CActor holder) =>
            holder?.ActiveBonuses?.FirstOrDefault(b => b != null && b.BaseCard?.ID == NightmarishCard && b.HasTracker && !b.Finishing() && !b.Finished());

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.Start))]
        [HarmonyPrefix]
        private static void BeforeStart(CAbility __instance, CActor targetingActor)
        {
            try
            {
                if (__instance is CAbilityAttack atk && !atk.IsAttackAbilityWithOverrides && IsPH(targetingActor))
                {
                    CActiveBonus b = Nightmarish(targetingActor);
                    if (b != null)
                    {
                        if (NightmarishAtStart.Count > 64) NightmarishAtStart.Clear();
                        NightmarishAtStart[__instance] = (b, b.TrackerIndex);
                    }
                }
            }
            catch (Exception e) { Report("Nightmarish Affliction (start)", e); }
            try
            {
                if (__instance.MiscVars != null && GHPlague.Marked(__instance, "GH2E_FromPoisonedAlly") && targetingActor != null
                    && LastPoisonedAlly.TryGetValue(targetingActor, out CActor ally) && ally != null && !ally.IsDead && ScenarioManager.Scenario.HasActor(ally))
                {
                    __instance.TargetingOverrideArrayIndex = ally.ArrayIndex;
                    Rule("FromAllyHex", GHPlague.CardOf(__instance), $"{GHPlague.NameOf(targetingActor)} attacks as if occupying the hex of {GHPlague.NameOf(ally)} ({ally.ArrayIndex})");
                }
            }
            catch (Exception e) { Report("Tainted Offering (ally hex)", e); }
        }

        private static void NightmarishEnded(CAbility a)
        {
            if (!(a is CAbilityAttack atk) || !NightmarishAtStart.TryGetValue(a, out var start)) return;
            NightmarishAtStart.Remove(a);
            if (atk.IsMeleeAttack || !a.AbilityHasHappened) return;
            CActiveBonus b = start.bonus;
            if (b == null || b.Finishing() || b.Finished() || b.TrackerIndex != start.index) return;   // the paid toggle already used the slot
            b.UpdateXPTracker();
            Rule("NightmarishSlot", NightmarishCard, $"ranged attack ability without the cost: a slot used anyway ({b.Remaining} left)");
            b.FinishIfNoRemaining();
        }

        // ------------------------------------------------------------------ Poison given / removed (Tainted Offering's ally, the second mastery)

        private static readonly Dictionary<CActor, CActor> LastPoisonedAlly = new Dictionary<CActor, CActor>();

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyCondition), new[] { typeof(CActor), typeof(CCondition.ENegativeCondition), typeof(int), typeof(EConditionDecTrigger), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        private static void ConditionApplied(CActor __instance, CActor actorApplying, CCondition.ENegativeCondition condition)
        {
            try
            {
                if (condition != CCondition.ENegativeCondition.Poison || !IsPH(actorApplying) || __instance == null || ReferenceEquals(__instance, actorApplying)) return;
                if (!__instance.Tokens.HasKey(CCondition.ENegativeCondition.Poison)) return;   // immune
                if (Allied(actorApplying, __instance))
                {
                    if (LastPoisonedAlly.Count > 16) LastPoisonedAlly.Clear();
                    LastPoisonedAlly[actorApplying] = __instance;
                }
                MarkPoisonRound(actorApplying, "gave Poison to " + GHPlague.NameOf(__instance));
            }
            catch (Exception e) { Report("Poison given", e); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.RemoveNegativeConditionToken))]
        [HarmonyPostfix]
        private static void ConditionRemoved(CActor __instance, CCondition.ENegativeCondition negativeCondition, CActor actedOnBy, bool __result)
        {
            try
            {
                if (!__result || negativeCondition != CCondition.ENegativeCondition.Poison || !IsPH(actedOnBy) || ReferenceEquals(__instance, actedOnBy)) return;
                MarkPoisonRound(actedOnBy, "removed Poison from " + GHPlague.NameOf(__instance));
            }
            catch (Exception e) { Report("Poison removed", e); }
        }

        private static void MarkPoisonRound(CActor ph, string what)
        {
            int round = ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;
            if (round <= 0 || ph.GetCharacterResourceAmount(PoisonLast) == round) return;
            SetAmount(ph, PoisonLast, round);
            SetAmount(ph, PoisonRounds, ph.GetCharacterResourceAmount(PoisonRounds) + 1);
            Rule("PoisonEveryRound", 0, $"mastery: round {round}: {GHPlague.NameOf(ph)} {what} ({ph.GetCharacterResourceAmount(PoisonRounds)} rounds so far)");
        }

        // ------------------------------------------------------------------ perk Purging Rest

        private static CPlayerActor _rester;

        [HarmonyPatch(typeof(GameState), nameof(GameState.PlayerLongRested))]
        [HarmonyPrefix]
        private static void LongRestStarts(CPlayerActor playerActor)
        {
            try
            {
                CPlayerActor p = playerActor ?? GameState.InternalCurrentActor as CPlayerActor;
                _rester = p != null && GHPerks.HasPerk(p, PurgingRest) ? p : null;
            }
            catch (Exception e) { Report("Purging Rest (rest)", e); }
        }

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyPrefix]
        private static void BeforeRestHeal(CAbilityHeal __instance, CActor actor, out int __state)
        {
            __state = 0;
            try
            {
                if (_rester == null || actor == null || !ReferenceEquals(actor, _rester) || !ReferenceEquals(__instance.TargetingActor, _rester)) return;
                CBaseCard card = null;
                try { card = __instance.GetAbilityBaseCard(); } catch { }
                if (card != null) return;                      // a card's heal: the rest heal has none
                CPlayerActor ph = _rester;
                _rester = null;                                // one rest heal
                var allies = ScenarioManager.Scenario.AllAliveActors
                    .Where(x => x != null && !x.IsDead && !ReferenceEquals(x, ph) && Allied(ph, x) && x.Tokens.HasKey(CCondition.ENegativeCondition.Poison)).ToList();
                foreach (CActor ally in allies)
                {
                    if (!Ask(ph, "FHMODS_PH2_PURGE", GHPlague.NameOf(ally))) continue;
                    ally.RemoveNegativeConditionToken(CCondition.ENegativeCondition.Poison, EConditionDecTrigger.None, ph);
                    __instance.Strength += 1;
                    __state = 1;
                    Rule("PurgingRest", 0, $"Purging Rest: {GHPlague.NameOf(ph)} removes Poison from {GHPlague.NameOf(ally)}; the long rest heal is {__instance.Strength}");
                    return;
                }
                Rule("PurgingRest", 0, allies.Count == 0 ? "Purging Rest: no ally has Poison" : "Purging Rest: declined");
            }
            catch (Exception e) { Report("Purging Rest", e); }
        }

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.ApplyToActor))]
        [HarmonyFinalizer]
        private static Exception AfterRestHeal(Exception __exception, CAbilityHeal __instance, int __state)
        {
            if (__state != 0 && __instance != null) __instance.Strength -= __state;
            return __exception;
        }

        // ------------------------------------------------------------------ perk Xorn's Last Gift

        private static readonly AccessTools.FieldRef<CActor, bool> OnDeathAbilityUsed = AccessTools.FieldRefAccess<CActor, bool>("m_OnDeathAbilityUsed");
        private static readonly AccessTools.FieldRef<CEnemyActor, CAbility> CauseOfDeathAbility = AccessTools.FieldRefAccess<CEnemyActor, CAbility>("m_CauseOfDeathAbility");

        [HarmonyPatch(typeof(CEnemyActor), nameof(CEnemyActor.OnDeath))]
        [HarmonyPrefix]
        private static bool BeforeEnemyDies(CEnemyActor __instance, CActor targetingActor, CActor.ECauseOfDeath causeOfDeath, ref bool startedOnDeathAbility,
                                            bool fromDeathAbilityComplete, CAbility causeOfDeathAbility, CAttackSummary.TargetSummary attackSummary, ref bool __result)
        {
            bool committed = false, usedBefore = false;
            try
            {
                if (fromDeathAbilityComplete || causeOfDeath == CActor.ECauseOfDeath.ActorRemovedFromMap || OnDeathAbilityUsed(__instance)) return true;
                if (__instance.GetCharacterResourceAmount(GHPlague.Plague) <= 0) return true;
                CPlayerActor ph = GHPlague.Plagueherald();
                if (ph == null || ph.IsDead || !GHPerks.HasPerk(ph, XornsLastGift) || ph.GetCharacterResourceAmount(XornUsed) > 0) return true;
                if (!(PhaseManager.Phase is CPhaseAction phase)) return true;
                CMonsterAbilityCard card = __instance.OverrideRoundAbilityCard ?? __instance.MonsterClass?.RoundAbilityCard;
                if (card?.Action?.Abilities == null) return true;
                if (!Ask(ph, "FHMODS_PH2_XORN", GHPlague.NameOf(__instance))) return true;

                var list = new List<CAbility>();
                foreach (CAbility ability in card.Action.Abilities.Where(x => !x.OnDeath))
                {
                    CAbility clone = __instance.CloneAbilityAndApplyBaseStats(ability);
                    clone.ProcessIfDead = true;
                    if (clone is CAbilityAttack)
                    {
                        var give = clone.ResourcesToGiveToTargets != null ? new Dictionary<string, int>(clone.ResourcesToGiveToTargets) : new Dictionary<string, int>();
                        give[GHPlague.Plague] = Math.Max(1, give.TryGetValue(GHPlague.Plague, out int n) ? n : 0);
                        clone.ResourcesToGiveToTargets = give;
                    }
                    list.Add(clone);
                }
                CAbility move = list.Find(x => x.AbilityType == CAbility.EAbilityType.Move), attack = list.Find(x => x.AbilityType == CAbility.EAbilityType.Attack);
                if (move != null && attack != null) move.Range = attack.Range;   // as the monster's own turn does
                if (__instance.ActorActionHasHappened)
                    foreach (CAbility ability in card.Action.Abilities.Where(x => x.OnDeath))
                    {
                        CAbility clone = __instance.CloneAbilityAndApplyBaseStats(ability);
                        clone.ProcessIfDead = true;
                        list.Add(clone);
                    }
                if (__instance.MonsterClass?.CurrentMonsterStat?.OnDeathAbilities != null)
                    foreach (CAbility ability in __instance.MonsterClass.CurrentMonsterStat.OnDeathAbilities)
                    {
                        CAbility clone = CAbility.CopyAbility(ability, generateNewID: false);
                        clone.ProcessIfDead = true;
                        clone.OnDeath = true;
                        list.Add(clone);
                    }
                if (list.Count == 0) return true;

                usedBefore = OnDeathAbilityUsed(__instance);
                committed = true;
                SetAmount(ph, XornUsed, 1);
                __instance.CauseOfDeath = causeOfDeath;
                Traverse.Create(__instance).Property("AttackSummary").SetValue(attackSummary);
                CauseOfDeathAbility(__instance) = causeOfDeathAbility;
                __instance.m_AfterDeathListeners?.Invoke(__instance, null);
                Traverse.Create(__instance).Property("TargetingActor").SetValue(targetingActor);
                OnDeathAbilityUsed(__instance) = true;
                GameState.OverrideCurrentActorForOneAction(__instance, CActor.EType.Player, killActorAfterAction: true, null, ph,
                    useControllingActorModifierDeck: false, useControllingActorPlayerControl: true, immediatelySetCurrentActor: true);
                phase.StackInlineSubAbilities(list, ph, performNow: false, stopPlayerSkipping: false, true, stopPlayerUndo: false, null, ignorePerformNow: true, __instance);
                startedOnDeathAbility = true;
                __result = false;
                Rule("XornsLastGift", 0, $"Xorn's Last Gift: {GHPlague.NameOf(__instance)} would die with Plague; {GHPlague.NameOf(ph)} controls it for its ability card ({list.Count} abilities, its attacks give Plague), then it dies");
                return false;
            }
            catch (Exception e)
            {
                Report("Xorn's Last Gift", e);
                // failed half-way through taking control: hand the death back to the engine's own path whole (its own
                // on-death abilities run, the figure dies as it would without the perk)
                if (committed) try { OnDeathAbilityUsed(__instance) = usedBefore; } catch { }
                return true;
            }
        }

        // ------------------------------------------------------------------ the masteries

        [HarmonyPatch(typeof(MapRuleLibrary.YML.Shared.CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Mastery(MapRuleLibrary.YML.Shared.CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                    ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || (!tags.Contains(KillsTag) && !tags.Contains(PoisonTag))) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            try
            {
                if (tags.Contains(KillsTag)) KillsWithoutModifier(unlockConditionTarget.Value, stats, ref totalConditions, ref conditionsMet, ref __result);
                else PoisonEveryRound(stats, ref totalConditions, ref conditionsMet, ref __result);
            }
            catch (Exception e)
            {
                Report("mastery", e);
                totalConditions = Math.Max(1, unlockConditionTarget.Value);
                conditionsMet = 0;
                __result = false;
            }
            return false;
        }

        private static string SelfGuid(List<CPlayerStats> stats) =>
            stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Select(x => x?.ActionData?.MainActorGUID).FirstOrDefault(g => !string.IsNullOrEmpty(g));

        private static bool IsEnemyType(string t) => t == CActor.EType.Enemy.ToString() || t == CActor.EType.Enemy2.ToString();

        private static void KillsWithoutModifier(int value, List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            int needed = Math.Max(1, value);
            string self = SelfGuid(stats);
            var kills = new Dictionary<int, int>();
            foreach (CPlayerStatsKill k in stats.SelectMany(s => s.Kills ?? Enumerable.Empty<CPlayerStatsKill>()))
            {
                if (k?.ActionData == null || !IsEnemyType(k.ActorWhoDiedType)) continue;
                string turn = k.ActionData.TurnActorGUID;
                if (!string.IsNullOrEmpty(turn) && self != null && turn != self) continue;   // a kill on someone else's turn
                kills[k.Round] = (kills.TryGetValue(k.Round, out int n) ? n : 0) + 1;
            }
            var drew = new HashSet<int>(stats.SelectMany(s => s.Modifiers ?? Enumerable.Empty<CPlayerStatsModifiers>())
                .Where(m => m?.ActionData != null && m.ActionData.MainActorClassID == PH && !m.ActionData.PerformedBySummons).Select(m => m.Round));
            int best = 0;
            foreach (var kv in kills)
                if (!drew.Contains(kv.Key)) best = Math.Max(best, kv.Value);
            total = needed;
            met = Math.Min(best, needed);
            result = best >= needed;
            Plugin.Log.LogInfo($"GHRule KillsWithoutModifier card 0 mastery: best turn {best} kills without a modifier draw (needs {needed}){(result ? ": met" : "")}");
        }

        private static void PoisonEveryRound(List<CPlayerStats> stats, ref int total, ref int met, ref bool result)
        {
            List<int> rounds = stats.SelectMany(s => s.StartTurn ?? Enumerable.Empty<CPlayerStatsStartTurn>()).Where(x => x != null && x.Round > 0)
                .Select(x => x.Round).Distinct().ToList();
            CPlayerActor ph = GHPlague.Plagueherald();
            int marked = ph?.GetCharacterResourceAmount(PoisonRounds) ?? 0;
            total = Math.Max(1, rounds.Count);
            met = Math.Min(marked, total);
            result = rounds.Count > 0 && marked >= rounds.Count;
            Plugin.Log.LogInfo($"GHRule PoisonEveryRound card 0 mastery: Poison given or removed in {marked} rounds of {rounds.Count} with a turn{(result ? ": met" : "")}");
        }

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_PH2_THROUGH"] = "Heal {0} for {1} as if they did not have Poison? Their Poison stays (a normal heal removes the Poison instead of healing).",
            ["FHMODS_PH2_THROUGH_YES"] = "Heal through Poison",
            ["FHMODS_PH2_THROUGH_NO"] = "Normal heal",
            ["FHMODS_PH2_SICK"] = "Sickening Strength: {0} may suffer 2 damage to immediately play one card from their hand (top or bottom action). Do it?",
            ["FHMODS_PH2_SICK_YES"] = "Suffer 2, play a card",
            ["FHMODS_PH2_SICK_NO"] = "No",
            ["FHMODS_PH2_PURGE"] = "Purging Rest: remove Poison from {0} to add +1 Heal to your long rest heal?",
            ["FHMODS_PH2_PURGE_YES"] = "Remove Poison",
            ["FHMODS_PH2_PURGE_NO"] = "No",
            ["FHMODS_PH2_XORN"] = "Xorn's Last Gift (once each scenario): {0} would die with Plague. Control it first? It performs the abilities on its ability card, adding Plague to all its attacks, then it dies.",
            ["FHMODS_PH2_XORN_YES"] = "Control it",
            ["FHMODS_PH2_XORN_NO"] = "Let it die",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_PH2_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }

        // ------------------------------------------------------------------ Wretched Yield top: Plague before the attack

        // "Heal X, self, where X is the number of Plague the target of the attack ability had before the attack": the
        // count is noted when the attack's first target is about to draw its modifiers (a Carrier perk modifier drawn on
        // this attack would give it one more Plague first), into the hidden saved count GH2ECount9 on the Plagueherald
        // (GHSavedCounts slot 9), which the card's THeal reads. Reset when the attack starts (no target = no stale value).
        internal const int WretchedYieldCard = 33009, SlotWretchedPlague = 9;

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.Start))]
        [HarmonyPrefix]
        private static void WretchedStart(CAbilityAttack __instance, CActor targetingActor)
        {
            try
            {
                CActor me = targetingActor ?? __instance?.TargetingActor;
                if (__instance?.Name != "TAttack" || GHPlague.CardOf(__instance) != WretchedYieldCard || !(me is CPlayerActor p) || p.Class?.ID != PH) return;
                if (__instance.IsAttackAbilityWithOverrides) return;
                GHSavedCounts.Set(me, SlotWretchedPlague, 0);
            }
            catch (Exception e) { Report("Wretched Yield start", e); }
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.DrawAttackModifierCards))]
        [HarmonyPrefix]
        private static void WretchedBefore(CActor actor, CAbilityAttack attackAbility, CActor currentTarget)
        {
            try
            {
                if (attackAbility == null || currentTarget == null || attackAbility.Name != "TAttack" || GHPlague.CardOf(attackAbility) != WretchedYieldCard) return;
                if (!(actor is CPlayerActor p) || p.Class?.ID != PH) return;
                if (attackAbility.ActorsTargeted != null && attackAbility.ActorsTargeted.Any(t => t != null && t != currentTarget)) return;   // the first target only
                int n = currentTarget.GetCharacterResourceAmount(GHPlague.Plague);
                GHSavedCounts.Set(actor, SlotWretchedPlague, n);
                Rule("WretchedYield", WretchedYieldCard, $"{GHPlague.NameOf(currentTarget)} has {n} Plague before the attack: Heal {n}");
            }
            catch (Exception e) { Report("Wretched Yield", e); }
        }
    }
}
