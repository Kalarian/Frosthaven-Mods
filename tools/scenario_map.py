"""Frosthaven scenario map: every scenario, what unlocks it, what it unlocks and locks out, as one page.

The unlock data comes from the game's own rules file (Base.ruleset: the scenario and section definitions), plus a
table below of the unlocks the game makes in code (events, personal quests, town guard perks, puzzles, buildings,
treasure chests). With the "Scenario map file" house rule on, the mod writes the campaign's progress to
<game>/FrosthavenMods/scenarios/progress-<party>.txt after each autosave; the page is then coloured by it and shows,
for each choice still open (lockouts, scenario endings, section choices), how many scenarios each option keeps
reachable, and the best combination ("max path").

    python tools/scenario_map.py                      every progress file -> scenario-map-<party>.html next to it
    python tools/scenario_map.py progress-Foo.txt     one campaign
    python tools/scenario_map.py --blank              the map with no campaign (written to the scenarios folder)
    options: --data <Base.ruleset | unpacked folder>  -o <out.html>

Spoilers: the page shows every scenario's name and how to reach it.
"""

import argparse
import html
import itertools
import json
import math
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fh_ruleset  # noqa: E402

GAME = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Frosthaven")
RULESET = GAME / "Frosthaven_Data" / "StreamingAssets" / "Rulebase" / "Base.ruleset"
PROGRESS_DIR = GAME / "FrosthavenMods" / "scenarios"

CHAINS = {  # the game's ScenarioChainType, in display order
    "Introduction": "Introduction", "Algox1": "Algox: the Coral Court", "Algox2": "Algox: the Spire",
    "Algox3": "Algox: the Icespeakers", "Unfettered": "Unfettered", "Lurker": "Lurkers",
    "Other": "Other", "PersonalQuest": "Personal quests", "JobPosting": "Job postings",
    "PuzzleBook": "Puzzle book", "RandomScenario": "Random side scenarios",
}

# ---- unlocks the game makes in code (decompiled FHRL / ScenarioRuleLibrary), not in the rules file

# (source node, its label, target node, how). S<n> = scenario, X<id> = section.
CODE_LINKS = [
    ("Q:PQ_2", "Personal quest 2", "S69", "when its first step is done"),
    ("Q:PQ_7", "Personal quest 7", "S65", "when its first step is done"),
    ("Q:PQ_12", "Personal quest 12", "S71", "when its first step is done"),
    ("E:WR-13", "Winter road event WR-13", "S113", "one of its outcomes"),
    ("E:WR-49", "Winter road event WR-49", "S105", "one of its outcomes"),
    ("E:WO-18", "Winter outpost event WO-18", "S120", "one of its outcomes"),
    ("E:SR-13", "Summer road event SR-13", "S122", "one of its outcomes"),
    ("E:SR-41", "Summer road event SR-41", "S131", "one of its outcomes"),
    ("E:SR-52", "Summer road event SR-52", "S124", "one of its outcomes"),
    ("E:SR-52", "Summer road event SR-52", "X100.3", "another outcome"),
    ("E:WO-80", "Winter outpost event WO-80", "X163.3", "one of its outcomes"),
    ("E:SO-14", "Summer outpost event SO-14", "R:random", "one of its outcomes: a random side scenario"),
    ("C:coin", "Ancient coins", "X35.3", "the first ancient coin"),
    ("C:coin", "Ancient coins", "X113.1", "the third ancient coin"),
    ("C:brummix", "Brummix stickers", "X40.1", "the fifth Brummix sticker"),
    ("X113.1", None, "S137", "its event"),
    ("T:chest12", "Treasure chest 12 (Remains of Camilla)", "X99.1", "3 weeks after it is looted"),
    ("T:chest57", "Treasure chest 57", "S103", "looting it"),
    ("T:chests", "Treasure chests that unlock a random side scenario", "R:random", "looting one"),
    ("S115", None, "X66.2", "on the calendar, depending on the result"),
]
TOWN_GUARD_PERKS = {  # perk -> the job postings it adds (sections); job postings unlock their scenario
    1: ["19.2", "178.3"], 2: ["17.2", "166.4"], 3: ["151.3", "194.3"], 4: ["91.4", "157.3"], 5: ["130.3", "104.1"],
    6: ["167.2", "175.1"], 7: ["180.3"], 8: ["41.1"], 9: ["192.1"],
}
PUZZLE_REWARDS = {  # puzzle -> the section read when it is solved
    "AlchemyChart": ["11.8"], "AlgoxFriends": ["30.1"], "Alphabet": ["30.6"], "BeamsOfEnergy": ["43.5"],
    "EnergySource": ["46.2"], "FixingTheFracture": ["39.4"], "HiddenNumbers_PartTwo": ["59.3"],
    "HiddenNumbers": ["87.4"], "LockMechanism": ["124.1", "127.1"], "MercenariesMathematics": ["77.1"],
    "MetalMosaic": ["35.1"], "MuttonSandwich": ["14.1"], "Numbers": ["29.5"], "Peninsulas": ["52.3"],
    "Pets": ["68.5"], "Poetry": ["36.8"], "Prologue": ["190.4"], "SynthesisDevice": ["79.4"],
    "TheFinalRiddle": ["64.3"],
}
BUILDING_SECTIONS = {  # building upgrades that read a section
    "Alchemist": ["183.5"], "Boat": ["139.2"], "Carpenter": ["7.3"], "Climbing gear": ["161.1"],
    "Enhancer": ["193.1", "157.2"], "Garden": ["78.4"], "Hall of Revelry": ["178.2", "187.1"],
    "Hide depot": ["173.1"], "Hunting lodge": ["172.3"], "Inn": ["151.2"], "Jeweler": ["31.3"],
    "Library": ["190.3", "116.3", "89.2"], "Lumber depot": ["144.3"], "Metal depot": ["193.4"],
    "Mining camp": ["49.2"], "Sled": ["169.2"], "Stables": ["3.1"], "Tavern": ["160.2"],
    "Temple of the Great Oak": ["192.5"], "Town hall": ["189.1"],
    "Trading post": ["25.2", "12.4", "38.3", "196.2"],
}
LOOT_CARD_SECTIONS = {  # special loot cards that read a section each time they are drawn
    1418: ["43.4", "133.4", "177.3", "165.3", "168.1", "130.4", "174.5", "106.3", "13.2", "16.2"],
    1419: ["36.4", "29.1", "188.2", "182.3", "85.1", "11.1", "128.5", "153.4", "193.5", "9.3"],
}
ENDING_NOTES = {  # how the game picks the ending (ScenarioScript_FH<n>.GetConclusionIndexInternal)
    4: "Decided by which of the two bosses you defeat.",
    74: "Decided by #73: the second ending if the party isn't a Friend of the Fish King (a sticker from one of #73's endings).",
    76: "You pick it at the end of the scenario.",
    121: "Decided by what you choose to do with the automaton and its helmet during the scenario.",
}
OBSOLETE_SECTIONS = {"6.2"}  # "obsolete, merged with 113.1 into a custom event"
UNTRACED_NOTE = ("Nothing in the game's data or code unlocks this one (as of this game version), so it may come "
                 "from content the digital game hasn't added yet.")

SCENARIO_UNLOCKS = {"UnlockScenario", "UnlockMandatoryScenario", "UnlockLinkedScenario"}


# ---- reading the rules file

def parse_yml(text):
    """The subset of YAML the scenario and section files use: top-level scalars, Effects / ConditionalEffects lists,
    and Conclusions (a list of Effects lists, each optionally preceded by a comment naming the ending)."""
    head, conclusions, lists = {}, [], {"Effects": [], "ConditionalEffects": [], "StartingCampaignEffects": []}
    target, effect, comment, top = None, None, None, None
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("#"):
            comment = line.lstrip("# ").strip()
            continue
        m = re.match(r"^(\w+):\s*(.*?)\s*$", raw)
        if m and not raw[0].isspace():
            top = m.group(1)
            if top in lists:
                target = lists[top]
            elif top == "Conclusions":
                target = None
            else:
                head.setdefault(top, m.group(2).strip('"'))
            comment = None
            continue
        if top == "Conclusions" and re.match(r"^-\s*Effects:", line):
            conclusions.append({"label": comment, "effects": []})
            target, comment = conclusions[-1]["effects"], None
            continue
        m = re.match(r"^-\s*Type:\s*(\S+)", line)
        if m and target is not None:
            effect = {"Type": m.group(1)}
            target.append(effect)
            continue
        m = re.match(r"^(Param\d+):\s*(.*?)\s*(#.*)?$", line)
        if m and effect is not None:
            effect[m.group(1)] = m.group(2).strip('"').strip()
    return head, conclusions, lists


