"""Retirement rewards for the ported classes: each Gloomhaven class's own city and road events, as Frosthaven events.

In Gloomhaven, a class's first retirement shuffles two events written about that character into the city and road
decks (Campaign.ruleset: Achievement_Retirement_<Class> -> TT_Achievement_Retirement_<Class>: CityEvent + RoadEvent).
Frosthaven rewards five of its own classes the same way (the Blinkblade's SO-64, the Deepwraith's SO-65, the HIVE's
WR-48...), the rest with a story section a few weeks later. So a ported class's retirement section enables its
Gloomhaven events (EnableEvent) instead of borrowing a Frosthaven class's reward: a city event becomes an outpost event,
a road event a road event, and an option that adds another event to a deck enables it the same way.

Frosthaven's events are C# classes, not data, so the plugin (GHEvents.cs) builds one definition per event from the
pack: pack.json "events" (ID, deck, options and their effects) and strings.tsv (the text, under the keys the event
screen looks up: <ID>_Front, <ID>_Option_<n>, <ID>_Option_<n>_Outcome). Both are covered by data_sha.

THE REWARD REVIEW. Gloomhaven's effects don't exist in Frosthaven as they are (reputation, Gloomhaven items, its own
discard counts), so every option is translated and every translation is reviewed by the player before it ships:
`python tools/gh_testsheet.py rewards <Class>` writes tests/<Class> rewards review.xlsx, one row per option with the
Gloomhaven effect, the Frosthaven effect, how often Frosthaven's own 264 events give that, and whether it is inside
the GUIDELINES below. An option is only built once its "status" here is "approved" (the review's verdict); until then
it ships with its Gloomhaven text and no effect, so the events can be tested in game while the numbers are discussed.

    EVENTS[cls] = [{"id", "deck", "start", "gh", "front", "options": [{"text", "outcome", "gh", "fh", "note", "status"}]}]
      deck:   SummerOutpost | WinterOutpost | SummerRoad | WinterRoad (Frosthaven's decks; a city event goes to an
              outpost deck, a road event to a road deck, the season chosen in the review)
      start:  "retire" (enabled by the class's retirement section) or "chain" (enabled by another option's EnableEvent)
      gh:     the Gloomhaven event it comes from; "fh" effects as (CampaignEffectType, params...)
"""

# How often Frosthaven's own events give each effect, by size (tallied from the game's 264 event classes,
# FHRL.OutpostEvents, build 1.0.34121): the yardstick for the review.
FH_BASELINE = {
    "GainMorale": {1: 62, 2: 29, 3: 8, 4: 1},
    "LoseMorale": {1: 28, 2: 6, 3: 1},
    "AddProsperity": {1: 19},
    "LoseProsperity": {1: 6},
    "GainInspiration": {1: 13, 2: 1},
    "GainGold": {10: 9, 15: 8, 20: 7, 30: 5, 40: 1},
    "LoseGold": {5: 6, 10: 5, 15: 3, 30: 1},
    "PartyDiscardCards": {1: 15, 2: 15},
    "PartyDamage": {1: 3, 2: 15, 3: 9},
    "PartyBless": {1: 10, 2: 3},
    "PartyCurse": {1: 8, 2: 1},
    "GainItem": {"a specific item": 31},
    "EnableEvent": {"another event": 0},
}

# THE GUIDELINES: a ported class's events may give what Frosthaven's own events give, at Frosthaven's own sizes, and
# nothing that lasts into scenarios. Per option: (effect -> largest allowed amount); effects not listed need a note.
GUIDELINES = [
    "One-time rewards only, like Frosthaven's own class events: morale, prosperity, inspiration, gold, resources, a "
    "shop-level item. Nothing permanent in scenarios (no town guard cards, perk checks, extra item slots, class unlocks).",
    "At most Frosthaven's common sizes per option: +2 morale, +1 prosperity, +1 inspiration, 20 gold, a Frosthaven item "
    "costing 30 gold or less.",
    "Penalties at Frosthaven's sizes too: -2 morale, discard 2, 3 damage, 15 gold.",
    "Reputation becomes morale (Frosthaven has no reputation; morale is the outpost's standing). Gloomhaven items "
    "become the same item where Frosthaven has it, else one of similar cost and use.",
    "A class's events together should not outweigh one Frosthaven class reward: roughly one good option per event.",
]
LIMITS = {"GainMorale": 2, "AddProsperity": 1, "GainInspiration": 1, "GainGold": 20, "AllCharactersGainGold": 10,
          "LoseMorale": 2, "PartyDiscardCards": 2, "PartyDamage": 3, "LoseGold": 15, "PartyBless": 1, "PartyCurse": 1}
