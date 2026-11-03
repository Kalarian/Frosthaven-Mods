using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI.DTO;
using FHGUI.States;
using HarmonyLib;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;

namespace FrosthavenMods
{
    /// <summary>
    /// Gloomhaven supply cards (the Sawbones' Medical Pack and Large Medical Pack): cards a class owns but never holds in
    /// its hand; other cards hand them to allies in a scenario. Frosthaven has none of its own, so its outpost hand
    /// editor offers every owned card, these too; a hand holding one then stops the scenario loading ("Loading could
    /// not complete": the scenario's card pool leaves supply cards out, and the hand names a card that isn't there).
    ///   - The hand editor no longer lists them.
    ///   - A hand saved with one before this fix loses it as the scenario starts (the game then asks for nothing more:
    ///     a hand may be short of the class's hand size).
    /// Inert without a content pack: no ruleset card is a supply card then.
    /// </summary>
    [HarmonyPatch]
    internal static class GHSupplyCards
    {
        private static HashSet<int> _ids;

        private static HashSet<int> Ids
        {
            get
            {
                if (_ids == null || _ids.Count == 0)
                    _ids = new HashSet<int>((ScenarioRuleClient.SRLYML?.AbilityCards ?? new List<AbilityCardYMLData>())
                        .Where(c => c != null && c.SupplyCard == true).Select(c => c.ID));
                return _ids;
            }
        }

        [HarmonyPatch(typeof(HandManagementViewState), "FillTempCardsFromCharacterDTO")]
        [HarmonyPostfix]
        private static void HideInEditor(ref List<CardDTO> ____tempCardDtos)
        {
            if (____tempCardDtos == null || Ids.Count == 0) return;
            int removed = ____tempCardDtos.RemoveAll(c => c != null && Ids.Contains(c.Id));
            if (removed > 0 && Reported.Add("editor"))
                Plugin.Log.LogInfo($"Gloomhaven supply cards: {removed} kept out of the hand editor (handed out in scenarios, never held)");
        }

        [HarmonyPatch(typeof(CMapParty), nameof(CMapParty.ExportPlayerStates))]
        [HarmonyPrefix]
        private static void DropFromHands(CMapParty __instance)
        {
            if (Ids.Count == 0) return;
            foreach (CMapCharacter character in __instance.SelectedCharacters)
            {
                List<int> hand = character?.HandAbilityCardIDs;
                if (hand == null) continue;
                int removed = hand.RemoveAll(id => Ids.Contains(id));
                if (removed > 0)
                    Plugin.Log.LogInfo($"Gloomhaven supply cards: {removed} taken out of {character.CharacterID}'s hand before the scenario (a supply card is never held)");
            }
        }

        /// <summary>A supply card comes in copies, each a card of its own (Medical Pack 20417..20423), since two in one hand
        /// may not share an ID (the hand UI looks cards up by ID and throws on a duplicate). The ability names the first
        /// copy; it is given the first copy of that card that no hero holds right now, so every give is one card, as in
        /// Gloomhaven. The ability object is reused, so its list is put back afterwards.</summary>
        /// <summary>The 2nd edition packs (supply cards 30000-39999): "add one random card from the supply" (Sawbones' class
        /// notes), so a free copy is drawn at random with the scenario's seeded generator (the same draw on every co-op PC);
        /// the 1st edition's packs keep the lowest free copy (its packs are identical cards).</summary>
        private static bool RandomCopy(int id) => id >= 30000 && id < 40000;

        /// <summary>Whose supply a give comes from: the performer's class when it owns the card, else (a Prescription in an
        /// ally's active area giving the ally a pack at the start of its turn: Take Your Medicine top) the hero in the
        /// scenario whose class owns it.</summary>
        internal static CPlayerActor Supplier(CPlayerActor giver, IEnumerable<int> ids)
        {
            if (giver?.CharacterClass?.SupplyCards == null || ids == null) return giver;
            List<int> list = ids.ToList();
            if (list.Count == 0 || giver.CharacterClass.SupplyCards.Any(c => list.Contains(c.ID))) return giver;
            return ScenarioManager.Scenario?.PlayerActors?.FirstOrDefault(p => p?.CharacterClass?.SupplyCards != null
                && p.CharacterClass.SupplyCards.Any(c => list.Contains(c.ID))) ?? giver;
        }

        [HarmonyPatch(typeof(CAbilityGiveSupplyCard), nameof(CAbilityGiveSupplyCard.ApplyToActor))]
        [HarmonyPrefix]
        private static bool FreeCopy(CAbilityGiveSupplyCard __instance, CActor actor, out List<int> __state, ref bool __result)
        {
            __state = null;
            try
            {
                return FreeCopyBody(__instance, actor, ref __state, ref __result);
            }
            catch (Exception e)
            {
                // let the engine's own give run with the ability's original list
                if (__state != null) { __instance.SupplyCardIDs = __state; __state = null; }
                if (GHClasses.Reported.Add("supply free copy")) Plugin.Log.LogError("GHSupplyCards (free copy): " + e);
                return true;
            }
        }

