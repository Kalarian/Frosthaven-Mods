using System;
using System.Collections.Generic;
using System.Linq;
using FHRL;
using HarmonyLib;
using MapRuleLibrary.YML.Shared;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Elementalist's printed rules that are not card data (card-accuracy pass, 2026-11-02):
    ///   - "... if you consume two or more elements" (Malleable Evocation top, Shaping the Ether top, Prismatic Barrage
    ///     bottom, Eye of the Hurricane bottom): the bonus is the card's element-less consume named <c>?CTwo</c>. It is
    ///     never a button (hidden from the consume bar): it switches on by itself when the action's toggled element
    ///     consumes reach two and off again when they drop below two, on every peer alike (CPhaseAction's own toggle,
    ///     called from inside the toggle every peer runs). "GHRule TwoOrMoreConsumes".
    ///   - Pulsing Cores bottom (card 32809): "at the end of this and your next five turns, in order: Fire/Ice, Air/Earth,
    ///     Light/Dark" (twice): the end-of-turn infuse offers only the pair for the bonus's slot (slot 1 and 4 Fire/Ice,
    ///     2 and 5 Air/Earth, 3 and 6 Light/Dark). "GHRule PulsingCores".
    ///   - Lingering Essence top (card 32811, a persistent marker): "Whenever an enemy consumes an element, you may infuse
    ///     that element": asked when a monster hostile to the holder consumes (ElementInfusionBoardManager.EnemyConsume);
    ///     on yes the element is infused for the holder, strong at the end of that monster's turn (Frosthaven's pending
    ///     infusions, processed when the monster set finishes). "GHRule LingeringEssence".
    ///   - Class rule, mixed and wild infusions: "you choose which element you are infusing at the end of that turn": an
    ///     Elementalist's picker infusion of two or more elements (or any element) during her own turn is noted, not
    ///     asked; at the end of the turn the picker comes for each noted infusion and the element goes straight to the
    ///     strong column. "GHRule DeferredInfusion".
    ///   - Primed (perk Elementalist210): "At the start of each scenario, you may infuse any": an optional infuse-any with
    ///     the picker among the scenario-start abilities (Frosthaven's own StartScenarioAbilities queue, HIVE's mode
    ///     choice), strong at once. "GHRule Primed".
    ///   - Elemental Mastery (perk Elementalist212): Fire, Ice, Air and Earth do not wane at the end of the round; Light
    ///     and Dark do (Frosthaven's ElementsDontWane holds all six). "GHRule ElementalMastery".
    ///   - Masteries: FrosthavenMods_ConsumesInOneTurn (Value turns with Amount or more element consumes, from the element
    ///     records) and FrosthavenMods_AllSixElementsInScenarios (all six elements strong or waning at once, in Value
    ///     different scenarios). The second is seen as it happens (a hidden resource GH2EAllSixElements on the
    ///     Elementalist, saved with the scenario) and counted per character across scenarios in synced campaign state,
    ///     a marker in CampaignState.LootedChestTreasureTableIDs ("FHMODS:AllSix:&lt;character id&gt;=&lt;scenario ids&gt;", as
    ///     DwarfTweaks' Boneshaper count), at the scenario result every peer receives.
    /// Gated on the pack's Elementalist2ID class, card IDs 32801-32829 and perk IDs: inert without the 2nd edition pack.
    /// </summary>
    [HarmonyPatch]
    internal static class GHElementalist2e
    {
        internal const string ClassId = "Elementalist2ID";
        private const int FirstCard = 32801, LastCard = 32829;
        private const int PulsingCores = 32809, LingeringEssence = 32811;
        private const string Primed = "Elementalist210", ElementalMastery = "Elementalist212";
        private const string AllSixResource = "GH2EAllSixElements";
        private const string AllSixMarker = "FHMODS:AllSix:";
        private static readonly MasteryID AllSixMastery = new MasteryID("Elementalist2_MASTERY_2");
        internal const string ConsumesTag = "FrosthavenMods_ConsumesInOneTurn";
        internal const string AllSixTag = "FrosthavenMods_AllSixElementsInScenarios";

        private static readonly ElementInfusionBoardManager.EElement[] Six =
        {
            ElementInfusionBoardManager.EElement.Fire, ElementInfusionBoardManager.EElement.Ice, ElementInfusionBoardManager.EElement.Air,
            ElementInfusionBoardManager.EElement.Earth, ElementInfusionBoardManager.EElement.Light, ElementInfusionBoardManager.EElement.Dark,
        };

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static void Report(string what, Exception e)
        {
            if (Reported.Add(what)) Plugin.Log.LogError($"Gloomhaven 2e Elementalist: {what} failed: {e}");
        }

        private static int CardId(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; } catch { return 0; }
        }

        private static bool IsElementalist(CActor a) => a is CPlayerActor p && p.CharacterClass?.ID == ClassId;

        // ================================================================== two or more consumes

        private static bool IsTwoBonus(CActionAugmentation a, int cardId) =>
            a != null && cardId >= FirstCard && cardId <= LastCard && (a.Elements == null || a.Elements.Count == 0)
            && a.Name != null && a.Name.EndsWith("CTwo", StringComparison.Ordinal);

        private static bool s_Syncing;

        private static void SyncTwo(CPhaseAction phase)
        {
            if (s_Syncing || phase == null) return;
            GameState.CurrentActorAction current = GameState.CurrentAction;
            int cardId = current?.BaseCard?.ID ?? 0;
            if (cardId < FirstCard || cardId > LastCard || current.Action?.Augmentations == null) return;
            CActionAugmentation two = current.Action.Augmentations.FirstOrDefault(a => IsTwoBonus(a, cardId));
            if (two == null) return;
            List<CActionAugmentation> toConsume = phase.ActionAugmentationsToConsume ?? new List<CActionAugmentation>();
            List<CActionAugmentation> consumed = phase.ActionAugmentationsConsumed ?? new List<CActionAugmentation>();
            if (consumed.Any(a => a?.Name == two.Name)) return;   // already spent with its action
            int elements = toConsume.Concat(consumed).Where(a => a?.Elements != null && a.Elements.Count > 0 && !IsTwoBonus(a, cardId))
                .Select(a => a.Name).Distinct().Count();
            CActionAugmentation on = toConsume.FirstOrDefault(a => a?.Name == two.Name);
            if ((elements >= 2) == (on != null)) return;
            s_Syncing = true;
            try { phase.ToggleActionAugmentation(on ?? two); }
            finally { s_Syncing = false; }
            Plugin.Log.LogInfo($"GHRule TwoOrMoreConsumes card {cardId} {two.Name} {(on == null ? "on" : "off")}: {elements} element consume(s) toggled");
        }

        [HarmonyPatch(typeof(CPhaseAction), nameof(CPhaseAction.ToggleActionAugmentation))]
        [HarmonyPostfix]
        private static void AfterToggle(CPhaseAction __instance)
        {
            try { SyncTwo(__instance); } catch (Exception e) { Report("two or more consumes", e); }
        }

        [HarmonyPatch(typeof(CPhaseAction), nameof(CPhaseAction.RefreshConsumeBar))]
        [HarmonyPostfix]
        private static void AfterRefresh(CPhaseAction __instance)
        {
            try { SyncTwo(__instance); } catch (Exception e) { Report("two or more consumes (refresh)", e); }
        }

        /// <summary>The bonus is never a button of its own: the plugin switches it.</summary>
        [HarmonyPatch(typeof(UIUseAugmentationsBar), nameof(UIUseAugmentationsBar.Show))]
        [HarmonyPrefix]
        private static void HideTwoButton(List<UIUseAugmentationsBar.ConsumeAugmentationParam> consumes)
        {
            try
            {
                int cardId = GameState.CurrentAction?.BaseCard?.ID ?? 0;
                if (cardId < FirstCard || cardId > LastCard || consumes == null) return;
                consumes.RemoveAll(c => IsTwoBonus(c.Augmentation, cardId));
            }
            catch (Exception e) { Report("two or more consumes (bar)", e); }
        }

        // ================================================================== Pulsing Cores, deferred infusions

        private static readonly ElementInfusionBoardManager.EElement[][] CorePairs =
        {
            new[] { ElementInfusionBoardManager.EElement.Fire, ElementInfusionBoardManager.EElement.Ice },
            new[] { ElementInfusionBoardManager.EElement.Air, ElementInfusionBoardManager.EElement.Earth },
            new[] { ElementInfusionBoardManager.EElement.Light, ElementInfusionBoardManager.EElement.Dark },
        };

        private struct Deferred { public CActor Actor; public List<ElementInfusionBoardManager.EElement> Elements; public CBaseCard Card; }
        private static readonly List<Deferred> s_Deferred = new List<Deferred>();
        private static readonly HashSet<CAbilityInfuse> s_Skip = new HashSet<CAbilityInfuse>();
        private static object s_DeferredState;
        private static readonly AccessTools.FieldRef<CAbility, bool> CanUndo = AccessTools.FieldRefAccess<CAbility, bool>("m_CanUndo");

        private static void NewScenarioCheck()
        {
            if (ReferenceEquals(s_DeferredState, ScenarioManager.CurrentScenarioState)) return;
            s_DeferredState = ScenarioManager.CurrentScenarioState;
            s_Deferred.Clear();
            s_Skip.Clear();
        }

        [HarmonyPatch(typeof(CAbilityInfuse), nameof(CAbilityInfuse.Start))]
        [HarmonyPrefix]
        private static void BeforeInfuseStart(CAbilityInfuse __instance)
        {
            try
            {
                if (__instance?.TargetingActor == null || __instance.ElementsToInfuse == null) return;
                int card = CardId(__instance);
                if (card == PulsingCores && __instance.ElementsToInfuse.Contains(ElementInfusionBoardManager.EElement.Any))
                {
                    PulsingCoresPair(__instance);
                    return;
                }
                if (!IsElementalist(__instance.TargetingActor) || !__instance.ShowPicker || __instance.InfuseOnlyWaning) return;
                if (__instance.GetMiscVar("GH2E_DeferredInfuse").GetBool()) return;
                if (!(__instance.ElementsToInfuse.Contains(ElementInfusionBoardManager.EElement.Any) || __instance.ElementsToInfuse.Count > 1)) return;
                if (GameState.TurnActor != __instance.TargetingActor || !(PhaseManager.CurrentPhase is CPhaseAction)) return;
                // an extra turn whose end the engine doesn't scan for end-of-turn bonuses (PhaseManager EndTurn: only
                // BothActionsLater / SingleCardAnyActionAtTheEndOfTurn) has no place to ask at its end: the noted infusion
                // would wait a round, or be lost. The element is picked now instead (it still infuses at the turn's end).
                CActor taker = __instance.TargetingActor;
                if (taker.IsTakingExtraTurn && taker.TakingExtraTurnOfType != CAbilityExtraTurn.EExtraTurnType.BothActionsLater
                    && taker.TakingExtraTurnOfType != CAbilityExtraTurn.EExtraTurnType.SingleCardAnyActionAtTheEndOfTurn)
                {
                    Plugin.Log.LogInfo($"GHRule DeferredInfusion card {card} {string.Join("/", __instance.ElementsToInfuse)}: extra turn ({taker.TakingExtraTurnOfType}) with no end-of-turn step: the element is picked now");
                    return;
                }
                NewScenarioCheck();
                s_Deferred.Add(new Deferred
                {
                    Actor = __instance.TargetingActor,
                    Elements = __instance.ElementsToInfuse.ToList(),
                    Card = __instance.GetAbilityBaseCard() ?? __instance.ParentBaseCard,
                });
                s_Skip.Add(__instance);
                Plugin.Log.LogInfo($"GHRule DeferredInfusion card {card} {string.Join("/", __instance.ElementsToInfuse)}: the element is chosen at the end of the turn");
            }
            catch (Exception e) { Report("infuse start", e); }
        }

        [HarmonyPatch(typeof(CAbilityInfuse), nameof(CAbilityInfuse.Perform))]
        [HarmonyPrefix]
        private static bool BeforeInfusePerform(CAbilityInfuse __instance, ref bool __result)
        {
            if (__instance == null || !s_Skip.Remove(__instance)) return true;
            try
            {
                __instance.AbilityHasHappened = true;
                CanUndo(__instance) = false;
                PhaseManager.NextStep();
            }
            catch (Exception e) { Report("deferred infuse skip", e); }
            __result = true;
            return false;
        }

        private static void PulsingCoresPair(CAbilityInfuse infuse)
        {
            CActor me = infuse.TargetingActor;
            CActiveBonus bonus = GameState.PendingStartEndTurnAbilityBonusTriggers?.OfType<CEndTurnAbilityActiveBonus>()
                .FirstOrDefault(b => b.BaseCard?.ID == PulsingCores && b.Actor == me)
                ?? me.ActiveBonuses?.FirstOrDefault(b => b?.BaseCard?.ID == PulsingCores && b.HasBehaviour(CActiveBonus.EActiveBonusBehaviourType.EndTurnAbility));
            if (bonus == null) return;
            int slot = Math.Max(0, bonus.TrackerIndex);          // not yet advanced: the action's end advances it
            var pair = CorePairs[slot % 3];
            infuse.ElementsToInfuse = pair.ToList();
            Plugin.Log.LogInfo($"GHRule PulsingCores card {PulsingCores} slot {slot + 1} of 6: infuse {pair[0]} or {pair[1]}");
        }

        private static CPhase s_LastEnd;

        /// <summary>PhaseManager collects the turn actor's end-of-turn bonuses with this call (inside CPhaseEndTurn, after
        /// it cleared the pending lists): the noted infusions are queued here (GHTinkerer queues its own the same way).</summary>
        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.FindApplicableActiveBonuses),
            new[] { typeof(CActor), typeof(CAbility.EAbilityType), typeof(CActiveBonus.EActiveBonusBehaviourType), typeof(bool) })]
        [HarmonyPostfix]
        private static void AfterFindBonuses(CActor actor, CAbility.EAbilityType type, CActiveBonus.EActiveBonusBehaviourType behaviourType)
        {
            if (type != CAbility.EAbilityType.AddActiveBonus || actor == null || actor != GameState.InternalCurrentActor) return;
            if (behaviourType != CActiveBonus.EActiveBonusBehaviourType.EndTurnAbility) return;
            try
            {
                if (!(PhaseManager.CurrentPhase is CPhaseEndTurn end) || end.m_SentEndTurnMessage || ReferenceEquals(end, s_LastEnd)) return;
                s_LastEnd = end;
                NewScenarioCheck();
                if (s_Deferred.Count == 0 || GameState.PendingScenarioModifierAbilities == null) return;
                foreach (Deferred d in s_Deferred.Where(d => d.Actor == actor).ToList())
                {
                    s_Deferred.Remove(d);
                    if (d.Actor.IsDead) continue;
                    CAbility infuse = CAbility.CreateAbility(CAbility.EAbilityType.Infuse, CAbilityFilterContainer.CreateDefaultFilter(), isMonster: false, isTargetedAbility: false,
                        elementsToInfuse: d.Elements.ToList(), showElementPicker: true, parentBaseCard: d.Card);
                    if (infuse == null) continue;
                    infuse.SetMiscVar("GH2E_DeferredInfuse", new TValue(true));
                    infuse.SetMiscVar("InfuseAsStrong", new TValue(true));
                    infuse.SkipAnim = true;
                    GameState.PendingScenarioModifierAbilities.Add(new Tuple<CAbility, CActor>(infuse, actor));
                    Plugin.Log.LogInfo($"GHRule DeferredInfusion card {d.Card?.ID ?? 0} end of turn: {GHPlague.NameOf(actor)} picks {string.Join("/", d.Elements)} now (strong)");
                }
            }
            catch (Exception e) { Report("deferred infusion at turn end", e); }
        }

        // ================================================================== Lingering Essence

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.EnemyConsume))]
        [HarmonyPostfix]
        private static void AfterEnemyConsume(ElementInfusionBoardManager.EElement element, CActor actor)
        {
            try
            {
                if (actor == null || element == ElementInfusionBoardManager.EElement.Any || ScenarioManager.Scenario?.PlayerActors == null) return;
                foreach (CPlayerActor p in ScenarioManager.Scenario.PlayerActors.ToList())
                {
                    if (p == null || p.IsDead || !CActor.AreActorsEnemies(p.Type, actor.Type, p, actor)) continue;
                    if (p.ActiveBonuses == null || !p.ActiveBonuses.Any(b => b?.BaseCard?.ID == LingeringEssence)) continue;
                    if (!GHPlague.Ask(p, false, "FHMODS_ELEM2_LINGER", "FHMODS_ELEM2_INFUSE", "FHMODS_ELEM2_NO", GHPlague.NameOf(actor), element.ToString(), GHPlague.NameOf(p)))
                    {
                        Plugin.Log.LogInfo($"GHRule LingeringEssence card {LingeringEssence} {GHPlague.NameOf(actor)} consumed {element}: {GHPlague.NameOf(p)} declined");
                        continue;
                    }
                    ElementInfusionBoardManager.Infuse(element, p);
                    Plugin.Log.LogInfo($"GHRule LingeringEssence card {LingeringEssence} {GHPlague.NameOf(actor)} consumed {element}: {GHPlague.NameOf(p)} infuses it (strong at the end of that monster's turn)");
                }
            }
            catch (Exception e) { Report("Lingering Essence", e); }
        }

        // ================================================================== Primed

        [HarmonyPatch(typeof(PhaseManager), nameof(PhaseManager.SetNextPhase))]
        [HarmonyPostfix]
        private static void AfterSetPhase(CPhase.PhaseType type)
        {
            if (type != CPhase.PhaseType.StartScenarioEffects) return;
            try
            {
                if (GameState.PendingScenarioModifierAbilities == null || ScenarioManager.Scenario?.PlayerActors == null) return;
                foreach (CPlayerActor p in ScenarioManager.Scenario.PlayerActors)
                {
                    if (!GHPerks.HasPerk(p, Primed)) continue;
                    CAbility infuse = CAbility.CreateAbility(CAbility.EAbilityType.Infuse, CAbilityFilterContainer.CreateDefaultFilter(), isMonster: false, isTargetedAbility: false,
                        elementsToInfuse: new List<ElementInfusionBoardManager.EElement> { ElementInfusionBoardManager.EElement.Any }, showElementPicker: true);
                    if (infuse == null) continue;
                    infuse.SetMiscVar("InfuseAsStrong", new TValue(true));
                    infuse.SetMiscVar("GH2E_DeferredInfuse", new TValue(true));
                    infuse.SkipAnim = true;
                    GameState.PendingScenarioModifierAbilities.Add(new Tuple<CAbility, CActor>(infuse, p));
                    Plugin.Log.LogInfo($"GHRule Primed card 0 Primed: {GHPlague.NameOf(p)} may infuse any element at the start of the scenario");
                }
            }
            catch (Exception e) { Report("Primed", e); }
        }

        // ================================================================== Elemental Mastery

        private static readonly ElementInfusionBoardManager.EElement[] Held =
        {
            ElementInfusionBoardManager.EElement.Fire, ElementInfusionBoardManager.EElement.Ice,
            ElementInfusionBoardManager.EElement.Air, ElementInfusionBoardManager.EElement.Earth,
        };

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.EndRound))]
        [HarmonyPrefix]
        private static void BeforeEndRound(CActor.EType lastActorType, out List<ElementInfusionBoardManager.EElement> __state)
        {
            __state = null;
            try
            {
                CPlayerActor holder = ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p != null && !p.IsDead && GHPerks.HasPerk(p, ElementalMastery));
                if (holder == null) return;
                ElementInfusionBoardManager.ProcessEndTurnInfusions(lastActorType);   // the round's pending infusions first (EndRound's own call then finds none)
                __state = Held.Where(e => ElementInfusionBoardManager.ElementColumn(e) == ElementInfusionBoardManager.EColumn.Strong).ToList();
            }
            catch (Exception e) { Report("Elemental Mastery", e); }
        }

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.EndRound))]
        [HarmonyPostfix]
        private static void AfterEndRound(List<ElementInfusionBoardManager.EElement> __state)
        {
            if (__state == null || __state.Count == 0) return;
            try
            {
                foreach (var e in __state) ElementInfusionBoardManager.SetElementInstantly(e, ElementInfusionBoardManager.EColumn.Strong);
                Plugin.Log.LogInfo($"GHRule ElementalMastery card 0 Elemental Mastery: {string.Join(", ", __state)} stay strong at the end of the round");
            }
            catch (Exception e) { Report("Elemental Mastery (restore)", e); }
        }

        // ================================================================== all six elements strong or waning (mastery 2)

        private static void CheckAllSix(string why)
        {
            if (ScenarioManager.Scenario?.PlayerActors == null) return;
            if (!Six.All(e => ElementInfusionBoardManager.ElementColumn(e) != ElementInfusionBoardManager.EColumn.Inert)) return;
            foreach (CPlayerActor p in ScenarioManager.Scenario.PlayerActors)
            {
                if (!IsElementalist(p) || p.GetCharacterResourceAmount(AllSixResource) > 0) continue;
                p.AddCharacterResource(AllSixResource, 1);
                Plugin.Log.LogInfo($"GHRule AllSixElements card 0 mastery: all six elements strong or waning at once ({why}); scenario noted for {GHPlague.NameOf(p)}");
            }
        }

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.ProcessEndTurnInfusions))]
        [HarmonyPostfix]
        private static void AfterProcessInfusions()
        {
            try { CheckAllSix("end-of-turn infusions"); } catch (Exception e) { Report("all six elements", e); }
        }

        [HarmonyPatch(typeof(ElementInfusionBoardManager), nameof(ElementInfusionBoardManager.SetElementInstantly))]
        [HarmonyPostfix]
        private static void AfterSetInstantly()
        {
            try { CheckAllSix("element set strong"); } catch (Exception e) { Report("all six elements", e); }
        }

        private static List<string> SeenScenarios(CampaignState state, string characterId, out string old)
        {
            string prefix = AllSixMarker + characterId + "=";
            old = state?.LootedChestTreasureTableIDs?.FirstOrDefault(s => s != null && s.StartsWith(prefix, StringComparison.Ordinal));
            return old == null ? new List<string>() : old.Substring(prefix.Length).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool NotedThisScenario()
        {
            CScenario s = ScenarioManager.Scenario;
            if (s == null) return false;
            return s.PlayerActors.Concat(s.ExhaustedPlayers ?? new List<CPlayerActor>())
                .Any(p => IsElementalist(p) && p.GetCharacterResourceAmount(AllSixResource) > 0);
        }

        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPrefix]
        private static void CountAllSix(ScenarioController __instance, ScenarioResult result)
        {
            if (result == null || result.isRestarting) return;
            try
            {
                CampaignState state = __instance.CampaignController?.State;
                if (state == null) return;
                string scenarioId = ScenarioManager.CurrentScenarioState?.ID ?? "?";
                bool noted = NotedThisScenario();
                foreach (ScenarioResult.CharacterResult r in result.Characters)
                {
                    if (r.Character?.ID.ID != ClassId) continue;
                    CharacterState c = state.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null || c.CompletedMasteryIDs.Contains(AllSixMastery)) continue;
                    List<string> seen = SeenScenarios(state, c.ID.ID.ToString(), out string old);
                    if (noted && !seen.Contains(scenarioId))
                    {
                        seen.Add(scenarioId);
                        if (old != null) state.LootedChestTreasureTableIDs.Remove(old);
                        state.LootedChestTreasureTableIDs.Add(AllSixMarker + c.ID.ID.ToString() + "=" + string.Join(";", seen));
                    }
                    Plugin.Log.LogInfo($"GHRule AllSixElements card 0 mastery: {seen.Count} of 3 different scenarios with all six elements strong or waning{(noted ? " (this one counted)" : "")}");
                    if (seen.Count >= 3 && result.ScenarioWon && !r.MasteriesCompleted.Contains(AllSixMastery))
                        r.MasteriesCompleted.Add(AllSixMastery);
                }
            }
            catch (Exception e) { Report("all six elements (career count)", e); }
        }

        // ================================================================== mastery tags

        [HarmonyPatch(typeof(CUnlockCondition), "CheckUnlockConditionTarget")]
        [HarmonyPrefix]
        private static bool Answer(CUnlockConditionTarget unlockConditionTarget, List<CPlayerStats> comparePlayerStats,
                                   ref int totalConditions, ref int conditionsMet, ref bool __result)
        {
            List<string> tags = unlockConditionTarget?.TargetEnemyFamilies;
            if (tags == null || !(tags.Contains(ConsumesTag) || tags.Contains(AllSixTag))) return true;
            List<CPlayerStats> stats = (comparePlayerStats ?? new List<CPlayerStats>()).Where(s => s != null).ToList();
            try
            {
                if (tags.Contains(ConsumesTag))
                {
                    int needed = Math.Max(1, unlockConditionTarget.Value), per = Math.Max(1, unlockConditionTarget.Amount);
                    var byRound = new Dictionary<int, int>();
                    foreach (CPlayerStats s in stats)
                        foreach (CPlayerStatsElement e in s.Consumed ?? Enumerable.Empty<CPlayerStatsElement>())
                            if (e != null && e.Round > 0) byRound[e.Round] = (byRound.TryGetValue(e.Round, out int n) ? n : 0) + 1;
                    int turns = byRound.Values.Count(v => v >= per);
                    totalConditions = needed; conditionsMet = Math.Min(turns, needed); __result = turns >= needed;
                    Plugin.Log.LogInfo($"GHRule ConsumesInOneTurn card 0 mastery: {turns} turn(s) with {per}+ element consumes (needs {needed}){(__result ? ": met" : "")}");
                }
                else
                {
                    int needed = Math.Max(1, unlockConditionTarget.Value);
                    CampaignState state = GHClassesRule.LiveState;
                    CharacterState c = state?.PartyCharacters?.FirstOrDefault(p => p?.ClassDef?.ID.ID == ClassId);
                    List<string> seen = c != null ? SeenScenarios(state, c.ID.ID.ToString(), out _) : new List<string>();
                    string id = ScenarioManager.CurrentScenarioState?.ID ?? "?";
                    int count = seen.Count + (NotedThisScenario() && !seen.Contains(id) ? 1 : 0);
                    totalConditions = needed; conditionsMet = Math.Min(count, needed); __result = count >= needed;
                }
            }
            catch (Exception e)
            {
                Report("mastery " + string.Join(",", tags), e);
                totalConditions = Math.Max(1, unlockConditionTarget.Value); conditionsMet = 0; __result = false;
            }
            return false;
        }

        // ================================================================== text

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_ELEM2_LINGER"] = "Lingering Essence: {0} consumed {1}. Should {2} infuse {1}?",
            ["FHMODS_ELEM2_INFUSE"] = "Infuse",
            ["FHMODS_ELEM2_NO"] = "No",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_ELEM2_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
