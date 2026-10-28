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
    # ---- Group 2 (drafted 2026-10-27 for the user's review; the reward is each class's own Gloomhaven events)
    "Cragheart": {
        "name": "Cragheart Retires",
        "effects": [],
        "text": (
            "The Cragheart has been sitting on the same boulder outside the north wall for three days. Nobody thought much "
            "of it; the Savvas sits still the way other people breathe. On the fourth morning the boulder is gone, and so is "
            "a long stretch of the rubble that the last attack left along the palisade. In its place stands a wall: raw, "
            "ugly, and a good deal thicker than the old one.\n"
            "\"It was in the way,\" the Cragheart says when you find them at the gate, as if that explains the week of work. "
            "They have never wasted a word on anything a stone could say for them.\n"
            "They are going back to the mountains, they tell you, slowly, the way they say everything. Not to the Savvas, "
            "who would not have them, but to the high country past the Frozen Pass, where the rock is older and quieter and "
            "nobody asks it to fight.\n"
            "They lay a hand flat against the new wall before they leave, as if listening. Whatever it says satisfies "
            "them. The Cragheart walks north, and the ground under the gate does not shake once."
        ),
    },
    "Plagueherald": {
        "name": "Plagueherald Retires",
        "effects": [],
        "text": (
            "The Alchemist finds you first, which tells you something has gone wrong before she says a word. \"Your Harrower "
            "friend,\" she says, \"has been in my stillroom all night. I would like them out of it. I would also like to know "
            "what they left in the third jar from the end.\"\n"
            "You find the Plagueherald on the roof of the Alchemist's, the swarm settled over them like a shawl, watching "
            "the sun come up over the bay. They do not turn around. \"The jar is a cure,\" they say. \"For the cough the "
            "lumber crews have. She will work it out in a month; I have saved her the month.\"\n"
            "They are leaving. The hive has decided, they explain, and a Harrower does not argue with the hive. Somewhere "
            "warmer, somewhere with more rot to feed on; they say it the way someone else would mention the weather.\n"
            "The swarm lifts off the roof all at once, a dark sheet that folds and is gone over the water. The cough is gone "
            "from the crews by the end of the week. The Alchemist still will not open the third jar."
        ),
    },
    "Nightshroud": {
        "name": "Nightshroud Retires",
        "effects": [],
        "text": (
            "You had agreed to meet the Nightshroud at the Barracks at dusk. You arrive early. He is already there, which "
            "you only learn when he speaks from the shadow of the doorway you have been standing in for a minute.\n"
            "\"I am going back,\" he says. Not to Gloomhaven; back, in the way the Aesther mean it, to whichever fold of "
            "the world they step out of and into. He has found the edge of it here, in the long dark of the Frosthaven "
            "winter. \"It is thin, this far north. I have been half through for weeks.\"\n"
            "He hands you his knife, hilt first, the one you have watched end a dozen things before they knew they were in "
            "danger. \"Keep it in the dark,\" he says, and for once you cannot tell whether it is advice or a joke.\n"
            "Then the lamp in the doorway gutters, and when it steadies the shadow is only a shadow. The guards on the wall "
            "swear nobody passed them. For the first night in months, nothing in the outpost moves that you did not see."
        ),
    },
    "Berserker": {
        "name": "Berserker Retires",
        "effects": [],
        "text": (
            "The Berserker is in the Tavern, which is not news, and she is sober, which is. There is a bandage on her arm "
            "and a letter on the table in front of her, read so many times the folds have gone soft.\n"
            "\"My sister,\" she says, pushing it toward you. \"The clan is moving the herds south before the snows. She asks "
            "if the one who ran off to get herself killed would like to come home instead.\" She takes the letter back "
            "before you can finish it. \"She has a way with words.\"\n"
            "You ask about the arm. She shrugs: it will heal, they always do, that was never the problem. The problem was "
            "that for a long time she did not mind whether they did. \"Not any more,\" she says, and looks surprised to hear "
            "it out loud.\n"
            "She leaves the next morning with the trade caravan, walking beside the wagons rather than riding, because "
            "sitting still was never going to happen. At the bend in the road she turns and raises the bandaged arm, once, "
            "and then she is gone into the white."
        ),
    },
    # ---- Group 3, Jaws of the Lion (drafted 2026-10-27 for the user's review). These four have no Gloomhaven events
    # of their own (gh_events.retirement_effects is None), so each story's lasting mark is a campaign effect Frosthaven's
    # own sections use (2026-10-28 defaults, pending the user's verdict; docs/reports/2026-10-27-status.md section 4).
    "Hatchet": {
        "name": "Hatchet Retires",
        "effects": [("AddTownGuardMod", "P20")],
        "text": (
            "The Hatchet has a new coat. It is dark green, cut in the Capital style, and entirely wrong for Frosthaven, "
            "which he knows perfectly well. He wears it to the gate anyway, the Favorite hanging at his hip where the "
            "fashion says a sword should go.\n"
            "\"A troupe is going south with the caravan,\" he says. \"Knife throwers, fire eaters, that sort of thing. "
            "They lost their axe man in a tavern in the Capital. I told them I could be worse.\" He grins, which the "
            "coat was clearly bought for. \"Nobody in a crowd ever throws back. I could get used to that.\"\n"
            "He looks at the wall for a moment, the way everyone does before they go, then pulls the Favorite and "
            "throws it. It buries itself in the gatepost at head height, a hair from the guard's ear. The guard does "
            "not flinch; the Hatchet taught him not to, over a long winter of exactly this.\n"
            "He walks over, works the axe free, kisses the blade, and goes. The notch stays in the post. New recruits "
            "are told to stand with their heads against it, and never told why."
        ),
    },
    "RedGuard": {
        "name": "Red Guard Retires",
        "effects": [("AddSoldiers", 1)],
        "text": (
            "The Red Guard does not leave. That is the first thing she tells you, before you can ask. She has walked "
            "the wall every night since the first attack, in the red armor that marks her as an exile from a city "
            "that no longer wants her, and somewhere in all those nights the wall became hers.\n"
            "\"In Jhinda the guard is a caste,\" she says. \"You are born to it or you are not. Here you are a guard "
            "because you stand on the wall when it matters. I find I prefer that.\" The captain of the watch has "
            "offered her a post, a bunk in the Barracks and the north tower. She has already moved her things.\n"
            "She hands you the sickle and chain, the weapon she pulled a hundred enemies off their feet with. \"The "
            "guard issues a spear. I would hate to be the one who makes an exception.\"\n"
            "That night the torch in the north tower stays lit until dawn, and it does every night after. The "
            "lumber crews stop asking who is up there. They just stop looking over their shoulders."
        ),
    },
    "Voidwarden": {
        "name": "Voidwarden Retires",
        "effects": [("AddProsperity", 1)],
        "text": (
            "The Voidwarden has been filling a jar with black sand. A pinch at a time, after every scenario, from "
            "wherever the Void touched the world through her and left its coarse, dark residue behind. The jar is "
            "nearly full. \"That is the measure of it,\" she says, turning it in the light. \"Everything I ever made "
            "disappear. It does not look like much.\"\n"
            "She is going to the Capital, to the archives, to write down what she knows. \"No one who touches the Void "
            "lives long enough to teach the next one. I intend to be the exception.\" She says it without drama; the "
            "scars down her left side say the rest.\n"
            "She leaves the jar with the Alchemist, with a label in her small, exact hand: DO NOT OPEN. The "
            "Alchemist, who has opened everything else anyone ever left her, puts it on the highest shelf and does "
            "not touch it.\n"
            "The morning the caravan goes, the sick in the Infirmary are quieter than usual, and the healers find "
            "every one of them a little better than the day before. Nobody saw her go in."
        ),
    },
    "Demolitionist": {
        "name": "Demolitionist Retires",
        "effects": [("GainMetal", 3)],
        "text": (
            "The explosion wakes the whole outpost an hour before dawn. By the time you reach the harbor the "
            "Demolitionist is standing on the ice, or what is left of it, surrounded by delighted fishermen. The "
            "channel that has been frozen shut since midwinter is open water.\n"
            "\"You said the boats could not get out,\" she says, as if that settled it. The rocket boots are "
            "smoking gently. The mech suit stands on the quay behind her like a patient dog.\n"
            "The foreman from the mine has been watching from the harbor wall. He has a seam that will not open and "
            "a crew that is tired of picks, and he offers her the job before the ice has stopped moving. She "
            "considers it for as long as it takes to reload. \"Things that need breaking,\" she says. \"I can do "
            "that.\"\n"
            "She takes the suit up the mountain road the next day, the fists folded, the whole contraption leaving "
            "prints like a bear in the snow. Twice a week, for years afterwards, a dull thump rolls down from the "
            "mine, and the whole of Frosthaven looks up and smiles."
        ),
    },
    # ---- Group 4 (drafted 2026-10-27 for the user's review, ahead of the ports; the reward is each class's own
    # Gloomhaven events, which gh_events supplies when the class is ported)
    "Spellweaver": {
        "name": "Spellweaver Retires",
        "effects": [],
        "text": (
            "The Spellweaver has been counting. You noticed it weeks ago: a small tally on the inside of her sleeve, "
            "a mark for every time Reviving Ether brought her cards back from the fire. \"Thirty-one,\" she says, when "
            "you finally ask. \"The sages say the ether forgives you nine times. After that, you are borrowing.\"\n"
            "She is going home to the Orchid groves to pay it back, which as far as you can tell means sitting very "
            "still for a very long time. She does not seem to mind. \"I have been loud enough,\" she says, and for a "
            "woman who once set a frozen lake on fire from the far shore, it is true.\n"
            "Before she goes she walks the Alchemist's garden with the keeper, touching each bed in turn. Nothing "
            "visible happens. The following spring the garden comes up a week early and nobody can say why.\n"
            "She leaves on foot, south, with a single staff and no coat, because the cold has never once been "
            "allowed to touch her and she sees no reason to start now."
        ),
    },
    "Mindthief": {
        "name": "Mindthief Retires",
        "effects": [],
        "text": (
            "The rats go first. For three days the outpost's granary is quieter than anyone can remember, and the "
            "Craftsman, who has fought the vermin for years, walks around looking cheated. Then the Mindthief comes to "
            "find you, a swarm's worth of small warm bodies gone from the folds of her cloak, and tells you she has "
            "sent them ahead.\n"
            "\"There is a nest in the Copperneck,\" she says, \"a big one. They have been talking about it for a "
            "month.\" You ask who. She looks at you the way she looks at anyone who asks a question she has already "
            "answered in their head. \"The rats.\"\n"
            "She leaves you her blade, the thin wicked one, and with it a thought she presses into you so firmly that "
            "you still feel it years later whenever you draw it: nothing in front of you is as dangerous as what is "
            "behind it. You have never been sure whether it was advice or a joke at your expense.\n"
            "By the time the Craftsman has finished celebrating, every rat in Frosthaven is back, and the granary is "
            "exactly as it was. The Mindthief is not."
        ),
    },
    "Elementalist": {
        "name": "Elementalist Retires",
        "effects": [],
        "text": (
            "The Elementalist calls it balancing the ledger. Six elements, drawn and spent and drawn again over a "
            "winter of fighting, and the Savvas keep count of such things the way merchants keep count of coin. \"I "
            "have taken more fire from this valley than I gave it,\" they say, standing at the frozen edge of the bay. "
            "\"That is not a thing to leave behind.\"\n"
            "So they give it back. For a week the outpost is warmer than it has any right to be in that season: the "
            "ice in the harbour rots, the lumber yard's frozen stack comes loose with a sound like a sigh, and the "
            "Infirmary's patients sleep without blankets for the first time since autumn.\n"
            "Then the Elementalist takes the cold instead, folds it into themself with the same unhurried care, and "
            "walks north into the white with it, toward wherever a Savvas goes to be respected. They do not say "
            "goodbye. They have never seen the point.\n"
            "The thaw they left lasts exactly as long as they said it would, to the day. Frosthaven learns to plan "
            "around it, and nobody ever quite believes it was weather."
        ),
    },
    "Soothsinger": {
        "name": "Soothsinger Retires",
        "effects": [],
        "text": (
            "The last song is in the Tavern, which is where all of them were, and the whole outpost is there to hear "
            "it. The Soothsinger plays it twice: once the way she played it on the ice, fast and hard, the one that "
            "put spine into the guard when the Algox came over the wall, and once slowly, so that everyone can learn "
            "the words.\n"
            "They do. By the end of the week the lumber crews are singing it on the road and getting it wrong, which "
            "she says is the point. \"A song you have to be told is a song you forget,\" she tells you, packing the "
            "lute with the care other people give to children. \"A song you get wrong is yours.\"\n"
            "She is going south with the merchants, to the Capital, where there are halls with proper ceilings and "
            "audiences who have never heard what a Quatryl can do with eight strings and a grudge. She expects to be "
            "rich by midsummer. Knowing her, she will be.\n"
            "The Tavern keeps the song. It is the one they play when the walls hold, and it is still, years on, "
            "slightly wrong."
        ),
    },
    # ---- Groups 5 and 6 (drafted 2026-10-27 for the user's review, ahead of the ports)
    "Tinkerer": {
        "name": "Tinkerer Retires",
        "effects": [],
        "text": (
            "The Tinkerer's workshop has taken over half the Craftsman's floor by the time he decides to leave it. "
            "Nobody argues; the Craftsman has been using his drill press for a month and has no intention of giving it "
            "back. \"Keep it,\" the Tinkerer says. \"Keep all of it. I am tired of carrying things.\"\n"
            "He is going east, to look for the places the old maps mark and the new ones do not, with a bag of elixirs "
            "and one battle bot that follows him like a dog and has never once worked properly. He says the not-working "
            "is a feature. He has always said that.\n"
            "The last thing he builds in Frosthaven is a lamp for the Infirmary that lights itself when someone comes "
            "in after dark, so the healers stop knocking over the tray of knives. It works the first time. He looks "
            "faintly disappointed.\n"
            "A letter comes the next summer, smudged and full of diagrams. The Craftsman reads it twice and starts "
            "building something. He will not say what."
        ),
    },
    "Doomstalker": {
        "name": "Doomstalker Retires",
        "effects": [],
        "text": (
            "The Doomstalker has not spoken more than a sentence a week since he arrived, so when he asks you to walk "
            "the north ridge with him at dawn, you go. He says nothing for an hour. Then he points, and you see the "
            "tracks you have walked past a hundred times: something large, keeping its distance, watching the "
            "outpost as patiently as he has.\n"
            "\"I have marked it,\" he says. You do not ask what that means. You have seen what happens to things he "
            "marks.\n"
            "He leaves the bow with the guard captain, strung, with a single arrow laid beside it, and the captain "
            "understands the arrangement without being told: the day the thing on the ridge comes closer, there is "
            "one shot, and it will not miss. It never comes closer.\n"
            "He goes north himself, after it or past it, with his animals ranging ahead of him in a loose silent "
            "fan. For years after, the hunters swear the valley's wolves give the outpost a wider berth than any "
            "other place on the pass, and nobody can tell them why."
        ),
    },
    "BeastTyrant": {
        "name": "Beast Tyrant Retires",
        "effects": [],
        "text": (
            "You find the Beast Tyrant where you always find him: at the bear. It is sitting on its haunches at the "
            "edge of the lumber yard, as it has every evening of the campaign, pretending not to watch the crews. The "
            "crews pretend not to be watched. It is a comfortable arrangement and it is about to end.\n"
            "\"The wild is going back to being wild,\" the Vermling says. \"Up past the pass, where the Algox do not "
            "go. The bear wants it. I have never once told the bear no.\" He scratches the enormous head, which "
            "leans into his hand with the whole of its weight. \"I am not starting now.\"\n"
            "He leaves you his collection of totems, the wind one and the spirit banner, and a short list of which "
            "animals in the valley are to be left alone, in an order that suggests the animals wrote it.\n"
            "They go at dusk, the man and the bear and a loose tide of smaller things in the undergrowth around "
            "them. The lumber yard is quiet for a week. Then the crews start leaving a cut of the day's catch at "
            "the edge of the trees, and nobody admits to it."
        ),
    },
    "Summoner": {
        "name": "Summoner Retires",
        "effects": [],
        "text": (
            "The Summoner has been bringing things through from the other side for as long as you have known her, "
            "and sending them back when they were done, and you had assumed the arithmetic came out even. It does "
            "not, she tells you, on the last night, sitting at the Alchemist's bench with the black unicorn asleep "
            "across the doorway. \"Every crossing costs the crosser. The planes keep the change.\"\n"
            "She is thinner than she was. The Aesther are hard to read, but even you can see that.\n"
            "\"I am going to stop,\" she says, \"somewhere the boundary is thick and nothing can hear me call.\" She "
            "has a place in mind, inland, where the ground is old and does not remember magic. She will not say where.\n"
            "The unicorn goes back first, in the yard at dawn, unmade in a scatter of light that the children of the "
            "outpost talk about for the rest of their lives. Then the lava golem, then the sprite. Then the Summoner "
            "walks out of the gate on her own two feet, alone for the first time in years, and does not look as if "
            "she minds."
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