def load_defs(source):
    """The definitions the map needs, from a Base.ruleset file or an unpacked ruleset folder: scenarios, sections, the
    campaign's starting effects (its first calendar entries) and each class's retirement section."""
    files = []  # (parser, text)
    if source.is_dir():
        for sub, kind in (("Outpost/Scenarios", "FHScenario"), ("Outpost/Sections", "CampaignSection"),
                          ("Outpost/GameMode", "GameMode"), ("Global/Characters", "Character")):
            files += [(kind, p.read_text("utf-8-sig", "replace")) for p in sorted((source / sub).rglob("*.yml"))]
    else:
        for e in fh_ruleset.read_ruleset(source.read_bytes()):
            kind = fh_ruleset.PARSER_TYPES.get(e["parser"])
            if kind in ("FHScenario", "CampaignSection", "GameMode", "Character") and not e["binary"]:
                files.append((kind, e["data"].decode("utf-8-sig", "replace")))
    defs = {"scen": {}, "sect": {}, "start": [], "retire": {}}
    for kind, text in files:
        head, conclusions, lists = parse_yml(text)
        if head.get("Parser") != kind or ("ID" not in head and kind != "GameMode"):
            continue
        if kind == "FHScenario":
            sid = int(head["ID"])
            if not 1 <= sid < 200:  # 0 is an unused draft, 200+ the tutorials and Road to Frosthaven, 666 a benchmark
                continue
            name = head.get("Name") or re.sub(r"^FS_\d+_", "", head.get("LevelID", "?")).replace("_", " ")
            defs["scen"][sid] = {"id": sid, "name": name, "chain": head.get("Chain", "Other"),
                                 "location": humanize(head.get("Location", "")), "difficulty": head.get("Difficulty", ""),
                                 "head": head, "endings": conclusions}
        elif kind == "CampaignSection":
            defs["sect"][head["ID"]] = {"id": head["ID"], "name": head.get("Name", ""), "effects": lists["Effects"],
                                        "cond": lists["ConditionalEffects"]}
        elif kind == "GameMode" and head.get("Name") == "Frosthaven":
            defs["start"] = lists["StartingCampaignEffects"]
        elif kind == "Character" and head.get("RetireCampaignSection"):
            defs["retire"][humanize(head["ID"].removesuffix("ID"))] = head["RetireCampaignSection"]
    return defs


def humanize(camel):
    fixes = {"CopperneckMountaions": "Copperneck Mountains"}
    return fixes.get(camel, re.sub(r"(?<=[a-z])(?=[A-Z])", " ", camel))


# ---- the unlock graph

class Graph:
    """Nodes: S<n> scenario, X<id> section, P:<id> puzzle, E:<id> event, Q/C/T/B/G:<id> other sources, R:random (any
    random side scenario), K:<travel> (sled/boat/climbing scenarios allowed).
    Edges: dicts {src, dst, how, cond: [nodes that must also be reached], choice: (choice id, option) or None}."""

    def __init__(self, defs):
        self.defs = defs
        self.scen, self.sect = defs["scen"], defs["sect"]
        self.label = {}
        self.edges, self.locks = [], []  # locks: (scenario, ending or None, locked scenario)
        self.choices = {}  # id -> {"kind", "title", "options": [labels], "at": node}
        self.requires = defaultdict(list)  # node -> nodes that must be reached first (AND)
        self.enabled_events = set()
        for sid, s in self.scen.items():
            self.label[f"S{sid}"] = f"#{sid} {s['name']}"
        for xid, x in self.sect.items():
            self.label[f"X{xid}"] = f"section {xid}" + (f" ({x['name']})" if x["name"] else "")
        self.build()

    def add(self, src, dst, how, cond=(), choice=None):
        if dst.startswith("X") and dst[1:] in OBSOLETE_SECTIONS:
            return
        self.edges.append({"src": src, "dst": dst, "how": how, "cond": list(cond), "choice": choice})

    def effects(self, node, effects, choice=None):
        for i, e in enumerate(effects):
            t, p = e["Type"], [e.get(f"Param{k}") for k in range(1, 5)]
            S = lambda v: f"S{int(v)}"  # noqa: E731
            if t in SCENARIO_UNLOCKS:
                self.add(node, S(p[0]), "unlocks", choice=choice)
            elif t == "UnlockScenarioIfScenarioComplete":
                self.add(node, S(p[0]), f"unlocks, if #{p[1]} is complete", cond=[S(p[1])], choice=choice)
            elif t == "LockScenario":
                self.locks.append((node, choice, S(p[0])))
            elif t == "ChooseUnlockMandatoryScenario":
                cid = f"pick:{node}:{i}"
                self.choices[cid] = {"kind": "pick", "at": node, "title": f"{self.label.get(node, node)}: pick the next scenario",
                                     "options": [f"#{p[1]}", f"#{p[2]}"]}
                self.add(node, S(p[1]), "you pick this one", choice=(cid, 0))
                self.add(node, S(p[2]), "you pick this one", choice=(cid, 1))
            elif t == "AddCalendarCampaignSection":
                self.add(node, f"X{p[0]}", f"on the calendar, {p[1]} week{'s' if p[1] != '1' else ''} later", choice=choice)
            elif t == "AddCalendarCampaignSectionNextSummer":
                self.add(node, f"X{p[0]}", f"on the calendar, week {p[1]} of next summer", choice=choice)
            elif t == "ActivateCampaignSection":
                self.add(node, f"X{p[0]}", "read right away", choice=choice)
            elif t == "ChooseNextCampaignSection":
                cid = f"pick:{node}:{i}"
                self.choices[cid] = {"kind": "pick", "at": node, "title": f"{self.label.get(node, node)}: a choice",
                                     "options": [f"section {p[1]}", f"section {p[2]}"]}
                self.add(node, f"X{p[1]}", "if you choose it", choice=(cid, 0))
                self.add(node, f"X{p[2]}", "if you choose it", choice=(cid, 1))
            elif t == "IfScenarioActivateCampaignSection":
                self.add(node, f"X{p[1]}", f"if #{p[0]} is complete", cond=[S(p[0])], choice=choice)
            elif t in ("IfRaceActivateCampaignSections", "IfTraitActivateCampaignSections"):
                self.add(node, f"X{p[1]}", f"if the party has a {humanize(p[0])}", choice=choice)
                self.add(node, f"X{p[2]}", f"if the party has no {humanize(p[0])}", choice=choice)
            elif t == "ReadSectionIfHasRequiredBuilding":
                self.add(node, f"X{p[1]}", f"if you have the {humanize(p[0])}", choice=choice)
            elif t in ("ReadSectionIfPartyHasTrait", "ReadSectionIfPartyHasRace"):
                self.add(node, f"X{p[1]}", f"if the party has a {humanize(p[0])}", choice=choice)
            elif t == "IfHerbsOverCActivateSection":
                self.add(node, f"X{p[0]}", "if you have more herbs than characters", choice=choice)
            elif t == "UnlockPuzzle":
                self.add(node, f"P:{p[0]}", "unlocks the puzzle", choice=choice)
            elif t in ("EnableEvent", "ActivateEvent"):
                self.add(node, f"E:{p[0]}", "adds the event", choice=choice)
                self.enabled_events.add(f"E:{p[0]}")
            elif t == "AddAncientCoin":
                self.add(node, "C:coin", "an ancient coin", choice=choice)
            elif t == "AddAdditionalLootCard":
                self.add(node, f"L:{p[0]}", "adds a special loot card", choice=choice)
            elif t == "AddBrummix":
                self.add(node, "C:brummix", "a Brummix sticker", choice=choice)
            elif t in ("UnlockSledScenarios", "UnlockBoatScenarios", "UnlockClimbingGearScenarios"):
                k = {"UnlockSledScenarios": "sled", "UnlockBoatScenarios": "boat", "UnlockClimbingGearScenarios": "climbing"}[t]
                self.add(node, f"K:{k}", "allows " + TRAVEL[k] + " scenarios", choice=choice)

    def build(self):
        for sid, s in self.scen.items():
            node = f"S{sid}"
            endings = s["endings"]
            sigs = [json.dumps(sorted(json.dumps(e, sort_keys=True) for e in c["effects"] if relevant(e)))
                    for c in endings]
            branching = len(set(sigs)) > 1
            if branching:
                self.choices[f"end:{node}"] = {"kind": "ending", "at": node, "title": f"{self.label[node]}: its ending",
                                               "note": ENDING_NOTES.get(sid, "Decided by what happens in the scenario."),
                                               "options": [ending_label(c, i) for i, c in enumerate(endings)]}
            for i, c in enumerate(endings):
                self.effects(node, c["effects"], (f"end:{node}", i) if branching else None)
            h = s["head"]
            for k, field in (("sled", "RequiresSled"), ("boat", "RequiresBoat"), ("climbing", "RequiresClimbingGear")):
                if h.get(field, "").lower() == "true":
                    self.requires[node].append(f"K:{k}")
        for xid, x in self.sect.items():
            if xid in OBSOLETE_SECTIONS:
                continue
            self.effects(f"X{xid}", x["cond"] + x["effects"])
        self.label["START"] = "The campaign's start"
        self.effects("START", self.defs["start"])
        for cls, x in self.defs["retire"].items():
            self.label[f"Z:{cls}"] = f"Retiring a {cls}"
            self.add(f"Z:{cls}", f"X{x}", "read when one retires")
        for card, sections in LOOT_CARD_SECTIONS.items():
            self.label[f"L:{card}"] = f"Special loot card {card}"
            for x in sections:
                self.add(f"L:{card}", f"X{x}", "read when it is drawn (one section per draw, in order)")
        for src, label, dst, how in CODE_LINKS:
            if label:
                self.label[src] = label
            self.add(src, dst, how)
        for n, sections in TOWN_GUARD_PERKS.items():
            self.label[f"G:{n}"] = f"Town guard perk {n} (adds job postings)"
            for x in sections:
                self.add(f"G:{n}", f"X{x}", "a job posting")
        for p, sections in PUZZLE_REWARDS.items():
            for x in sections:
                self.add(f"P:{p}", f"X{x}", "when the puzzle is solved")
        for b, sections in BUILDING_SECTIONS.items():
            self.label[f"B:{b}"] = f"Building: {b}"
            for x in sections:
                self.add(f"B:{b}", f"X{x}", "an upgrade of it")
        for sid, s in self.scen.items():
            if s["chain"] == "RandomScenario":
                self.add("R:random", f"S{sid}", "drawn at random")
        self.label["R:random"] = "A random side scenario"
        for k, v in TRAVEL.items():
            self.label[f"K:{k}"] = f"{v.capitalize()} scenarios allowed"
        for p in PUZZLE_REWARDS:
            self.label.setdefault(f"P:{p}", f"Puzzle: {humanize(p).replace('_', ' ')}")
        for e in self.edges:
            for n in (e["src"], e["dst"]):
                if n.startswith("P:"):
                    self.label.setdefault(n, f"Puzzle: {humanize(n[2:]).replace('_', ' ')}")
                elif n.startswith("E:"):
                    self.label.setdefault(n, f"Event {n[2:]}")
        self.out = defaultdict(list)
        self.inc = defaultdict(list)
        for e in self.edges:
            self.out[e["src"]].append(e)
            self.inc[e["dst"]].append(e)

    # Sources that need nothing earlier in the graph: the start, and the code-side sources (an event in the starting
    # deck, a personal quest, a town guard perk, a building, a chest).
    def roots(self):
        r = {"START", "S1"}  # scenario 1 is the prologue
        for n in set(self.out):
            if n[0] in "QGBTZ" or n == "R:random" or (n.startswith("E:") and n not in self.enabled_events):
                r.add(n)
        return r

    def reach(self, plan=None, removed=frozenset(), start=frozenset()):
        """Every node reachable when each choice in `plan` ({choice: option}) is fixed (choices not in it allow every
        option), scenarios in `removed` can't be played, and `start` is already reached."""
        plan = plan or {}
        got = set(self.roots()) | set(start)
        frontier, blocked = list(got), []
        while frontier:
            for n in frontier:
                blocked.extend(self.out.get(n, ()))
            frontier, keep = [], []
            for e in blocked:
                d, ch = e["dst"], e["choice"]
                if d in got or d in removed or (ch and ch[0] in plan and plan[ch[0]] != ch[1]):
                    continue
                if all(c in got for c in e["cond"]) and all(c in got for c in self.requires.get(d, ())):
                    got.add(d)
                    frontier.append(d)
                else:
                    keep.append(e)
            blocked = keep
        return got


