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
        [HarmonyPatch(typeof(CAbilityGiveSupplyCard), nameof(CAbilityGiveSupplyCard.ApplyToActor))]
        [HarmonyPrefix]
        private static void FreeCopy(CAbilityGiveSupplyCard __instance, out List<int> __state)
        {
            __state = null;
            if (!(__instance.TargetingActor is CPlayerActor giver) || __instance.SupplyCardIDs == null || Ids.Count == 0) return;
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
                CAbilityCard named = giver.CharacterClass.SupplyCards.FirstOrDefault(c => c.ID == id);
                CAbilityCard free = named == null ? null : giver.CharacterClass.SupplyCards
                    .Where(c => c.Name == named.Name && !held.Contains(c.ID)).OrderBy(c => c.ID).FirstOrDefault();
                picked.Add(free?.ID ?? id);
                if (free != null) held.Add(free.ID);
            }
            __state = __instance.SupplyCardIDs;
            __instance.SupplyCardIDs = picked;
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

        private static readonly HashSet<string> Reported = new HashSet<string>();
    }
}
