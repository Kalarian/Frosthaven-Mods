using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI;
using FHGUI.States;
using FHRL;
using HarmonyLib;

namespace FrosthavenMods
{
    /// <summary>One scenario's loot, per party member, as the game recorded it.</summary>
    internal sealed class LootBatch
    {
        public sealed class Looted
        {
            public CharacterID Character;
            public int[] Amounts = new int[4]; // by GiveKind: gold, lumber, metal, hide
        }

        public readonly List<Looted> Characters = new List<Looted>();
        public readonly List<(ItemID Item, CharacterID Looter)> Items = new List<(ItemID, CharacterID)>();

        public int Total(GiveKind kind) => Characters.Sum(c => c.Amounts[(int)kind]);

        public bool IsEmpty => Items.Count == 0 && Characters.All(c => c.Amounts.All(a => a == 0));
    }

    /// <summary>
    /// House rule "Always allocate scenario loot". The game applies a scenario's result in
    /// ScenarioController.CompleteActiveScenario (a synced command): each character's gold, materials and looted items
    /// go to that character. A postfix on the local copy records what each one got (ScenarioResult.CharacterResult:
    /// GoldChange, the material part of ResourceChange, NewItemIDs still owned afterwards - a duplicate offered to the
    /// party by Share duplicate loot is not). Once the outpost is quiet (no pending reward windows or events, nothing
    /// loading, no window open), the party leader gets the Allocate Loot window. It only sends the Give window's
    /// synced commands, so nothing new needs to sync. If the game closes first, the loot stays with the looters.
    /// </summary>
    [HarmonyPatch]
    internal static class LootAllocation
    {
        internal static LootBatch Pending;

        /// <summary>What everyone owned before the result is applied: a looted copy of an item the looter already had
        /// is sold (or offered to the party) by the game, and must not make their old copy look like loot.</summary>
        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPrefix]
        private static void Before(ScenarioController __instance, out Dictionary<CharacterID, HashSet<ItemID>> __state)
        {
            __state = null;
            try
            {
                __state = __instance.CampaignController.State.PartyCharacters.ToDictionary(c => c.ID, c => new HashSet<ItemID>(c.OwnedItemIDs));
            }
            catch (Exception e) { Plugin.Log.LogError("Allocate loot: could not read the items owned before the scenario: " + e); }
        }

        [HarmonyPatch(typeof(ScenarioController), nameof(ScenarioController.CompleteActiveScenario))]
        [HarmonyPostfix]
        private static void Capture(ScenarioController __instance, ScenarioResult result, Dictionary<CharacterID, HashSet<ItemID>> __state)
        {
            try
            {
                if (result == null || result.isRestarting || __state == null || !ModRules.IsOn(ModRules.AllocateLoot)) return;
                var map = Singleton<FHMapChoreographer>.InstanceFast;
                CampaignController campaign = __instance.CampaignController;
                if (map == null || campaign == null || map.GameController != campaign) return; // the host's checking copy
                var batch = new LootBatch();
                foreach (ScenarioResult.CharacterResult r in result.Characters)
                {
                    CharacterState c = campaign.State.PartyCharacters.FirstOrDefault(p => p.ClassDef == r.Character);
                    if (c == null) continue;
                    OutpostResources m = r.ResourceChange.ToMaterialResources();
                    var looted = new LootBatch.Looted { Character = c.ID };
                    looted.Amounts[(int)GiveKind.Gold] = Math.Max(0, r.GoldChange);
                    looted.Amounts[(int)GiveKind.Lumber] = Math.Max(0, m.Lumber);
                    looted.Amounts[(int)GiveKind.Metal] = Math.Max(0, m.Metal);
                    looted.Amounts[(int)GiveKind.Hide] = Math.Max(0, m.Hide);
                    batch.Characters.Add(looted);
                    __state.TryGetValue(c.ID, out HashSet<ItemID> before);
                    foreach (ItemID id in r.NewItemIDs.Distinct())
                        if (c.OwnedItemIDs.Contains(id) && (before == null || !before.Contains(id))) batch.Items.Add((id, c.ID));
                }
                // Only the party gets the window, and only when there is something to split.
                if (batch.Characters.Count < 2 || batch.IsEmpty) { Pending = null; return; }
                Pending = batch;
                CoopLog.Write("LOOT captured: " + string.Join(" | ", batch.Characters.Select(l =>
                {
                    campaign.CharacterController.TryGetCharacter(l.Character, out CharacterState c);
                    var items = batch.Items.Where(i => i.Looter == l.Character).Select(i => i.Item.ID.ToString()).ToArray();
                    return $"{GiveRules.Name(c)} gold={l.Amounts[0]} lumber={l.Amounts[1]} metal={l.Amounts[2]} hide={l.Amounts[3]} items={(items.Length == 0 ? "-" : string.Join(",", items))}";
                })));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Allocate loot: could not record the loot (it stays with the looters): " + e);
            }
        }

        private const float QuietSeconds = 1f;
        private static float _quietSince = -1f;
        private static readonly AccessTools.FieldRef<FHMapView, CampaignEvent.ScenarioCompleted> PendingCompletion =
            AccessTools.FieldRefAccess<FHMapView, CampaignEvent.ScenarioCompleted>("PendingScenarioCompletionEvent");

        /// <summary>Opens the window once the map is quiet for a moment. Runs every frame (InputFHManager.LateUpdate).</summary>
        [HarmonyPatch(typeof(InputFHManager), "LateUpdate")]
        [HarmonyPostfix]
        private static void OpenWhenReady()
        {
            if (Pending == null) return;
            try
            {
                var map = Singleton<FHMapChoreographer>.InstanceFast;
                CampaignState state = map?.GameState;
                if (state?.ActiveScenario != null) { Pending = null; return; } // a new scenario started first: keep as looted
                if (!Quiet(map)) { _quietSince = -1f; return; }
                // Stay quiet a moment: screens can hand over to each other across a few frames.
                if (_quietSince < 0f) _quietSince = UnityEngine.Time.realtimeSinceStartup;
                if (UnityEngine.Time.realtimeSinceStartup - _quietSince < QuietSeconds) return;
                _quietSince = -1f;
                LootBatch batch = Pending;
                Pending = null;
                map.View.PushState(new LootAllocationViewState(batch));
            }
            catch (Exception e)
            {
                Pending = null;
                Plugin.Log.LogError("Allocate loot: could not open the window (the loot stays with the looters): " + e);
            }
        }

        /// <summary>Nothing else is showing or waiting: the end-of-scenario sequence on the map (the closing story
        /// NarrationViewState, the scenario rewards modal, "scenario unlocked" screens), reward windows, events,
        /// loading, another window of ours.</summary>
        private static bool Quiet(FHMapChoreographer map)
        {
            FHMapView view = map?.View;
            CampaignState state = map?.GameState;
            if (view == null || state == null) return false;
            if (!view.IsPartyLeader || ModWindow.Open != null || view.CurrentState == null || view.CurrentModalState != null) return false;
            if (view.CurrentState is NarrationViewState || view.CurrentState is ScenarioUnlockViewState) return false;
            if (PendingCompletion(view) != null) return false;
            if (SceneController.Instance != null && SceneController.Instance.IsLoading) return false;
            if (state.HasUnresolvedActions() || map.GameClient.HasUnprocessedResults) return false;
            return !map.GameController.OutpostEventController.HasEventToResolve;
        }
    }
}
