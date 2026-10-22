using System;
using System.Collections.Generic;
using System.Linq;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// House rule "More story events": when the week's town event is drawn, if the top valid card can't unlock anything
    /// new, it goes to the bottom of the deck and the next valid card is drawn instead (once; the second card is kept
    /// whatever it is). The same idea as Dwarf's personal quest re-draw, applied to the outpost event deck. Outpost
    /// attacks are never put back, so they come up as often as before.
    ///
    /// "Can unlock something new": events are code (each option builds its effects when chosen), so every option the
    /// party could pick is played out on a throwaway copy of the campaign (the game's own StateSerializer clone, the way
    /// FHMultiplayerClient copies it) and the copy is compared before and after: a scenario, a calendar or read section,
    /// an event added to any deck, a building to construct, an item in the shop or craftable, a town guard card or perk,
    /// a personal quest, a class, a puzzle, a job posting or a sticker the campaign didn't have. Steps the party carries
    /// out (pay, pick a character...) are taken as done so the option plays to its end. If a copy fails to play out, the
    /// card is kept (no redraw).
    ///
    /// Co-op: the draw is OutpostEventController.Action_ActivateEventForOutpost, inside the phase command every peer
    /// runs; the check reads campaign state only and changes only the deck order, so every peer moves the same card.
    /// </summary>
    [HarmonyPatch]
    internal static class StoryEvents
    {
        private static bool On => ModRules.IsOn(ModRules.StoryEvents);

        // before Dwarf's early Barracks prefix: when that one forces WO-20 instead, the card moved here just waits at
        // the bottom like any other
        [HarmonyPatch(typeof(OutpostEventController), "Action_ActivateEventForOutpost")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void RedrawIfNothingNew(OutpostEventController __instance, bool isWinterEvent)
        {
            if (!On) return;
            try
            {
                CampaignController campaign = __instance.CampaignController;
                OutpostState outpost = campaign.State.Outpost;
                List<OutpostEventID> deck = isWinterEvent ? outpost.WinterOutpostEventDeckIDs : outpost.SummerOutpostEventDeckIDs;
                List<OutpostEventID> valid = deck.Where(id => campaign.IsEventValid(id)).ToList();
                if (valid.Count < 2) return;
                OutpostEventID top = valid[0];
                OutpostEventDef def = campaign.DefRepository.OutpostEvents[top];
                if (!def.AttackInfo.IsEmpty) return;
                if (UnlocksSomethingNew(campaign, def, out string what))
                {
                    CoopLog.Write($"STORY: week {outpost.Week}: {top.ID} kept ({what})");
                    return;
                }
                deck.Remove(top);
                deck.Add(top);
                CoopLog.Write($"STORY: week {outpost.Week}: {top.ID} unlocks nothing new, put on the bottom; next valid card is {valid[1].ID}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("More story events: draw left as is: " + e);
            }
        }

        /// <summary>True if any option the party can pick now unlocks something; what = the first thing found (for the
        /// co-op log). Also true when a copy can't be played out (keep the card rather than guess).</summary>
        internal static bool UnlocksSomethingNew(CampaignController campaign, OutpostEventDef def, out string what)
        {
            DefRepository repo = campaign.DefRepository;
            var serializer = new StateSerializer<CampaignState>(repo) { StateResolveAction = s => s.ResolveDefs(repo) };
            HashSet<string> before = Unlocks(campaign.State);

            // a copy with the event open lists the options, then plays out the first one; each other option gets its own
            CampaignState first = serializer.Clone(campaign.State);
            first.Outpost.SetActiveEvent(def);
            var firstSim = new CampaignController(first, repo);
            List<uint> options = def.GetOptions(first, firstSim.OutpostEventController.EventToResolve)
                .Where(o => o.IsNotEmpty).Select(o => o.ID).ToList();

            foreach (uint optionID in options)
            {
                try
                {
                    CampaignState copy = first;
                    CampaignController sim = firstSim;
                    if (optionID != options[0])
                    {
                        copy = serializer.Clone(campaign.State);
                        copy.Outpost.SetActiveEvent(def);
                        sim = new CampaignController(copy, repo);
                    }
                    OutpostEventState ev = sim.OutpostEventController.EventToResolve;
                    OutpostEventDef.Option option = def.GetOptions(copy, ev).FirstOrDefault(o => o.ID == optionID);
                    if (!option.IsNotEmpty || !sim.OutpostEventController.IsValidOption(option)) continue;
                    sim.OutpostEventController.ResolveCurrentEvent(option);
                    for (int step = 0; step < 5 && !ev.Completed && ev.OptionSelected; step++)
                    {
                        // the party's part (pay, pick...) taken as done, as Reveal event outcomes does
                        copy.UnresolvedActions.Clear();
                        copy.AnyHerbsToLose = copy.AnyHerbsToGain = 0;
                        copy.MaterialsToLose = copy.MaterialsToGain = default;
                        sim.OutpostEventController.ContinueResolveCurrentEvent();
                    }
                    string found = Unlocks(copy).FirstOrDefault(u => !before.Contains(u) && u != "event:" + def.ID.ID);
                    if (found != null)
                    {
                        what = $"option {optionID}: {found}";
                        return true;
                    }
                }
                catch (Exception e)
                {
                    what = $"option {optionID} not checked: {(e.InnerException ?? e).Message}";
                    return true;
                }
            }
            what = null;
            return false;
        }

        /// <summary>Everything the campaign has unlocked, as "kind:id" strings.</summary>
        private static HashSet<string> Unlocks(CampaignState s)
        {
            OutpostState o = s.Outpost;
            var set = new HashSet<string>();
            void Add(string kind, IEnumerable<object> ids)
            {
                foreach (object id in ids) set.Add(kind + ":" + id);
            }
            Add("scenario", s.UnlockedScenarios.Select(x => (object)x.ScenarioID));
            Add("section", s.ActivatedCampaignSectionIDs.Concat(s.PendingCampaignSectionIDs).Concat(o.CalendarEvents.Select(c => c.SectionID)).Cast<object>());
            Add("event", o.SummerOutpostEventDeckIDs.Concat(o.WinterOutpostEventDeckIDs).Concat(o.SummerRoadEventDeckIDs)
                .Concat(o.WinterRoadEventDeckIDs).Concat(o.BoatEventDeckIDs).Cast<object>());
            Add("building", o.AvailableBuildingToConstructIDs.Cast<object>());
            Add("item", o.PurchasableItemIDs.Concat(o.CraftableItemIDs).Cast<object>());
            Add("town guard card", o.TownGuardDeckIDs.Concat(o.TownGuardUsedDeckIDs).Cast<object>());
            Add("town guard perk", o.AddedTownGuardPerks.Cast<object>());
            Add("personal quest", s.PersonalQuestDeckIDs.Cast<object>());
            Add("class", s.UnlockedClassIDs.Cast<object>());
            Add("puzzle", s.UnlockedPuzzles.Select(p => (object)p.ID));
            Add("job", o.JobPostings.Cast<object>());
            Add("sticker", s.Stickers.Cast<object>());
            return set;
        }
    }
}
