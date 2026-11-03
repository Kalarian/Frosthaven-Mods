using System;
using System.Collections.Generic;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// A pack card that lets ANOTHER hero play a card in the middle of the acting hero's action (Frosthaven's
    /// SelectAndPlayCard performed by the ally): the 2nd edition Plagueherald's Sickening Strength top (GHPlagueherald2e,
    /// templates PH2_SickDamage / PH2_SickPlay) and the Sawbones' Take Your Medicine bottom (GHSawbones2e, template
    /// BPlayPack). Frosthaven itself only ever plays a card for the hero whose card holds the ability (Blinkblade Sand in
    /// the Hourglass, Geminate, Boneshaper: Filter Self on the hero's own card), so its end of the play
    /// (CAbilitySelectAndPlayCard.EndOutOfPhaseAbilities) restores the acting hero's action from two things it took at
    /// the start: previousRoundCard (the ability's own base card) and the ability's TargetingActor (the hero).
    ///
    /// For an ally's play both are wrong: the ability is a template copy (its base card is the dummy card, never the
    /// hero's), and its TargetingActor is the ally. So at the end GameState.CurrentAction was set to null instead of the
    /// hero's card action, GameState.RoundAbilityCardSelected stayed the ALLY's played card, and the hand / Choreographer
    /// current actor were set to the ally. When the hero's action then ended, GameState's end-of-action step progressed
    /// the action flags and the discard from the ally's card: the hero's own card was never discarded and its half never
    /// counted (bench 20261103-010804: the Plagueherald played Sickening Strength top again as a third action, and the
    /// next time every half, skip and ready stayed dark: a soft lock).
    ///
    /// Fix, Frosthaven's own restore with the right inputs: when such a play starts (a pack card's action is being
    /// performed by hero H, and the SelectAndPlayCard is performed by someone else or carries no card of H), the hero and
    /// the card H is performing are kept; at EndOutOfPhaseAbilities (also reached by its Skip) previousRoundCard is that
    /// card and previousActor is H, so the engine calls GameState.PlayerSelectedAbilityCardAction on H's card exactly as
    /// after Sand in the Hourglass; and for the duration of EndOutOfPhaseAbilities the ability's TargetingActor is H, so
    /// its CEndedExtraTurn / CEndSelectAndPlayCard messages hand the turn and the hand back to H (the cards the ally
    /// played are still returned from the ally's piles: the engine takes those from ActorsToTarget[0], the ally).
    /// Logs "GHRule AllyCardPlay card N ...". Inert without the packs (only pack cards, IDs 20000-39999, are touched).
    /// </summary>
    [HarmonyPatch]
    internal static class GHAllyCardPlay
    {
        private static readonly AccessTools.FieldRef<CAbilitySelectAndPlayCard, CAbilityCard> PreviousRoundCard =
            AccessTools.FieldRefAccess<CAbilitySelectAndPlayCard, CAbilityCard>("previousRoundCard");
        private static readonly AccessTools.FieldRef<CAbilitySelectAndPlayCard, CActor> PreviousActor =
            AccessTools.FieldRefAccess<CAbilitySelectAndPlayCard, CActor>("previousActor");

        private static readonly Dictionary<CAbilitySelectAndPlayCard, (CPlayerActor hero, CAbilityCard card)> Plays =
            new Dictionary<CAbilitySelectAndPlayCard, (CPlayerActor, CAbilityCard)>();

        private static readonly HashSet<string> Reported = new HashSet<string>();

        private static bool IsPack(CBaseCard c) => c != null && c.ID >= 20000 && c.ID < 40000;

        private static string Name(CActor a) => a?.GetPrefabName() ?? "?";

        [HarmonyPatch(typeof(CAbilitySelectAndPlayCard), nameof(CAbilitySelectAndPlayCard.Start))]
        [HarmonyPostfix]
        private static void Started(CAbilitySelectAndPlayCard __instance)
        {
            try
            {
                Plays.Remove(__instance);
                if (PhaseManager.PreviousPhase != null) return;   // an active bonus' play: Frosthaven restores no card there either
                if (!(PreviousActor(__instance) is CPlayerActor hero) || hero.IsDead) return;
                CAbilityCard card = GameState.RoundAbilityCardSelected;
                if (!IsPack(card) || card.SelectedAction == null || !hero.CharacterClass.AbilityCardsPool.Contains(card)) return;
                CBaseCard own = __instance.GetAbilityBaseCard();
                bool othersPlay = __instance.TargetingActor != null && __instance.TargetingActor != hero;
                bool noHeroCard = !(own is CAbilityCard oc) || !hero.CharacterClass.AbilityCardsPool.Contains(oc);
                if (!othersPlay && !noHeroCard) return;   // the hero's own card's play: Frosthaven's case, untouched
                Plays[__instance] = (hero, card);
            }
            catch (Exception e) { if (Reported.Add("start")) Plugin.Log.LogError("GHAllyCardPlay (start): " + e); }
        }

        [HarmonyPatch(typeof(CAbilitySelectAndPlayCard), nameof(CAbilitySelectAndPlayCard.EndOutOfPhaseAbilities))]
        [HarmonyPrefix]
        private static void Ending(CAbilitySelectAndPlayCard __instance, out CActor __state)
        {
            __state = null;
            try
            {
                if (!Plays.TryGetValue(__instance, out var p)) return;
                Plays.Remove(__instance);
                if (p.hero == null || p.card == null || p.card.SelectedAction == null) return;
                PreviousRoundCard(__instance) = p.card;
                PreviousActor(__instance) = p.hero;
                CActor player = __instance.TargetingActor;
                if (player != p.hero)
                {
                    __state = player;
                    __instance.TargetingActor = p.hero;
                }
                bool top = p.card.SelectedAction.ID == p.card.TopAction.ID;
                Plugin.Log.LogInfo($"GHRule AllyCardPlay card {p.card.ID} {Name(player)}'s card play ended: {Name(p.hero)}'s {p.card.Name} {(top ? "top" : "bottom")} action resumes (turn and hand back to {Name(p.hero)})");
            }
            catch (Exception e) { if (Reported.Add("end")) Plugin.Log.LogError("GHAllyCardPlay (end): " + e); }
        }

        [HarmonyPatch(typeof(CAbilitySelectAndPlayCard), nameof(CAbilitySelectAndPlayCard.EndOutOfPhaseAbilities))]
        [HarmonyFinalizer]
        private static Exception Ended(CAbilitySelectAndPlayCard __instance, CActor __state, Exception __exception)
        {
            try { if (__state != null) __instance.TargetingActor = __state; }
            catch (Exception e) { if (Reported.Add("restore")) Plugin.Log.LogError("GHAllyCardPlay (restore): " + e); }
            return __exception;
        }
    }
}
