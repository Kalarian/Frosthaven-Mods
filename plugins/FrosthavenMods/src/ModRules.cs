using System.Collections.Generic;
using System.Linq;

namespace FrosthavenMods
{
    /// <summary>
    /// Our house rules live in the game's own campaign rules number (PartyAdventureData.Rules, an ERulesFlag) as extra
    /// bits. The game saves that number in the save header and sends it to every co-op player (join token +
    /// ChangeHouseRuleSettings action), so our rules ride along for free. The vanilla game ignores bits it does not know,
    /// so a save stays loadable without the mod. We use high bits so a future official rule cannot collide with ours
    /// (vanilla uses 0x01..0x40).
    /// </summary>
    internal static class ModRules
    {
        public sealed class Rule
        {
            public ERulesFlag Flag;
            public string Name;
            public string Description;
        }

        // Bit 24 was first released as "Shared gold", then "Give resources"; it now means Tradeable resources and
        // equipment (a save with it on keeps it on). Bit 25 was "Item trading" before the two were merged: unused now,
        // and harmless if an old test save still has it set.
        public const ERulesFlag Trading = (ERulesFlag)(1 << 24);
        public const ERulesFlag ShareDuplicateLoot = (ERulesFlag)(1 << 26);
        // Bit 27 was "Always allocate scenario loot" (tried and removed 2026-10-20): unused, harmless if a save has it.

        public static readonly List<Rule> All = new List<Rule>
        {
            new Rule
            {
                Flag = Trading,
                Name = "Tradeable resources and equipment (mod)",
                Description = "In the outpost, Ctrl+click a character's portrait in the party column (or use the GIVE " +
                              "button under their name) to give their gold, lumber, metal, hide or items to another " +
                              "party member. Gold and materials can also go to a character being created.\n\n" +
                              "Herbs are already shared in this game. Nobody can own two copies of an item.\n" +
                              "Board game rule bent: gold, resources and items normally can't be transferred between characters.",
            },
            new Rule
            {
                Flag = ShareDuplicateLoot,
                Name = "Share duplicate loot (mod)",
                Description = "When a character loots a copy of an item they already own, the party picks who gets it " +
                              "back in the outpost (the usual item reward window) instead of it being sold automatically. " +
                              "If nobody else in the party can take it, it is still sold.\n\n" +
                              "Board game rule restored: the looter gives it to another character or sells it. To sell, " +
                              "the new owner sells it in Downtime for the same gold.",
            },
        };

        public static ERulesFlag Mask => All.Aggregate((ERulesFlag)0, (m, r) => m | r.Flag);

        /// <summary>The current campaign's rules (None outside a campaign).</summary>
        public static ERulesFlag Current => (SaveData.Instance?.Global?.CurrentAdventureData?.Rules).GetValueOrDefault();

        public static bool IsOn(ERulesFlag flag) => (Current & flag) == flag;

        /// <summary>The Give window (Ctrl+click, the GIVE button, the hint line) is there while the trading rule is on.</summary>
        public static bool GiveOn => IsOn(Trading);

        public static string Describe(ERulesFlag rules)
        {
            var on = All.Where(r => (rules & r.Flag) == r.Flag).Select(r => r.Name).ToArray();
            return on.Length == 0 ? "none on" : string.Join(", ", on);
        }

        public static string Describe() => All.Count + " defined";
    }
}
