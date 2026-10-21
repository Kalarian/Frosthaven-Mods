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
    ///   first once four personal quests are complete; and a deal prefers quests that still unlock something new.
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

                // The priority group, once four personal quests are complete. PQ 09 and 10 count while both are still
                // in the deck ("either one"); once one of them is taken, the other is an ordinary quest.
                var priority = new HashSet<PersonalQuestID>();
                if (state.CompletedPersonalQuests.Count >= 4)
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

        // ---- PQ 15: goal 60 instead of 70

        [HarmonyPatch(typeof(PQ_15), nameof(PQ_15.CalcProgress))]
        [HarmonyPostfix]
        private static void LowerTempleGoal(ref uint max)
        {
            if (On && max == 70u) max = 60u;
        }
    }
}
