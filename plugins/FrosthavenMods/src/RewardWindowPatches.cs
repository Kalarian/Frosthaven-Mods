using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI.DTO;
using FHGUI.Modules;
using FHGUI.Services;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>
    /// Two conveniences for the game's item reward window (the "gain items" distribution, used for event / quest item
    /// rewards and for Share duplicate loot):
    /// - It opens with nothing assigned. The game pre-assigns each item to the first party member who can own it
    ///   (CampaignUtil.GenerateDefaultGainItemDistirubtion, run by the ResetItemDistribution command), which then had
    ///   to be right-clicked away. The window already requires every item to be assigned before Done, so nothing can
    ///   be skipped by accident.
    /// - Left-clicking another character's cell moves an assigned item there in one click. The game locks the other
    ///   cells once every copy is assigned; we unlock them and send the game's own "unassign" for the current holder
    ///   before the "assign" (both UpdateItemsDistribution, the synced commands the window uses). Only cells the
    ///   game already lets this player click (the party leader's) are touched.
    /// </summary>
    [HarmonyPatch]
    internal static class RewardWindowPatches
    {
        /// <summary>Set while DuplicateLoot asks the game for its default split (to see whether it would work).</summary>
        [ThreadStatic] internal static bool Probing;

        [HarmonyPatch(typeof(CampaignUtil), nameof(CampaignUtil.GenerateDefaultGainItemDistirubtion))]
        [HarmonyPostfix]
        private static void StartUnassigned(List<CharacterItemDistribution> __result)
        {
            if (Probing || __result == null) return;
            foreach (CharacterItemDistribution d in __result) d.Item = null;
        }

        [HarmonyPatch(typeof(ResourceAllocationService), nameof(ResourceAllocationService.Populate_DefaultItemDistribution))]
        [HarmonyPostfix]
        private static void ClickToMove(ResourceAllocationService __instance, ref DistributionModule.Params data, DistributionChangeDTO change, DistributionsDTO distributions)
        {
            try
            {
                if (change == null || change.ChangeType != DistributionChangeType.Gain || change.Items == null) return;
                if (data.Table?.Rows == null || data.Table.Columns == null || distributions?.Items == null) return;
                List<ItemDef> items = change.Items.Distinct().ToList();
                if (items.Count != data.Table.Rows.Count) return; // not the layout we know: leave it alone
                for (int r = 0; r < items.Count; r++)
                {
                    ItemDef item = items[r];
                    List<UIDistributionCell.Params> cells = data.Table.Rows[r].CellData;
                    // Only when every copy is handed out (otherwise the game's own click already assigns).
                    if (change.Items.Count(i => i == item) > distributions.Items.Count(d => d.Item == item)) continue;
                    for (int c = 0; c < cells.Count && c < data.Table.Columns.Count; c++)
                    {
                        UIDistributionCell.Params cell = cells[c];
                        if (!cell.Enabled || cell.Owned || cell.CurrentDelta != 0 || cell.MaxDelta != 0) continue;
                        CharacterID target = data.Table.Columns[c].Character.CharacterID;
                        CharacterItemDistribution to = distributions.Items.FirstOrDefault(d => d.CharacterID == target);
                        CharacterItemDistribution from = distributions.Items.FirstOrDefault(d => d.Item == item && d.CharacterID != target);
                        if (to == null || from == null) continue;
                        cell.MaxDelta = 1;
                        cell.OnChange = delta => GiveResourcesPatches.Safe("move a reward item", () =>
                        {
                            if (delta <= 0) return;
                            from.Item = null;
                            __instance.OnItemDistributionChanged(from);
                            to.Item = item;
                            __instance.OnItemDistributionChanged(to);
                        });
                        cells[c] = cell;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Reward window: could not enable click-to-move: " + e);
            }
        }
    }
}