TRAVEL = {"sled": "sled", "boat": "boat", "climbing": "climbing gear"}


def relevant(effect):
    t = effect["Type"]
    return (t in SCENARIO_UNLOCKS or "Section" in t or "Scenario" in t or t in
            ("UnlockPuzzle", "EnableEvent", "ActivateEvent", "AddAncientCoin", "AddBrummix"))


def ending_label(conclusion, i):
    c = conclusion.get("label") or ""
    m = re.match(r"^[\d.]+\s*/\s*outro\s*(\w)\s*-\s*(.*)$", c, re.I)
    if m:
        return f"Ending {m.group(1).upper()}: {m.group(2)}"
    classes = [humanize(e["Param1"].removesuffix("ID")) for e in conclusion["effects"] if e["Type"] == "UnlockClass"]
    return c or f"Ending {i + 1}" + (f" (unlocks the {', '.join(classes)} class)" if classes else "")


# ---- the campaign's progress (written by the mod)

def read_progress(path):
    p = {"party": path.stem.removeprefix("progress-"), "week": None, "travel": set(), "scen": {}, "sections": set(),
         "pending": set(), "calendar": [], "jobs": set(), "puzzles": {}, "quests": {}, "quests_done": set(),
         "stickers": [], "active": None}
    for line in path.read_text("utf-8").splitlines():
        f = line.split("\t")
        if not line or line.startswith("#") or len(f) < 2:
            continue
        k = f[0]
        if k == "party":
            p["party"] = f[1]
        elif k == "week":
            p["week"] = int(f[1])
        elif k == "travel":
            p["travel"] = set(f[1].split())
        elif k == "active":
            p["active"] = int(f[1])
        elif k == "scenario" and len(f) >= 3:
            st = p["scen"].setdefault(int(f[1]), {"completed": False, "locked": False, "ending": None, "by": None})
            flags = f[2].split(",")
            st["completed"] |= "completed" in flags
            st["locked"] |= "locked" in flags
            for fl in flags:
                if fl.startswith("ending="):
                    st["ending"] = int(fl[7:])
            if len(f) > 3 and f[3]:
                st["by"] = int(f[3])
        elif k == "section":
            p["sections"].add(f[1])
        elif k == "pending":
            p["pending"].add(f[1])
        elif k == "calendar" and len(f) >= 3:
            p["calendar"].append((f[1], int(f[2])))
        elif k == "job":
            p["jobs"].add(f[1])
        elif k == "puzzle" and len(f) >= 3:
            p["puzzles"][f[1]] = f[2] == "solved"
        elif k == "quest" and len(f) >= 3:
            if f[2] == "done":
                p["quests_done"].add(f[1])
            else:
                p["quests"][f[1]] = f[2]
        elif k == "sticker":
            p["stickers"].append(f[1])
    return p


# ---- choices and the max path

