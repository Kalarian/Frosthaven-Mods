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

import json
from pathlib import Path
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
    "UnlockRandomSideScenario": {"a random side scenario": 2},
    "AllCharactersGainGold": {5: 1, 10: 1},
    "PartyPoison": {1: 4}, "PartyWound": {1: 6}, "PartyMuddle": {1: 3},
}

# THE GUIDELINES: a ported class's events may give what Frosthaven's own events give, at Frosthaven's own sizes, and
# nothing that lasts into scenarios. Per option: (effect -> largest allowed amount); effects not listed need a note.
GUIDELINES = [
    "One-time rewards only, like Frosthaven's own class events: morale, prosperity, inspiration, gold, resources, a "
    "shop-level item. Nothing permanent in scenarios (no town guard cards, perk checks, extra item slots, class unlocks).",
    "At most Frosthaven's common sizes per option: +2 morale, +1 prosperity, +1 inspiration, 20 gold, a Frosthaven item "
    "costing 30 gold or less.",
    "Penalties at Frosthaven's sizes too: -2 morale, discard 2, 3 damage, a condition on the party, 30 gold at most "
    "(and only when the option is a purchase the party chooses).",
    "A Gloomhaven quest unlock becomes a random side scenario, the one-time unlock Frosthaven's own events give.",
    "Reputation becomes morale (Frosthaven has no reputation; morale is the outpost's standing). Gloomhaven items "
    "become the same item where Frosthaven has it, else one of similar cost and use.",
    "A class's events together should not outweigh one Frosthaven class reward: roughly one good option per event.",
]
LIMITS = {"GainMorale": 2, "AddProsperity": 1, "GainInspiration": 1, "GainGold": 20, "AllCharactersGainGold": 10,
          "LoseMorale": 2, "PartyDiscardCards": 2, "PartyDamage": 3, "LoseGold": 30, "PartyBless": 1, "PartyCurse": 1}
