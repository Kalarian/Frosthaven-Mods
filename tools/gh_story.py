"""Retirement stories for the ported classes: the campaign section Frosthaven plays the first time a class retires.

Frosthaven gives each of its classes a "<Class> Retires" section (Outpost/Sections/CampaignSection_48.3.yml is the
Drifter's). The class file names it (RetireCampaignSection), CharacterController.FinishRetire activates it at the FIRST
retirement of that class in a campaign (later retirements of the same class show the story again but the section does
not fire twice), the retirement screen shows its text (key "Event_" + section ID + "_Text") above the rewards, and the
retire dialog previews its effects. Each of Frosthaven's leaves one lasting mark on the campaign: the Banner Spear
enlists in the town guard (AddTownGuardMod P20, a +20 card in the town guard deck), the Drifter comes back four weeks
later with a blueprint (AddCalendarCampaignSection), the Blinkblade and the Pain Conduit enable a new outpost event.

Gloomhaven has no such text (its retirements just end the character), so these are written here, in the voice of
Frosthaven's own: second person, present tense, a concrete spot in the outpost, the departure at the end. Used by
gh_port.py: section_id(cls), files(cls) -> [(path, text)], strings(cls) -> {key: English}. The pack manifest lists the
section IDs so the plugin's "Copy without content packs" can take them out of a save (GHSaveStrip).

The reward is the class's own Gloomhaven events (gh_events.py: EnableEvent per event), the way Frosthaven
rewards its Blinkblade, Deepwraith, HIVE, Pain Conduit and Crashing Tide.

A section ID is any string (Frosthaven has "CampaignWon" beside its "48.3"s); "GH.<Class>" keeps ours apart from
every number the game will ever use.
"""

import gh_events  # noqa: E402