class Analysis:
    """Where a campaign stands: what is still reachable, which choices are still open, and what each option of each
    open choice costs. Lockout pairs are choices too: each option is a set of the pair's scenarios you play."""

    MAX_COMBOS = 4096

    def __init__(self, g, prog=None):
        self.g, self.prog = g, prog
        self.start, self.removed, self.plan = set(), set(), {}
        if prog:
            for sid, st in prog["scen"].items():
                n = f"S{sid}"
                if n not in g.label:
                    continue
                if st["locked"] and not st["completed"]:
                    self.removed.add(n)
                else:
                    self.start.add(n)
                if st["completed"] and f"end:{n}" in g.choices and st["ending"] is not None:
                    self.plan[f"end:{n}"] = st["ending"]
            sections = prog["sections"] | prog["pending"] | {c for c, _ in prog["calendar"]} | prog["jobs"]
            self.start |= {f"X{x}" for x in sections}
            self.start |= {f"P:{p}" for p in prog["puzzles"]} | {f"K:{t}" for t in prog["travel"]}
            for cid, c in g.choices.items():
                if c["kind"] == "pick" and c["at"] in self.start:
                    for e in g.out[c["at"]]:
                        if e["choice"] and e["choice"][0] == cid and e["dst"] in self.start:
                            self.plan[cid] = e["choice"][1]
        self.done = {f"S{s}" for s, st in (prog or {"scen": {}})["scen"].items() if st["completed"]}
        self.groups = self.lock_groups()
        self.cache = {}
        self.fresh_all = self.scen(g.reach())
        self.base = self.eval({})

    def scen(self, nodes):
        return frozenset(n for n in nodes if n[0] == "S" and n[1:].isdigit() and int(n[1:]) in self.g.scen)

    def lock_groups(self):
        adj = defaultdict(set)
        for a, _, b in self.g.locks:
            adj[a].add(b)
            adj[b].add(a)
        groups, seen = [], set()
        for n in sorted(adj, key=lambda x: int(x[1:])):
            if n in seen:
                continue
            comp, todo = set(), [n]
            while todo:
                x = todo.pop()
                if x not in comp:
                    comp.add(x)
                    todo.extend(adj[x])
            seen |= comp
            members = sorted(comp, key=lambda x: int(x[1:]))
            options = []
            for k in range(len(members), 0, -1):
                for sub in itertools.combinations(members, k):
                    s = set(sub)
                    if any(b in s for a in s for b in adj[a]) or any(s < o for o in options):
                        continue
                    options.append(s)
            ok = [o for o in options if self.done & comp <= o and not (o & self.removed)]
            groups.append({"id": "lock:" + "-".join(m[1:] for m in members), "members": comp, "options": ok,
                           "all": options})
        return groups

    def eval(self, fixed):
        key = frozenset(fixed.items())
        if key not in self.cache:
            plan, removed = dict(self.plan), set(self.removed)
            for cid, o in fixed.items():
                if cid.startswith("lock:"):
                    grp = next(gr for gr in self.groups if gr["id"] == cid)
                    removed |= grp["members"] - grp["options"][o]
                else:
                    plan[cid] = o
            self.cache[key] = self.scen(self.g.reach(plan, removed, self.start))
        return self.cache[key]

    def open_choices(self):
        """Choices still open whose options lead to different scenarios: [(id, title, [option labels])]."""
        out = []
        for grp in self.groups:
            if len(grp["options"]) > 1:
                labels = [" and ".join(self.g.label[m] for m in sorted(o, key=lambda x: int(x[1:]))) for o in grp["options"]]
                out.append((grp["id"], "Lockout: " + " or ".join(sorted((f"#{m[1:]}" for m in grp["members"]),
                                                                          key=lambda x: int(x[1:]))), labels))
        for cid, c in self.g.choices.items():
            if cid in self.plan or c["at"] not in self.g.reach(self.plan, self.removed, self.start):
                continue
            if c["kind"] == "ending" and c["at"] in self.done:
                continue
            out.append((cid, c["title"], c["options"]))
        return [(cid, t, o) for cid, t, o in out if len({self.eval({cid: i}) for i in range(len(o))}) > 1]

    def best_path(self, choices):
        """The option of each choice that together reach the most scenarios, that set, and for each (choice, option)
        the most scenarios reachable with that option taken (None when there were too many combinations to try)."""
        ids = [c[0] for c in choices]
        sizes = [len(c[2]) for c in choices]
        per_option = {}
        if math.prod(sizes) <= self.MAX_COMBOS:
            best, top = None, -1
            for pick in itertools.product(*[range(s) for s in sizes]):
                n = len(self.eval(dict(zip(ids, pick))))
                if n > top:
                    best, top = pick, n
                for cid, o in zip(ids, pick):
                    per_option[(cid, o)] = max(per_option.get((cid, o), 0), n)
        else:  # too many to try them all: improve one choice at a time until nothing improves
            best = [max(range(s), key=lambda o, cid=cid: len(self.eval({cid: o}))) for cid, s in zip(ids, sizes)]
            better = True
            while better:
                better = False
                for i, s in enumerate(sizes):
                    for o in range(s):
                        trial = list(best)
                        trial[i] = o
                        if len(self.eval(dict(zip(ids, trial)))) > len(self.eval(dict(zip(ids, best)))):
                            best, better = trial, True
        return dict(zip(ids, best)), self.eval(dict(zip(ids, best))), per_option


# ---- the page data

def paths_from(g, src):
    """[(scenario reached, [edges])] for every scenario `src` leads to without passing through another scenario."""
    out, seen, todo = [], {src}, [(src, [])]
    while todo:
        n, path = todo.pop(0)
        for e in g.out.get(n, ()):
            d = e["dst"]
            if d.startswith("S"):
                out.append((d, path + [e]))
            elif d not in seen:
                seen.add(d)
                todo.append((d, path + [e]))
    return out


def path_text(g, path, with_choice=True):
    steps = []
    first = path[0]
    for e in path[:-1]:
        steps.append(f"{short(g, e['dst'])} ({e['how']})")
    last = path[-1]
    tail = "" if last["how"] in ("unlocks", "drawn at random") else f" ({last['how']})"
    pre = ""
    if with_choice and first["choice"]:
        cid, o = first["choice"]
        c = g.choices[cid]
        pre = f"{c['options'][o]}: " if c["kind"] == "ending" else ""
    return pre + " → ".join(steps + [f"unlocks{tail}"])


def short(g, n):
    if n.startswith("X"):
        return f"section {n[1:]}"
    return g.label.get(n, n)


