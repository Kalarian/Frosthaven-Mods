using System;
using System.Collections.Generic;
using System.Linq;
using FHGUI;
using FHGUI.DTO;
using FHGUI.Modules;
using FHGUI.Services;
using FHRL;

namespace FrosthavenMods
{
    /// <summary>
    /// The Give resources window: the game's own distribution table (the Building Donation screen), filled with our
    /// data. Columns are the rest of the party (at most 3, so the table fits like every other party window), rows are
    /// gold and the three materials, the row headers count down the giver's amount left. Clicks only change the numbers
    /// on this screen; "Give" sends one synced GiveResources command per filled cell. Click +1 / right-click -1 come
    /// from the table; Ctrl (+/-10) and Shift (all / none) are handled here. Esc closes it (GiveEscape).
    /// The game shows the modules a state exposes as properties (FHViewStateBase.UseModules), hence DistributionModule.
    /// </summary>
    internal sealed class GiveResourcesViewState : FHMapViewState
    {
        public static readonly GiveKind[] Kinds = { GiveKind.Gold, GiveKind.Lumber, GiveKind.Metal, GiveKind.Hide };

        /// <summary>The open Give window, if any (for Esc).</summary>
        internal static GiveResourcesViewState Open { get; private set; }

        private readonly CharacterID _giverId;
        private readonly List<CharacterID> _receivers;
        private readonly Dictionary<(int Column, GiveKind Kind), int> _pending = new Dictionary<(int, GiveKind), int>();

        public override FHMapViewLayer.LayerType TargetLayerType { get; set; } = FHMapViewLayer.LayerType.Modal;

        private DistributionModule DistributionModule => View.Modules.DistributionModule;

        private ResourceAllocationService Allocation => View.GetService<ResourceAllocationService>();

        public GiveResourcesViewState(CharacterID giverId, List<CharacterID> receivers)
        {
            _giverId = giverId;
            _receivers = receivers;
        }

        public override void OnShow()
        {
            Open = this;
            GiveResourcesPatches.Safe("open the window", Build);
        }

        public override void OnHide()
        {
            if (Open == this) Open = null;
        }

        internal void Close() => PopThisState();

        private int Pending(int column, GiveKind kind) => _pending.TryGetValue((column, kind), out int v) ? v : 0;

        private int PendingTotal(GiveKind kind) => _pending.Where(p => p.Key.Kind == kind).Sum(p => p.Value);

        private void Build()
        {
            CharacterState giver = GiveResourcesPatches.Find(View, _giverId);
            if (giver == null) { PopThisState(); return; }
            var tools = UIInfoTools.Instance;
            var table = new DistributionTableDTO
            {
                Columns = _receivers.Select(id => new DistributionTableDTO.ColumnData
                {
                    Character = Allocation.GetPartySlotDTO(id),
                    CharacterResources = Allocation.GetCharacterResources(id),
                }).ToList(),
                Rows = new List<DistributionTableDTO.DistributionRowData>(),
            };
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
                table.Rows.Add(row);
            }

            DistributionModule.Init(new DistributionModule.Params
            {
                Table = table,
                Title = "FHMODS_GIVE_TITLE",
                DescriptionLiteral = $"{GiveRules.Name(giver)} gives. Left-click +1, right-click -1. Hold Ctrl for 10, Shift for all / none.",
                IsDone = () => _pending.Values.Any(v => v > 0),
                OnDone = () => GiveResourcesPatches.Safe("give", GiveAll),
                OnCancel = PopThisState,
                HasDistributionAuthority = true,
                DoneButtonKey = "FHMODS_GIVE",
                CancelButtonKey = "GUI_CANCEL",
            });
        }

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

        private void GiveAll()
        {
            CharacterState giver = GiveResourcesPatches.Find(View, _giverId);
            var gifts = _pending.Where(p => p.Value > 0).OrderBy(p => p.Key.Column).ThenBy(p => p.Key.Kind).ToList();
            PopThisState();
            if (giver == null) return;
            foreach (var gift in gifts)
            {
                CharacterState receiver = GiveResourcesPatches.Find(View, _receivers[gift.Key.Column]);
                if (!GiveResourcesPatches.Give(View, giver, receiver, gift.Key.Kind, gift.Value)) break;
            }
        }
    }
}
