using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// Retirement envelopes. A pack marks some of its classes as "starting" (open the moment the pack's switch is on,
    /// see GHClassesRule) and leaves the rest in envelopes. Each retirement in the campaign then opens one envelope: two
    /// still-locked pack classes are drawn and the party picks one on the game's own class-choice screen (the one
    /// scenario 14 uses for Crashing Tide or Deepwraith); with a single class left it unlocks outright, with the game's
    /// "new class unlocked" reveal. Gloomhaven tied its classes to personal quests; a retirement is a personal quest
    /// completed, so this keeps that feel without binding classes to Frosthaven's quest cards.
    ///
    /// The envelope opens inside the retirement command (FinishRetire runs on every PC's copy of the campaign), right
    /// after the quest's own rewards, and draws with the campaign's replicated random generator, so every copy agrees.
    /// A retirement that pays inspiration for a second quest applies rewards twice but opens one envelope. The unlock
    /// goes through the game's own effects, so it lands in the outpost log and in the retirement summary like any
    /// other reward.
    /// </summary>
    [HarmonyPatch]
    internal static class GHEnvelopes
    {
        private static readonly MethodInfo ApplyEffect =
            AccessTools.Method(typeof(CampaignController), "Action_ApplyEffect", new[] { typeof(CampaignEffect), typeof(List<CampaignEffect>) });

        private static CharacterID _retiring;   // set when a retirement starts, consumed by its first quest-reward step

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.FinishRetire))]
        [HarmonyPrefix]
        private static void Retiring(CharacterID characterID) => _retiring = characterID;

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.FinishRetire))]
        [HarmonyPostfix]
        private static void Retired() => _retiring = null;

        [HarmonyPatch(typeof(CharacterController), "Action_ApplyPersonalQuestRewards")]
        [HarmonyPostfix]
        private static void OpenEnvelope(CharacterController __instance, CharacterState character, List<CampaignEffect> appliedEffects)
        {
            if (_retiring == null || character == null || !(character.ID == _retiring)) return;
            _retiring = null;
            try
            {
                if (!GHClassesRule.On) return;
                CampaignController campaign = __instance.CampaignController;
                CampaignState state = campaign?.State;
                DefRepository repo = campaign?.DefRepository;
                if (state == null || repo == null) return;
                List<CharacterClassID> locked = GHClassesRule.EnvelopeClassIDs(repo).Where(id => !state.UnlockedClassIDs.Contains(id)).ToList();
                if (locked.Count == 0) return;
                locked.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));   // the same order on every PC before the draw
                CharacterClassID first = state.Random.Element(locked);
                locked.Remove(first);
                CampaignEffect effect = locked.Count == 0
                    ? new CampaignEffect(CampaignEffectType.UnlockClass, first.ID)
                    : new CampaignEffect(CampaignEffectType.UnlockClassChoise, first.ID, state.Random.Element(locked).ID);
                ApplyEffect.Invoke(campaign, new object[] { effect, appliedEffects });
                CoopLog.Write($"ENVELOPE opened by {GiveRules.Name(character)}'s retirement: {effect.Type} " +
                              $"{string.Join(",", effect.GetStringParams())} role={CoopLog.Role()}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Gloomhaven envelopes: opening the envelope failed: " + e);
            }
        }
    }
}
