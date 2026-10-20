using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FHGUI;
using FHGUI.Common.Tooltips;
using FHGUI.DTO;
using FHGUI.Modules;
using FHGUI.Services;
using FHRL;
using HarmonyLib;
using UnityEngine.UI;

namespace FrosthavenMods
{
    /// <summary>
    /// The Give window: the game's own distribution table (the Building Donation screen), filled with our data, with a
    /// Resources tab and an Items tab (GiveTabs). Columns are the rest of the party (at most 3,
    /// so the table fits like every other party window).
    /// - Resources: rows are gold and the three materials, the row headers count down the giver's amount left. Click
    ///   +1 / right-click -1 come from the table; Ctrl (+/-10) and Shift (all / none) are handled here.
    /// - Items: one row per item the giver owns (icon and the game's item card on hover), laid out like the game's own
    ///   item reward window. A cell is an on/off pick; a receiver who already owns the item is marked and locked.
    ///   Long lists scroll: the table is a vertical scroll view with a height cap (UI dump).
    /// Clicks only change this screen; "Give" sends one synced command per gift, from both tabs. Esc closes it (GiveEscape).
    /// The game shows the modules a state exposes as properties (FHViewStateBase.UseModules), hence DistributionModule.
    /// </summary>
    internal sealed class GiveResourcesViewState : FHMapViewState
    {
        public static readonly GiveKind[] Kinds = { GiveKind.Gold, GiveKind.Lumber, GiveKind.Metal, GiveKind.Hide };

        private static readonly MethodInfo ItemTooltip = AccessTools.Method(typeof(ResourceAllocationService), "ItemTooltipFunc");

        /// <summary>The open Give window, if any (for Esc).</summary>
        internal static GiveResourcesViewState Open { get; private set; }

        private readonly CharacterID _giverId;
        private readonly List<CharacterID> _receivers;
        private bool _onItems;
        private readonly Dictionary<(int Column, GiveKind Kind), int> _pending = new Dictionary<(int, GiveKind), int>();
        private readonly Dictionary<ItemID, int> _pendingItems = new Dictionary<ItemID, int>(); // item -> column
        private readonly GiveTabs _tabs = new GiveTabs();

        public override FHMapViewLayer.LayerType TargetLayerType { get; set; } = FHMapViewLayer.LayerType.Modal;

        private DistributionModule DistributionModule => View.Modules.DistributionModule;

        private ResourceAllocationService Allocation => View.GetService<ResourceAllocationService>();

        public GiveResourcesViewState(CharacterID giverId, List<CharacterID> receivers, bool startOnItems)
        {
            _giverId = giverId;
            _receivers = receivers;
            _onItems = startOnItems;
        }

        public override void OnShow()
        {
            Open = this;
            GiveResourcesPatches.Safe("open the window", Build);
        }

        public override void OnHide()
        {
            if (Open == this) Open = null;
            GiveResourcesPatches.Safe("restore the row labels", _labels.Restore);
            GiveResourcesPatches.Safe("remove the tabs", _tabs.Hide);
        }

        internal void Close() => PopThisState();

        private int Pending(int column, GiveKind kind) => _pending.TryGetValue((column, kind), out int v) ? v : 0;

        private int PendingTotal(GiveKind kind) => _pending.Where(p => p.Key.Kind == kind).Sum(p => p.Value);

        private int ResourceGifts => _pending.Count(p => p.Value > 0);

