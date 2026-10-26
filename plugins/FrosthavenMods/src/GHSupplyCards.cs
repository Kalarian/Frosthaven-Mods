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

        private static readonly HashSet<string> Reported = new HashSet<string>();
    }
}
