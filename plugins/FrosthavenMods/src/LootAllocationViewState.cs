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

namespace FrosthavenMods
{
    /// <summary>
    /// The Allocate Loot window (house rule "Always allocate scenario loot"): the Give window's table, but every party
    /// member is a column and the cells are what each one ends up with from this scenario's loot. It starts as looted.
    /// - Gold / material rows: the row header is "unallocated/looted". Right-click takes from a character, left-click
    ///   gives (Ctrl 10, Shift all / none). Allocate needs every row fully handed out.
    /// - Item rows: always assigned to someone; a left-click moves it. Characters who already own a copy are locked.
    /// "Allocate" turns the differences into the Give window's synced commands (GiveResources / GiveItem) from the
    /// characters who end up with less to those who end up with more. "Keep as looted" (or Esc) changes nothing.
    /// </summary>
    internal sealed class LootAllocationViewState : FHMapViewState, IModWindow
    {
        private static readonly MethodInfo ItemTooltip = AccessTools.Method(typeof(ResourceAllocationService), "ItemTooltipFunc");

        private readonly LootBatch _loot;
        private readonly List<CharacterID> _columns;
        private readonly Dictionary<(int Column, GiveKind Kind), int> _alloc = new Dictionary<(int, GiveKind), int>();
        private readonly int[] _itemColumn; // by index in _loot.Items
        private readonly RowLabelFix _labels = new RowLabelFix();

        public override FHMapViewLayer.LayerType TargetLayerType { get; set; } = FHMapViewLayer.LayerType.Modal;

        private DistributionModule DistributionModule => View.Modules.DistributionModule;

        private ResourceAllocationService Allocation => View.GetService<ResourceAllocationService>();

        public LootAllocationViewState(LootBatch loot)
        {
            _loot = loot;
            _columns = loot.Characters.Select(c => c.Character).ToList();
            for (int col = 0; col < _columns.Count; col++)
                foreach (GiveKind kind in GiveResourcesViewState.Kinds)
                    _alloc[(col, kind)] = loot.Characters[col].Amounts[(int)kind];
            _itemColumn = loot.Items.Select(i => _columns.IndexOf(i.Looter)).ToArray();
        }

        public override void OnShow()
        {
            ModWindow.Open = this;
            GiveResourcesPatches.Safe("open the loot window", Build);
        }

        public override void OnHide()
        {
            if (ModWindow.Open == this) ModWindow.Open = null;
            GiveResourcesPatches.Safe("restore the row labels", _labels.Restore);
        }

        /// <summary>Esc / Keep as looted.</summary>
        public void Close()
        {
            CoopLog.Write("LOOT kept as looted");
            PopThisState();
        }

        private int Alloc(int column, GiveKind kind) => _alloc.TryGetValue((column, kind), out int v) ? v : 0;

        private int Unallocated(GiveKind kind) => _loot.Total(kind) - Enumerable.Range(0, _columns.Count).Sum(c => Alloc(c, kind));

        private void Build()
        {
            var table = new DistributionTableDTO
            {
                Columns = _columns.Select(id => new DistributionTableDTO.ColumnData
                {
                    Character = Allocation.GetPartySlotDTO(id),
                    CharacterResources = Allocation.GetCharacterResources(id),
                }).ToList(),
                Rows = new List<DistributionTableDTO.DistributionRowData>(),
            };
            table.Rows.AddRange(ResourceRows());
            table.Rows.AddRange(ItemRows());
            DistributionModule.Init(new DistributionModule.Params
            {
                Table = table,
                Title = "FHMODS_LOOT_TITLE",
                DescriptionLiteral = "Split this scenario's loot. Right-click takes, left-click gives (hold Ctrl for 10, Shift " +
                                     "for all / none); the number by each row is unallocated/looted. Click a character to move an item.",
                IsDone = () => GiveResourcesViewState.Kinds.All(k => Unallocated(k) == 0),
                OnDone = () => GiveResourcesPatches.Safe("allocate the loot", AllocateAll),
                OnCancel = Close,
                HasDistributionAuthority = true,
                DoneButtonKey = "FHMODS_LOOT_DONE",
                CancelButtonKey = "FHMODS_LOOT_KEEP",
            });
            _labels.Fix(DistributionModule.transform);
            UiDump.Once("Allocate Loot window", DistributionModule.transform);
        }

        private IEnumerable<DistributionTableDTO.DistributionRowData> ResourceRows()
        {
            var tools = UIInfoTools.Instance;
            foreach (GiveKind kind in GiveResourcesViewState.Kinds)
            {
                int total = _loot.Total(kind);
                if (total == 0) continue;
                int left = Unallocated(kind);
                var row = new DistributionTableDTO.DistributionRowData
                {
                    HeaderData = new UIDistributionRowHeader.Params
                    {
                        Icon = kind == GiveKind.Gold ? tools.GetResourceConfig("Gold").Icon : tools.GetResourceConfig((OutpostMaterialType)kind).Icon,
                        Text = $"{left}/{total}",
                    },
                    CellData = new List<UIDistributionCell.Params>(),
                };
                for (int col = 0; col < _columns.Count; col++)
                {
                    int column = col, current = Alloc(col, kind);
                    row.CellData.Add(new UIDistributionCell.Params
                    {
                        CurrentDelta = current,
                        MaxDelta = current + left,
                        Enabled = true,
                        IsGain = true,
                        OnChange = delta => GiveResourcesPatches.Safe("change loot", () => Change(column, kind, delta)),
                    });
                }
                yield return row;
            }
        }