ITEM_COST_LIMIT = 30
NEVER = {"AddTownGuardMod", "AllCharactersAddPerkCheck", "UnlockClass", "UnlockClassChoise", "IncreaseMaxSmallItem",
         "AllCharactersGainExperience", "UnlockBuilding", "UpgradeBuilding", "OpenEnvelope"}

EVENTS = {
    "Brute": [
        {
            "id": "GH.Brute-1", "deck": "SummerOutpost", "start": "retire", "gh": "City event 42",
            "front": (
                "Walking near the outpost gate, you are approached by a scrawny kid in a guard uniform, which is clearly "
                "too big for his small frame.\n\n"
                "“Hey, you all are famous!” he says with a big smile. “I worked as a caravan guard before I got "
                "this job. Met with a big Inox guy who always talked about you. He made you seem like the nicest, most "
                "amazing people.”\n\n"
                "His smile and excitement don’t waver. “Say, my detail is doing some work to repair the wall damage "
                "from the last attack. Would you mind helping us? It’ll be fun!”"),
            "edits": "\"the West Gate\" -> \"the outpost gate\"; \"the last Vermling attack\" -> \"the last attack\" (Gloomhaven's city).",
            "options": [
                {"text": "Agree to help the presumptuous kid.",
                 "outcome": ("It’s hard work, but the kid’s enthusiasm is infectious. He wants to know all about your "
                             "exploits and adventures, and he and his detail prove to be a great audience. By the end of "
                             "the day, it hardly feels like you’ve worked at all."),
                 "gh": "+1 reputation, +1 prosperity",
                 "fh": [("GainMorale", 1), ("AddProsperity", 1)],
                 "note": "Reputation -> morale. The Blinkblade's SO-64 gives +1 prosperity and +1 inspiration the same way.",
                 "status": "approved"},
                {"text": "Tell this kid that he must have the wrong group of adventurers.",
                 "outcome": ("You shake your head and avert your gaze. You claim that the kid must have you confused with a "
                             "different adventuring party. He looks crushed as you walk away."),
                 "gh": "-1 reputation",
                 "fh": [("LoseMorale", 1)],
                 "note": "Reputation -> morale.",
                 "status": "approved"},
            ],
        },
        {
            "id": "GH.Brute-2", "deck": "SummerRoad", "start": "retire", "gh": "Road event 42",
            "front": (
                "A trade caravan passes by you on the road. This event isn’t too uncommon, but what catches your eye is "
                "a familiar-looking Inox traveling with the caravan as a guard.\n\n"
                "At the sight of you, the Brute lights up and gives you a big wave, then rushes over to greet you. He is "
                "positively joyful to see you and immediately starts to reminisce about all the exciting battles you "
                "fought together.\n\n"
                "Slapping you on the back, he implores you to travel with the caravan for a while to give you all a "
                "chance to catch up."),
            "edits": "none",
            "options": [
                {"text": "Agree to travel with the Brute for a little while. So what if it’s in the wrong direction?",
                 "outcome": ("As you walk with the Brute and talk with him about your early adventuring days, you find a hint "
                             "of sadness behind his enthusiasm. He explains how he fell on hard times after leaving the "
                             "group and had to resort to jobs like this to make ends meet. The Brute seems uplifted by "
                             "your conversation, but the day grows late. By the time you head back and arrive at your "
                             "destination, you are incredibly tired."),
                 "gh": "start the scenario with 3 cards discarded",
                 "fh": [("PartyDiscardCards", 2)],
                 "note": "Frosthaven's road events discard 1 or 2, never 3: lowered to its largest.",
                 "status": "approved"},
                {"text": "Regretfully leave the Brute to his duties and continue on your way.",
                 "outcome": ("You explain to the Brute that you have a long day’s travel ahead of you and cannot take the "
                             "time to travel in the other direction. As you make your good-byes, a shadow falls over him. He "
                             "walks away forlorn."),
                 "gh": "add road event 60 to the deck",
                 "fh": [("EnableEvent", "GH.Brute-3")],
                 "note": "The follow-up event, as in Gloomhaven. No effect now.",
                 "status": "approved"},
            ],
        },
        {
            "id": "GH.Brute-3", "deck": "SummerRoad", "start": "chain", "gh": "Road event 60",
            "front": (
                "You see smoke on the horizon and sigh, expecting the worst. Sure enough, you approach the scene to find a "
                "ransacked caravan—burned, pillaged, and destroyed by what appears to be raiders.\n\n"
                "You find one survivor among the victims who laments that when the raiders came upon them, one of the "
                "guards, a large Inox, seemed to throw aside his weapon and simply accept his fate.\n\n"
                "That’s when you find the body of the Brute, torn and bloody among the wreckage. Seeing him this way, "
                "the fire gone from his eyes, he appears unnaturally small."),
            "edits": "\"Vermlings\" -> \"raiders\" (twice).",
            "options": [
                {"text": "Respectfully bury the dead and mourn their loss.",
                 "outcome": ("Though you didn’t take the time to talk with the Brute earlier, now you take the time to "
                             "bury his corpse and say a few words about his strength of body and character. The survivor "
                             "thanks you for your help, and you continue on your way once more."),
                 "gh": "+1 reputation",
                 "fh": [("GainMorale", 1)],
                 "note": "Reputation -> morale.",
                 "status": "approved"},
                {"text": "Take what you can find and move on.",
                 "outcome": ("Near the corpse of the Brute, you find an axe, presumably the weapon he threw away. There’s "
                             "really nothing else among the debris, so you wordlessly pick it up and move on."),
                 "gh": "gain the Battle Axe",
                 "fh": [("GainItem", 136)],
                 "note": "Frosthaven has the same item: Battle Axe (136), a two-handed weapon, 20 gold. Goes to the first "
                         "character who can take it; sold for gold if every copy is owned.",
                 "status": "approved"},
            ],
        },
    ],
}

