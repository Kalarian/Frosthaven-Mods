using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// The Bruiser's (2nd edition) card rules Frosthaven's engine doesn't have (2e card-accuracy pass, task rest-a); each is
    /// gated on the pack's card IDs or a misc var on the pack's card, so Frosthaven's own cards never change:
    ///   - Fearsome Taunt bottom (card 32012): "you MAY have that attack target you instead": the engine's redirect is
    ///     automatic; the Bruiser's player is now asked each time (the game's own yes/no box, network-synced).
    ///   - Defensive Tactics top (card 32021): "The first time each round that you gain Shield or Retaliate from one of your
    ///     ability or attack modifier cards, gain Shield 1 and Retaliate 1 for the round": the card's persistent Shield 1
    ///     and Retaliate 1 count only in a round in which the Bruiser gained a shield or retaliate from an ability card of
    ///     his or a modifier (not an item, not this card), from that moment to the round's end.
    ///   - Let Fly top (push marked GH2E_PushPathDamage: N): "Each enemy occupying a hex the target enters suffers damage
    ///     N and gains Wound": after the push, the enemies standing on the hexes of the push path (not the target).
    ///   - Brute Force bottom (card 32028, persistent): "Whenever you Push or Pull an enemy at least one hex, after the
    ///     forced movement, that enemy and all enemies adjacent to it suffer damage 1".
    ///   - War Cry perk (its aura marked GH2E_WarCry): "Each character gains advantage on their first attack during the
    ///     first round of each scenario": the round-1 aura stops counting for a character once its first attack is done.
    ///   - Spiked Hide perk modifier (AMC_GH2E_Bruiser_SpikedHide_P0_R): drawn and used = one BruiserSpikedHide token on the
    ///     Bruiser (the card "in the active area", out of the modifier deck until spent); the retaliate itself is in
    ///     GHRetaliate2e, and it triggers Defensive Tactics (a retaliate from an attack modifier card).
    /// Every action logs "GHRule &lt;Rule&gt; card N ...".
    /// </summary>
    [HarmonyPatch]
    internal static class GHBruiser2e
    {
        internal const string ClassId = "BruiserID";
        internal const int FearsomeTauntCard = 32012, DefensiveTacticsCard = 32021, LetFlyCard = 32024, BruteForceCard = 32028;

        private static string Name(CActor a) => GHPlague.NameOf(a);
        private static int CardOf(CAbility a) => GHPlague.CardOf(a);
        private static int Round => ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;

        private static ScenarioState _scenario;
        private static readonly Dictionary<CActor, int> TacticsRound = new Dictionary<CActor, int>();
        private static readonly HashSet<CActor> FirstAttackDone = new HashSet<CActor>();

        private static void Fresh()
        {
            ScenarioState s = ScenarioManager.CurrentScenarioState;
            if (ReferenceEquals(s, _scenario)) return;
            _scenario = s;
            TacticsRound.Clear();
            FirstAttackDone.Clear();
        }

        private static CActiveBonus CardBonus(CActor a, int card) =>
            a?.ActiveBonuses?.FirstOrDefault(b => b != null && !b.Finished() && b.BaseCard != null && b.BaseCard.ID == card);

        // ------------------------------------------------------------------ Fearsome Taunt

        [HarmonyPatch(typeof(CActor), nameof(CActor.ApplyRedirector))]
        [HarmonyPostfix]
        private static void MayRedirect(CActor __instance, CAbility ability, CActor originalTargetActor, ref CActor __result)
        {
            try
            {
                if (__result == originalTargetActor || __result != __instance) return;
                CActiveBonus taunt = __instance.ActiveBonuses?.FirstOrDefault(b => b != null && !b.Finished() && b.BaseCard != null
                    && b.BaseCard.ID == FearsomeTauntCard && b.Ability is CAbilityRedirect);
                if (taunt == null) return;
                CActor attacker = ability?.TargetingActor;
                bool yes = GHPlague.Ask(__instance, true, "FHMODS_RA_TAUNT", "FHMODS_RA_YES", "FHMODS_RA_NO", Name(attacker), Name(originalTargetActor), Name(__instance));
                Plugin.Log.LogInfo($"GHRule FearsomeTaunt card {FearsomeTauntCard} {Name(attacker)} attacks {Name(originalTargetActor)}: {(yes ? $"{Name(__instance)} takes the attack" : "the attack stays on its target")}");
                if (!yes) __result = originalTargetActor;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra taunt")) Plugin.Log.LogError("GHBruiser2e (Fearsome Taunt): " + e); }
        }

        // ------------------------------------------------------------------ Defensive Tactics

        // the round is also saved on the Bruiser (GHSavedCounts slot 1 = round number + 1 of the round Defensive Tactics
        // triggered), so a save/reload or a rejoin in the middle of the round keeps the Shield 1 / Retaliate 1
        internal const int SlotTacticsRound = 1;

        private static bool TacticsOn(CActor a)
        {
            Fresh();
            if (a == null) return false;
            if (TacticsRound.TryGetValue(a, out int r) && r == Round) return true;
            if (GHSavedCounts.Get(a, SlotTacticsRound) == Round + 1) { TacticsRound[a] = Round; return true; }
            return false;
        }

        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.ReferenceStrength))]
        [HarmonyPostfix]
        private static void TacticsStrength(CActiveBonus __instance, ref int __result)
        {
            try
            {
                if (__instance.BaseCard == null || __instance.BaseCard.ID != DefensiveTacticsCard) return;
                if (!(__instance.Ability is CAbilityShield) && !(__instance.Ability is CAbilityRetaliate)) return;
                if (!TacticsOn(__instance.Actor)) __result = 0;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra tactics strength")) Plugin.Log.LogError("GHBruiser2e (Defensive Tactics): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityShield), nameof(CAbilityShield.ApplyToActor))]
        [HarmonyPostfix]
        private static void ShieldGained(CAbilityShield __instance, CActor actor, bool __result) => Gained(__instance, actor, __result, "Shield");

        [HarmonyPatch(typeof(CAbilityRetaliate), nameof(CAbilityRetaliate.ApplyToActor))]
        [HarmonyPostfix]
        private static void RetaliateGained(CAbilityRetaliate __instance, CActor actor, bool __result) => Gained(__instance, actor, __result, "Retaliate");

        private static void Gained(CAbility ability, CActor actor, bool applied, string what)
        {
            try
            {
                if (!applied || actor == null || CardBonus(actor, DefensiveTacticsCard) == null) return;
                CBaseCard source = null;
                try { source = ability.GetAbilityBaseCard(); } catch { }
                if (source != null)
                {
                    if (source.ID == DefensiveTacticsCard) return;
                    if (source is CItem || source.IsItemOrPet) return;
                    if (source is CAbilityCard ac && actor is CPlayerActor p && !string.IsNullOrEmpty(ac.ClassID) && ac.ClassID != p.Class?.ID) return;   // another player's card
                }
                // no base card: counts only for an attack modifier card's ability (CAbilityAttack.ProcessModifierAbilities
                // runs those with the attack as ParentAbility; IsModifierAbility for the engine's own modifier abilities)
                if (source == null && !ability.IsModifierAbility && !(ability.ParentAbility is CAbilityAttack)) return;
                Trigger(actor, what, source == null ? "a modifier card" : "card " + source.ID);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra tactics")) Plugin.Log.LogError("GHBruiser2e (Defensive Tactics): " + e); }
        }

        /// <summary>Spiked Hide (perk modifier card, GHRetaliate2e) gave the Bruiser Retaliate 2: a retaliate "from one of
        /// your attack modifier cards".</summary>
        /// Returns the card's Retaliate that it turned on (for the attack being answered), 0 if it was on already.
        internal static int SpikedHideRetaliate(CActor actor)
        {
            try
            {
                if (actor == null || CardBonus(actor, DefensiveTacticsCard) == null || TacticsOn(actor)) return 0;
                Trigger(actor, "Retaliate", "the Spiked Hide modifier card");
                return actor.ActiveBonuses.Where(b => b != null && !b.Finished() && b.BaseCard != null && b.BaseCard.ID == DefensiveTacticsCard && b.Ability is CAbilityRetaliate)
                    .Sum(b => Math.Max(0, b.Ability.Strength));
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra tactics spiked")) Plugin.Log.LogError("GHBruiser2e (Defensive Tactics, Spiked Hide): " + e); }
            return 0;
        }

        private static void Trigger(CActor actor, string what, string from)
        {
            {
                if (TacticsOn(actor)) return;
                TacticsRound[actor] = Round;
                GHSavedCounts.Set(actor, SlotTacticsRound, Round + 1);
                actor.CheckForCachedValuesAfterActiveBonusesUpdate();
                Plugin.Log.LogInfo($"GHRule DefensiveTactics card {DefensiveTacticsCard} {Name(actor)} gained {what} from {from}: Shield 1 and Retaliate 1 for the rest of round {Round}");
            }
        }

        // ------------------------------------------------------------------ Let Fly, Brute Force (after a push or pull)

        private static readonly FieldInfo PushTarget = AccessTools.Field(typeof(CAbilityPush), "m_CurrentTarget");
        private static readonly FieldInfo PullTarget = AccessTools.Field(typeof(CAbilityPull), "m_CurrentTarget");

        private struct Forced { public bool Final; public CActor Target; }

        [HarmonyPatch(typeof(CAbilityPush), nameof(CAbilityPush.Perform))]
        [HarmonyPrefix]
        private static void PushStep(CAbilityPush __instance, out Forced __state)
        {
            __state = default(Forced);
            try { __state = new Forced { Final = __instance.State == CAbilityPush.EPushState.ActorFinalizePush, Target = PushTarget?.GetValue(__instance) as CActor }; }
            catch (Exception e) { if (GHClasses.Reported.Add("ra push step")) Plugin.Log.LogError("GHBruiser2e (push step): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityPush), nameof(CAbilityPush.Perform))]
        [HarmonyPostfix]
        private static void PushDone(CAbilityPush __instance, bool __result, Forced __state)
        {
            if (!__state.Final || !__result || __state.Target == null) return;
            try { AfterForced(__instance, __state.Target, __instance.m_AbilityPath, __instance.m_StartingPoint, "push"); }
            catch (Exception e) { if (GHClasses.Reported.Add("ra push")) Plugin.Log.LogError("GHBruiser2e (push): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityPull), nameof(CAbilityPull.Perform))]
        [HarmonyPrefix]
        private static void PullStep(CAbilityPull __instance, out Forced __state)
        {
            __state = default(Forced);
            try { __state = new Forced { Final = __instance.State == CAbilityPull.EPullState.ActorFinalizePull, Target = PullTarget?.GetValue(__instance) as CActor }; }
            catch (Exception e) { if (GHClasses.Reported.Add("ra pull step")) Plugin.Log.LogError("GHBruiser2e (pull step): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityPull), nameof(CAbilityPull.Perform))]
        [HarmonyPostfix]
        private static void PullDone(CAbilityPull __instance, bool __result, Forced __state)
        {
            if (!__state.Final || !__result || __state.Target == null) return;
            try { AfterForced(__instance, __state.Target, __instance.m_AbilityPath, __instance.m_StartingPoint, "pull"); }
            catch (Exception e) { if (GHClasses.Reported.Add("ra pull")) Plugin.Log.LogError("GHBruiser2e (pull): " + e); }
        }

        private static void AfterForced(CAbility ability, CActor target, List<Point> path, Point start, string kind)
        {
            CActor me = ability.TargetingActor;
            if (me == null || path == null || path.Count == 0) return;
            bool moved = target.ArrayIndex.X != start.X || target.ArrayIndex.Y != start.Y;
            if (moved && CActor.AreActorsEnemies(target.Type, me.Type, target, me)) GHMasteriesRestA.NoteForced(me, target);
            // Let Fly: the enemies on the hexes the target entered
            int pathDamage = 0;
            try { if (ability.MiscVars != null && ability.MiscVars.Any(x => x.Item1 == "GH2E_PushPathDamage")) pathDamage = ability.GetMiscVar("GH2E_PushPathDamage").GetInt(); } catch { }
            if (pathDamage > 0)
            {
                var hexes = new HashSet<string>(path.Select(p => p.X + "," + p.Y));
                List<CActor> hit = ScenarioManager.Scenario.AllAliveActors.Where(x => x != null && x != target && !x.IsDead
                    && hexes.Contains(x.ArrayIndex.X + "," + x.ArrayIndex.Y) && CActor.AreActorsEnemies(x.Type, me.Type, x, me)).ToList();
                foreach (CActor e in hit)
                {
                    bool alive = Damage(e, pathDamage, me, ability);
                    if (alive) e.ApplyCondition(me, CCondition.ENegativeCondition.Wound);
                }
                Plugin.Log.LogInfo($"GHRule PushPathDamage card {CardOf(ability)} {Name(target)} pushed through {path.Count} hex(es): {hit.Count} enemy(ies) on its path suffer {pathDamage} and gain Wound{(hit.Count > 0 ? " (" + string.Join(", ", hit.Select(Name)) + ")" : "")}");
            }
            // Brute Force
            // the bottom's persistent bonus only (the top's round Shield and reaction carry the same card ID)
            bool bruteForce = me.ActiveBonuses?.Any(b => b != null && !b.Finished() && b.BaseCard != null && b.BaseCard.ID == BruteForceCard
                && b.Ability?.Name == "BForcedMoveBuff") == true;
            if (moved && bruteForce && CActor.AreActorsEnemies(target.Type, me.Type, target, me))
            {
                var hit = new List<CActor>();
                if (!target.IsDead && ScenarioManager.Scenario.HasActor(target)) hit.Add(target);
                hit.AddRange(ScenarioManager.Scenario.AllAliveActors.Where(x => x != null && x != target && !x.IsDead && target.IsAdjacentTo(x)
                    && CActor.AreActorsEnemies(x.Type, me.Type, x, me)));
                foreach (CActor e in hit.ToList()) Damage(e, 1, me, ability);
                Plugin.Log.LogInfo($"GHRule BruteForce card {BruteForceCard} after the {kind} of {Name(target)}: damage 1 to {hit.Count} enemy(ies) ({string.Join(", ", hit.Select(Name))})");
            }
        }

        /// <summary>Damage from the Bruiser, the way the push's own extra damage is dealt. True if the figure survived.</summary>
        private static bool Damage(CActor e, int amount, CActor me, CAbility ability)
        {
            if (e == null || e.IsDead || !ScenarioManager.Scenario.HasActor(e)) return false;
            int health = e.Health;
            bool asleep = e.Tokens.HasKey(CCondition.ENegativeCondition.Sleep);
            GameState.ActorBeenDamaged(e, amount, checkIfPlayerCanAvoidDamage: true, me, ability, ability.AbilityType);
            if (GameState.ActorHealthCheck(me, e, isTrap: false, isTerrain: false, asleep))
            {
                ScenarioRuleClient.MessageHandler(new CActorBeenDamaged_MessageData(me)
                {
                    m_ActorBeingDamaged = e, m_DamageAbility = null, m_ActorOriginalHealth = health, m_ActorWasAsleep = asleep
                });
                return !e.IsDead;
            }
            return false;
        }

        // ------------------------------------------------------------------ War Cry perk

        private static readonly HashSet<string> WarCryLogged = new HashSet<string>();

        [HarmonyPatch(typeof(CBespokeBehaviour), "IsValidTarget")]
        [HarmonyPostfix]
        private static void WarCryFirstAttack(CBespokeBehaviour __instance, CAbility ability, CActor target, ref bool __result)
        {
            try
            {
                if (!__result || !(__instance is CAttackActiveBonus_BuffAttack) || !(ability is CAbilityAttack)) return;
                CActiveBonus bonus = __instance.ActiveBonus;
                if (bonus == null || !GHPlague.Marked(bonus.Ability, "GH2E_WarCry")) return;
                Fresh();
                CActor who = ability.TargetingActor;
                if (who != null && FirstAttackDone.Contains(who)) { __result = false; return; }
                // each target of an attack ability is a separate attack: only the first target attacked has advantage
                CActor first = ability.ActorsTargeted?.FirstOrDefault(x => x != null) ?? ability.ActorsToTarget?.FirstOrDefault(x => x != null)
                               ?? (ability as CAbilityAttack)?.AttackSummary?.Targets?.FirstOrDefault(t => t?.ActorToAttack != null)?.ActorToAttack;
                if (target != null && first != null && target != first)
                {
                    __result = false;
                    string k2 = ability.ID + "/" + who?.ActorGuid + "/not first/" + target.ActorGuid;
                    if (WarCryLogged.Add(k2)) Plugin.Log.LogInfo($"GHRule WarCry card 0 perk War Cry: {Name(who)}'s attack on {Name(target)} is not the first attack (first target {Name(first)}): no advantage");
                    return;
                }
                string key = ability.ID + "/" + who?.ActorGuid;
                if (WarCryLogged.Add(key))
                {
                    if (WarCryLogged.Count > 200) { WarCryLogged.Clear(); WarCryLogged.Add(key); }
                    Plugin.Log.LogInfo($"GHRule WarCry card 0 perk War Cry: {Name(who)}'s first attack this scenario (round 1) has advantage");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra war cry")) Plugin.Log.LogError("GHBruiser2e (War Cry): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityAttack), nameof(CAbilityAttack.AbilityEnded))]
        [HarmonyPostfix]
        private static void AttackEnded(CAbilityAttack __instance)
        {
            try
            {
                if (!__instance.AbilityHasHappened || !(__instance.TargetingActor is CPlayerActor p) || Round != 1) return;
                Fresh();
                FirstAttackDone.Add(p);
                GHMasteriesRestA.NoteAttack(p);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra first attack")) Plugin.Log.LogError("GHBruiser2e (first attack): " + e); }
        }

        // ------------------------------------------------------------------ Rested and Ready perk

        // "Whenever you long rest, add +1 Move to your first move ability the following round": the perk's after-long-rest
        // BuffMove (MiscVars GH2E_RestedAndReady) counts only in the round after the long rest (not a move granted later in
        // the rest's own round) and ends at the end of that round if no move used it.
        private static bool RestedBonus(CBespokeBehaviour b) => b?.ActiveBonus != null && GHPlague.Marked(b.ActiveBonus.Ability, "GH2E_RestedAndReady");

        [HarmonyPatch(typeof(CMoveActiveBonus_BuffMove), nameof(CMoveActiveBonus_BuffMove.OnMoving))]
        [HarmonyPrefix]
        private static bool RestedMoving(CMoveActiveBonus_BuffMove __instance)
        {
            try
            {
                if (!RestedBonus(__instance)) return true;
                if (__instance.ActiveBonus.InternalRoundNumber == 1) return true;
                Plugin.Log.LogInfo($"GHRule RestedAndReady card 0 perk Rested and Ready: not the round after the long rest (round {Round}): no +1 Move");
                return false;
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra rested")) Plugin.Log.LogError("GHBruiser2e (Rested and Ready): " + e); return true; }
        }

        [HarmonyPatch(typeof(CMoveActiveBonus_BuffMove), nameof(CMoveActiveBonus_BuffMove.ReferenceStrength))]
        [HarmonyPostfix]
        private static void RestedStrength(CMoveActiveBonus_BuffMove __instance, ref int __result)
        {
            try { if (__result != 0 && RestedBonus(__instance) && __instance.ActiveBonus.InternalRoundNumber != 1) __result = 0; }
            catch { }
        }

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.EndRound))]
        [HarmonyPrefix]
        private static void RestedRoundEnd()
        {
            try
            {
                foreach (CPlayerActor p in ScenarioManager.Scenario?.PlayerActors?.Where(x => x?.Class?.ID == ClassId).ToList() ?? new List<CPlayerActor>())
                    foreach (CActiveBonus b in p.ActiveBonuses?.Where(b => b != null && !b.Finished() && GHPlague.Marked(b.Ability, "GH2E_RestedAndReady") && b.InternalRoundNumber >= 1).ToList() ?? new List<CActiveBonus>())
                    {
                        b.Finish();
                        Plugin.Log.LogInfo($"GHRule RestedAndReady card 0 perk Rested and Ready: the round after the long rest ends unused: the +1 Move ends");
                    }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra rested end")) Plugin.Log.LogError("GHBruiser2e (Rested and Ready, round end): " + e); }
        }

        // ------------------------------------------------------------------ Spiked Hide drawn

        // "Add this card to your active area": only a card the attack uses (without advantage/disadvantage every drawn card,
        // asked as it is drawn; with advantage/disadvantage the used list CActor.DrawAttackModifierCards returns: a
        // rolling card drawn with disadvantage is not used). The card leaves the modifier deck while it is held (taken back
        // out of the discard pile, where the engine put it) and returns to the discard pile when it is spent (GHRetaliate2e
        // calls SpikedHideSpent).
        [HarmonyPatch(typeof(GameState), nameof(GameState.ChangeModifierValues))]
        [HarmonyPostfix]
        private static void SpikedHideDrawn(CActor actor, List<AttackModifierYMLData> modifiers, EAdvantageStatuses advantageStatus)
        {
            if (advantageStatus != EAdvantageStatuses.None) return;
            SpikedHideHeld(actor, modifiers);
        }

        [HarmonyPatch(typeof(CActor), nameof(CActor.DrawAttackModifierCards))]
        [HarmonyPostfix]
        private static void SpikedHideUsed(CActor actor, EAdvantageStatuses advStatus, List<AttackModifierYMLData> __result)
        {
            if (advStatus == EAdvantageStatuses.None) return;
            SpikedHideHeld(actor, __result);
        }

        private static void SpikedHideHeld(CActor actor, List<AttackModifierYMLData> modifiers)
        {
            try
            {
                if (modifiers == null || !(actor is CPlayerActor p) || p.Class?.ID != ClassId) return;
                int n = modifiers.Count(c => c != null && c.Name == GHRetaliate2e.SpikedHideCard);
                if (n <= 0) return;
                actor.AddCharacterResource(GHRetaliate2e.SpikedHideResource, n);
                int out_ = 0;
                List<AttackModifierYMLData> discard = p.CharacterClass?.DiscardedAttackModifierCards;
                for (int i = 0; i < n && discard != null; i++)
                {
                    int at = discard.FindLastIndex(c => c != null && c.Name == GHRetaliate2e.SpikedHideCard);
                    if (at < 0) break;
                    discard.RemoveAt(at);
                    out_++;
                }
                Plugin.Log.LogInfo($"GHRule SpikedHide card 0 perk Spiked Hide: drawn, now {actor.GetCharacterResourceAmount(GHRetaliate2e.SpikedHideResource)} in {Name(actor)}'s active area (Retaliate 2 on the next attack by an adjacent enemy); {out_} card(s) out of the modifier deck until spent");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra spiked hide draw")) Plugin.Log.LogError("GHBruiser2e (Spiked Hide): " + e); }
        }

        /// <summary>The held Spiked Hide cards were discarded for Retaliate: back onto the modifier discard pile.</summary>
        internal static void SpikedHideSpent(CActor actor, int n)
        {
            try
            {
                if (!(actor is CPlayerActor p) || p.CharacterClass == null || n <= 0) return;
                p.CharacterClass.AddAdditionalModifierCards(Enumerable.Repeat(GHRetaliate2e.SpikedHideCard, n).ToList(), toDiscard: true);
            }
            catch (Exception e) { if (GHClasses.Reported.Add("ra spiked hide spent")) Plugin.Log.LogError("GHBruiser2e (Spiked Hide back to the discard pile): " + e); }
        }
    }
}
