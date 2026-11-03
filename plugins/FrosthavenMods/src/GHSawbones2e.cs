using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Sawbones' printed rules Frosthaven's abilities can't express. Each is switched on by a misc var on
    /// a pack card's ability or by a pack perk ID, so nothing changes without the 2nd edition pack:
    ///   - GH2E_ChooseCondition (Curative Mixture top and bottom, Return the Favor bottom: "remove one negative condition"):
    ///     when the target holds two or more of the conditions the ability may remove, the performer is asked which one
    ///     (the engine's own yes/no box, one condition at a time; Frosthaven's pick-one Choose works on yourself only).
    ///     Logs "GHRule ChooseCondition card N ...".
    ///   - GH2E_TransferRemoved (Return the Favor bottom): "if you do, ... you may give one adjacent enemy the negative
    ///     condition you removed": each enemy next to the Sawbones is offered in turn; the one accepted gains it.
    ///     Logs "GHRule ReturnTheFavor card 33223 ...".
    ///   - GH2E_PrescriptionBack (Precaution top's Shield): "each ally may move the character token backward one slot on
    ///     one active Prescription they have": each ally the Shield reaches that holds a Prescription with a used slot is
    ///     asked (Prescription by Prescription) and the first accepted gets one slot back. GH2E_PrescriptionBackOne (perk
    ///     Restorative Rounds' pack): "move the token on one ally's active Prescription backward one slot": the Sawbones
    ///     picks one among all allies' Prescriptions. (Frosthaven's MoveActiveBonusTracker only moves bonuses of the
    ///     holder's own class cards; a Prescription stays a Sawbones card in the ally's active area.)
    ///     Logs "GHRule PrescriptionBack card N ...".
    ///   - GH2E_SecretOfTheOak (Secret of the Oak bottom's persistent marker): while it is out, after each heal ability of
    ///     a Medical Pack (33230-33235) the character who performed it gets the card's template BSecretChoice (a skippable
    ///     Choose: +4 Attack on the next attack this round, or +4 Move on the next move ability this round).
    ///     Logs "GHRule SecretOfTheOak card 33228 ...".
    ///   - GH2E_AlliesPlayPacks (Take Your Medicine bottom): "in initiative order, each of your allies may immediately
    ///     play one Medical Pack from their hand to perform one action of the card": each ally holding a pack, in
    ///     initiative order, performs the card's template BPlayPack (SelectAndPlayCard from their own hand, supply cards
    ///     only, skippable), one ally after the other. Logs "GHRule AlliesPlayPacks card 33226 ...".
    ///     The Sawbones' action and hand come back after each play through GHAllyCardPlay.
    ///   - Templates: an ability tagged GH2E_Template is taken out of its card's action when the card is read (so it never
    ///     runs as part of the card) and kept by name for the rules above.
    ///   - Perk Sawbones208, Friend of the Oak (the FAQ's Devout: "works just as normal for the Temple, once it is built"):
    ///     in each outpost phase (one per calendar week) one Temple of the Great Oak blessing for the Sawbones or an ally
    ///     costs no gold (no refunds; the blessing's cost is Frosthaven's). Logs "GHRule FriendOfTheOak card 0 ...". The
    ///     "used this outpost phase" mark is saved in the campaign ("FHMODS:FriendOfTheOak=<week>").
    /// </summary>
    [HarmonyPatch]
    internal static class GHSawbones2e
    {
        internal const string ChooseTag = "GH2E_ChooseCondition";
        internal const string TransferTag = "GH2E_TransferRemoved";
        internal const string BackTag = "GH2E_PrescriptionBack";
        internal const string BackOneTag = "GH2E_PrescriptionBackOne";
        internal const string SecretTag = "GH2E_SecretOfTheOak";
        internal const string PlayPacksTag = "GH2E_AlliesPlayPacks";
        internal const string TemplateTag = "GH2E_Template";
        internal const string PrescriptionTag = "GH2E_Prescription";
        internal const string FriendOfTheOak = "Sawbones208";

        internal static readonly Dictionary<string, CAbility> Templates = new Dictionary<string, CAbility>(StringComparer.Ordinal);

        private static void Log(string rule, int card, string detail) => Plugin.Log.LogInfo($"GHRule {rule} card {card} {detail}");

        internal static bool Tagged(CAbility a, string tag)
        {
            try { return a?.MiscVars != null && a.GetMiscVar(tag).GetBool(); }
            catch (Exception) { return false; }
        }

        private static int CardOf(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; }
            catch (Exception) { return 0; }
        }

        private static string Name(CActor a) => a?.GetPrefabName() ?? "?";

        private static bool Ask(CActor asker, string key, params object[] args) =>
            GHPlague.Ask(asker, false, key, key + "_YES", key + "_NO", args);

        private static void Stack(CActor performer, CAbility ability, bool overrideActor)
        {
            if (!(PhaseManager.CurrentPhase is CPhaseAction phase)) return;
            if (overrideActor && GameState.InternalCurrentActor != performer) GameState.OverrideCurrentActorForOneAction(performer);
            phase.StackNextAbilities(new List<CAbility> { ability }, performer, killAfter: false, stackToNextCurrent: true, copyCurrentActionID: !overrideActor);
        }

        // ------------------------------------------------------------------ "remove one negative condition": the performer picks

        [HarmonyPatch(typeof(CAbilityRemoveConditions), nameof(CAbilityRemoveConditions.ApplyToActor))]
        [HarmonyPrefix]
        private static void PickCondition(CAbilityRemoveConditions __instance, CActor actor, out List<CCondition.ENegativeCondition> __state)
        {
            __state = null;
            try
            {
                if (actor == null || !Tagged(__instance, ChooseTag) || __instance.OriginalNegativeConditions == null) return;
                var held = __instance.OriginalNegativeConditions.Keys.Where(c => actor.Tokens.HasKey(c)).ToList();
                __state = held;
                if (held.Count < 2 || __instance.Strength != 1) return;
                CActor performer = __instance.TargetingActor;
                CCondition.ENegativeCondition chosen = held[held.Count - 1];
                string how = "the last one offered (every offer declined three times)";
                bool picked = false;
                for (int pass = 0; pass < 3 && !picked; pass++)
                    foreach (CCondition.ENegativeCondition c in held)
                        if (Ask(performer, "FHMODS_SAW_REMOVE", c.ToString(), Name(actor)))
                        {
                            chosen = c; picked = true; how = "picked by the performer";
                            break;
                        }
                CAbility value = __instance.OriginalNegativeConditions[chosen];
                __instance.NegativeConditions = new Dictionary<CCondition.ENegativeCondition, CAbility> { [chosen] = value };
                Log("ChooseCondition", CardOf(__instance), $"{Name(performer)} removes {chosen} from {Name(actor)} ({how}; it had {string.Join(", ", held)})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw choose")) Plugin.Log.LogError("GHSawbones2e (choose condition): " + e); }
        }

        [HarmonyPatch(typeof(CAbilityRemoveConditions), nameof(CAbilityRemoveConditions.ApplyToActor))]
        [HarmonyPostfix]
        private static void TransferRemoved(CAbilityRemoveConditions __instance, CActor actor, List<CCondition.ENegativeCondition> __state)
        {
            try
            {
                if (__state == null || actor == null || !Tagged(__instance, TransferTag)) return;
                var removed = __state.Where(c => !actor.Tokens.HasKey(c)).ToList();
                if (removed.Count == 0) return;
                CCondition.ENegativeCondition condition = removed[0];
                CActor me = __instance.TargetingActor;
                if (me == null || me.IsDead) return;
                var enemies = ScenarioManager.Scenario.AllAliveActors.Where(x => x != null && !x.IsDead && CActor.AreActorsEnemies(me.Type, x.Type, me, x)
                    && ScenarioManager.IsTileAdjacent(me.ArrayIndex.X, me.ArrayIndex.Y, x.ArrayIndex.X, x.ArrayIndex.Y)).ToList();
                int card = CardOf(__instance);
                if (enemies.Count == 0)
                {
                    Log("ReturnTheFavor", card, $"{condition} removed from {Name(actor)}; no enemy adjacent to {Name(me)}: nothing to give");
                    return;
                }
                CActor target = enemies.FirstOrDefault(x => Ask(me, "FHMODS_SAW_TRANSFER", condition.ToString(), Name(x)));
                if (target == null)
                {
                    Log("ReturnTheFavor", card, $"{condition} removed from {Name(actor)}; {Name(me)} gives it to nobody");
                    return;
                }
                CAbility give = CAbility.CreateAbility(CCondition.AbilityTypeFromNegativeCondition(condition), CAbilityFilterContainer.CreateDefaultFilter(), me.IsOriginalMonsterType, true, 1);
                give.Start(me, me);
                (give as CAbilityTargeting)?.ApplyToActor(target);
                Log("ReturnTheFavor", card, $"{condition} removed from {Name(actor)} and given to {Name(target)}");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw transfer")) Plugin.Log.LogError("GHSawbones2e (Return the Favor): " + e); }
        }

        // ------------------------------------------------------------------ Prescription tokens moved back

        internal static bool IsPrescription(CActiveBonus b) => b?.Ability != null && Tagged(b.Ability, PrescriptionTag);

        private static List<CActiveBonus> MovablePrescriptions(CActor holder) =>
            (holder?.ActiveBonuses ?? new List<CActiveBonus>()).Where(b => IsPrescription(b) && b.Actor == holder && !b.Finished() && !b.Finishing()
                && b.HasTracker && b.TrackerIndex > 0).Distinct().ToList();

        private static void MoveBack(CActiveBonus b, CActor by, int card, string how)
        {
            b.TrackerIndex -= 1;
            ScenarioRuleClient.MessageHandler(new CActiveBonusTrackerIncremented_MessageData(b.Actor) { m_Actor = b.Actor, m_ActiveBonus = b });
            Log("PrescriptionBack", card, $"{b.BaseCard?.Name} on {Name(b.Actor)}: token moved back one slot by {Name(by)} ({how}; {b.Remaining} uses left)");
        }

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.ApplyToActor))]
        [HarmonyPostfix]
        private static void PrescriptionBack(CAbility __instance, CActor actor)
        {
            try
            {
                if (__instance?.MiscVars == null || actor == null) return;
                bool each = Tagged(__instance, BackTag), one = !each && Tagged(__instance, BackOneTag);
                if (!each && !one) return;
                if (!ScenarioRuleClient.IsWorkerThread) return;
                int card = CardOf(__instance);
                CActor me = __instance.TargetingActor;
                if (each)
                {
                    if (actor == me) return;                                    // "each ally"
                    List<CActiveBonus> mine = MovablePrescriptions(actor);
                    if (mine.Count == 0) return;
                    CActiveBonus pick = mine.FirstOrDefault(b => Ask(actor, "FHMODS_SAW_BACK", b.BaseCard?.Name ?? "?", Name(actor)));
                    if (pick != null) MoveBack(pick, actor, card, "Precaution");
                    else Log("PrescriptionBack", card, $"{Name(actor)} keeps its Prescription tokens where they are");
                    return;
                }
                var all = ScenarioManager.Scenario.PlayerActors.Where(p => p != null && !p.IsDead && p != me).SelectMany(MovablePrescriptions).ToList();
                if (all.Count == 0)
                {
                    Log("PrescriptionBack", card, "Restorative Rounds: no ally's Prescription has a used slot");
                    return;
                }
                CActiveBonus chosen = all.FirstOrDefault(b => Ask(me, "FHMODS_SAW_BACK", b.BaseCard?.Name ?? "?", Name(b.Actor)));
                if (chosen != null) MoveBack(chosen, me, card, "Restorative Rounds");
                else Log("PrescriptionBack", card, "Restorative Rounds: no token moved (declined)");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw back")) Plugin.Log.LogError("GHSawbones2e (Prescription back): " + e); }
        }

        // ------------------------------------------------------------------ Secret of the Oak bottom

        private static readonly HashSet<CAbility> SecretDone = new HashSet<CAbility>();

        [HarmonyPatch(typeof(CAbilityHeal), nameof(CAbilityHeal.AbilityEnded))]
        [HarmonyPostfix]
        private static void AfterPackHeal(CAbilityHeal __instance)
        {
            try
            {
                if (__instance == null || !__instance.AbilityHasHappened || !GHPerks.IsMedicalPack2e(__instance)) return;
                CActor performer = __instance.TargetingActor;
                if (!(performer is CPlayerActor) || performer.IsDead) return;
                CActiveBonus secret = ScenarioManager.Scenario.PlayerActors.Where(p => p != null && !p.IsDead)
                    .SelectMany(p => p.ActiveBonuses ?? new List<CActiveBonus>())
                    .FirstOrDefault(b => b?.Ability != null && !b.Finished() && !b.Finishing() && Tagged(b.Ability, SecretTag));
                if (secret == null) return;
                if (SecretDone.Count > 200) SecretDone.Clear();
                if (!SecretDone.Add(__instance)) return;
                if (!Templates.TryGetValue("BSecretChoice", out CAbility template))
                {
                    if (GHClasses.Reported.Add("saw secret template")) Plugin.Log.LogWarning("GHSawbones2e: Secret of the Oak's choice template was not read");
                    return;
                }
                CAbility copy = CAbility.CopyAbility(template, generateNewID: true);
                Stack(performer, copy, overrideActor: GameState.InternalCurrentActor != performer);
                Log("SecretOfTheOak", secret.BaseCard?.ID ?? 0, $"{Name(performer)} performed {__instance.GetAbilityBaseCard()?.Name}'s heal: +4 Attack or +4 Move offered");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw secret")) Plugin.Log.LogError("GHSawbones2e (Secret of the Oak): " + e); }
        }

        // ------------------------------------------------------------------ Take Your Medicine bottom

        private static readonly Queue<CPlayerActor> PackQueue = new Queue<CPlayerActor>();
        private static CAbility _packChain;
        private static int _packCard;

        private static bool HoldsPack(CPlayerActor p) =>
            p != null && !p.IsDead && p.CharacterClass?.HandAbilityCards?.Any(c => c != null && c.SupplyCard) == true;

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void AbilityEnded(CAbility __instance)
        {
            try
            {
                if (__instance == null) return;
                if (_packChain != null && ReferenceEquals(__instance, _packChain))
                {
                    _packChain = null;
                    NextPack();
                    return;
                }
                if (__instance.MiscVars == null || !Tagged(__instance, PlayPacksTag)) return;
                CActor saw = __instance.TargetingActor;
                if (saw == null) return;
                PackQueue.Clear();
                _packChain = null;
                _packCard = CardOf(__instance);
                var allies = ScenarioManager.Scenario.PlayerActors.Where(p => p != null && p != saw && HoldsPack(p))
                    .OrderBy(p => p.Initiative()).ThenBy(p => p.SubInitiative()).ToList();
                Log("AlliesPlayPacks", _packCard, allies.Count == 0 ? "no ally holds a Medical Pack" : "allies offered a Medical Pack play, in initiative order: " + string.Join(", ", allies.Select(Name)));
                foreach (CPlayerActor p in allies) PackQueue.Enqueue(p);
                NextPack();
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw packs")) Plugin.Log.LogError("GHSawbones2e (Take Your Medicine): " + e); }
        }

        private static void NextPack()
        {
            while (PackQueue.Count > 0)
            {
                CPlayerActor ally = PackQueue.Dequeue();
                if (!HoldsPack(ally)) continue;
                if (!Templates.TryGetValue("BPlayPack", out CAbility template))
                {
                    if (GHClasses.Reported.Add("saw pack template")) Plugin.Log.LogWarning("GHSawbones2e: Take Your Medicine's play template was not read");
                    PackQueue.Clear();
                    return;
                }
                CAbility copy = CAbility.CopyAbility(template, generateNewID: true);
                _packChain = copy;
                Stack(ally, copy, overrideActor: true);
                Log("AlliesPlayPacks", _packCard, $"{Name(ally)} may play one Medical Pack from its hand now");
                return;
            }
        }

        // ------------------------------------------------------------------ perk Sedation: "+1, you may gain Disarm to add Stun"

        internal const string SedationCard = "AMC_GH2E_Sawbones2_DisarmStun_P1";

        /// <summary>The Sedation modifier carries one Disarm ability (Filter Self) only as its marker: a Choose on a modifier card
        /// stopped the game ("Invalid targeting type", 2026-11-01) and a Disarm is a kind the engine's modifier code doesn't
        /// run. Before the engine processes the card's abilities (only a card that was USED in the attack gets here, not
        /// the discarded one of an advantage pair), the marker is taken out and the drawer's player is asked; yes = the
        /// target gains Stun (the attack's added effect; none if the target already died) and the drawer gains Disarm.
        /// Logs "GHRule Sedation card 0 ...".</summary>
        [HarmonyPatch(typeof(CAbilityAttack), "ProcessModifierAbilities")]
        [HarmonyPrefix]
        private static void Sedation(CAbilityAttack __instance, List<CAbilityAttack.AttackModifierAbilityInfo> modifierAbilities, CActor targetActor)
        {
            try
            {
                if (modifierAbilities == null || modifierAbilities.Count == 0) return;
                var ours = modifierAbilities.Where(i => i?.Card?.Name == SedationCard).ToList();
                if (ours.Count == 0) return;
                foreach (CAbilityAttack.AttackModifierAbilityInfo info in ours) modifierAbilities.Remove(info);
                CActor me = ours[0].TargetingActor ?? __instance.TargetingActor;
                if (me == null || me.IsMirrorActor()) return;
                bool alive = targetActor != null && !targetActor.IsDead && ScenarioManager.Scenario.HasActor(targetActor);
                if (!alive)
                {
                    Log("Sedation", 0, $"Sedation drawn by {Name(me)}: the target is gone, nothing to Stun (not offered)");
                    return;
                }
                if (!Ask(me, "FHMODS_SAW_SEDATE", Name(targetActor)))
                {
                    Log("Sedation", 0, $"Sedation drawn by {Name(me)}: declined (no Stun, no Disarm)");
                    return;
                }
                targetActor.ApplyCondition(me, CCondition.ENegativeCondition.Stun, 1, EConditionDecTrigger.Turns);
                me.ApplyCondition(me, CCondition.ENegativeCondition.Disarm, 1, EConditionDecTrigger.Turns);
                Log("Sedation", 0, $"Sedation drawn by {Name(me)}: {Name(targetActor)} gains Stun ({(targetActor.Tokens.HasKey(CCondition.ENegativeCondition.Stun) ? "stunned" : "not stunned: immune or prevented")}), "
                                   + $"{Name(me)} gains Disarm ({(me.Tokens.HasKey(CCondition.ENegativeCondition.Disarm) ? "disarmed" : "not disarmed: prevented")})");
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw sedation")) Plugin.Log.LogError("GHSawbones2e (Sedation): " + e); }
        }

        // ------------------------------------------------------------------ perk Friend of the Oak (Temple of the Great Oak)

        // the outpost phase whose free blessing is used: a marker in the campaign save (synced campaign state, the inert
        // spot GHSilentKnife's FHMODS:SKMoney12 uses), so restarting the game or reloading in the same outpost phase
        // doesn't offer a second one
        private const string OakMarker = "FHMODS:FriendOfTheOak=";
        [ThreadStatic] private static FHRL.CharacterState _freeBless;

        private static bool UsedThisWeek(FHRL.CampaignState state) =>
            state?.LootedChestTreasureTableIDs?.Contains(OakMarker + state.Outpost.Week) == true;

        private static void MarkWeek(FHRL.CampaignState state, bool used)
        {
            if (state?.LootedChestTreasureTableIDs == null) return;
            state.LootedChestTreasureTableIDs.RemoveAll(x => x != null && x.StartsWith(OakMarker, StringComparison.Ordinal));
            if (used) state.LootedChestTreasureTableIDs.Add(OakMarker + state.Outpost.Week);
        }

        private static bool HasPerk(FHRL.CharacterState c, string perk) => c?.PerkIDs != null && c.PerkIDs.Any(p => p.ID == perk);

        private static bool FreeAvailable(FHRL.OutpostController outpost, out FHRL.CharacterState saw)
        {
            saw = null;
            FHRL.CampaignState state = outpost?.CampaignController?.State;
            if (state?.PartyCharacters == null || state.Outpost == null) return false;
            saw = state.PartyCharacters.FirstOrDefault(c => HasPerk(c, FriendOfTheOak));
            if (saw == null) return false;
            return !UsedThisWeek(state);
        }

        [HarmonyPatch(typeof(FHRL.OutpostController), nameof(FHRL.OutpostController.CanBlessCharacter))]
        [HarmonyPostfix]
        private static void CanBlessFree(FHRL.OutpostController __instance, FHRL.CharacterState state, ref bool __result)
        {
            try
            {
                if (__result || state == null || !__instance.IsBlessingEnabled || state.TempleBlessed) return;
                if (!FreeAvailable(__instance, out _)) return;
                if (!__instance.CampaignController.CharacterController.IsPartyMember(state)) return;
                __result = true;                                                      // only the gold was missing
            }
            catch (Exception e) { if (GHClasses.Reported.Add("oak can")) Plugin.Log.LogError("GHSawbones2e (Friend of the Oak): " + e); }
        }

        [HarmonyPatch(typeof(FHRL.OutpostController), nameof(FHRL.OutpostController.BlessCharacter), new[] { typeof(FHRL.CharacterState) })]
        [HarmonyPrefix]
        private static void BlessStart(FHRL.OutpostController __instance, FHRL.CharacterState character, out FHRL.CharacterState __state)
        {
            __state = _freeBless;
            try
            {
                if (character != null && FreeAvailable(__instance, out FHRL.CharacterState saw))
                {
                    _freeBless = character;
                    MarkWeek(__instance.CampaignController.State, used: true);
                    Log("FriendOfTheOak", 0, $"Friend of the Oak ({saw.Name}): {character.Name}'s Temple blessing this outpost phase (week {__instance.CampaignController.State.Outpost.Week}) costs no gold (saved in the campaign)");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("oak bless")) Plugin.Log.LogError("GHSawbones2e (Friend of the Oak): " + e); }
        }

        [HarmonyPatch(typeof(FHRL.OutpostController), nameof(FHRL.OutpostController.BlessCharacter), new[] { typeof(FHRL.CharacterState) })]
        [HarmonyFinalizer]
        private static Exception BlessEnd(FHRL.OutpostController __instance, Exception __exception, FHRL.CharacterState __state)
        {
            if (__exception != null && _freeBless != null)   // the blessing failed: still free
                try { MarkWeek(__instance?.CampaignController?.State, used: false); } catch (Exception) { }
            _freeBless = __state;
            return __exception;
        }

        // CanBlessCharacter(throwException: true) inside BlessCharacter checks the gold before our postfix can answer
        [HarmonyPatch(typeof(FHRL.OutpostController), nameof(FHRL.OutpostController.CanBlessCharacter))]
        [HarmonyPrefix]
        private static bool CanBlessDuringFree(FHRL.OutpostController __instance, FHRL.CharacterState state, ref bool __result)
        {
            if (_freeBless == null || !ReferenceEquals(state, _freeBless)) return true;
            __result = __instance.IsBlessingEnabled && !state.TempleBlessed;
            return !__result;                                                         // let the game throw its own reason
        }

        [HarmonyPatch(typeof(FHRL.CharacterController), "Action_TakeGold")]
        [HarmonyPrefix]
        private static bool NoGoldTaken(FHRL.CharacterState character) => _freeBless == null || !ReferenceEquals(character, _freeBless);

        [HarmonyPatch(typeof(FHRL.CharacterController), "Action_IncrementPersonalQuest")]
        [HarmonyPrefix]
        private static bool NoDonationCounted(FHRL.CharacterState character, FHRL.PersonalQuestDef.IncrementType incrementType) =>
            _freeBless == null || !ReferenceEquals(character, _freeBless) || incrementType != FHRL.PersonalQuestDef.IncrementType.DonateGoldTemple;

        // ------------------------------------------------------------------ our text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_SAW_REMOVE"] = "Remove {0} from {1}?",
            ["FHMODS_SAW_REMOVE_YES"] = "Remove this one",
            ["FHMODS_SAW_REMOVE_NO"] = "Next condition",
            ["FHMODS_SAW_TRANSFER"] = "Return the Favor: give {0} to {1}?",
            ["FHMODS_SAW_TRANSFER_YES"] = "Give it",
            ["FHMODS_SAW_TRANSFER_NO"] = "Next enemy",
            ["FHMODS_SAW_BACK"] = "Move the token on {0} ({1}'s Prescription) back one slot?",
            ["FHMODS_SAW_BACK_YES"] = "Move it back",
            ["FHMODS_SAW_BACK_NO"] = "No",
            ["FHMODS_SAW_SEDATE"] = "Sedation: gain Disarm to add Stun to this attack on {0}?",
            ["FHMODS_SAW_SEDATE_YES"] = "Stun it (gain Disarm)",
            ["FHMODS_SAW_SEDATE_NO"] = "No",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_SAW_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }

    /// <summary>GHSawbones2e's templates: an ability tagged GH2E_Template is taken out of its pack card's action when the
    /// card is read (AbilityData.GetAction) and kept by name.</summary>
    [HarmonyPatch]
    internal static class GHSawbones2eTemplates
    {
        [HarmonyTargetMethod]
        private static System.Reflection.MethodBase TargetMethod() => typeof(ScenarioRuleLibrary.YML.AbilityData)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .First(m => m.Name == "GetAction" && m.GetParameters().Any(p => p.ParameterType == typeof(CAction).MakeByRefType()));

        [HarmonyPostfix]
        private static void Read(int cardID, ref CAction action)
        {
            try
            {
                if (action?.Abilities == null || cardID < 20000 || cardID >= 40000) return;
                List<CAbility> marked = action.Abilities.Where(a => a?.Name != null && GHSawbones2e.Tagged(a, GHSawbones2e.TemplateTag)).ToList();
                foreach (CAbility a in marked)
                {
                    action.Abilities.Remove(a);
                    bool known = GHSawbones2e.Templates.ContainsKey(a.Name);
                    GHSawbones2e.Templates[a.Name] = a;
                    if (!known) Plugin.Log.LogInfo($"Gloomhaven 2e templates: card {cardID} keeps {a.Name} for its plugin rule");
                }
            }
            catch (Exception e) { if (GHClasses.Reported.Add("saw templates read")) Plugin.Log.LogWarning("GHSawbones2eTemplates (read): " + e); }
        }
    }
}
