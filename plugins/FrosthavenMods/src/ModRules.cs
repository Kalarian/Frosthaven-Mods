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

        // Bit 24 was first released as "Shared gold"; it now means Give resources (a save with it on keeps it on).
        public const ERulesFlag GiveResources = (ERulesFlag)(1 << 24);
        public const ERulesFlag ItemTrading = (ERulesFlag)(1 << 25);
        public const ERulesFlag ShareDuplicateLoot = (ERulesFlag)(1 << 26);

        public static readonly List<Rule> All = new List<Rule>
        {
            new Rule
            {
                Flag = GiveResources,
                Name = "Give resources (mod)",
                Description = "In the outpost, Ctrl+click a character's portrait in the party column to give their gold, " +
                              "lumber, metal or hide to another party member (or one being created).\n\n" +
                              "The giver must have played at least one scenario. Herbs are already shared in this game.\n" +
                              "Board game rule bent: gold and resources normally can't be transferred between characters.",
            },
            new Rule
            {
                Flag = ItemTrading,
                Name = "Item trading (mod)",
                Description = "In the outpost, give an item a character owns (worn or not) to another party member: the " +
                              "Items tab of the same Give window.\n\n" +
                              "The giver must have played at least one scenario, and nobody can own two copies of an item.\n" +
                              "Board game rule bent: items normally can't be transferred or traded between characters.",
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

        /// <summary>The Give window (Ctrl+click, the GIVE button, the hint line) is there when either give rule is on.</summary>
        public static bool GiveOn => IsOn(GiveResources) || IsOn(ItemTrading);

        public static string Describe(ERulesFlag rules)
        {
            var on = All.Where(r => (rules & r.Flag) == r.Flag).Select(r => r.Name).ToArray();
            return on.Length == 0 ? "none on" : string.Join(", ", on);
        }

        public static string Describe() => All.Count + " defined";
    }
}