# effects without an amount that Frosthaven's own events use: allowed as they are
PLAIN_OK = {"EnableEvent", "UnlockRandomSideScenario", "PartyPoison", "PartyWound", "PartyMuddle"}
ITEM_COST_LIMIT = 30
BREWABLE = {83, 99}   # Healing Potion, Major Healing Potion: brewed from herbs in Frosthaven, no shop price
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
    "Scoundrel": [
        {
            "id": "GH.Scoundrel-1", "deck": "SummerOutpost", "start": "retire", "gh": "City event 45",
            "front": (
                "“You there! Stop!”\n\n"
                "You turn to see a town guard walking briskly toward you.\n\n"
                "“You are known associates of this woman, yes?” He holds up a crude sketch of the Scoundrel. “We believe she "
                "is behind a number of high-profile robberies and demand that you give us any information you have on her "
                "whereabouts and activities.”\n\n"
                "His face softens a bit. “Look, I know you do a lot of good for the outpost, but the merchants are breathing "
                "down my neck on this one, and I could really use a lead.”"),
            "edits": "\"city guard\" -> \"town guard\"; \"the city\" -> \"the outpost\"; \"the merchant guild\" -> \"the merchants\".",
            "options": [
                {"text": "Cooperate with the guard and tell him everything you know.",
                 "outcome": ("The guard taps his notes with a smile on his face.\n\n“This is great,” he says. “I understand that "
                             "you haven’t seen her in a while, but the information you’ve provided should still be useful. Have "
                             "a nice day!”"),
                 "gh": "+1 reputation", "fh": [("GainMorale", 1)], "note": "Reputation -> morale.", "status": "proposed"},
                {"text": "Be as vague and unhelpful as possible, giving no useful information.",
                 "outcome": ("The guard scratches his head and looks down at his notes.\n\n“Well, that wasn’t very helpful,” he "
                             "sighs. “But thanks for your time, I guess. Let me know if you remember anything or get any new "
                             "information.”"),
                 "gh": "adds city event 62 to the deck", "fh": [("EnableEvent", "GH.Scoundrel-3")],
                 "note": "The follow-up event, as in Gloomhaven. No effect now.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Scoundrel-2", "deck": "SummerRoad", "start": "retire", "gh": "Road event 45",
            "front": (
                "Riding up on horses, a band of dirty, rough men quickly overtake and surround your group. They look hard and "
                "dangerous, and you immediately pull out your weapons as they approach.\n\n"
                "Then you hear a familiar laugh.\n\n"
                "“Well, if it isn’t my old crew.” A woman in dark leather armor leaps off her horse to come greet you. “You "
                "know what, boys? This lot gets a pass.”\n\n"
                "She winks at you and turns around. “Plus, they’d probably kill you all if things turned ugly.”\n\n"
                "Jumping back onto her horse, the Scoundrel looks back at you. “Well, it was nice seeing you. Just remember—you "
                "never saw me.”"),
            "edits": "\"to comes greet you\" -> \"to come greet you\" (Gloomhaven's typo).",
            "options": [
                {"text": "Attack. Friend or not, those who prey on the weak should be brought to justice.",
                 "outcome": ("You see the bandits relax a little and begin to form up. That is when you strike. Some are scared "
                             "off by the sudden attack, but others engage you hungrily. The Scoundrel throws a few good daggers, "
                             "too, before she rides off, criticizing your poor decision-making."),
                 "gh": "each character: Poison and 2 damage", "fh": [("PartyPoison",), ("PartyDamage", 2)],
                 "note": "The same, both at Frosthaven's sizes (its road events deal 2 to the party often).", "status": "proposed"},
                {"text": "Let the bandits go.",
                 "outcome": "The bandits seem a little disappointed as they form up and ride off in the opposite direction.",
                 "gh": "nothing", "fh": [], "note": "Nothing, as in Gloomhaven.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Scoundrel-3", "deck": "SummerOutpost", "start": "chain", "gh": "City event 62",
            "front": (
                "“So, I hear you stood up for me with the guards the other day. Such annoying pests, aren’t they?”\n\n"
                "You turn around in the street to see the Scoundrel emerge from a dark alley with a smile on her face.\n\n"
                "“I just wanted to thank you in person,” she says. “I’ve known quite a few persons who would have ratted me out "
                "for their chance at a piece of gold. It’s good to know some friendships last.”\n\n"
                "She looks over her shoulder. “Listen, I’m leaving town for a good long while. You wanna grab a drink before I "
                "take off?”"),
            "edits": "\"the city guards\" -> \"the guards\".",
            "options": [
                {"text": "Have a drink and reminisce about old times.",
                 "outcome": ("You laugh for a while and trade tales. As the sun drops below the tavern windows, the Scoundrel "
                             "gets up to leave.\n\n“Listen, I really did enjoy our time together, and, well, I just wanted to "
                             "leave you with something to remember me by.”\n\nA piece of black cloth flutters down onto the "
                             "table. When you look back up, the Scoundrel is gone."),
                 "gh": "gain the Thief's Hood", "fh": [("GainItem", 10109)],
                 "note": "Frosthaven has the same item: Thief's Hood (10109), head, 20 gold.", "status": "proposed"},
                {"text": "Overpower the Scoundrel and deliver her to the guards.",
                 "outcome": ("A look of horrid surprise flashes in the Scoundrel’s eyes as you leap forward and throw her to the "
                             "ground. She doesn’t even speak as you bind her arms and take her to the guard. They thank you for "
                             "your service, but their words aren’t much counter to the cold, angry stare of the Scoundrel as "
                             "she’s taken away."),
                 "gh": "+3 reputation", "fh": [("GainMorale", 2)],
                 "note": "Reputation -> morale, at Frosthaven's largest common size (+2; +3 is rare there).", "status": "proposed"},
            ],
        },
    ],
    "Sunkeeper": [
        {
            "id": "GH.Sunkeeper-1", "deck": "SummerOutpost", "start": "retire", "gh": "City event 48",
            "front": (
                "You are haggling over the price of road rations with an irritating Valrath merchant when a messenger boy "
                "approaches you holding a note.\n\n"
                "“Sir, I have a pressing letter from an important Valrath Sunkeeper,” the messenger says. “I was told to "
                "deliver it to you.”\n\n"
                "You reach for it, but the messenger moves back a little. “Sir, this note has traveled a long distance to get "
                "to you. If you could, please pay a small fee to take possession of it.”"),
            "edits": "none",
            "options": [
                {"text": "Begrudgingly pay for the note.",
                 "outcome": ("You throw the kid a few coins and grab the note. Enclosed with the note is a small medallion "
                             "engraved with the image of the sun. Apparently the Sunkeeper thinks the object may help you at "
                             "some point in the future. The note doesn’t say exactly how and the thing has no apparent use. You "
                             "stow it away in your pack."),
                 "gh": "-5 gold; the Sun-Blessed medallion (a Gloomhaven story token)",
                 "fh": [("LoseGold", 5), ("GainInspiration", 1)],
                 "note": "The medallion unlocks nothing in Frosthaven: a keepsake, so +1 inspiration for the 5 gold. Gloomhaven "
                         "only offers the purchase to a party with 5 gold; here the gold goes negative at worst.",
                 "status": "proposed"},
                {"text": "Refuse to pay for the note.",
                 "outcome": ("You laugh in indignation and push the messenger away. He opens his mouth to object, then thinks "
                             "better of it and runs off. You turn back to bartering, now leveraging the proof of your extreme "
                             "thriftiness against the merchant."),
                 "gh": "+3 gold", "fh": [("GainGold", 3)], "note": "The same.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Sunkeeper-2", "deck": "SummerRoad", "start": "retire", "gh": "Road event 48",
            "front": (
                "You see them long before they get close to you—radiant balls of light blazing against the horizon.\n\n"
                "Sun Demons.\n\n"
                "As they approach, you prepare for an attack, but the demons have other plans. They menace you with their "
                "glowing claws, but they would rather have information than kill you.\n\n"
                "“Where is the one you call a Sunkeeper?” one of them hisses. “The Valrath neglects her duties, and now we must "
                "resolve matters personally.”\n\n"
                "You press for further information, but the demons get angry. “These are not your concerns! Tell us where she "
                "is or die at our hands!”"),
            "edits": "none",
            "options": [
                {"text": "Give them whatever information you can on the Sunkeeper’s whereabouts.",
                 "outcome": ("You’re not sure exactly what happened to the Sunkeeper after she left the party, but you tell the "
                             "demons everything you know.\nThey seem satisfied with your response and even offer a bitter thanks "
                             "as they fly off to the west."),
                 "gh": "adds road event 61 to the deck", "fh": [("EnableEvent", "GH.Sunkeeper-3")],
                 "note": "The follow-up event, as in Gloomhaven.", "status": "proposed"},
                {"text": "Attack the demons. There are a lot of them, but you do not take kindly to threats.",
                 "outcome": ("Without even speaking, you draw your weapons and attack. The demons are caught off guard, but they "
                             "bounce back with great ferocity. It is a long, brutal battle, and you continue on in your adventure "
                             "greatly wounded and bloody."),
                 "gh": "each character: Muddle, Wound and 3 damage",
                 "fh": [("PartyMuddle",), ("PartyWound",), ("PartyDamage", 3)],
                 "note": "The same; 3 damage is Frosthaven's largest.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Sunkeeper-3", "deck": "SummerRoad", "start": "chain", "gh": "Road event 61",
            "front": (
                "You are heading through a small forest when you hear the sound of a woman screaming off to the west. You "
                "silently move closer, catching a glimpse of multiple radiant figures through the trees.\n\n"
                "They are Sun Demons, perhaps the same ones who accosted you earlier. That suspicion is confirmed when you get "
                "closer and see they are hovering around an armored Valrath woman bound to a tree, torturing her for "
                "information.\n\n"
                "“Don’t you see how important this is!” one of the demons says. “Give us the location of the temple!”\n\n"
                "“Lies and tricks of a demon,” the Sunkeeper responds. “The temple is in no danger except from you.”"),
            "edits": "none",
            "options": [
                {"text": "Leave the scene quietly and return to your own business.",
                 "outcome": "Not wanting to get involved, you simply move on. Eventually the screams of the Sunkeeper fade away.",
                 "gh": "adds road event 62 to the deck", "fh": [("EnableEvent", "GH.Sunkeeper-4")],
                 "note": "The follow-up event, as in Gloomhaven.", "status": "proposed"},
                {"text": "Attack the demons and free the Sunkeeper.",
                 "outcome": ("You charge out of the trees and attack the Sun Demons. Fueled by righteous indignation and a bit of "
                             "guilt, you slay them all. The battle leaves you wounded, but the Sunkeeper happily mends your wounds "
                             "once you free her.\n\n“They seemed to think the Sun Temple is under attack by Night Demons,” she "
                             "says. “I have no reason to believe them, but still, would you mind helping me look into it?”"),
                 "gh": "nothing (the Sunkeeper heals you)", "fh": [("GainMorale", 1)],
                 "note": "Gloomhaven gives nothing; a friend freed is worth +1 morale here, Frosthaven's commonest reward.",
                 "status": "proposed"},
            ],
        },
        {
            "id": "GH.Sunkeeper-4", "deck": "SummerRoad", "start": "chain", "gh": "Road event 62",
            "front": (
                "By now, the Sun Demons cut a very recognizable portrait against the horizon as they move toward you. You "
                "sincerely had hoped you were done with these creatures.\n\n"
                "“We have urgent need of your assistance,” one of them starts. “Night Demons have infested the Sun Temple, "
                "attempting to desecrate it and send the world into eternal darkness.\n\n"
                "“We have tried to stop them, but they are too fortified inside the temple. We regrettably need more "
                "strength.\n\n"
                "“Please, you cannot want an absence of a sun on this plane any less than we do. It would be disastrous.”"),
            "edits": "\"Sun Temple. attempting\" -> \"Sun Temple, attempting\" (Gloomhaven's typo).",
            "options": [
                {"text": "Agree to help the Sun Demons.",
                 "outcome": ("You sigh and agree, warning them that this had better be the last time they ask you for a favor. "
                             "They give you the location of the Sun Temple and implore you to hurry."),
                 "gh": "unlocks Gloomhaven's scenario 85", "fh": [("UnlockRandomSideScenario",)],
                 "note": "Frosthaven has no Sun Temple: a random side scenario instead, the one-time unlock its own events give.",
                 "status": "proposed"},
                {"text": "Attack the Sun Demons. It may be the only way to stop them from bothering you.",
                 "outcome": ("Without even speaking, you draw your weapons and attack. The demons are caught off guard, but they "
                             "bounce back with great ferocity. It is a long, brutal battle, and you continue on in your adventure "
                             "greatly wounded and bloody."),
                 "gh": "each character: Muddle, Wound and 3 damage; unlocks scenario 85 anyway",
                 "fh": [("PartyMuddle",), ("PartyWound",), ("PartyDamage", 3), ("UnlockRandomSideScenario",)],
                 "note": "The same conditions and damage; the scenario becomes a random side scenario.", "status": "proposed"},
            ],
        },
    ],
    "Sawbones": [
        {
            "id": "GH.Sawbones-1", "deck": "SummerOutpost", "start": "retire", "gh": "City event 56",
            "front": (
                "Looking for a little bit of variety, you head to the roughest table in the Tavern as night falls. After some "
                "wait, you are surprised to see the Sawbones sitting near the back of the room by himself. Considering this "
                "corner is about as seedy as Frosthaven gets, it is just about the last place you would expect to find him.\n\n"
                "“I just...I don’t know what to do now,” the Sawbones confesses after you sit down. “I’d like to join the "
                "sanctuary and lead a nice, quiet life, but after adventuring with all of you, that just doesn’t seem "
                "fulfilling anymore.”\n\n"
                "He shakes his head. “I don’t know. This life seems so boring now. What do you think?”"),
            "edits": "Gloomhaven's bar \"the Brown Door\" -> the rough end of Frosthaven's Tavern.",
            "options": [
                {"text": "Tell him that joining the sanctuary is the right thing to do.",
                 "outcome": ("“I guess you’re right,” he shrugs. “Here’s to one last night of freedom.” He lifts his glass and "
                             "gulps down the contents. You follow his lead and the rest is a blur."),
                 "gh": "+1 prosperity", "fh": [("AddProsperity", 1)], "note": "The same.", "status": "proposed"},
                {"text": "Tell him to follow his dreams.",
                 "outcome": ("“You’re right! I’m in command of my own life, and I’m going to do what I want with it. Maybe I’ll "
                             "go to the Capital and start my own business or something. Who knows?”\n\nHe lifts his glass and "
                             "looks at you. “To endless possibilities! The next round’s on me.”"),
                 "gh": "adds city event 66 to the deck", "fh": [("EnableEvent", "GH.Sawbones-3")],
                 "note": "The follow-up event, as in Gloomhaven.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Sawbones-2", "deck": "SummerRoad", "start": "retire", "gh": "Road event 56",
            "front": (
                "At first the man looks dead, with numerous open wounds and lying in a pool of blood in the dirt.\n\n"
                "But then he coughs and whispers something unintelligible. The man looks pretty bad off, likely attacked by some "
                "wild animal, but it’s possible he could be saved."),
            "edits": "none",
            "options": [
                {"text": "Help the wounded man as best you can.",
                 "outcome": ("The man’s wounds actually look worse than they really are, and your time with the Sawbones has "
                             "given you some experience in what to do. You bind the wounds to stop the bleeding and then give him "
                             "something to eat. He lost a lot of blood, but after some nursing, he’s able to stand and make his "
                             "way back to Frosthaven, thanking you profusely in the process."),
                 "gh": "+1 reputation", "fh": [("GainMorale", 1)],
                 "note": "Reputation -> morale; \"Gloomhaven\" -> \"Frosthaven\".", "status": "proposed"},
                {"text": "Let the man die and take his belongings.",
                 "outcome": ("You riffle through the man’s pockets as he speaks his last words. They’re too soft to make out, but "
                             "the tone is one of anger and disappointment. You make off with a decent amount of money and leave "
                             "the corpse to rot."),
                 "gh": "+3 gold per character", "fh": [("AllCharactersGainGold", 3)], "note": "The same.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Sawbones-3", "deck": "SummerOutpost", "start": "chain", "gh": "City event 66",
            "front": (
                "You head back to your rooms for the night when you see the Sawbones sitting in front of your doorstep, looking "
                "rather dejected.\n\n"
                "“I, uh, well, you see...” he stammers, “I followed your advice. Chased my dreams and all that, but I ended up "
                "in debt to some rather unscrupulous people. They’ve threatened to kill me if I don’t pay them off, but, uh, but "
                "I simply don’t have the money.”\n\n"
                "He hesitates. “Seeing as how, uh, you bear some responsibility for setting me down this path, I was wondering "
                "if you’d be willing to offer some assistance in getting me out of this mess?”"),
            "edits": "none",
            "options": [
                {"text": "Offer to pay off the Sawbones’ debt.",
                 "outcome": ("It’s a hefty chunk of money, but the Sawbones is incredibly grateful.\n\n“This is wonderful! I knew I "
                             "could count on you. This should be enough to put this mess behind me and finally allow me to live "
                             "free.”\n\nHe hands you a potion and thanks you once again for all your help."),
                 "gh": "-40 gold (only if the party has it); a Super Healing Potion",
                 "fh": [("LoseGold", 30), ("GainItem", 99)],
                 "note": "Frosthaven's largest gold loss, and its Major Healing Potion (99, brewed, no shop price) for "
                         "Gloomhaven's. The game lets the gold go negative if the party is short; Gloomhaven refuses the option.",
                 "status": "proposed"},
                {"text": "Get the Sawbones on a boat across the Misty Sea. New adventures surely await there.",
                 "outcome": ("You flatly refuse to pay off the Sawbones’ debt and convince him the only way to escape his pursuers "
                             "is to board a ship bound for the eastern continent, far out of their reach. He is reluctant, but "
                             "eventually agrees when you remind him what unknown adventures await in the land across the ocean. "
                             "You see him off in the night and desperately hope that is the last you hear of this situation."),
                 "gh": "adds city event 67 to the deck", "fh": [("EnableEvent", "GH.Sawbones-4")],
                 "note": "The follow-up event, as in Gloomhaven.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Sawbones-4", "deck": "SummerOutpost", "start": "chain", "gh": "City event 67",
            "front": (
                "You are trying to enjoy a relaxing drink in the Tavern when a number of large men and Inox burst in and "
                "approach your table.\n\n"
                "“So, you must be that little creep’s collateral,” one of the Inox says. “We’re here to collect.”\n\n"
                "When you stare at her in silence, she tries again. “Little grey-bearded man. Wore symbols of the Great Oak. He "
                "said you lot would pay off his debt if he couldn’t.”\n\n"
                "She pats an axe at her side. “Now, seeing as how he apparently skipped town, you can pay us, or things are "
                "gonna get ugly.”"),
            "edits": "Gloomhaven's tavern \"the Sleeping Lion\" -> Frosthaven's Tavern; \"approaches\" -> \"approach\".",
            "options": [
                {"text": "Pay off the thugs.",
                 "outcome": ("You sigh and hand over the Sawbones’ debt, plus interest. Though it feels terrible, the thugs are "
                             "satisfied and walk out the same way they came in. At least it is over now."),
                 "gh": "-60 gold (only if the party has it)", "fh": [("LoseGold", 30)],
                 "note": "Halved to Frosthaven's largest gold loss. Gloomhaven refuses the option without the gold; here it goes "
                         "negative at worst.", "status": "proposed"},
                {"text": "Refuse to pay. You won’t be strongarmed by anyone.",
                 "outcome": ("You laugh and wave your hand dismissively. There’s no way you will be paying such a large amount of "
                             "money to these thugs. Unfortunately, the Inox is not amused.\n\n“Then your payment will be blood!” "
                             "she says. “Meet us in the back alley, and we’ll see if we can’t come to terms.”"),
                 "gh": "unlocks Gloomhaven's scenario 92 (the brawl)", "fh": [("UnlockRandomSideScenario",)],
                 "note": "A random side scenario stands in for the brawl.", "status": "proposed"},
            ],
        },
    ],
    "Quartermaster": [
        {
            "id": "GH.Quartermaster-1", "deck": "SummerOutpost", "start": "retire", "gh": "City event 49",
            "front": (
                "“Your Quartermaster friend sent me.” You look quizzically at the Valrath standing at your door.\n\n"
                "“I’m from the Capital, and I deal in arms and armor,” he says. “I’m looking to set up a contract with the "
                "Frosthaven guard, and our friend says that you may have some contacts with them. He suggested I meet up with "
                "you once I got here.”\n\n"
                "The Valrath shifts his feet nervously. “So, can you help me?”"),
            "edits": "\"the Gloomhaven military\" -> \"the Frosthaven guard\".",
            "options": [
                {"text": "Agree to help him make contact with the guard.",
                 "outcome": ("You take the Valrath over to the Barracks, where you make introductions to the captain of the guard. "
                             "The captain seems skeptical at first, but admits that they could use an additional supply of "
                             "weapons. Terms are made, and a contract is signed."),
                 "gh": "+1 prosperity", "fh": [("AddProsperity", 1)],
                 "note": "The same. \"the Ghost Fortress\" -> \"the Barracks\".", "status": "proposed"},
                {"text": "Demand a finder’s fee for setting him up with buyers.",
                 "outcome": "The Valrath looks at you with trepidation, but after some persuasion, you get him to agree.",
                 "gh": "+15 gold, but only with a Scoundrel, Sawbones or Soothsinger in the party; otherwise he storms off",
                 "fh": [("GainGold", 15)],
                 "note": "The party-composition condition is dropped (our events have one outcome per option): the fee is always "
                         "paid. 15 gold is a common Frosthaven event sum.", "status": "proposed"},
            ],
        },
        {
            "id": "GH.Quartermaster-2", "deck": "SummerRoad", "start": "retire", "gh": "Road event 49",
            "front": (
                "You are approached on the road by a group of men on horseback. They don’t immediately seem menacing, but they "
                "are formidable and well organized.\n\n"
                "One of the men, who wears plate armor and has a sword at his side, addresses you coldly. “You hail from "
                "Frosthaven, correct? What say you about the politics of the place? Are you for or against military rule in the "
                "region? Do you think those fool merchants could do a better job, like they’re doing with the rest of this "
                "doomed land?”\n\n"
                "Looking closer, you see all the men wear the signet of a tower with an eye at its center."),
            "edits": "\"Gloomhaven\" -> \"Frosthaven\".",
            "options": [
                {"text": "Claim allegiance to the military.",
                 "outcome": ("The armored men seem satisfied with your response and take their hands off their weapons.\n\n“Good "
                             "to find some fellow patriots,” the one in front says with a disturbing smile. “You know, we have a "
                             "stronghold not far from here up in the mountains. Feel free to visit there if you ever get serious "
                             "about your loyalties.”"),
                 "gh": "unlocks Gloomhaven's scenario 80 (the stronghold)", "fh": [("UnlockRandomSideScenario",)],
                 "note": "A random side scenario stands in for the stronghold.", "status": "proposed"},
                {"text": "Claim allegiance to the merchant guilds.",
                 "outcome": ("“Well then, today’s your unlucky day,” the man in front says. “Because we of the Vigil make it a "
                             "point to execute any and all commerce sympathizers we can find in this land.”\n\nThe men grimly draw "
                             "swords and advance. After a hard battle, the survivors retreat, leaving you to pick through the "
                             "corpses. Among the loot you find a map."),
                 "gh": "unlocks scenario 80; +5 gold per character; 4 damage each",
                 "fh": [("UnlockRandomSideScenario",), ("AllCharactersGainGold", 5), ("PartyDamage", 3)],
                 "note": "The map becomes a random side scenario; the damage drops to Frosthaven's largest (3).", "status": "proposed"},
            ],
        },
    ],
}

DECKS = {"SummerOutpost": ("Outpost", False), "WinterOutpost": ("Outpost", True),
         "SummerRoad": ("Road", False), "WinterRoad": ("Road", True)}


def events(cls):
    """The class's events: the hand-written entry above, else the proposal tools/gh_rewards.py generated from Gloomhaven's
    data (tools/gh_events_proposed.json, every option status "proposed" until the review)."""
    if cls in EVENTS:
        return EVENTS[cls]
    proposed = Path(__file__).with_name("gh_events_proposed.json")
    if proposed.exists():
        data = json.loads(proposed.read_text(encoding="utf-8")).get(cls, [])
        for e in data:
            for o in e["options"]:
                o["fh"] = [tuple(x) for x in o["fh"]]
        return data
    return []


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
            if cost is None and int(params[0]) not in BREWABLE:
                why.append(f"item {params[0]}: price unknown")
            elif cost is not None and cost > ITEM_COST_LIMIT:
                why.append(f"item {params[0]} costs {cost} gold (limit {ITEM_COST_LIMIT})")
        elif kind in LIMITS and params and isinstance(params[0], int) and params[0] > LIMITS[kind]:
            why.append(f"{kind} {params[0]} (limit {LIMITS[kind]})")
        elif kind not in LIMITS and kind not in PLAIN_OK:
            why.append(f"{kind}: no guideline, needs a look")
    return "; ".join(why) or None
