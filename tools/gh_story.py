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

A section ID is any string (Frosthaven has "CampaignWon" beside its "48.3"s); "GH.<Class>" keeps ours apart from
every number the game will ever use.
"""

STORIES = {
    "Brute": {
        "name": "Brute Retires",
        # his shield stays with the town guard: the Banner Spear's own reward, a +20 card in the town guard deck
        "effects": [("AddTownGuardMod", "P20")],
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
    if story["effects"]:
        lines.append("Effects:")
        for kind, *params in story["effects"]:
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
