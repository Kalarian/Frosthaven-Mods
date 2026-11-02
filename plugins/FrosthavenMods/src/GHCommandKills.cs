using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace FrosthavenMods
{
    /// <summary>
    /// "If this attack kills an enemy, repeat this action" on a commanded attack (the 2nd edition Wildfury's Jaws of the
    /// Death top: COMMAND the bear Move 4, Attack 6, repeats and XP gated on CPhaseAction.ActorsKilledThisAction, the
    /// Drifter's Shockwave expression). Frosthaven builds that list from the targets of the abilities the card action
    /// itself lists, and a commanded figure's abilities sit inside the ControlActor, so its kills never count: the harness
    /// saw the bear kill and both repeats and both XP steps start and stop at once (2026-11-02). The kills made by a
    /// commanded ability of a pack card are kept for that card, the round and the commanding hero, and added to the list
    /// while that card's abilities run (in the commanded figure's own steps and in the hero's after them). Only pack cards
    /// (IDs 20000-39999) are touched, so without the content packs this is inert.
    /// </summary>
    [HarmonyPatch]
    internal static class GHCommandKills
    {
        private static readonly List<CActor> _kills = new List<CActor>();
        private static int _card = -1, _round = -1;
        private static CActor _by;
        private static bool _reported;

        private static bool IsPack(CBaseCard card) => card != null && card.ID >= 20000 && card.ID < 40000;

        [HarmonyPatch(typeof(CAbility), nameof(CAbility.AbilityEnded))]
        [HarmonyPostfix]
        private static void Ended(CAbility __instance)
        {
            if (__instance == null || !__instance.IsControlAbility || __instance.ActorsTargeted == null) return;
            CBaseCard card = __instance.GetAbilityBaseCard();
            if (!IsPack(card)) return;
            int round = GameState.RoundCount;
            CActor by = __instance.ControllingActor;
            if (card.ID != _card || round != _round || by != _by)
            {
                _kills.Clear();
                _card = card.ID; _round = round; _by = by;
            }
            foreach (CActor a in __instance.ActorsTargeted)
                if (a != null && a.IsDead && !_kills.Contains(a)) _kills.Add(a);
        }

        [HarmonyPatch(typeof(CPhaseAction), nameof(CPhaseAction.ActorsKilledThisAction), MethodType.Getter)]
        [HarmonyPostfix]
        private static void Count(ref List<CActor> __result)
        {
            if (_kills.Count == 0 || GameState.RoundCount != _round || __result == null) return;
            CPhaseAction pa = PhaseManager.CurrentPhaseAction;
            if (pa == null) return;
            CBaseCard card = pa.CurrentAndPreviousPhaseAbilities.Select(x => x?.m_Ability?.GetAbilityBaseCard()).FirstOrDefault(c => c != null);
            if (card == null || card.ID != _card) return;
            int added = 0;
            foreach (CActor a in _kills)
                if (!__result.Contains(a)) { __result.Add(a); added++; }
            if (added > 0 && !_reported)
            {
                _reported = true;
                Plugin.Log.LogInfo($"Gloomhaven commanded kills: {added} kill(s) by a commanded figure counted for card {card.ID}'s 'this action'");
            }
        }
    }
}
