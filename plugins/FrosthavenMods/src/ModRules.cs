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

        public static readonly List<Rule> All = new List<Rule>
        {
            new Rule
            {
                Flag = GiveResources,
                Name = "Give resources (mod)",
                Description = "In the outpost, Ctrl+click a character's portrait in the party column to give their gold, " +
                              "lumber, metal or hide to another character: in the party, on the bench, or being created.\n\n" +
                              "The giver must have played at least one scenario. Herbs are already shared in this game.\n" +
                              "Board game rule bent: gold and resources normally can't be transferred between characters.",
            },
            new Rule
            {
                Flag = ItemTrading,
                Name = "Item trading (mod)",
                Description = "Not active yet: this switch only tests that the setting saves and syncs to everyone.\n\n" +
                              "Planned: give an item you own to another character in the outpost.",
            },
        };

        public static ERulesFlag Mask => All.Aggregate((ERulesFlag)0, (m, r) => m | r.Flag);

        /// <summary>The current campaign's rules (None outside a campaign).</summary>
        public static ERulesFlag Current => (SaveData.Instance?.Global?.CurrentAdventureData?.Rules).GetValueOrDefault();

        public static bool IsOn(ERulesFlag flag) => (Current & flag) == flag;

        public static string Describe(ERulesFlag rules)
        {
            var on = All.Where(r => (rules & r.Flag) == r.Flag).Select(r => r.Name).ToArray();
            return on.Length == 0 ? "none on" : string.Join(", ", on);
        }

        public static string Describe() => All.Count + " defined";
    }
}