        private static bool FreeCopyBody(CAbilityGiveSupplyCard __instance, CActor actor, ref List<int> __state, ref bool __result)
        {
            if (!(__instance.TargetingActor is CPlayerActor giver) || __instance.SupplyCardIDs == null || Ids.Count == 0) return true;
            CPlayerActor supplier = Supplier(giver, __instance.SupplyCardIDs);
            if (supplier?.CharacterClass?.SupplyCards == null) return true;
            var held = new HashSet<int>();
            foreach (CPlayerActor p in ScenarioManager.Scenario?.PlayerActors ?? new List<CPlayerActor>())
            {
                CCharacterClass cc = p?.CharacterClass;
                if (cc == null) continue;
                foreach (var pile in new[] { cc.HandAbilityCards, cc.RoundAbilityCards, cc.DiscardedAbilityCards, cc.LostAbilityCards, cc.ActivatedAbilityCards })
                    foreach (CAbilityCard c in pile ?? new List<CAbilityCard>()) if (c != null) held.Add(c.ID);
            }
            var picked = new List<int>();
            foreach (int id in __instance.SupplyCardIDs)
            {
                CAbilityCard named = supplier.CharacterClass.SupplyCards.FirstOrDefault(c => c.ID == id);
                List<CAbilityCard> free = named == null ? new List<CAbilityCard>() : supplier.CharacterClass.SupplyCards
                    .Where(c => c.Name == named.Name && !held.Contains(c.ID)).OrderBy(c => c.ID).ToList();
                // every copy held: give nothing (a second card of the same ID in one hand made the hand screen throw;
                // the 2e Sawbones' six packs run out fast, 2026-11-01)
                if (free.Count == 0)
                {
                    if (named != null && GHClasses.Reported.Add("supply out " + named.Name))
                        Plugin.Log.LogInfo($"Gloomhaven supply cards: every {named.Name} is held; none given");
                    continue;
                }
                CAbilityCard chosen = free[0];
                if (RandomCopy(id) && free.Count > 1)
                {
                    int roll = 0;
                    try { roll = ScenarioManager.CurrentScenarioState.ScenarioRNG.Next(free.Count); } catch (Exception) { roll = 0; }
                    chosen = free[roll];
                    Plugin.Log.LogInfo($"GHRule RandomMedicalPack card {chosen.ID} drawn at random from {free.Count} free {named.Name} cards ({string.Join(", ", free.Select(c => c.ID))})");
                }
                picked.Add(chosen.ID);
                held.Add(chosen.ID);
            }
            __state = __instance.SupplyCardIDs;
            __instance.SupplyCardIDs = picked;
            // none left in the supply: the give can't be performed (Sawbones class notes: "abilities that give medical packs
            // may only be performed if there is at least one medical pack remaining in the supply"): nothing happens, so no
            // XP and no ability record (Constant Care doesn't count it)
            if (picked.Count == 0 && __state.Count > 0 && __state.All(RandomCopy))   // 2nd edition packs only: the released 1st edition keeps its behaviour
            {
                __result = false;
                Plugin.Log.LogInfo($"GHRule PackSupplyEmpty card {SafeCard(__instance)} {giver.GetPrefabName()}'s give: no free copy left in the supply: not performed (no card, no XP)");
                return false;
            }
            if (ReferenceEquals(supplier, giver)) return true;
            // the performer's class owns none of these cards: give them from the owner's supply, as the game's own give does
            __result = true;   // the engine's own ApplyToActor always returns true
            try
            {
                __instance.AbilityHasHappened = true;
                if (__instance.ActorsTargeted == null) Traverse.Create(__instance).Property("ActorsTargeted").SetValue(new List<CActor>());
                __instance.ActorsTargeted.Add(actor);
                var given = new List<CAbilityCard>();
                if (actor is CPlayerActor receiver)
                    foreach (int id in picked)
                    {
                        CAbilityCard card = supplier.CharacterClass.SupplyCards.FirstOrDefault(c => c.ID == id);
                        if (card == null) continue;
                        CAbilityCard copy = card.Copy();
                        given.Add(copy);
                        receiver.CharacterClass.HandAbilityCards.Add(copy);
                        CharacterClassManager.AllAbilityCardInstances.Add(copy);
                    }
                if (actor is CPlayerActor to)
                    ScenarioRuleClient.MessageHandler(new CSupplyCardsGiven_MessageData(__instance.TargetingActor) { m_ActorGivenCards = to, m_SupplyCardsGiven = given });
                Plugin.Log.LogInfo($"GHRule PackFromOwnerSupply card {SafeCard(__instance)} {giver.GetPrefabName()} gains {string.Join(", ", given.Select(c => c.Name + " " + c.ID))} from {supplier.GetPrefabName()}'s supply");
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven supply cards: give from the owner's supply failed: " + e); }
            return false;
        }

        private static int SafeCard(CAbility a)
        {
            try { return a?.GetAbilityBaseCard()?.ID ?? 0; }
            catch (Exception) { return 0; }
        }

        [HarmonyPatch(typeof(CAbilityGiveSupplyCard), nameof(CAbilityGiveSupplyCard.ApplyToActor))]
        [HarmonyFinalizer]
        private static Exception PutBack(Exception __exception, CAbilityGiveSupplyCard __instance, List<int> __state)
        {
            if (__state != null) __instance.SupplyCardIDs = __state;
            return __exception;
        }

        /// <summary>A used supply card is cleaned up through the class that owns it (the Sawbones), so the game removes its
        /// card from the owner's hand on screen, while it sits in the receiver's: "Unable to find attached card UI to
        /// destroy", and the game's error screen. Frosthaven never hands cards between heroes, so the path was never
        /// walked. A supply card not in this hand is removed from whichever hand holds it.</summary>
        [HarmonyPatch(typeof(CardsHandUI), nameof(CardsHandUI.DestroyCardUI))]
        [HarmonyPrefix]
        private static bool FromItsHolder(CardsHandUI __instance, CAbilityCard abilityCard)
        {
            if (abilityCard == null || !abilityCard.SupplyCard || __instance.AllCards.Any(x => x.AbilityCard == abilityCard)) return true;
            CardsHandUI holder = Singleton<CardsHandManager>.Instance?.cardHandsUI?.FirstOrDefault(h => h != null && h != __instance && h.AllCards.Any(x => x.AbilityCard == abilityCard));
            if (holder == null) return true;
            // used mid-turn by the hero acting: its card stays on screen until that turn ends. Removed at once, the turn's
            // card controller lost the first card played and offered no half of the second (the turn locked up)
            if (holder.PlayerActor != null && GameState.CurrentActor == holder.PlayerActor)
            {
                CoroutineHost.Get().StartCoroutine(AfterTurn(holder, abilityCard));
                return false;
            }
            holder.DestroyCardUI(abilityCard);
            return false;
        }

        private static System.Collections.IEnumerator AfterTurn(CardsHandUI holder, CAbilityCard card)
        {
            CActor actor = holder.PlayerActor;
            while (GameState.CurrentActor == actor && holder != null) yield return null;
            if (holder != null && holder.AllCards.Any(x => x.AbilityCard == card)) holder.DestroyCardUI(card);
        }

        /// <summary>"View all cards" copies each card's state from the hand; a supply card already used is in no hand any
        /// more, so the lookup came back empty and the window threw ("Could not find a card with ID 20417"). It shows as
        /// discarded instead.</summary>
        [HarmonyPatch(typeof(CardsHandPreviewUI), "CopyState")]
        [HarmonyPrefix]
        private static bool UsedSupplyCard(AbilityCardUI cardUI, CardsHandUI ___cardHand)
        {
            if (cardUI?.AbilityCard == null || !cardUI.AbilityCard.SupplyCard || ___cardHand == null) return true;
            if (___cardHand.AllCards.Any(x => x.AbilityCard?.ID == cardUI.AbilityCard.ID)) return true;
            cardUI.SetType(CardPileType.Discarded);
            cardUI.UpdateCard();
            return false;
        }

        /// <summary>A given supply card sits in the receiver's hand list with no pile of its own (CurrentCardPile None: the
        /// engine's give, like ours, adds the copy to HandAbilityCards and never sets it). A card played out of phase from
        /// the hand (SelectAndPlayCard: Take Your Medicine bottom's "each ally may play one Medical Pack", Sickening
        /// Strength) is checked against the list of its pile, and the list of pile None is null: "Could not process on
        /// card selected", the game's error screen (harness 2026-11-03, CardsHandUI.OnCardSelected IL_056e). A supply card
        /// picked out of phase from a hand it is in is given that hand's pile first (round selection is left alone).</summary>
        [HarmonyPatch(typeof(CardsHandUI), nameof(CardsHandUI.OnCardSelected))]
        [HarmonyPrefix]
        private static void SupplyCardPile(CardsHandUI __instance, AbilityCardUI cardUI)
        {
            try
            {
                CAbilityCard card = cardUI?.AbilityCard;
                if (card == null || !card.SupplyCard || card.CurrentCardPile != CBaseCard.ECardPile.None || !Ids.Contains(card.ID)) return;
                if (!GameState.IsPerformingCardOutOfPhase) return;   // only that branch reads the pile (round selection works as is)
                CCharacterClass cc = __instance?.PlayerActor?.CharacterClass;
                if (cc?.HandAbilityCards == null || !cc.HandAbilityCards.Contains(card)) return;
                card.CurrentCardPile = CBaseCard.ECardPile.Hand;
                Plugin.Log.LogInfo($"GHRule SupplyCardPile card {card.ID} {__instance.PlayerActor.GetPrefabName()} picks {card.Name} from the hand: its pile set to Hand (it had none)");
            }
            catch (Exception e) { if (Reported.Add("supply pile")) Plugin.Log.LogError("GHSupplyCards (pile): " + e); }
        }

        private static readonly HashSet<string> Reported = new HashSet<string>();
    }
}