        private void Build()
        {
            CharacterState giver = GiveResourcesPatches.Find(View, _giverId);
            if (giver == null) { PopThisState(); return; }
            var table = new DistributionTableDTO
            {
                Columns = _receivers.Select(id => new DistributionTableDTO.ColumnData
                {
                    Character = Allocation.GetPartySlotDTO(id),
                    CharacterResources = Allocation.GetCharacterResources(id),
                }).ToList(),
                Rows = _onItems ? ItemRows(giver) : ResourceRows(giver),
            };

            string name = GiveRules.Name(giver);
            string text = _onItems
                ? (table.Rows.Count == 0
                    ? $"{name} has no items to give."
                    : $"{name} gives. Click a cell to give that item to that character, click again to undo. Worn items are unequipped.")
                : $"{name} gives. Left-click +1, right-click -1. Hold Ctrl for 10, Shift for all / none.";
            DistributionModule.Init(new DistributionModule.Params
            {
                Table = table,
                Title = _onItems ? "FHMODS_GIVE_ITEMS_TITLE" : "FHMODS_GIVE_TITLE",
                DescriptionLiteral = text,
                IsDone = () => ResourceGifts > 0 || _pendingItems.Count > 0,
                OnDone = () => GiveResourcesPatches.Safe("give", GiveAll),
                OnCancel = PopThisState,
                HasDistributionAuthority = true,
                DoneButtonKey = "FHMODS_GIVE",
                CancelButtonKey = "GUI_CANCEL",
            });
            _labels.Fix(DistributionModule.transform);
            _tabs.Show(View, DistributionModule.transform, _onItems, Tab("Resources", ResourceGifts), Tab("Items", _pendingItems.Count),
                () => SwitchTo(false), () => SwitchTo(true));
            if (_onItems) UiDump.Once("Give window, items tab", DistributionModule.transform);
        }

        private static string Tab(string name, int queued) => queued > 0 ? $"{name} ({queued})" : name;

