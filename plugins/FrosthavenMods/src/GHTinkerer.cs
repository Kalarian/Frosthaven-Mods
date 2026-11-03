using System;
using System.Collections.Generic;
using System.Linq;
using AStar;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// The 2nd edition Tinkerer's rules that are neither a card's data nor a map token (GHMapTokens.cs):
    ///   - Chimeric Formula bottom: "One ally within Range 3 may Recover one card from the lost pile of one ally adjacent to
    ///     them." The recovered card goes back to its owner's hand, so the targets of the recover (misc var
    ///     GHAdjacentToAllyInRange) are the characters adjacent to an ally (a character or a summon, not the Tinkerer)
    ///     who stands within range 3 of the Tinkerer; the owner may be the Tinkerer (QUESTIONS.md C: own card allowed, the
    ///     chosen ally must be within range 3). The owner picks the card (Frosthaven's recover). "GHRule ChimericRecover".
    ///   - Spare Parts (perk Tinkerer209): "Whenever you perform an action with [lost], you may [wild]": when the Tinkerer
    ///     starts a card action whose half has a lost icon (discard type Lost or Permanently Lost), an Infuse of any
    ///     element with Frosthaven's element picker (the Frozen Fist's Ice Uppercut shape: Infuse [Any], ShowElementPicker)
    ///     is queued at the end of that action; it can be skipped. "GHRule SpareParts".
    ///   - Shared Supplies (perk Tinkerer208): "Once each scenario, one adjacent ally may use one of your potion [small item]
    ///     items during their turn without it becoming [lost]." At the start and at the end of the turn of a character
    ///     adjacent to the Tinkerer, while the perk is unused this scenario, that character's player is asked, potion by
    ///     potion (the Tinkerer's unspent small items named "potion"), whether to use it now; on yes the item's abilities
    ///     are performed by that character (Frosthaven's pending turn abilities, as Regenerate is) and the Tinkerer's item
    ///     stays as it was. "GHRule SharedSupplies".
    /// Everything is gated on the pack's Tinkerer2ID class, its perk IDs and a misc var only the pack's card carries:
    /// without the 2nd edition pack this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHTinkerer
    {
        internal const string SharedSupplies = "Tinkerer208";
        internal const string SpareParts = "Tinkerer209";
        internal const int ChimericCard = 32531;

        // ---- the turn hooks: inside PhaseManager's StartTurn / EndTurn steps ----

        private static CPhase s_LastStart, s_LastEnd;

        /// <summary>PhaseManager collects the turn actor's start- and end-of-turn bonuses with this call, after it cleared
        /// the pending lists and before it starts them: the place to queue our own turn abilities.</summary>
        [HarmonyPatch(typeof(CActiveBonus), nameof(CActiveBonus.FindApplicableActiveBonuses),
            new[] { typeof(CActor), typeof(CAbility.EAbilityType), typeof(CActiveBonus.EActiveBonusBehaviourType), typeof(bool) })]
        [HarmonyPostfix]
        private static void AfterFindBonuses(CActor actor, CAbility.EAbilityType type, CActiveBonus.EActiveBonusBehaviourType behaviourType)
        {
            if (type != CAbility.EAbilityType.AddActiveBonus || actor == null || actor != GameState.InternalCurrentActor) return;
            try
            {
                if (behaviourType == CActiveBonus.EActiveBonusBehaviourType.EndTurnAbility && PhaseManager.CurrentPhase is CPhaseEndTurn end
                    && !end.m_SentEndTurnMessage && !ReferenceEquals(end, s_LastEnd))
                {
                    s_LastEnd = end;
                    GHMapTokens.OnEndTurn(actor);
                    OfferSharedSupplies(actor, "end");
                }
                else if (behaviourType == CActiveBonus.EActiveBonusBehaviourType.StartTurnAbility && PhaseManager.CurrentPhase is CPhaseStartTurn start
                         && !ReferenceEquals(start, s_LastStart))
                {
                    s_LastStart = start;
                    OfferSharedSupplies(actor, "start");
                }
            }
            catch (Exception e) { GHMapTokens.Report("Tinkerer turn hook", e); }
        }

        // ---- Shared Supplies ----

        private static object s_SharedState;
        private static bool s_SharedUsed;

        private static void OfferSharedSupplies(CActor actor, string when)
        {
            if (!(actor is CPlayerActor ally) || ally.IsDead || GameState.PendingScenarioModifierAbilities == null) return;
            if (!ReferenceEquals(s_SharedState, ScenarioManager.CurrentScenarioState)) { s_SharedState = ScenarioManager.CurrentScenarioState; s_SharedUsed = false; }
            if (s_SharedUsed) return;
            foreach (CPlayerActor tinkerer in ScenarioManager.Scenario.PlayerActors.Where(p => p != null && p != ally && !p.IsDead && HasPerk(p, SharedSupplies)))
            {
                if (!CPathFinder.IsAdjacent(ally.ArrayIndex, tinkerer.ArrayIndex)) continue;
                foreach (CItem item in Potions(tinkerer))
                {
                    if (!GHPlague.Ask(ally, false, "FHMODS_TINKER_SHARED", "FHMODS_TINKER_USE", "FHMODS_TINKER_NOTNOW",
                            GHPlague.NameOf(tinkerer), ItemName(item), GHPlague.NameOf(ally))) continue;
                    foreach (CAbility a in item.YMLData.Data.Abilities)
                        GameState.PendingScenarioModifierAbilities.Add(new Tuple<CAbility, CActor>(CAbility.CopyAbility(a, generateNewID: false), ally));
                    s_SharedUsed = true;
                    Plugin.Log.LogInfo($"GHRule SharedSupplies card 0 Shared Supplies: {GHPlague.NameOf(ally)} uses {GHPlague.NameOf(tinkerer)}'s {ItemName(item)} at the {when} of their turn; the item is not lost");
                    return;
                }
            }
        }

        private static IEnumerable<CItem> Potions(CPlayerActor p) =>
            (p.Inventory?.AllItems ?? new List<CItem>()).Where(i => i?.YMLData?.Data?.Abilities != null && i.YMLData.Data.Abilities.Count > 0
                && i.Slot == CItem.EItemSlot.SmallItem
                && i.SlotState != CItem.EItemSlotState.Consumed && i.SlotState != CItem.EItemSlotState.Spent
                && i.SlotState != CItem.EItemSlotState.Locked && i.SlotState != CItem.EItemSlotState.TempLocked
                && ((i.YMLData.StringID ?? "").IndexOf("Potion", StringComparison.OrdinalIgnoreCase) >= 0
                    || (i.YMLData.Name ?? "").IndexOf("potion", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

        private static string ItemName(CItem item)
        {
            string key = item?.YMLData?.Name ?? "";
            string name = null;
            try { name = GLOOM.LocalizationManager.GetTranslation(key.Trim('$')); } catch { }
            return string.IsNullOrEmpty(name) ? (item?.YMLData?.StringID ?? "?") : name;
        }

        internal static bool HasPerk(CPlayerActor player, string perkId) =>
            player?.CharacterClass?.Perks != null && player.CharacterClass.Perks.Any(p => p != null && p.ID == perkId);

        // ---- Spare Parts ----

        [HarmonyPatch(typeof(CPhaseAction), MethodType.Constructor)]
        [HarmonyPostfix]
        private static void AfterActionBuilt(CPhaseAction __instance)
        {
            try
            {
                List<CPhaseAction.CPhaseAbility> list = __instance?.RemainingPhaseAbilities;
                GameState.CurrentActorAction current = GameState.CurrentAction;
                if (list == null || current?.Action == null || !(current.BaseCard is CAbilityCard card)) return;
                if (GameState.CurrentActionInitiator != GameState.EActionInitiator.AbilityCard) return;   // not a persistent bonus's later trigger
                if (!(GameState.InternalCurrentActor is CPlayerActor me) || !HasPerk(me, SpareParts)) return;
                DiscardType discard = current.Action == card.TopAction ? card.GetAbilityCardYML.TopDiscardType
                    : current.Action == card.BottomAction ? card.GetAbilityCardYML.BottomDiscardType : DiscardType.None;
                if (discard != DiscardType.Lost && discard != DiscardType.PermanentlyLost) return;
                CAbility infuse = CAbility.CreateAbility(CAbility.EAbilityType.Infuse, CAbilityFilterContainer.CreateDefaultFilter(), isMonster: false, isTargetedAbility: false,
                    elementsToInfuse: new List<ElementInfusionBoardManager.EElement> { ElementInfusionBoardManager.EElement.Any }, showElementPicker: true, parentBaseCard: card);
                if (infuse == null) return;
                infuse.SkipAnim = true;
                list.Add(new CPhaseAction.CPhaseAbility(infuse, me, card, current.Action.ID));
                Plugin.Log.LogInfo($"GHRule SpareParts card {card.ID} Spare Parts: {GHPlague.NameOf(me)} performs a lost action ({(current.Action == card.TopAction ? "top" : "bottom")}): may infuse any element at its end");
            }
            catch (Exception e) { GHMapTokens.Report("Spare Parts", e); }
        }

        // ---- Chimeric Formula bottom ----

        [HarmonyPatch(typeof(SharedAbilityTargeting), nameof(SharedAbilityTargeting.GetValidActorsInRange), new[] { typeof(CAbility) })]
        [HarmonyPostfix]
        private static void AfterValidActors(CAbility ability)
        {
            try
            {
                if (ability?.MiscVars == null || ability.TargetingActor == null || !GHPlague.Marked(ability, "GHAdjacentToAllyInRange")) return;
                CActor me = ability.TargetingActor;
                var allies = new List<CActor>();
                foreach (CTile t in GameState.GetTilesInRange(me.ArrayIndex, Math.Max(1, ability.Range), CAbility.EAbilityTargeting.Range, emptyTilesOnly: false, ignoreBlocked: true))
                    foreach (CActor a in ScenarioManager.Scenario.FindActorsAt(t.m_ArrayIndex) ?? new List<CActor>())
                        if (a != null && a != me && !a.IsDead && CActor.AreActorsAllied(me.Type, a.Type, me, a) && !allies.Contains(a)) allies.Add(a);
                var owners = ScenarioManager.Scenario.PlayerActors
                    .Where(p => p != null && !p.IsDead && allies.Any(a => a != p && CPathFinder.IsAdjacent(a.ArrayIndex, p.ArrayIndex)))
                    .Where(p => ability.AbilityFilter.IsValidTarget(p, me, ability.IsTargetedAbility, useTargetOriginalType: false, ability.MiscAbilityData?.CanTargetInvisible))
                    .Cast<CActor>().ToList();
                ability.ValidActorsInRange.Clear();
                ability.ValidActorsInRange.AddRange(owners);
                if (ability.TilesInRange != null)
                    foreach (CActor p in owners)
                    {
                        CTile tile = ScenarioManager.Tiles[p.ArrayIndex.X, p.ArrayIndex.Y];
                        if (tile != null && !ability.TilesInRange.Contains(tile)) ability.TilesInRange.Add(tile);
                    }
                Plugin.Log.LogInfo($"GHRule ChimericRecover card {ChimericCard} {owners.Count} character(s) adjacent to an ally within range {ability.Range} may recover a lost card: {string.Join(", ", owners.Select(GHPlague.NameOf))}");
            }
            catch (Exception e) { GHMapTokens.Report("Chimeric Formula", e); }
        }

        // ---- our text ----

        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["FHMODS_TINKER_SHARED"] = "Shared Supplies (once each scenario): {2} may use {0}'s {1} now, and it is not lost. Use it?",
            ["FHMODS_TINKER_USE"] = "Use it",
            ["FHMODS_TINKER_NOTNOW"] = "Not now",
        };

        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void Translate(string term, ref string __result)
        {
            if (term != null && term.StartsWith("FHMODS_TINKER_", StringComparison.Ordinal) && Strings.TryGetValue(term, out string text)) __result = text;
        }
    }
}
