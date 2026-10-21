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

            /// <summary>Changes what happens inside scenarios (or at their end). A rule change reaches each co-op player
            /// outside the scenario's lockstep, so these are locked during online scenarios (a change landing at
            /// different moments on different PCs would desync them). Display-only and outpost-only rules aren't.</summary>
            public bool AffectsScenarios;
        }

        /// <summary>The note added to a locked rule's description while it is locked.</summary>
        public const string LockedNote = "\n\n(Locked during online scenarios: change it in the outpost.)";

        // Bit 24 was first released as "Shared gold", then "Give resources"; it now means Tradeable resources and
        // equipment (a save with it on keeps it on). Bit 25 was "Item trading" before the two were merged: unused now,
        // and harmless if an old test save still has it set.
        public const ERulesFlag Trading = (ERulesFlag)(1 << 24);
        public const ERulesFlag ShareDuplicateLoot = (ERulesFlag)(1 << 26);
        // Bit 27 was "Always allocate scenario loot" (tried and removed 2026-10-20): unused, harmless if a save has it.
        public const ERulesFlag RevealEvents = (ERulesFlag)(1 << 28);
        public const ERulesFlag DwarfTweaks = (ERulesFlag)(1 << 29);
        public const ERulesFlag BugFixes = (ERulesFlag)(1 << 30);

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
                AffectsScenarios = true,
                Name = "Share duplicate loot (mod)",
                Description = "When a character loots a copy of an item they already own, the party picks who gets it " +
                              "back in the outpost (the usual item reward window) instead of it being sold automatically. " +
                              "If nobody else in the party can take it, it is still sold.\n\n" +
                              "Board game rule restored: the looter gives it to another character or sells it. To sell, " +
                              "the new owner sells it in Downtime for the same gold.",
            },
            new Rule
            {
                Flag = RevealEvents,
                Name = "Reveal event outcomes (mod)",
                Description = "On town, road and boat events, each option shows what it will do for your party (gold, " +
                              "damage, morale, choices you'll make, an outpost attack) before you pick it. Hover over an " +
                              "option for its outcome text. Random outcomes show the roll you would actually get.\n\n" +
                              "Board game rule bent: you normally read only the outcome of the option you chose.",
            },
            new Rule
            {
                Flag = DwarfTweaks,
                AffectsScenarios = true,
                Name = "Dwarf's balance changes (mod)",
                Description = "Tweaks from Dwarf74's Unofficial Frosthaven Campaign Tweaks (he maintains the official FAQ " +
                              "and was a main campaign playtester):\n" +
                              "- Personal quests: once one per party member is complete (2 to 4), the ones that unlock the Tavern (74) and the " +
                              "Stables (88) are dealt first, and new deals prefer quests that still unlock something new.\n" +
                              "- Early Barracks: in Winter of year 1 (from week 2, every 2 weeks), with morale 10+ and " +
                              "the Barracks at level 1, the outpost event is WO-20, the Barracks upgrade event.\n" +
                              "- PQ 07 (Aesther Outpost): its two 6-week waits are 5 weeks. PQ 15: the goal is 60 gold.\n" +
                              "- Scenario 14: also kill all Abael Herders to win; Piranha Pigs spawn only on odd rounds " +
                              "and Lightning Eels only on even rounds, alternating spawn points.\n" +
                              "- Boneshaper's first mastery (kill 15 of your summons) counts over any number of scenarios.",
            },
            new Rule
            {
                Flag = BugFixes,
                AffectsScenarios = true,
                Name = "Game bug fixes (mod)",
                Description = "Fixes for bugs in the game's own code:\n" +
                              "- Fair attack modifier shuffles. The game's shuffle is off by one (a card can never stay " +
                              "where it was) and new decks always put a +0 at the bottom, which makes nulls and x2s " +
                              "slightly more frequent (about 6.25% of draws each instead of 6%). With this on, player " +
                              "and monster decks are shuffled fairly.\n" +
                              "- Co-op desync fixes: a failed outpost command no longer freezes that player's outpost; " +
                              "the Strategy favor draws the same cards for everyone; a Restful Slippers short rest no " +
                              "longer puts the players' dice out of step; \"infuse any element\" uses the shared dice.\n" +
                              "- Gameplay fixes: rebuilding costs no extra morale; morale can't go below 0; the Jeweler " +
                              "discount is charged as shown; PQ 14 needs five different areas; the last week of a season " +
                              "counts as week 10 (not 0); SO-10's delivery arrives in later years too; rolling modifiers " +
                              "aren't duplicated by a mid-attack reshuffle.",
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