        private void SwitchTo(bool items)
        {
            if (_onItems == items) return;
            _onItems = items;
            Build();
            // Start the other tab at the top of its list.
            var scroll = DistributionModule.transform.Find("Panel/Content/Table")?.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        // ---- Resources tab

        private List<DistributionTableDTO.DistributionRowData> ResourceRows(CharacterState giver)
        {
            var tools = UIInfoTools.Instance;
            var rows = new List<DistributionTableDTO.DistributionRowData>();
            foreach (GiveKind kind in Kinds)
            {
                int have = GiveRules.Amount(giver, kind);
                int left = have - PendingTotal(kind);
                var row = new DistributionTableDTO.DistributionRowData
                {
                    HeaderData = new UIDistributionRowHeader.Params
                    {
                        Icon = kind == GiveKind.Gold ? tools.GetResourceConfig("Gold").Icon : tools.GetResourceConfig((OutpostMaterialType)kind).Icon,
                        Text = $"{left}/{have}",
                    },
                    CellData = new List<UIDistributionCell.Params>(),
                };
                for (int col = 0; col < _receivers.Count; col++)
                {
                    int column = col, current = Pending(col, kind);
                    row.CellData.Add(new UIDistributionCell.Params
                    {
                        CurrentDelta = current,
                        MaxDelta = current + left,
                        Enabled = true,
                        IsGain = true,
                        OnChange = delta => GiveResourcesPatches.Safe("change an amount", () => Change(column, kind, delta)),
                    });
                }
                rows.Add(row);
            }
            return rows;
        }

        // The row labels ("110/110") sit 20 px left of their slot and the table clips its left edge, which cut off the
        // first digit of 3-digit amounts. Centre them (and let long ones shrink) while our window is open; the table is
        // shared with the game's Building Donation screen, so put them back on close.
        private readonly RowLabelFix _labels = new RowLabelFix();

        private void Change(int column, GiveKind kind, int delta)
        {
            CharacterState giver = GiveResourcesPatches.Find(View, _giverId);
            if (giver == null) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            int current = Pending(column, kind);
            int left = GiveRules.Amount(giver, kind) - PendingTotal(kind);
            // Shift: all that's left / none. Checked here because the table's own Shift uses the legacy input API.
            if (kb?.shiftKey.isPressed == true) delta = delta > 0 ? left : -current;
            else if (Math.Abs(delta) == 1 && kb?.ctrlKey.isPressed == true) delta *= 10;
            _pending[(column, kind)] = Math.Max(0, Math.Min(current + delta, current + left));
            Build();
        }

        // ---- Items tab

        private List<DistributionTableDTO.DistributionRowData> ItemRows(CharacterState giver)
        {
            CampaignController campaign = View.GameController;
            var tools = UIInfoTools.Instance;
            var tooltip = ItemTooltip == null ? null
                : (FHItemTooltip.ItemTooltipParamsFunc)Delegate.CreateDelegate(typeof(FHItemTooltip.ItemTooltipParamsFunc), Allocation, ItemTooltip);
            List<CharacterState> receivers = _receivers.Select(id => GiveResourcesPatches.Find(View, id)).ToList();
            var rows = new List<DistributionTableDTO.DistributionRowData>();
            foreach (ItemDef item in ItemRules.Giveable(campaign, giver))
            {
                var dto = new ItemDataDTO(item);
                var icon = tools.GetItemConfig(item.Icon).miniIcon;
                int? pickedColumn = _pendingItems.TryGetValue(item.ID, out int c) ? c : (int?)null;
                var row = new DistributionTableDTO.DistributionRowData
                {
                    HeaderData = new UIDistributionRowHeader.Params { Icon = icon, Item = dto, TooltipParamsFunc = tooltip },
                    CellData = new List<UIDistributionCell.Params>(),
                };
                for (int col = 0; col < receivers.Count; col++)
                {
                    int column = col;
                    CharacterState receiver = receivers[col];
                    bool picked = pickedColumn == col;
                    bool owned = receiver != null && !campaign.CharacterController.CanHaveItem(receiver, item);
                    bool can = receiver != null && ItemRules.WhyCantReceive(campaign, receiver, item) == null;
                    row.CellData.Add(new UIDistributionCell.Params
                    {
                        Icon = picked || owned ? icon : null,
                        Item = picked || owned ? dto : null,
                        TooltipParamsFunc = tooltip,
                        Owned = owned,
                        NotEligible = owned,
                        // A pick is 0 or 1. Picked cells get room for one more so a second left-click reaches us (= undo).
                        CurrentDelta = picked ? 1 : 0,
                        MaxDelta = picked ? 2 : (can && pickedColumn == null ? 1 : 0),
                        Enabled = can,
                        IsGain = true,
                        HideValueString = true,
                        OnChange = delta => GiveResourcesPatches.Safe("pick an item", () => Pick(item.ID, column, delta)),
                    });
                }
                rows.Add(row);
            }
            return rows;
        }

        private void Pick(ItemID item, int column, int delta)
        {
            bool picked = _pendingItems.TryGetValue(item, out int c) && c == column;
            if (picked || delta < 0) _pendingItems.Remove(item);
            else _pendingItems[item] = column;
            Build();
        }

        // ---- Give

        private void GiveAll()
        {
            CharacterState giver = GiveResourcesPatches.Find(View, _giverId);
            var gifts = _pending.Where(p => p.Value > 0).OrderBy(p => p.Key.Column).ThenBy(p => p.Key.Kind).ToList();
            var items = _pendingItems.OrderBy(p => p.Value).ThenBy(p => p.Key.ID).ToList();
            PopThisState();
            if (giver == null) return;
            foreach (var gift in gifts)
            {
                CharacterState receiver = GiveResourcesPatches.Find(View, _receivers[gift.Key.Column]);
                if (!GiveResourcesPatches.Give(View, giver, receiver, gift.Key.Kind, gift.Value)) return;
            }
            foreach (var gift in items)
            {
                CharacterState receiver = GiveResourcesPatches.Find(View, _receivers[gift.Value]);
                ItemDef item = ItemRules.Find(View.GameController, (int)gift.Key.ID);
                if (!GiveResourcesPatches.GiveItem(View, giver, receiver, item)) return;
            }
        }
    }
}
