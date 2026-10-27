using System;
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

            /// <summary>Why the switch can't be moved to <c>wantOn</c> right now (it is shown greyed, with the reason), or
            /// null. Content packs use it: the pack must be installed to turn on, and nothing in the campaign may still
            /// use it to turn off.</summary>
            public Func<bool, string> Blocked;

            /// <summary>A named save the host's game makes just before this rule goes on (a point to go back to when the
            /// rule commits the campaign to something, as a content pack does).</summary>
            public string BackupBeforeOn;
        }

        /// <summary>The note added to a locked rule's description while it is locked.</summary>
        public static readonly string LockedNote = "\n\n" + Red("(Locked during online scenarios: change it in the outpost.)");

        /// <summary>Text in the warning red the rules screen uses for "can't change this now" notes (TextMeshPro rich text).</summary>
        public static string Red(string text) => "<color=#FF6B6B>" + text + "</color>";

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
        public const ERulesFlag QuestRewards = (ERulesFlag)(1 << 17);
        public const ERulesFlag KeepMasteries = (ERulesFlag)(1 << 16);
        public const ERulesFlag GloomhavenClasses = (ERulesFlag)(1 << 15);
        // under the pack rule: every pack class open at once instead of one envelope per retirement (2026-10-27)
        public const ERulesFlag AllGloomhavenClassesOpen = (ERulesFlag)(1 << 14);
        // The game's own rule bit, which it never shows on its rules screen (ReducedRandomness.cs passes it to the
        // scenario rules). A save keeps it without the mods too. (Its CanRespecCharacters bit was offered as "Respec
        // without the Library" for a day and dropped, 2026-10-25: the Library's respec is enough.)
        public const ERulesFlag ReducedRandomness = ERulesFlag.ReducedRandomness;

        public static readonly List<Rule> All = new List<Rule>
        {
            new Rule
            {
                Flag = GloomhavenClasses,
                Name = "Gloomhaven classes (content pack)",
                Description = "Adds Gloomhaven's classes to this campaign (the Brute, Scoundrel, Sunkeeper, Sawbones and " +
                              "Quartermaster so far), each built on a player's PC from their own copy of Gloomhaven. The " +
                              "classes wait in envelopes: each retirement in the campaign opens one, and the party picks one " +
                              "of two classes to unlock, on the game's own class-choice screen (a pack's starting classes, if " +
                              "it has any, open the moment this is turned on; in a solo scenario, at the next outpost). " +
                              "\"Gloomhaven classes: all open at once\" (the next switch) opens them all instead.\n\n" +
                              "This is a content pack, not a rule: every PC in the game needs the same pack installed before " +
                              "the game starts (see the mod guide). A player without it is refused when they try to join; " +
                              "a player without the mod at all only sees their join window reset, so tell them. Once a " +
                              "character of one of these classes has been created, the campaign needs the pack for good: " +
                              "this switch locks on, and the campaign must never be opened in a game without the pack (the " +
                              "game would move the save to its corrupted folder). The host's game makes a backup save the " +
                              "moment this is turned on.\n\n" +
                              "The way out: \"Copy without content packs\" (in the Save Game window here, or on any campaign " +
                              "in Load game) saves a copy of the campaign as a new campaign without the pack's characters and " +
                              "unlocks, which any game loads. It needs the mods, not the pack.",
                Blocked = GHClassesRule.WhyBlocked,
                BackupBeforeOn = "Mod backup - before Gloomhaven classes",
            },
            new Rule
            {
                Flag = AllGloomhavenClassesOpen,
                Name = "Gloomhaven classes: all open at once (mod)",
                Description = "With the Gloomhaven classes pack on: every installed Gloomhaven class is open for new " +
                              "characters at once, instead of arriving one envelope per retirement. For testing the " +
                              "classes, or a party that wants the whole roster from the start. Turning it off later keeps " +
                              "the classes already open; the still-locked ones go back to waiting for retirements.",
                Blocked = GHClassesRule.WhyAllOpenBlocked,
            },
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
                Flag = QuestRewards,
                Name = "Show personal quest rewards (mod)",
                Description = "When a new character picks a personal quest (or a retirement draws an extra one), each " +
                              "quest card shows the building it unlocks when that character retires, what it unlocks " +
                              "instead if that building is unlocked first, and whether another party member's quest " +
                              "would unlock the same building.\n\n" +
                              "Board game rule restored: the quest card lists the building envelopes it unlocks. " +
                              "Display only: it changes nothing in play.",
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
                Flag = KeepMasteries,
                AffectsScenarios = true,
                Name = "Masteries carry over (mod)",
                Description = "Once a character earns a mastery, every later character of that class starts with it " +
                              "already completed and gets its perk mark when created. Only masteries earned while this " +
                              "rule is on count.\n\n" +
                              "Board game rule bent: masteries belong to the character who earned them.",
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
                Flag = ReducedRandomness,
                AffectsScenarios = true,
                Name = "Reduced randomness (mod)",
                Description = "Gloomhaven digital's house rule, which this game carries but never offers: every attack " +
                              "modifier deck, players' and monsters', has a -2 instead of the null and a +2 instead of the " +
                              "x2, and both still reshuffle the deck at the end of the round. Curses are -2 and blesses +2 " +
                              "(instead of null and x2). No more wasted attacks or huge spikes.\n\n" +
                              "Takes effect when the next scenario starts. Perks work as usual (removing a -2 takes the plain " +
                              "one).\n" +
                              "Board game rule bent: the null and the x2 are part of every modifier deck.",
            },
            new Rule
            {
                Flag = MilestoneSaves,
                Name = "Milestone saves (mod)",
                Description = "The host's game makes a named save at the start of each scenario and when the outpost opens " +
                              "after one (\"Mod backup - week 23 - scenario 14 start\", \"Mod backup - week 23 - after scenario 14\"). They are " +
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
                              "Campaign button with each character's totals over every scenario played with this on.\n" +
                              "Each finished scenario's full battle record is also saved for the companion app's Stats " +
                              "tab (wins, losses and resigns; never restarts or unfinished runs).\n\n" +
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
                              "deck will reshuffle at the end of the round. Curses count as nulls and blesses as x2s (as -2 and +2 under Reduced randomness).\n\n" +
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

        /// <summary>Runs exactly once per rule change on every PC, host and clients, after the campaign's rules number
        /// changed (RuleChangePatches in GHClassesRule.cs). Features that must react to a change hook in here.</summary>
        public static void Applied(ERulesFlag before, ERulesFlag after)
        {
            if (before == after) return;
            CoopLog.Write($"RULES APPLIED here: {(int)before:X} -> {(int)after:X} ({Describe(after)}) role={CoopLog.Role()}");
            GHClassesRule.OnRulesApplied(before, after);
        }
    }
}