DECKS = {"SummerOutpost": ("Outpost", False), "WinterOutpost": ("Outpost", True),
         "SummerRoad": ("Road", False), "WinterRoad": ("Road", True)}


def events(cls):
    return EVENTS.get(cls, [])


def retirement_effects(cls):
    """The class's retirement section effects: enable each of its "retire" events (None: the class has no events)."""
    evs = [e for e in events(cls) if e["start"] == "retire"]
    return [("EnableEvent", e["id"]) for e in evs] if evs else None


def effects(option):
    """What an option does in the build: its Frosthaven effects once approved, nothing before (see THE REWARD REVIEW).
    An EnableEvent is structure (the chain), not a reward: it always ships."""
    if option.get("status") == "approved":
        return list(option["fh"])
    return [e for e in option["fh"] if e[0] == "EnableEvent"]


def manifest(cls):
    """pack.json "events" entries for the plugin."""
    out = []
    for e in events(cls):
        kind, winter = DECKS[e["deck"]]
        out.append({"id": e["id"], "type": kind, "winter": winter, "class": cls,
                    "options": [{"id": i, "effects": [list(x) for x in effects(o)]} for i, o in enumerate(e["options"], 1)]})
    return out


def strings(cls):
    """The text, under the keys Frosthaven's event screen looks up (NarrationSetting)."""
    t = {}
    for e in events(cls):
        t[f"{e['id']}_Front"] = e["front"]
        for i, o in enumerate(e["options"], 1):
            t[f"{e['id']}_Option_{i}"] = o["text"]
            t[f"{e['id']}_Option_{i}_Outcome"] = o["outcome"]
    return t


def check(option, item_costs=None):
    """None when the option's Frosthaven effects are inside the GUIDELINES, else why not."""
    why = []
    for kind, *params in option["fh"]:
        if kind in NEVER:
            why.append(f"{kind} is never a ported reward")
        elif kind == "GainItem":
            cost = (item_costs or {}).get(int(params[0]))
            if cost is None:
                why.append(f"item {params[0]}: price unknown")
            elif cost > ITEM_COST_LIMIT:
                why.append(f"item {params[0]} costs {cost} gold (limit {ITEM_COST_LIMIT})")
        elif kind in LIMITS and params and isinstance(params[0], int) and params[0] > LIMITS[kind]:
            why.append(f"{kind} {params[0]} (limit {LIMITS[kind]})")
        elif kind not in LIMITS and kind not in ("EnableEvent",):
            why.append(f"{kind}: no guideline, needs a look")
    return "; ".join(why) or None
