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
        public const ERulesFlag MilestoneSaves = (ERulesFlag)(1 << 23);
        public const ERulesFlag BattleStats = (ERulesFlag)(1 << 22);
        public const ERulesFlag RewardWindow = (ERulesFlag)(1 << 21);
        public const ERulesFlag DeckOdds = (ERulesFlag)(1 << 20);
        public const ERulesFlag ScenarioMap = (ERulesFlag)(1 << 19);
        public const ERulesFlag StoryEvents = (ERulesFlag)(1 << 18);

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
                Flag = StoryEvents,
                Name = "More story events (mod)",
                Description = "When the week's town event is drawn, if none of its options could unlock anything new (a " +
                              "scenario, a section, another event, a building, an item, a quest...), it goes to the " +
                              "bottom of the deck and the next card is drawn instead. Only once a week: the second card " +
                              "is kept, whatever it is. Outpost attacks are never put back.\n\n" +
                              "Roughly half of summer weeks and a quarter of winter weeks bring something new, instead " +
                              "of about a third and a seventh.\n" +
                              "Board game rule bent: the top card of the event deck is always the one you read.",
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
                              "aren't duplicated by a mid-attack reshuffle; the loot deck gets its fair share of " +
                              "\"+2 at 2-3 players\" material cards, and Restful Slippers offers fairly picked cards.\n" +
                              "- Outpost attacks: events that give a target order (WO-33, WO-34, WO-35, WO-71) follow it, " +
                              "SO-23 spares odd-numbered buildings, and WO-33's threat cards go back into the town guard " +
                              "deck instead of being lost for the campaign.",
            },
            new Rule
            {
                Flag = MilestoneSaves,
                Name = "Milestone saves (mod)",
                Description = "The host's game makes a named save at the start of each scenario and of each outpost phase " +
                              "(\"Mod backup - week 23 - scenario 14 start\", \"Mod backup - week 24 - outpost\"). They are " +
                              "in the usual Load list. The newest " + FrosthavenMods.MilestoneSaves.Keep + " are kept; your own " +
                              "named saves and the game's autosaves are never touched.\n\n" +
                              "The game keeps only its last 15 autosaves (about the last hour or two of play). To go back " +
                              "in co-op, the host loads a backup on their own, then hosts again. Not available in Iron Man.",
            },
            new Rule
            {
                Flag = BattleStats,
                Name = "Battle stats (mod)",
                Description = "After each scenario, the Statistics tab gets about 40 more rows (offense, defense, support, " +
                              "luck, cards and elements, exploration), with summons counted for their owner, and a " +
                              "Campaign button with each character's totals over every scenario played with this on.\n\n" +
                              "Display only: it changes nothing in play.",
            },
            new Rule
            {
                Flag = RewardWindow,
                Name = "Easier item reward window (mod)",
                Description = "The window that hands out item rewards (events, quests, shared duplicate loot) opens with " +
                              "nothing assigned, so you choose who gets each item, and left-clicking another character " +
                              "moves an assigned item to them in one click (instead of right-click, then left-click).\n\n" +
                              "Every item still has to be assigned before Done.",
            },
            new Rule
            {
                Flag = DeckOdds,
                Name = "Modifier deck odds (mod)",
                Description = "In scenarios, right-click a character: under Attack Modifiers (which already shows how many " +
                              "of each card are left), it also shows the odds of their next draw: null, -1/-2, +0, +1 or " +
                              "more, x2 and a rolling bonus, plus each card's own chance on its icon. Right-click a monster for the monster deck. It notes when a " +
                              "deck will reshuffle at the end of the round. Curses count as nulls and blesses as x2s.\n\n" +
                              "Display only: it changes nothing in play, and it only does the math on the counts the " +
                              "game already shows.",
            },
            new Rule
            {
                Flag = ScenarioMap,
                Name = "Scenario map file (mod)",
                Description = "Each time the game autosaves, writes this campaign's scenario progress (completed, available " +
                              "and locked-out scenarios, read sections, the calendar, puzzles, personal quests) to a file " +
                              "in the game folder, under FrosthavenMods\\scenarios. The mod's scenario map page " +
                              "(tools/scenario_map.py) uses it to colour the unlock map and to show what each choice " +
                              "still leaves open.\n\n" +
                              "Display only: it changes nothing in play. The map shows every scenario's name (spoilers).",
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