def layout(g, nodes):
    """x by depth (longest unlock path from the start), y in one lane per story line."""
    kids, parents = defaultdict(set), defaultdict(set)
    for s in nodes:
        for d, _ in paths_from(g, s):
            if d in nodes and d != s:
                kids[s].add(d)
    # break cycles (depth-first, drop edges back to a node on the current path)
    state, order = {}, []

    def dfs(n):
        state[n] = 1
        for k in sorted(kids[n], key=lambda x: int(x[1:])):
            if state.get(k) == 1:
                kids[n] = kids[n] - {k}
            elif k not in state:
                dfs(k)
        state[n] = 2
        order.append(n)

    for n in sorted(nodes, key=lambda x: int(x[1:])):
        if n not in state:
            dfs(n)
    for a in kids:
        for b in kids[a]:
            parents[b].add(a)
    depth = {}
    for n in reversed(order):
        depth[n] = max((depth[p] + 1 for p in parents[n] if p in depth), default=0)
    col, row, lane_gap, cap = 210, 38, 26, 6  # cap: rows per column in a lane before it wraps into another column
    pos, lanes, y0 = {}, [], 0
    chain_of = {n: g.scen[int(n[1:])]["chain"] for n in nodes}
    count = defaultdict(int)
    for n in nodes:
        count[(chain_of[n], depth[n])] += 1
    deepest = max(depth.values())
    subs = [max((math.ceil(c / cap) for (_, d), c in count.items() if d == dd), default=1) for dd in range(deepest + 1)]
    xs = [sum(subs[:d]) * col for d in range(deepest + 1)]
    for key in list(CHAINS) + sorted({c for c in chain_of.values() if c not in CHAINS}):
        mine = [n for n in nodes if chain_of[n] == key]
        if not mine:
            continue
        by_depth = defaultdict(list)
        for n in mine:
            by_depth[depth[n]].append(n)
        height = max(min(len(v), cap) for v in by_depth.values())
        for d in sorted(by_depth):
            col_nodes = by_depth[d]

            def bary(n):
                ys = [pos[p][1] for p in parents[n] if p in pos]
                return sum(ys) / len(ys) if ys else 1e9

            col_nodes.sort(key=lambda n: (bary(n), int(n[1:])))
            per = math.ceil(len(col_nodes) / math.ceil(len(col_nodes) / cap))
            for i, n in enumerate(col_nodes):
                pos[n] = (xs[d] + (i // per) * col, y0 + (i % per) * row)
        lanes.append({"key": key, "label": CHAINS.get(key, key), "y": y0, "h": height * row})
        y0 += height * row + lane_gap
    return pos, lanes, {(a, b) for a in kids for b in kids[a]}


def page_data(g, a, prog):
    nodes = sorted((f"S{s}" for s in g.scen), key=lambda x: int(x[1:]))
    pos, lanes, tree = layout(g, nodes)
    has = prog is not None
    p_scen = prog["scen"] if has else {}
    coming = set()
    if has:  # unlocked by a calendar entry or a pending section through sections only (not via an event or a puzzle)
        for x in [c for c, _ in prog["calendar"]] + sorted(prog["pending"]):
            coming |= {d for d, path in paths_from(g, f"X{x}") if all(e["dst"][0] in "XS" for e in path)}

    def status(n):
        sid = int(n[1:])
        st = p_scen.get(sid)
        if st and st["completed"]:
            return "done"
        if st and st["locked"]:
            return "locked"
        if st:
            return "open"
        if n not in a.fresh_all:
            return "untraced"
        if n in coming and n in a.base:
            return "coming"
        if n in a.base:
            return "later"
        return "lost"

    froms, tos = defaultdict(list), defaultdict(list)
    for n in nodes:
        for d, path in paths_from(g, n):
            if d == n:
                continue
            tos[n].append({"to": int(d[1:]), "t": path_text(g, path)})
            froms[d].append({"from": int(n[1:]), "t": path_text(g, path)})
    for r in sorted(g.roots() - set(nodes)):
        for d, path in paths_from(g, r):
            froms[d].append({"src": g.label.get(r, r), "t": path_text(g, path)})
    locks, locked_by = defaultdict(set), defaultdict(set)
    for s, ch, t in g.locks:
        pre = f"{g.choices[ch[0]]['options'][ch[1]]}: " if ch else ""
        locks[s].add((int(t[1:]), pre))
        locked_by[t].add(int(s[1:]))
    out_nodes = []
    for n in nodes:
        s = g.scen[int(n[1:])]
        h = s["head"]
        needs = [f"the {TRAVEL[k]}" for k in TRAVEL if f"K:{k}" in g.requires.get(n, ())]
        if h.get("RequiresPersonalQuest"):
            needs.append("its personal quest (" + h["RequiresPersonalQuest"].replace("PQ_", "PQ ") + ")")
        if h.get("RequiredLurkerCrownShards"):
            needs.append(f"{h['RequiredLurkerCrownShards']} Lurker crown shards")
        st = p_scen.get(int(n[1:]))
        x, y = pos[n]
        out_nodes.append({
            "id": int(n[1:]), "name": s["name"], "chain": CHAINS.get(s["chain"], s["chain"]), "loc": s["location"],
            "x": x, "y": y, "status": status(n), "needs": needs,
            "from": froms[n], "to": tos[n],
            "locks": [{"id": t, "pre": pre} for t, pre in sorted(locks[n])], "lockedBy": sorted(locked_by[n]),
            "by": st["by"] if st else None,
            "ending": (g.choices[f"end:{n}"]["options"][st["ending"]]
                       if st and st["completed"] and st["ending"] is not None and f"end:{n}" in g.choices else None),
        })
    edges = []
    for a_, b in sorted(tree):
        sty = "c" if any(e["cond"] or e["choice"] for d, path in paths_from(g, a_) if d == b for e in path) else ""
        edges.append([int(a_[1:]), int(b[1:]), sty])
    lock_edges = sorted({tuple(sorted((int(s[1:]), int(t[1:])))) for s, _, t in g.locks})

    # choices
    opens = a.open_choices()
    best, best_set, per_option = a.best_path(opens)
    choices = []
    for cid, title, labels in opens:
        opts = []
        for i, lab in enumerate(labels):
            got = a.eval({cid: i})
            top = per_option.get((cid, i))
            opts.append({"label": lab, "top": top, "lost": sorted(int(x[1:]) for x in a.base - got),
                         "best": top == len(best_set) if top is not None else best.get(cid) == i})
        note = ("Completing one locks the other out." if cid.startswith("lock:")
                else g.choices[cid].get("note", "You choose when it comes up."))
        choices.append({"title": title, "note": note, "options": opts})
    decided = []
    for grp in a.groups:
        played = a.done & grp["members"]
        out = grp["members"] & a.removed
        if played and out:
            decided.append(f"You played {', '.join(g.label[m] for m in sorted(played))}: "
                           f"{', '.join(g.label[m] for m in sorted(out))} locked out.")
    for cid, o in a.plan.items():
        c = g.choices[cid]
        decided.append(f"{c['title']}: {c['options'][o]}.")
    done = len(a.done)
    counts = defaultdict(int)
    for nd in out_nodes:
        counts[nd["status"]] += 1
    return {
        "has": has, "party": prog["party"] if has else None, "week": prog["week"] if has else None,
        "total": len(nodes), "counts": counts, "lanes": lanes, "nodes": out_nodes, "edges": edges,
        "locks": lock_edges, "choices": choices, "decided": decided,
        "best": {"total": len(best_set), "todo": len(best_set - a.done), "done": done,
                 "picks": [f"{t} → {labels[best[cid]]}" + (" (or any: they tie)" if all(
                     per_option.get((cid, i)) == len(best_set) for i in range(len(labels))) else "")
                     for cid, t, labels in opens]},
        "reachable": len(a.base), "fresh": len(a.fresh_all),
        "untraced": [nd["id"] for nd in out_nodes if nd["status"] == "untraced"],
        "untracedNote": UNTRACED_NOTE,
    }


def build(data):
    payload = json.dumps(data, separators=(",", ":"), ensure_ascii=False).replace("</", "<\\/")
    title = f"Campaign: {data['party']}" if data["has"] else "Frosthaven scenario map"
    return PAGE.replace("__TITLE__", html.escape(title)).replace("__DATA__", payload)


PAGE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
:root { --bg:#f6f4ef; --card:#fff; --ink:#1d1d20; --muted:#666; --line:#ddd6c8; --accent:#7a5a1e; --good:#2e8b45; --bad:#c0392b; --lane:#efebe2; --edge:#9b9384; --hl:#7a5a1e;
  --done:#cfe8d4; --done-b:#2e8b45; --open:#f6e2b0; --open-b:#b8860b; --coming:#d6e4f5; --coming-b:#3a6fb0; --later:#fff; --later-b:#bdb6a6;
  --lost:#e4e2de; --lost-b:#a8a49c; --locked:#f3d3cf; --locked-b:#c0392b; --untraced:#fff; --untraced-b:#a8a49c; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg:#16171a; --card:#202226; --ink:#e8e6e1; --muted:#9a9a9a; --line:#33363c; --accent:#d9b56a; --lane:#1c1e21; --edge:#5f636b; --hl:#d9b56a;
  --done:#1f3a27; --done-b:#5cc27a; --open:#43391a; --open-b:#e0b440; --coming:#1d2d44; --coming-b:#6aa0e0; --later:#202226; --later-b:#4a4e56;
  --lost:#232427; --lost-b:#44474d; --locked:#45211d; --locked-b:#e5735f; --untraced:#202226; --untraced-b:#55585e; } }
:root[data-theme="dark"] { --bg:#16171a; --card:#202226; --ink:#e8e6e1; --muted:#9a9a9a; --line:#33363c; --accent:#d9b56a; --lane:#1c1e21; --edge:#5f636b; --hl:#d9b56a;
  --done:#1f3a27; --done-b:#5cc27a; --open:#43391a; --open-b:#e0b440; --coming:#1d2d44; --coming-b:#6aa0e0; --later:#202226; --later-b:#4a4e56;
  --lost:#232427; --lost-b:#44474d; --locked:#45211d; --locked-b:#e5735f; --untraced:#202226; --untraced-b:#55585e; }
* { box-sizing:border-box; }
html, body { height:100%; }
body { margin:0; background:var(--bg); color:var(--ink); font:15px/1.5 system-ui, Segoe UI, sans-serif; display:flex; flex-direction:column; }
.bar { background:var(--bg); border-bottom:1px solid var(--line); padding:10px 16px; flex:0 0 auto; }
.bar h1 { font-size:18px; margin:0 0 2px; }
.sub { color:var(--muted); font-size:13px; }
nav { display:flex; flex-wrap:wrap; gap:6px; margin-top:8px; }
nav button, .btn { font:inherit; font-size:14px; padding:4px 12px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--ink); cursor:pointer; }
nav button.on { background:var(--accent); border-color:var(--accent); color:var(--bg); }
.view { display:none; flex:1 1 auto; min-height:0; } .view.on { display:block; }
#v-map.on { display:flex; flex-direction:column; position:relative; }
.tools { display:flex; flex-wrap:wrap; gap:8px; align-items:center; padding:8px 16px; border-bottom:1px solid var(--line); }
input[type=search] { font:inherit; font-size:14px; padding:5px 8px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--ink); width:100%; max-width:280px; }
.legend { display:flex; flex-wrap:wrap; gap:4px 12px; font-size:13px; color:var(--muted); }
.legend span { display:inline-flex; align-items:center; gap:5px; }
.sw { display:inline-block; width:14px; height:11px; border-radius:3px; border:1.5px solid var(--b); background:var(--f); }
#wrap { flex:1 1 auto; min-height:0; overflow:hidden; position:relative; touch-action:none; cursor:grab; }
#wrap.drag { cursor:grabbing; }
svg#map { width:100%; height:100%; display:block; user-select:none; }
.lane rect { fill:var(--lane); } .lane text { fill:var(--muted); font-size:13px; font-weight:600; }
.e { fill:none; stroke:var(--edge); stroke-width:1.2; opacity:.55; } .e.c { stroke-dasharray:5 4; }
.lk { fill:none; stroke:var(--bad); stroke-width:1.6; stroke-dasharray:3 3; opacity:.8; }
.n rect { stroke-width:1.5; rx:6; } .n text { font-size:12px; fill:var(--ink); pointer-events:none; }
.n { cursor:pointer; } .n.lost text { fill:var(--muted); text-decoration:line-through; } .n.untraced rect { stroke-dasharray:4 3; }
.n.match rect { stroke:var(--hl); stroke-width:3; }
svg.focus .n { opacity:.28; } svg.focus .e, svg.focus .lk { opacity:.08; }
svg.focus .n.sel, svg.focus .n.nb { opacity:1; } svg.focus .n.rel { opacity:.75; }
svg.focus .e.rel { opacity:.45; stroke:var(--hl); } svg.focus .e.hl { opacity:1; stroke:var(--hl); stroke-width:2.4; } svg.focus .lk.hl { opacity:1; stroke-width:2.4; }
.n.sel rect { stroke:var(--hl); stroke-width:3.5; }
aside { position:absolute; top:52px; right:12px; width:min(420px, calc(100% - 24px)); max-height:calc(100% - 64px); overflow:auto; background:var(--card); border:1px solid var(--line); border-radius:10px; padding:12px 16px; box-shadow:0 6px 24px rgba(0,0,0,.18); display:none; }
aside.on { display:block; }
aside h3 { margin:0 28px 2px 0; font-size:17px; } aside .x { position:absolute; top:6px; right:8px; border:0; background:none; color:var(--muted); font-size:22px; cursor:pointer; }
aside h4 { margin:12px 0 4px; font-size:13px; letter-spacing:.04em; color:var(--muted); text-transform:uppercase; }
aside ul { margin:0; padding-left:18px; } aside li { margin:3px 0; font-size:14px; }
a.go { color:var(--accent); cursor:pointer; text-decoration:underline; text-underline-offset:2px; }
.pill { display:inline-block; font-size:12px; padding:1px 8px; border-radius:10px; border:1.5px solid var(--b); background:var(--f); margin:4px 0; }
.muted { color:var(--muted); } .small { font-size:13px; }
.page { padding:12px 16px 60px; max-width:1100px; margin:0 auto; }
#v-list.on, #v-choices.on, #v-notes.on { overflow:auto; }
.card { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:12px 16px; margin:12px 0; }
.card h3 { margin:0 0 4px; font-size:16px; }
h2 { font-size:16px; margin:18px 0 6px; color:var(--accent); }
table { border-collapse:collapse; width:100%; font-size:14px; }
th, td { padding:5px 8px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; }
th { font-size:13px; color:var(--muted); font-weight:600; cursor:pointer; user-select:none; white-space:nowrap; }
td.n, th.n { text-align:right; font-variant-numeric:tabular-nums; }
tr.click { cursor:pointer; } tr.click:hover td { background:var(--lane); }
tr.best td { font-weight:600; } .star { color:var(--accent); }
.tablewrap { overflow-x:auto; }
.big { font-size:28px; font-weight:700; color:var(--accent); }
@media (max-width:640px) { aside { top:auto; bottom:8px; max-height:55%; } .hide-sm { display:none; } }
</style></head>
<body>
<div class="bar">
  <h1 id="title"></h1>
  <div class="sub" id="sub"></div>
  <nav id="nav"></nav>
</div>
<section class="view" id="v-map">
  <div class="tools">
    <input type="search" id="q" placeholder="Find a scenario (number or name)">
    <button class="btn" id="fit">Fit</button><button class="btn" id="zin">+</button><button class="btn" id="zout">&minus;</button>
    <div class="legend" id="legend"></div>
  </div>
  <div id="wrap"><svg id="map"><g id="vp"></g></svg></div>
  <aside id="panel"><button class="x" id="close" aria-label="Close">&times;</button><div id="pbody"></div></aside>
</section>
<section class="view" id="v-list"><div class="page">
  <input type="search" id="lq" placeholder="Filter (number, name, story line, status)">
  <div class="tablewrap"><table id="list"></table></div>
</div></section>
<section class="view" id="v-choices"><div class="page" id="choices"></div></section>
<section class="view" id="v-notes"><div class="page" id="notes"></div></section>
<script>
const D = __DATA__;
const $ = s => document.querySelector(s);
const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]));
const N = new Map(D.nodes.map(n => [n.id, n]));
const STATUS = {
  done: ["Done", "--done"], open: ["Available now", "--open"], coming: ["On the calendar", "--coming"],
  later: [D.has ? "Still reachable" : "Reachable", "--later"], locked: ["Locked out", "--locked"],
  lost: ["Out of reach (choices made)", "--lost"], untraced: ["Unlock not traced", "--untraced"],
};
const sw = st => `style="--f:var(${STATUS[st][1]});--b:var(${STATUS[st][1]}-b)"`;
const name = id => { const n = N.get(id); return n ? `#${id} ${n.name}` : `#${id}`; };
const go = id => `<a class="go" data-go="${id}">${esc(name(id))}</a>`;
const goText = t => esc(t).replace(/#(\d+)/g, (m, i) => N.has(+i) ? `<a class="go" data-go="${i}">${m}</a>` : m);

// ---- header
$("#title").textContent = D.has ? `Campaign: ${D.party}` : "Frosthaven scenario map";
const c = D.counts;
$("#sub").textContent = D.has
  ? `Week ${D.week ?? "?"} · ${c.done || 0} done · ${c.open || 0} available · ${c.coming || 0} on the calendar · ${c.later || 0} still reachable · ${(c.locked || 0) + (c.lost || 0)} out of reach · best case ${D.best.total} of ${D.total}`
  : `${D.total} scenarios · ${D.fresh} reachable · the most one campaign can complete: ${D.best.total} · spoilers: every scenario's name and how to reach it`;
const VIEWS = [["map", "Map"], ["list", "Scenarios"], ["choices", "Choices & max path"], ["notes", "Notes"]];
function show(v) {
  for (const [k] of VIEWS) { $("#v-" + k).classList.toggle("on", k === v); }
  for (const b of $("#nav").children) b.classList.toggle("on", b.dataset.v === v);
  try { localStorage.setItem("fhmap-tab", v); } catch (e) {}
  if (v === "map" && !fitted) { fit(); if (T.k < 0.55) { T = { x: 10, y: 10, k: 0.85 }; apply(); } fitted = true; }
}
$("#nav").innerHTML = VIEWS.map(([k, t]) => `<button data-v="${k}">${t}</button>`).join("");
$("#nav").onclick = e => { if (e.target.dataset.v) show(e.target.dataset.v); };
$("#legend").innerHTML = Object.keys(STATUS).filter(s => D.nodes.some(n => n.status === s))
  .map(s => `<span><i class="sw" ${sw(s)}></i>${STATUS[s][0]}</span>`).join("")
  + `<span><svg width="26" height="10"><line x1="1" y1="5" x2="25" y2="5" stroke="var(--bad)" stroke-width="2" stroke-dasharray="3 3"/></svg>locks out</span>`;

// ---- map
const W = 176, H = 28, LX = 190;
const svg = $("#map"), vp = $("#vp");
let parts = [];
const right = Math.max(...D.nodes.map(n => n.x)) + LX + W + 40;
for (const l of D.lanes) parts.push(`<g class="lane"><rect x="-10" y="${l.y - 8}" width="${right}" height="${l.h + 4}" rx="8"/><text x="4" y="${l.y + 12}">${esc(l.label)}</text></g>`);
const P = id => { const n = N.get(id); return [n.x + LX, n.y]; };
for (const [a, b, s] of D.edges) {
  const [x1, y1] = P(a), [x2, y2] = P(b), sx = x1 + W, sy = y1 + H / 2, ty = y2 + H / 2, dx = Math.max(30, (x2 - sx) / 2);
  parts.push(`<path class="e ${s}" data-a="${a}" data-b="${b}" d="M${sx},${sy} C${sx + dx},${sy} ${x2 - dx},${ty} ${x2},${ty}"/>`);
}
for (const [a, b] of D.locks) {
  const [x1, y1] = P(a), [x2, y2] = P(b);
  const d = x1 === x2 ? `M${x1 + W},${y1 + H / 2} C${x1 + W + 46},${y1 + H / 2} ${x2 + W + 46},${y2 + H / 2} ${x2 + W},${y2 + H / 2}`
                      : `M${x1 + W / 2},${y1 + H} L${x2 + W / 2},${y2}`;
  parts.push(`<path class="lk" data-a="${a}" data-b="${b}" d="${d}"/>`);
}
for (const n of D.nodes) {
  const [x, y] = P(n.id), label = `#${n.id} ${n.name}`, t = label.length > 26 ? label.slice(0, 25) + "…" : label;
  parts.push(`<g class="n ${n.status}" data-id="${n.id}" transform="translate(${x},${y})"><title>${esc(label)} (${esc(STATUS[n.status][0])})</title>`
    + `<rect width="${W}" height="${H}" style="fill:var(${STATUS[n.status][1]});stroke:var(${STATUS[n.status][1]}-b)"/>`
    + `<text x="8" y="18">${esc(t)}</text></g>`);
}
vp.innerHTML = parts.join("");
const BW = right, BH = Math.max(...D.lanes.map(l => l.y + l.h)) + 20;
let T = { x: 0, y: 0, k: 1 }, fitted = false;
const apply = () => vp.setAttribute("transform", `translate(${T.x},${T.y}) scale(${T.k})`);
function fit() {
  const r = svg.getBoundingClientRect();
  T.k = Math.min(r.width / BW, r.height / BH, 1.2); T.x = (r.width - BW * T.k) / 2 + 10 * T.k; T.y = 10; apply();
}
function zoom(f, cx, cy) {
  const r = svg.getBoundingClientRect(); cx ??= r.width / 2; cy ??= r.height / 2;
  const k = Math.min(3, Math.max(0.15, T.k * f)); T.x = cx - (cx - T.x) * k / T.k; T.y = cy - (cy - T.y) * k / T.k; T.k = k; apply();
}
function center(id) {
  const r = svg.getBoundingClientRect(), [x, y] = P(id);
  T.k = Math.max(T.k, 0.9); T.x = r.width / 2 - (x + W / 2) * T.k - (r.width > 700 ? 200 : 0); T.y = r.height / 3 - (y + H / 2) * T.k; apply();
}
$("#fit").onclick = fit; $("#zin").onclick = () => zoom(1.25); $("#zout").onclick = () => zoom(0.8);
const wrap = $("#wrap");
wrap.addEventListener("wheel", e => { e.preventDefault(); const r = svg.getBoundingClientRect(); zoom(e.deltaY < 0 ? 1.15 : 1 / 1.15, e.clientX - r.left, e.clientY - r.top); }, { passive: false });
let drag = null;
wrap.addEventListener("pointerdown", e => { drag = { x: e.clientX, y: e.clientY, tx: T.x, ty: T.y, moved: false }; });
wrap.addEventListener("pointermove", e => {
  if (!drag) return;
  const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
  if (!drag.moved && Math.hypot(dx, dy) > 4) { drag.moved = true; wrap.classList.add("drag"); wrap.setPointerCapture(e.pointerId); }
  if (drag.moved) { T.x = drag.tx + dx; T.y = drag.ty + dy; apply(); }
});
wrap.addEventListener("pointerup", e => {
  const was = drag; drag = null; wrap.classList.remove("drag");
  if (was && !was.moved) { const g = e.target.closest(".n"); if (g) select(+g.dataset.id); else select(null); }
});

// edges by node, for highlighting
const kids = new Map(), pars = new Map();
for (const [a, b] of D.edges) { (kids.get(a) || kids.set(a, []).get(a)).push(b); (pars.get(b) || pars.set(b, []).get(b)).push(a); }
const walk = (start, m) => { const out = new Set(), todo = [start]; while (todo.length) for (const x of m.get(todo.pop()) || []) if (!out.has(x)) { out.add(x); todo.push(x); } return out; };
let selected = null;
function select(id, recenter) {
  selected = id;
  svg.classList.toggle("focus", id !== null);
  for (const g of vp.querySelectorAll(".n")) g.classList.remove("sel", "nb", "rel");
  for (const p of vp.querySelectorAll(".e, .lk")) p.classList.remove("hl", "rel");
  if (id === null) { $("#panel").classList.remove("on"); return; }
  const anc = walk(id, pars), desc = walk(id, kids), rel = new Set([...anc, ...desc]);
  for (const g of vp.querySelectorAll(".n")) {
    const i = +g.dataset.id;
    if (i === id) g.classList.add("sel");
    else if ((kids.get(id) || []).includes(i) || (pars.get(id) || []).includes(i)) g.classList.add("nb");
    else if (rel.has(i)) g.classList.add("rel");
  }
  for (const p of vp.querySelectorAll(".e, .lk")) {
    const a = +p.dataset.a, b = +p.dataset.b;
    if (a === id || b === id) { p.classList.add("hl"); if (p.classList.contains("lk")) vp.querySelector(`.n[data-id="${a === id ? b : a}"]`)?.classList.add("nb"); }
    else if ((rel.has(a) || a === id) && (rel.has(b) || b === id) && ((anc.has(a) && (anc.has(b) || b === id)) || ((desc.has(a) || a === id) && desc.has(b)))) p.classList.add("rel");
  }
  $("#pbody").innerHTML = details(N.get(id));
  $("#panel").classList.add("on");
  if (recenter) center(id);
}
$("#close").onclick = () => select(null);
document.addEventListener("click", e => {
  const a = e.target.closest("[data-go]"); if (!a) return;
  e.preventDefault(); show("map"); select(+a.dataset.go, true);
});
function details(n) {
  const li = xs => `<ul>${xs.join("")}</ul>`;
  let h = `<h3>#${n.id} ${esc(n.name)}</h3><div class="muted small">${esc(n.chain)}${n.loc ? " · " + esc(n.loc) : ""}</div>`
    + `<span class="pill" ${sw(n.status)}>${esc(STATUS[n.status][0])}</span>`;
  if (n.ending) h += ` <span class="small muted">Ended with: ${esc(n.ending)}</span>`;
  if (n.status === "untraced") h += `<p class="small">${esc(D.untracedNote)}</p>`;
  if (n.needs.length) h += `<h4>Needs</h4><div class="small">${n.needs.map(esc).join(", ")}</div>`;
  if (n.from.length) h += `<h4>Unlocked by</h4>` + li(n.from.map(f => `<li>${f.from ? go(f.from) : esc(f.src)}<div class="small muted">${goText(f.t)}</div></li>`));
  if (n.by) h += `<div class="small muted">In this campaign it was unlocked by ${go(n.by)}.</div>`;
  if (n.to.length) h += `<h4>Leads to</h4>` + li(n.to.map(t => `<li>${go(t.to)}<div class="small muted">${goText(t.t)}</div></li>`));
  if (n.locks.length) h += `<h4>Locks out</h4>` + li(n.locks.map(l => `<li>${esc(l.pre)}${go(l.id)}</li>`));
  if (n.lockedBy.length) h += `<h4>Locked out by</h4>` + li(n.lockedBy.map(i => `<li>${go(i)}</li>`));
  return h;
}
$("#q").addEventListener("input", () => {
  const q = $("#q").value.trim().toLowerCase();
  for (const g of vp.querySelectorAll(".n")) {
    const n = N.get(+g.dataset.id);
    g.classList.toggle("match", !!q && (String(n.id) === q.replace("#", "") || (q.length > 1 && n.name.toLowerCase().includes(q))));
  }
});
$("#q").addEventListener("keydown", e => {
  if (e.key !== "Enter") return;
  const m = vp.querySelector(".n.match"); if (m) select(+m.dataset.id, true);
});

// ---- list
const ORDER = Object.keys(STATUS);
let sortKey = "id", sortDir = 1;
function list() {
  const q = $("#lq").value.trim().toLowerCase();
  const rows = D.nodes.filter(n => !q || `#${n.id} ${n.name} ${n.chain} ${STATUS[n.status][0]}`.toLowerCase().includes(q));
  const key = { id: n => n.id, name: n => n.name, chain: n => n.chain, status: n => ORDER.indexOf(n.status), to: n => n.to.length }[sortKey];
  rows.sort((a, b) => (key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : a.id - b.id) * sortDir);
  const src = n => n.from.map(f => f.from ? `#${f.from}` : f.src).filter((v, i, a) => a.indexOf(v) === i);
  $("#list").innerHTML = `<tr><th class="n" data-k="id">#</th><th data-k="name">Scenario</th><th data-k="chain" class="hide-sm">Story line</th><th data-k="status">Status</th><th class="hide-sm">Unlocked by</th><th data-k="to" class="hide-sm">Leads to</th></tr>`
    + rows.map(n => `<tr class="click" data-go="${n.id}"><td class="n">${n.id}</td><td>${esc(n.name)}${n.needs.length ? `<div class="small muted">needs ${n.needs.map(esc).join(", ")}</div>` : ""}</td><td class="hide-sm">${esc(n.chain)}</td>`
      + `<td><span class="pill" ${sw(n.status)}>${esc(STATUS[n.status][0])}</span></td><td class="small hide-sm">${esc(src(n).join(", "))}</td><td class="small hide-sm">${esc(n.to.map(t => "#" + t.to).filter((v, i, a) => a.indexOf(v) === i).join(", "))}</td></tr>`).join("");
}
$("#lq").addEventListener("input", list);
$("#list").addEventListener("click", e => { const k = e.target.dataset.k; if (k) { sortDir = sortKey === k ? -sortDir : 1; sortKey = k; list(); } });
list();

// ---- choices
(function () {
  const b = D.best;
  let h = `<div class="card"><h3>Max path</h3>`;
  h += D.has
    ? `<div><span class="big">${b.total}</span> <span class="muted">of ${D.total} scenarios, if you take the best option at every open choice: ${b.done} done, ${b.todo} to go.</span></div>`
    : `<div><span class="big">${b.total}</span> <span class="muted">of ${D.total} scenarios is the most one campaign can complete. ${D.fresh} can be reached by some path, ${D.untraced.length} have no traced unlock.</span></div>`;
  h += b.picks.length ? `<p class="small">How:</p><ul class="small">${b.picks.map(p => `<li>${goText(p)}</li>`).join("")}</ul>`
                      : `<p class="small muted">No open choice changes how many scenarios you can reach.</p>`;
  h += `<p class="small muted">Best case: the most scenarios the campaign can complete with that option, taking the best option at every other choice. Gives up: what that option puts out of reach. ★ marks the best option (several when they tie). Counts every scenario that some path still reaches, including random side scenarios, events, puzzles and town guard perks (assumes you get to all of them). Choices that don't change any scenario aren't listed.</p></div>`;
  if (D.choices.length) {
    h += `<h2>Open choices</h2>`;
    for (const ch of D.choices) {
      h += `<div class="card"><h3>${goText(ch.title)}</h3>${ch.note ? `<div class="small muted">${esc(ch.note)}</div>` : ""}<div class="tablewrap"><table><tr><th>Option</th><th class="n">Best case</th><th>Gives up</th></tr>`
        + ch.options.map(o => `<tr class="${o.best ? "best" : ""}"><td>${o.best ? '<span class="star">★</span> ' : ""}${goText(o.label)}</td><td class="n">${o.top ?? "?"}</td><td class="small">${o.lost.length ? o.lost.map(go).join(", ") : '<span class="muted">nothing</span>'}</td></tr>`).join("")
        + `</table></div></div>`;
    }
  }
  if (D.decided.length) h += `<h2>Already decided</h2><div class="card"><ul class="small">${D.decided.map(d => `<li>${goText(d)}</li>`).join("")}</ul></div>`;
  $("#choices").innerHTML = h;
})();

// ---- notes
$("#notes").innerHTML = `<div class="card"><h3>Where this comes from</h3><p class="small">The unlocks are read from the game's own scenario and section data, plus the unlocks the game makes in code: events, personal quests, town guard perks (job postings), puzzles, buildings, treasure chests, special loot cards, class retirements and ancient coins.
${D.has ? "The colours come from the progress file the mod writes after each autosave (house rule <b>Scenario map file</b>)." : "Turn on the house rule <b>Scenario map file</b> to colour the map by a campaign's progress."}</p>
<p class="small">Reachable means some path still leads there. It doesn't mean it's likely: events are drawn at random, puzzles need solving, and job postings need town guard perks.</p>
<p class="small">Unlock not traced: ${D.untraced.map(go).join(", ") || "none"}. ${esc(D.untracedNote)}</p>
<p class="small">On the map, a dashed line is an unlock that depends on a choice or on another scenario being complete, and a red dotted line joins scenarios that lock each other out. Click a scenario to see its unlocks; its whole chain lights up.</p></div>`;

let tab = "map";
try { tab = localStorage.getItem("fhmap-tab") || "map"; } catch (e) {}
const hash = location.hash.slice(1);
if (VIEWS.some(([k]) => k === hash)) tab = hash;
show(VIEWS.some(([k]) => k === tab) ? tab : "map");
if (/^\d+$/.test(hash) && N.has(+hash)) { show("map"); select(+hash, true); }
</script>
</body></html>
"""


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("progress", nargs="*", help="progress-*.txt files (default: every one in the mod's scenarios folder)")
    ap.add_argument("--data", default=str(RULESET), help="the game's Base.ruleset, or an unpacked ruleset folder")
    ap.add_argument("--blank", action="store_true", help="the map without a campaign")
    ap.add_argument("-o", "--out", help="output .html (only for one map)")
    args = ap.parse_args()
    data = Path(args.data)
    if not data.exists():
        sys.exit(f"can't find the game's data at {data} (use --data)")
    g = Graph(load_defs(data))
    files = [Path(p) for p in args.progress] or ([] if args.blank else sorted(PROGRESS_DIR.glob("progress-*.txt")))
    jobs = [(None, PROGRESS_DIR / "scenario-map.html")] if args.blank or not files else \
        [(f, f.with_name("scenario-map-" + f.stem.removeprefix("progress-") + ".html")) for f in files]
    if not files and not args.blank:
        print(f"no progress files in {PROGRESS_DIR} (turn on the 'Scenario map file' house rule): writing the blank map")
    if args.out and len(jobs) > 1:
        sys.exit("-o needs a single progress file")
    for f, out in jobs:
        prog = read_progress(f) if f else None
        d = page_data(g, Analysis(g, prog), prog)
        out = Path(args.out) if args.out else out
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(build(d), "utf-8")
        print(f"{out}  ({d['counts'].get('done', 0)} done, {d['reachable']} reachable, best case {d['best']['total']} of {d['total']})")


if __name__ == "__main__":
    main()
