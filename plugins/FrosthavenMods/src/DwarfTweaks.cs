using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "Dwarf's balance changes": tweaks from Dwarf74's Unofficial Frosthaven Campaign Tweaks (he maintains
    /// the official FAQ and was a main campaign playtester). All of them change campaign state only inside the game's
    /// own commands (the personal quest deal, the outpost phase, scenario conclusions), from campaign state alone, so
    /// every co-op peer gets the same result.
    /// - Personal quest priority: PQ 03/04 (unlock Tavern 74) and PQ 07/08 (unlock Stables 88), plus PQ 09/10, are dealt
    ///   first once as many personal quests are complete as there are characters in the party (2 to 4; Dwarf: "the 4th
    ///   (or 2nd or 3rd, depending on party size)"); and a deal prefers quests that still unlock something new.
    /// - Early Barracks: from Winter of year 1, week 2, every 2 weeks: with the Barracks at level 1 and morale 10+, the
    ///   week's outpost event is WO-20 (the Barracks upgrade event); once only.
    /// - PQ 07 (Aesther Outpost): its two 6-week waits (sections 95.4 and 29.3) are 5 weeks.
    /// - PQ 15: the goal is 60 gold donated instead of 70.
    /// (PQ 19 needs nothing: the digital game already charges the missing potions' costs instead of requiring them.)
    /// </summary>
    [HarmonyPatch]
    internal static class DwarfTweaks
    {
        private static bool On => ModRules.IsOn(ModRules.DwarfTweaks);

        // ---- personal quest priority

        private static readonly PersonalQuestID[] Priority = { new PersonalQuestID("PQ_3"), new PersonalQuestID("PQ_4"), new PersonalQuestID("PQ_7"), new PersonalQuestID("PQ_8") };
        private static readonly PersonalQuestID PQ9 = new PersonalQuestID("PQ_9"), PQ10 = new PersonalQuestID("PQ_10");

        /// <summary>The order the deal takes quests from (MultiplayerGameController.DrawPersonalQuests takes the first two;
        /// puzzle-forced quests still go first). Kept: the game's own (shuffled) deck order within each group.</summary>
        [HarmonyPatch(typeof(MultiplayerGameController), nameof(MultiplayerGameController.GetAvailablePersonalQuests))]
        [HarmonyPostfix]
        private static void PrioritiseQuests(MultiplayerGameController __instance, ref IEnumerable<PersonalQuestID> __result)
        {
            if (!On) return;
            try
            {
                CampaignController campaign = __instance.CampaignController;
                CampaignState state = campaign.State;
                List<PersonalQuestID> available = __result.ToList();

                // The priority group, once one personal quest per party member (2 to 4) is complete. PQ 09 and 10 count while both are still
                // in the deck ("either one"); once one of them is taken, the other is an ordinary quest.
                var priority = new HashSet<PersonalQuestID>();
                int trigger = Math.Max(2, Math.Min(4, state.PartyCharacters.Count()));
                if (state.CompletedPersonalQuests.Count >= trigger)
                {
                    foreach (PersonalQuestID id in Priority)
                        if (state.PersonalQuestDeckIDs.Contains(id)) priority.Add(id);
                    if (state.PersonalQuestDeckIDs.Contains(PQ9) && state.PersonalQuestDeckIDs.Contains(PQ10)) { priority.Add(PQ9); priority.Add(PQ10); }
                }

                // Dwarf's "re-draw if neither quest would unlock anything new", done up front: quests that still unlock
                // an unopened envelope come before ones whose rewards are all opened.
                bool UnlocksNew(PersonalQuestID id) =>
                    campaign.DefRepository.PersonalQuests.TryGetValue(id, out PersonalQuestDef def) &&
                    def.GetAvailableRewardIDs().Any(r => !campaign.IsEnvelopeOpened(r));

                List<PersonalQuestID> ordered = available
                    .Select((id, i) => (id, i))
                    .OrderBy(x => priority.Contains(x.id) ? 0 : UnlocksNew(x.id) ? 1 : 2)
                    .ThenBy(x => x.i)
                    .Select(x => x.id)
                    .ToList();
                __result = ordered;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dwarf's balance changes: personal quest order left as dealt: " + e);
            }
        }

        // ---- early Barracks: WO-20

        private static readonly OutpostEventID WO20 = new OutpostEventID("WO-20");
        private static readonly MethodInfo ActivateEvent = AccessTools.Method(typeof(OutpostEventController), "Action_ActivateEvent", new[] { typeof(OutpostEventID) });

        [HarmonyPatch(typeof(OutpostEventController), "Action_ActivateEventForOutpost")]
        [HarmonyPrefix]
        private static bool EarlyBarracks(OutpostEventController __instance, bool isWinterEvent)
        {
            if (!On || !isWinterEvent) return true;
            try
            {
                CampaignController campaign = __instance.CampaignController;
                OutpostState outpost = campaign.State.Outpost;
                uint week = outpost.Week;
                // Winter of year 1 is weeks 11-20; Dwarf's section is read on week 12, then every 2 weeks.
                if (outpost.Calendar.Year != 1 || week < 12 || (week - 12) % 2 != 0) return true;
                BuildingState barracks = outpost.Buildings.FirstOrDefault(b => b.Def is FHRL.Buildings.Barrack_BuildingTypeDef);
                if (barracks == null || barracks.Level != 1 || barracks.Wrecked || outpost.Morale < 10) return true;
                if (!outpost.WinterOutpostEventDeckIDs.Contains(WO20) || !campaign.IsEventValid(WO20)) return true;
                if (outpost.ActivatedEvents.Any(e => e.ID == WO20)) return true; // once only
                ActivateEvent.Invoke(__instance, new object[] { WO20 });
                campaign.State.Random.Shuffle(outpost.WinterOutpostEventDeckIDs);
                CoopLog.Write($"DWARF: week {week}: WO-20 drawn for the early Barracks upgrade (morale {outpost.Morale})");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dwarf's balance changes: early Barracks check skipped: " + e);
                return true;
            }
        }

        // ---- PQ 07: the 6-week waits after scenarios 65 and 66 are 5 weeks

        [HarmonyPatch(typeof(OutpostController), "Action_AddCalendarEvent")]
        [HarmonyPrefix]
        private static void ShorterAestherWait(CampaignSectionID sectionID, ref uint week)
        {
            if (!On) return;
            string id = sectionID.ID;
            if ((id == "95.4" || id == "29.3") && week > 0) week -= 1;
        }

        // ---- Scenario 14 (Jagged Shoals) rework
        // The map, spawners and goals are in a binary level file (FS_014_Jagged_Shoals.lvldat), deserialized fresh by
        // CSRLYML.GetCustomLevel every time the scenario is set up, on every peer. Spawners: [0] pigs (d), [1] pigs (b),
        // [2]/[4] eels (e), [3]/[5] eels (c); today both kinds spawn every round (the script adds b + c on round 1).
        // Dwarf: pigs only on odd rounds, alternating b / d; eels only on even rounds, alternating c / e; and the
        // Abael Herders must die too. So: automatic spawning off, the scenario's script spawns by round number, and a
        // second win goal (all win goals are needed unless one is "enough alone", which the snow rock's isn't).

        [HarmonyPatch(typeof(CSRLYML), nameof(CSRLYML.GetCustomLevel))]
        [HarmonyPostfix]
        private static void ReworkScenario14(string name, ScenarioRuleLibrary.CustomLevels.CCustomLevelData __result)
        {
            if (!On || name == null || !name.Contains("FS_014_") || __result?.ScenarioState == null) return;
            try
            {
                ScenarioRuleLibrary.ScenarioState state = __result.ScenarioState;
                foreach (ScenarioRuleLibrary.CSpawner spawner in state.Spawners)
                    spawner.SpawnerData.SpawnRoundInterval = spawner.SpawnerData.SpawnRoundInterval.Select(_ => 0).ToList();
                if (!state.WinObjectives.Any(o => o is ScenarioRuleLibrary.CObjective_KillAllEnemies))
                    state.WinObjectives.Add(new ScenarioRuleLibrary.CObjective_KillAllEnemies(ScenarioRuleLibrary.EObjectiveResult.Win,
                        new ScenarioRuleLibrary.CObjectiveFilter(filterEnemyClassIDs: new List<string> { "AbaelHerderID" }),
                        hideCounter: false, customLoc: "FHMODS_S14_HERDERS"));
                Plugin.Log.LogInfo($"Dwarf's balance changes: Scenario 14 reworked ({state.Spawners.Count} spawners by round, Abael Herders goal)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dwarf's balance changes: Scenario 14 left as is: " + e);
            }
        }

        private static readonly MethodInfo OncePerRound = AccessTools.Method(typeof(ScenarioRuleLibrary.ScenarioScript), "OncePerRound");

        [HarmonyPatch(typeof(ScenarioRuleLibrary.ScenarioScript_FH014), "TickStartRound")]
        [HarmonyPrefix]
        private static bool Scenario14Spawns(ScenarioRuleLibrary.ScenarioScript_FH014 __instance)
        {
            if (!On) return true;
            try
            {
                var script = Traverse.Create(__instance);
                if ((bool)OncePerRound.Invoke(__instance, null))
                {
                    int round = ScenarioRuleLibrary.ScenarioScript.RoundNumber, size = ScenarioRuleLibrary.ScenarioScript.PartySize;
                    List<ScenarioRuleLibrary.CSpawner> spawners = ScenarioRuleLibrary.ScenarioManager.CurrentScenarioState.Spawners;
                    void Spawn(string field) => script.Field(field).GetValue<ScenarioRuleLibrary.CSpawner>()?.SpawnUnit(size, treatAsSummon: false, initial: false, startRound: true);
                    switch (round % 4)
                    {
                        case 1: Spawn("_bSpawner"); break;                                                   // pigs at b
                        case 2: Spawn("_cSpawner1"); Spawn("_cSpawner2"); break;                             // eels at c
                        case 3: if (spawners.Count > 0) spawners[0].SpawnUnit(size, false, false, true); break; // pigs at d
                        case 0: Spawn("_eSpawner1"); Spawn("_eSpawner2"); break;                             // eels at e
                    }
                }
                script.Field("_damagedThisRound").SetValue(false);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dwarf's balance changes: Scenario 14 spawns fell back to the game's: " + e);
                return true;
            }
        }

        // ---- Boneshaper mastery 1: "kill 15 of your summons" over any number of scenarios
        // The game checks it per scenario (Boneshaper_Mastery_1.yml: Kill, the six summon classes, 15, SubFilter
        // Scenario; only kills by the Boneshaper personally are Kills on its stats). The scenario result every peer
        // receives (CompleteActiveScenario, an Everybody command) lists each character's kills (MonstersKilled), so the
        // career count is added up there, from the payload, on every peer alike. It lives in synced campaign state: a
        // marker string in CampaignState.LootedChestTreasureTableIDs ("FHMODS:BoneKills:<character guid>=<n>"), a list
        // whose readers only test for real chest IDs (Contains / Except), so the marker is inert and the save still
        // loads without the mod. At 15 on a won scenario the mastery is added to the result's MasteriesCompleted, and
        // the game awards it its own way (validation, 1 perk mark) - masteries are only awarded on wins in the game.

        private static readonly MasteryID BoneMastery = new MasteryID("Boneshaper_MASTERY_1");
        private static readonly HashSet<string> BoneSummons = new HashSet<string>
            { "BoneHordeID", "RagingCorpseID", "ShamblingSkeletonID", "SkeletonSorcererID", "StitchedAtrocityID", "WraithID" };
        private const string BoneMarker = "FHMODS:BoneKills:";

        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPrefix]
        private static void BoneshaperCareer(ScenarioController __instance, ScenarioResult result)
        {
            if (!On || result == null || result.isRestarting) return;
            try
            {
                CampaignController campaign = __instance.CampaignController;
                CampaignState state = campaign.State;
                foreach (ScenarioResult.CharacterResult r in result.Characters)
                {
                    if (r.Character?.ID.ID != "BoneshaperID") continue;
                    CharacterState c = state.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null || c.CompletedMasteryIDs.Contains(BoneMastery)) continue;
                    int kills = r.MonstersKilled?.Count(k => k?.monsterClassID != null && BoneSummons.Contains(k.monsterClassID)) ?? 0;
                    string prefix = BoneMarker + c.ID.ID + "=";
                    string old = state.LootedChestTreasureTableIDs.FirstOrDefault(s => s.StartsWith(prefix));
                    int total = (old != null && int.TryParse(old.Substring(prefix.Length), out int n) ? n : 0) + kills;
                    if (old != null) state.LootedChestTreasureTableIDs.Remove(old);
                    state.LootedChestTreasureTableIDs.Add(prefix + total);
                    if (kills > 0) CoopLog.Write($"DWARF: {GiveRules.Name(c)} killed {kills} of their summons; career total {total}/15");
                    if (total >= 15 && result.ScenarioWon && !r.MasteriesCompleted.Contains(BoneMastery))
                    {
                        r.MasteriesCompleted.Add(BoneMastery);
                        CoopLog.Write($"DWARF: {GiveRules.Name(c)} completes the Boneshaper mastery (career total {total})");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dwarf's balance changes: Boneshaper career count skipped: " + e);
            }
        }

        /// <summary>The mastery's text says what the rule now is.</summary>
        [HarmonyPatch(typeof(GLOOM.LocalizationManager), nameof(GLOOM.LocalizationManager.GetTranslation))]
        [HarmonyPostfix]
        private static void BoneshaperText(string term, ref string __result)
        {
            if (term != null && term.EndsWith("Boneshaper_MASTERY_1_Desc") && On)
                __result = "Over any number of scenarios, kill at least 15 of your summons.";
        }

        // ---- PQ 15: goal 60 instead of 70

        [HarmonyPatch(typeof(PQ_15), nameof(PQ_15.CalcProgress))]
        [HarmonyPostfix]
        private static void LowerTempleGoal(ref uint max)
        {
            if (On && max == 70u) max = 60u;
        }
    }
}
