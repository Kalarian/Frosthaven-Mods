using System;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A played Soothsinger song or Mindthief augment stays in the active area, as in Gloomhaven. The bonus a song or
    /// augment puts on its card (CActor.AddAugmentOrSong) has no duration of its own (NA); Gloomhaven moves a played card
    /// to the active area when it carries any bonus at all, Frosthaven only when one has a duration
    /// (CCharacterClass.MoveAbilityCardToPile). So the card went to the discard pile at the end of the turn and the song
    /// ended before any ally could use it (the user's manual test, 2026-10-29: Song of Speed gave no one +1; the probe
    /// found no song bonus anywhere after the play). Such bonuses are made persistent before the move: they last until
    /// the next song or augment replaces them (RemoveSong / RemoveAugment finish them), which is Gloomhaven's rule. No
    /// Frosthaven card has a song or an augment.
    /// </summary>
    [HarmonyPatch]
    internal static class GHActiveArea
    {
        private static bool _reported;

        [HarmonyPatch(typeof(CCharacterClass), "MoveAbilityCardToPile")]
        [HarmonyPrefix]
        private static void Keep(CAbilityCard abilityCard)
        {
            try
            {
                if (abilityCard?.ActiveBonuses == null) return;
                foreach (CActiveBonus b in abilityCard.ActiveBonuses.Where(b => (b.IsSong || b.IsAugment) && b.Duration == CActiveBonus.EActiveBonusDurationType.NA))
                {
                    b.Duration = CActiveBonus.EActiveBonusDurationType.Persistent;
                    if (!_reported) { _reported = true; Plugin.Log.LogInfo($"Gloomhaven songs and augments: {abilityCard.Name} stays in the active area"); }
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven songs and augments: " + e); }
        }

        /// <summary>The same move marks the card Activated before it checks "not Activated yet" to add it to the class's
        /// activated list, so a card moved there this way never joins it; Frosthaven's own persistent cards join through
        /// their bonus. The songs are looked up on that list only (FindAllSongActiveBonuses): the song was held, its card
        /// marked Activated, and no ally ever found it (the probe, 2026-10-29). The card is added the game's own way.</summary>
        [HarmonyPatch(typeof(CCharacterClass), "MoveAbilityCardToPile")]
        [HarmonyPostfix]
        private static void Join(CCharacterClass __instance, CAbilityCard abilityCard)
        {
            try
            {
                if (abilityCard == null || abilityCard.CurrentCardPile != CBaseCard.ECardPile.Activated || __instance.ActivatedCards.Contains(abilityCard)) return;
                if (!abilityCard.ActiveBonuses.Any(b => b.IsSong || b.IsAugment)) return;
                __instance.ActivateCard(abilityCard);
                Plugin.Log.LogInfo($"Gloomhaven songs and augments: {abilityCard.Name} joined the active cards");
            }
            catch (Exception e) { Plugin.Log.LogError("Gloomhaven songs and augments: " + e); }
        }
    }
}