        private void Change(int column, GiveKind kind, int delta)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            int current = Alloc(column, kind);
            int left = Unallocated(kind);
            if (kb?.shiftKey.isPressed == true) delta = delta > 0 ? left : -current;
            else if (Math.Abs(delta) == 1 && kb?.ctrlKey.isPressed == true) delta *= 10;
            _alloc[(column, kind)] = Math.Max(0, Math.Min(current + delta, current + left));
            Build();
        }

        private IEnumerable<DistributionTableDTO.DistributionRowData> ItemRows()
        {
            CampaignController campaign = View.GameController;
            var tools = UIInfoTools.Instance;
            var tooltip = ItemTooltip == null ? null
                : (FHItemTooltip.ItemTooltipParamsFunc)Delegate.CreateDelegate(typeof(FHItemTooltip.ItemTooltipParamsFunc), Allocation, ItemTooltip);
            List<CharacterState> characters = _columns.Select(id => GiveResourcesPatches.Find(View, id)).ToList();
            for (int i = 0; i < _loot.Items.Count; i++)
            {
                int index = i;
                ItemDef item = ItemRules.Find(campaign, (int)_loot.Items[i].Item.ID);
                if (item == null) continue;
                var dto = new ItemDataDTO(item);
                var icon = tools.GetItemConfig(item.Icon).miniIcon;
                int looterColumn = _columns.IndexOf(_loot.Items[i].Looter);
                var row = new DistributionTableDTO.DistributionRowData
                {
                    HeaderData = new UIDistributionRowHeader.Params { Icon = icon, Item = dto, TooltipParamsFunc = tooltip },
                    CellData = new List<UIDistributionCell.Params>(),
                };
                for (int col = 0; col < characters.Count; col++)
                {
                    int column = col;
                    bool picked = _itemColumn[i] == col;
                    // The looter owns it now, so the game's "can own" check is only meaningful for everyone else.
                    bool owned = col != looterColumn && characters[col] != null && !campaign.CharacterController.CanHaveItem(characters[col], item);
                    row.CellData.Add(new UIDistributionCell.Params
                    {
                        Icon = picked || owned ? icon : null,
                        Item = picked || owned ? dto : null,
                        TooltipParamsFunc = tooltip,
                        Owned = owned,
                        NotEligible = owned,
                        CurrentDelta = picked ? 1 : 0,
                        MaxDelta = picked || owned ? (picked ? 1 : 0) : 1,
                        Enabled = !owned,
                        IsGain = true,
                        HideValueString = true,
                        OnChange = delta => GiveResourcesPatches.Safe("move a loot item", () =>
                        {
                            if (delta <= 0) return; // an item always belongs to someone: move it with a left-click
                            _itemColumn[index] = column;
                            Build();
                        }),
                    });
                }
                yield return row;
            }
        }

        private void AllocateAll()
        {
            var gifts = new List<(CharacterID From, CharacterID To, GiveKind Kind, int Amount)>();
            foreach (GiveKind kind in GiveResourcesViewState.Kinds)
            {
                // Who ends up with less than they looted gives to who ends up with more.
                var delta = Enumerable.Range(0, _columns.Count).Select(c => Alloc(c, kind) - _loot.Characters[c].Amounts[(int)kind]).ToArray();
                for (int to = 0; to < delta.Length; to++)
                {
                    for (int from = 0; from < delta.Length && delta[to] > 0; from++)
                    {
                        if (delta[from] >= 0) continue;
                        int amount = Math.Min(delta[to], -delta[from]);
                        gifts.Add((_columns[from], _columns[to], kind, amount));
                        delta[to] -= amount;
                        delta[from] += amount;
                    }
                }
            }
            PopThisState();
            CoopLog.Write($"LOOT allocated: {gifts.Count} resource gift(s), {_loot.Items.Where((it, i) => _columns.IndexOf(it.Looter) != _itemColumn[i]).Count()} item(s) moved");
            foreach (var g in gifts)
            {
                CharacterState from = GiveResourcesPatches.Find(View, g.From), to = GiveResourcesPatches.Find(View, g.To);
                if (!GiveResourcesPatches.Give(View, from, to, g.Kind, g.Amount)) return;
            }
            for (int i = 0; i < _loot.Items.Count; i++)
            {
                CharacterID looter = _loot.Items[i].Looter;
                if (_columns.IndexOf(looter) == _itemColumn[i]) continue;
                ItemDef item = ItemRules.Find(View.GameController, (int)_loot.Items[i].Item.ID);
                if (!GiveResourcesPatches.GiveItem(View, GiveResourcesPatches.Find(View, looter), GiveResourcesPatches.Find(View, _columns[_itemColumn[i]]), item)) return;
            }
        }
    }
}
