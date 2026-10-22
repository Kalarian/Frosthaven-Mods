using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Masteries carry over": once a character earns a mastery, every later character of that class starts
    /// with it already completed and gets its perk mark when created. Only masteries earned while the rule is on count.
    /// Board game rule bent: masteries belong to the character that earned them.
    ///
    /// Earning: the scenario result every peer receives (CompleteActiveScenario, an Everybody command) is where the game
    /// awards masteries (won scenarios only: perk marks, then CharacterState.CompletedMasteryIDs). After it, each mastery
    /// that's now on the character is recorded as a marker string in CampaignState.LootedChestTreasureTableIDs
    /// ("FHMODS:Mastery:&lt;mastery id&gt;"), the same inert, synced spot Dwarf's Boneshaper career count uses: the game only
    /// tests that list for real chest IDs, so the save still loads without the mod.
    ///
    /// Creating: both ways a character is finished (FinishCreateCharacter, FinishCreateCharacters, Everybody commands)
    /// end in CharacterController.Action_FinishCreateCharater, so every peer adds the same masteries and perk marks to the
    /// new character there, through the game's own Action_AddPerkMarks. A carried mastery is completed, so the game won't
    /// offer or award it again for that character.
    /// </summary>
    [HarmonyPatch]
    internal static class KeepMasteries
    {
        private const string Marker = "FHMODS:Mastery:";

        private static bool On => ModRules.IsOn(ModRules.KeepMasteries);

        private static readonly MethodInfo AddPerkMarks =
            AccessTools.Method(typeof(FHRL.CharacterController), "Action_AddPerkMarks", new[] { typeof(CharacterState), typeof(int) });

        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPostfix]
        private static void RecordEarned(ScenarioController __instance, ScenarioResult result)
        {
            if (!On || result == null || result.isRestarting || !result.ScenarioWon) return;
            try
            {
                CampaignState state = __instance.CampaignController.State;
                foreach (ScenarioResult.CharacterResult r in result.Characters)
                {
                    CharacterState c = state.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null || r.MasteriesCompleted == null) continue;
                    // only what the game actually awarded (it's on the character now)
                    foreach (MasteryID id in r.MasteriesCompleted.Where(m => c.CompletedMasteryIDs.Contains(m)))
                    {
                        if (state.LootedChestTreasureTableIDs.Contains(Marker + id.ID)) continue;
                        state.LootedChestTreasureTableIDs.Add(Marker + id.ID);
                        CoopLog.Write($"MASTERY: {GiveRules.Name(c)} earned {id.ID}; later {c.ClassID.ID.Replace("ID", "")}s start with it");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Masteries carry over: earned masteries not recorded: " + e);
            }
        }

        [HarmonyPatch(typeof(FHRL.CharacterController), "Action_FinishCreateCharater")]
        [HarmonyPostfix]
        private static void GiveCarried(FHRL.CharacterController __instance, CharacterState character)
        {
            if (!On || character?.ClassDef?.MasteryIDs == null) return;
            try
            {
                CampaignController campaign = __instance.CampaignController;
                List<string> markers = campaign.State.LootedChestTreasureTableIDs;
                foreach (MasteryID id in character.ClassDef.MasteryIDs)
                {
                    if (character.CompletedMasteryIDs.Contains(id) || !markers.Contains(Marker + id.ID)) continue;
                    character.CompletedMasteryIDs.Add(id);
                    int marks = (int)campaign.DefRepository.MasteryDefs[id].PerkMarksReward;
                    AddPerkMarks.Invoke(__instance, new object[] { character, marks });
                    CoopLog.Write($"MASTERY: {GiveRules.Name(character)} starts with {id.ID} (+{marks} perk mark)");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Masteries carry over: new character left as is: " + e);
            }
        }
    }
}