STORIES = {
    "Brute": {
        "name": "Brute Retires",
        # the reward: his own Gloomhaven city and road events (gh_events.py), not a Frosthaven class's reward
        "effects": [],
        "text": (
            "You find the Brute at the main gate at dawn, pack strapped on and shield across his back. He has been quieter "
            "than usual these past weeks, which for the Brute is saying something.\n"
            "\"Job's done,\" he says when you ask. \"Came north for the pay. Stayed for...\" He gestures at the wall, the "
            "gate, the people hauling lumber behind you, and leaves it at that.\n"
            "He unslings the shield and props it against the gatehouse. \"Guard can use it more than I can.\" A long pause. "
            "\"Nothing wrong with a wall that holds.\"\n"
            "You ask where he will go. He shrugs toward the south: the Frozen Pass, and past it a long walk home to the Great "
            "Grove, or to Gloomhaven, or wherever the next quiet place is. You point out he has never been good at quiet. For "
            "the first time you can remember, the Brute laughs.\n"
            "He walks out through the gate without looking back. Behind you, the guards take turns trying to lift the shield."
        ),
    },
    "Scoundrel": {
        "name": "Scoundrel Retires",
        "effects": [],
        "text": (
            "Nobody sees the Scoundrel leave. You find out the way you were always going to find out: her bunk is stripped, "
            "the lock on the Trading Post's back door has been picked and relocked out of sheer habit, and on your table "
            "sits a small, neat pile of everything she ever lifted from you. A coin purse. A whetstone. The good knife.\n"
            "There is a note under the knife, in handwriting far better than she ever let on. \"Nothing personal. It never "
            "was. Thanks for the company; it was more than I came for.\"\n"
            "The guard at the gate swears he saw nothing all night. He is probably telling the truth. She was always better "
            "than the people watching for her.\n"
            "Weeks later a caravan master mentions a woman in dark leather who paid her way south and spent the whole trip "
            "teaching the drivers to cheat at cards. You find you are smiling. Somewhere out there, the Scoundrel is doing "
            "fine."
        ),
    },
    "Sunkeeper": {
        "name": "Sunkeeper Retires",
        "effects": [],
        "text": (
            "The Sunkeeper asks you to meet her on the wall at sunrise. She is already there when you arrive, armor polished "
            "to a mirror, watching the light come up over the ice. \"The temple has sent for me,\" she says. \"They need a "
            "keeper, and there is no one else.\"\n"
            "She does not pretend to be sorry to go, and you respect her for it. Instead she walks the length of the wall with "
            "you, laying a gloved hand on each tower as she passes, speaking a few words under her breath. The guards on duty "
            "stand a little straighter without quite knowing why.\n"
            "At the gate she presses a small sun medallion into your palm, warm from her hand. \"For the dark months. It "
            "will not do much. It will do enough.\"\n"
            "Then she turns south and walks into the sunrise, which is exactly how she would have wanted it to look."
        ),
    },
    "Sawbones": {
        "name": "Sawbones Retires",
        "effects": [],
        "text": (
            "You find the Sawbones in the Alchemist's back room, labeling jars. He has been at it for days: every salve, "
            "splint and tincture he carried, written up in his cramped hand and shelved where anyone can find it.\n"
            "\"Enough people know how to break things in this town,\" he says without looking up. \"Somebody ought to know "
            "how to put them back together when I am gone.\" He closes the last jar, wipes his hands, and finally turns "
            "around. \"The sanctuary wrote. A quiet post, inland. Warm. I said yes before I could talk myself out of it.\"\n"
            "He leaves you his saw. Not as a weapon, he insists, though you both know how he used it. \"For the lumber. "
            "Honestly.\"\n"
            "The morning he goes, half the outpost turns out to see him off, every one of them someone he has stitched, "
            "set or sobered up. He pretends to be annoyed about it the whole way to the gate."
        ),
    },
    "Quartermaster": {
        "name": "Quartermaster Retires",
        "effects": [],
        "text": (
            "The Quartermaster spends his last week in Frosthaven doing inventory. Not his own: the outpost's. By the end of "
            "it every storeroom has a ledger, every ledger has a system, and the Craftsman has stopped arguing and started "
            "taking notes.\n"
            "\"A party without supplies is a funeral with extra steps,\" he tells you over the last of his good wine. \"You "
            "lot will be fine. You have the ledgers now.\" He taps the stack like a man patting a dog.\n"
            "He has a contract waiting in the Capital, outfitting a merchant house, and he is honest enough to admit the pay "
            "is obscene. He is also honest enough to look back at the walls for a long moment before he goes.\n"
            "His pack is the last thing to leave, strapped to a sled and roped like a siege engine. The Craftsman swears the "
            "storerooms have never been so tidy, and never will be again."
        ),
    },
}


def section_id(cls):
    """The class's retirement section ID, or None when it has no story yet (the plugin then shows a generic line)."""
    return f"GH.{cls}" if cls in STORIES else None


def files(cls):
    """The section file, in the form of Frosthaven's own retirement sections."""
    story = STORIES.get(cls)
    if not story:
        return []
    sid = section_id(cls)
    lines = ["Parser: CampaignSection", f"ID: {sid}", f"Name: {story['name']}", "SkipNarration: True",
             "SkipCompletedNotification: False"]
    effects = gh_events.retirement_effects(cls) or story["effects"]   # the class's own events, when it has them
    if effects:
        lines.append("Effects:")
        for kind, *params in effects:
            lines.append(f"    - Type: {kind}")
            lines += [f"      Param{i}: {p}" for i, p in enumerate(params, start=1)]
    return [(f"Outpost/Sections/CampaignSection_{sid}.yml", "\n".join(lines) + "\n")]


def strings(cls):
    """The story and the section's name, under the keys the game derives from the section ID."""
    story = STORIES.get(cls)
    if not story:
        return {}
    sid = section_id(cls)
    return {f"Event_{sid}_Text": story["text"], f"Event_{sid}_Name": story["name"]}
