"""Proposed retirement rewards for every Gloomhaven class, straight from Gloomhaven's data.

Gloomhaven rewards a class's first retirement with its own city and road events (TreasureTable/TT_Achievements.yml:
TT_Achievement_Retirement_<Class> -> CityEvent + RoadEvent; options can add further events). This tool reads those
events (text from LanguageExport.csv, effects from the Event_*_TreasureTables) for all 17 classes that have them (the
Jaws of the Lion four have none), translates each option's effect to a Frosthaven event effect by the rules of
tools/gh_events.py (THE REWARD REVIEW: reputation -> morale, quest unlocks -> a random side scenario, Frosthaven's own
sizes as caps, nothing lasting into scenarios) and writes:

    tools/gh_events_proposed.json     the EVENTS entries (status "proposed") gh_events.events() falls back to for a
                                      class that has no hand-written entry yet
    tests/GH rewards review.xlsx      one workbook, every class, one row per option: Gloomhaven's effect, the proposed
                                      Frosthaven effect, the guideline check, a verdict column

    python tools/gh_rewards.py            -> both files (hand-written classes in gh_events.EVENTS are kept as they are)
    python tools/gh_rewards.py --classes  -> the classes and their event chains, nothing written

A translation the rules can't settle (a Gloomhaven item with no Frosthaven twin, a random outcome table, a condition
Frosthaven's events never give) is proposed as the nearest thing, or nothing, and says so in its note: those rows are the
review's work. Verdicts come back through gh_events.py (status "approved" / a changed "fh") as for the Brute.
"""

import argparse
import datetime
import json
import re
import sys
from pathlib import Path

import yaml

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gh_events  # noqa: E402
import gh_port as g  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
PROPOSED = ROOT / "tools" / "gh_events_proposed.json"
OUT = ROOT / "tests" / "GH rewards review.xlsx"

# Gloomhaven item -> Frosthaven item ID, where Frosthaven sells the same or a near twin within the 30-gold limit.
# Anything else is flagged for the review.
ITEM_MAP = {
    "BattleAxeID": (136, "Frosthaven's Battle Axe, 20 gold"),
    "ThiefsHoodID": (10109, "the pack's Thief's Hood (Frosthaven item 10109, 20 gold), added for the Scoundrel"),
}
ITEM_NOTES = {
    "SunEarringID": "Frosthaven's Sun Earring (item 160) costs 35, over the 30 limit; the Moon Earring (156, 20 gold) is the nearest",
    "BlackCardID": "Frosthaven has no Black Card (a one-use curse-card item); its White Card (190, 20 gold) is the mirror image",
    "BloodyAxeID": "Frosthaven's axes: Flashing Axe (184, 20 gold) is the nearest shop axe; the Cleaving Axe (29) is a scenario reward",
}
PARTY_CONDITION = {"Bless": "PartyBless", "Curse": "PartyCurse", "Poison": "PartyPoison", "Wound": "PartyWound", "Muddle": "PartyMuddle"}
MORALE_CAP, PROSPERITY_CAP, GOLD_CAP, EACH_GOLD_CAP, LOSE_GOLD_CAP, DISCARD_CAP, DAMAGE_CAP = 2, 1, 20, 10, 30, 2, 3


class GH:
    """Gloomhaven's rule files, read once."""

    def __init__(self):
        gh, _ = g.find_gloomhaven()
        self.files = g.gh_files(gh)
        self.strings = g.gh_strings(gh)
        self.tables = {}
        for f in ("TreasureTable/Event_City_TreasureTables.yml", "TreasureTable/Event_Road_TreasureTables.yml",
                  "TreasureTable/EventTreasureTables.yml"):
            self.tables.update(self.load(f) or {})
        ach = self.load("TreasureTable/TT_Achievements.yml")
        self.retirement = {k[len("TT_Achievement_Retirement_"):]: v for k, v in ach.items() if k.startswith("TT_Achievement_Retirement_")}
        self.event_files = {}
        for p in self.files:
            if p.startswith(("CityEvent/", "RoadEvent/")):
                m = re.search(r"^ID:\s*(\S+)", g.text(self.files[p]), re.M)
                self.event_files[m.group(1)] = p

    def load(self, path):
        return yaml.safe_load(g.text(self.files[path]).replace("\t", "    "))

    def text(self, key):
        s = self.strings.get(key, f"[{key}]")
        return re.sub(r"<[^>]+>", "", s).replace("\\n", "\n").strip()


def camel(s):
    return re.sub(r"(?<=[a-z])(?=[A-Z])", " ", s)


def event_label(event_id):
    m = re.match(r"Event_(City|Road)_Campaign_(\d+)ID", event_id)
    return f"{m.group(1)} event {m.group(2)}" if m else event_id


def chain(gh, cls):
    """The class's events in discovery order: its city and road event, then every event an option adds."""
    t = gh.retirement[cls]
    todo = [t[k] for k in ("CityEvent", "RoadEvent") if k in t]
    seen = []
    while todo:
        e = todo.pop(0)
        if e in seen:
            continue
        seen.append(e)
        for opt in options(gh, gh.load(gh.event_files[e])):
            for eff in opt["effects"]:
                for k in ("CityEvent", "RoadEvent"):
                    if k in eff:
                        todo += eff[k] if isinstance(eff[k], list) else [eff[k]]
    return seen


def options(gh, ev):
    """Every choice of an event as one row: text, outcome text, the effect tables along the way. An option that opens
    a second choice (3 of the 29 events) is flattened into one row per final choice, 'A, then B'."""
    scr = ev["Screens"]
    out = []

    def rec(screen, texts, effects):
        s = scr.get(screen) or {}
        for letter, o in (s.get("Options") or {}).items():
            tt = o.get("TreasureTables")
            names = (tt if isinstance(tt, list) else [tt]) if tt else []
            effs = effects + [gh.tables.get(n) or {} for n in names]
            nxt = o.get("Next")
            res = scr.get(nxt) or {}
            t = texts + [gh.text(o["Text"])]
            # a screen with one "OK" button is a page break in the outcome text, not a second choice
            lead = []
            while res.get("Options") and len(res["Options"]) == 1:
                lead.append(gh.text(res["Text"]))
                o2 = next(iter(res["Options"].values()))
                tt2 = o2.get("TreasureTables")
                effs = effs + [gh.tables.get(n) or {} for n in ((tt2 if isinstance(tt2, list) else [tt2]) if tt2 else [])]
                res = scr.get(o2.get("Next")) or {}
            if res.get("Options"):
                rec(nxt, t, effs)
            else:
                outcome = "\n\n".join(lead + ([gh.text(res["Text"])] if res.get("Text") else []))
                out.append({"text": " — then — ".join(t), "outcome": outcome, "effects": effs, "nested": len(t) > 1})
    rec("Decision", [], [])
    return out


def amount(v, note):
    """A Gloomhaven amount: a number, a per-party-size list (Gloomhaven scales some by party size) or a Min/Max range."""
    if isinstance(v, list):
        note.append(f"Gloomhaven scales it by party size ({v[0]}..{v[-1]}); the 3-player value is proposed")
        return v[1] if len(v) > 1 else v[0]
    return v


def translate(effects, ids):
    """One option's Gloomhaven effect tables -> (Gloomhaven wording, Frosthaven effects, notes)."""
    gh_parts, fh, notes = [], [], []

    def cap(kind, n, limit):
        if n > limit:
            notes.append(f"{kind} {n} lowered to Frosthaven's largest, {limit}")
        return min(n, limit)

    def one(k, v):
        if k == "Reputation":
            gh_parts.append(f"{v:+d} reputation")
            fh.append(("GainMorale", cap("morale", v, MORALE_CAP)) if v > 0 else ("LoseMorale", cap("morale", -v, MORALE_CAP)))
            notes.append("reputation -> morale")
        elif k == "Prosperity":
            gh_parts.append(f"{v:+d} prosperity")
            fh.append(("AddProsperity", cap("prosperity", v, PROSPERITY_CAP)) if v > 0 else ("LoseProsperity", cap("prosperity", -v, PROSPERITY_CAP)))
        elif k == "Gold":
            d = v if isinstance(v, dict) else {"Amount": v}
            if "Amount" in d:
                a = amount(d["Amount"], notes)
            else:
                lo, hi = amount(d.get("MinAmount", 0), notes), amount(d.get("MaxAmount", 0), notes)
                a = (lo + hi) // 2
                notes.append(f"Gloomhaven rolls {lo}..{hi} gold; the middle is proposed")
            if d.get("Chance") is not None:
                notes.append(f"Gloomhaven gives it with a {amount(d['Chance'], notes)}% chance; proposed as certain")
            each = d.get("TreasureDistributionType") == "PerMercenaryInParty"
            gh_parts.append(f"{a:+d} gold" + (" each" if each else " (party)"))
            if a > 0:
                fh.append(("AllCharactersGainGold", cap("gold each", a, EACH_GOLD_CAP)) if each else ("GainGold", cap("gold", a, GOLD_CAP)))
            elif a < 0:
                fh.append(("LoseGold", cap("gold loss", -a, LOSE_GOLD_CAP)))
                notes.append("Frosthaven's LoseGold takes it from the party's total gold once" + (" (Gloomhaven took it from each character)" if each else ""))
        elif k == "Discard":
            gh_parts.append(f"start the next scenario with {v} cards discarded")
            fh.append(("PartyDiscardCards", cap("discard", v, DISCARD_CAP)))
        elif k == "Damage":
            d = v if isinstance(v, dict) else {"Amount": v}
            a = amount(d.get("Amount", 0), notes)
            if d.get("Chance") is not None:
                notes.append(f"Gloomhaven: {d['Chance']}% chance of the damage; proposed as certain")
            gh_parts.append(f"each character suffers {a} damage")
            fh.append(("PartyDamage", cap("damage", a, DAMAGE_CAP)))
        elif k == "Condition":
            d = v if isinstance(v, dict) else {"Name": v}
            name = d["Name"]
            if d.get("Chance") is not None:
                notes.append(f"Gloomhaven: {d['Chance']}% chance of {name}; proposed as certain")
            gh_parts.append(f"each character gains {name}")
            if name in PARTY_CONDITION:
                fh.append((PARTY_CONDITION[name], 1) if name in ("Bless", "Curse") else (PARTY_CONDITION[name],))
            else:
                notes.append(f"Frosthaven's events never give {name} to the party: no effect proposed (PartyWard exists for a positive one)")
        elif k == "Item":
            name = v["Name"] if isinstance(v, dict) else v
            gh_parts.append(f"gain {camel(name.removesuffix('ID'))}")
            if name in ITEM_MAP:
                fh.append(("GainItem", ITEM_MAP[name][0]))
                notes.append(ITEM_MAP[name][1])
            else:
                notes.append("ITEM TO CHOOSE: " + ITEM_NOTES.get(name, f"Gloomhaven's {name.removesuffix('ID')} has no Frosthaven twin; pick an item of 30 gold or less"))
        elif k == "UnlockQuest":
            gh_parts.append(f"unlock scenario {str(v).replace('Quest_Campaign_', '')}")
            fh.append(("UnlockRandomSideScenario",))
            notes.append("a Gloomhaven scenario unlock -> a random side scenario")
        elif k == "UnlockAchievement":
            gh_parts.append(f"achievement {str(v).replace('Achievement_', '')}")
            notes.append(f"the Gloomhaven achievement ({str(v).replace('Achievement_', '')}) is a town record with no Frosthaven meaning: nothing proposed")
        elif k == "XP":
            a = amount(v if not isinstance(v, dict) else v.get("Amount", 0), notes)
            gh_parts.append(f"+{a} experience each")
            notes.append("experience is never a ported reward (guidelines): nothing proposed")
        elif k == "PerkCheck":
            gh_parts.append(f"{v:+d} perk check" if isinstance(v, int) else "a perk check")
            notes.append("perk checks are never a ported reward (they last the campaign): nothing proposed")
        elif k in ("CityEvent", "RoadEvent"):
            for e in (v if isinstance(v, list) else [v]):
                gh_parts.append(f"add {event_label(e).lower()} to the deck")
                fh.append(("EnableEvent", ids[e]))
            notes.append("the follow-up event, as in Gloomhaven")
        elif k == "AddModifiers":
            mods = v.get("Modifiers", []) if isinstance(v, dict) else []
            desc = ", ".join(f"{n} x {m}" for m, n in mods)
            gh_parts.append(f"add {desc} to each modifier deck")
            fh.append(("PartyCurse", 1))
            notes.append("Gloomhaven's extra -1 cards sit in the deck until the next scenario ends; one curse card each is the Frosthaven event equivalent")
        elif k == "ConsumeItem":
            gh_parts.append("each character consumes a " + camel(str((v or {}).get("Slot", "Small"))).lower() + " item")
            notes.append("Frosthaven's events never consume items: nothing proposed")
        elif k == "LoseItem":
            gh_parts.append(f"lose {str((v or {}).get('Name', '')).removesuffix('ID')}")
            notes.append("losing a specific Gloomhaven item has no Frosthaven version: nothing proposed")
        elif k == "Infuse":
            gh_parts.append(f"{v} infused at the start of the next scenario")
            notes.append("Frosthaven's events never infuse elements: nothing proposed")
        elif k == "Group":
            gh_parts.append("a random outcome (" + ", ".join(str(x) for x in v) + ")")
            notes.append("RANDOM TABLE in Gloomhaven: " + json.dumps(v, default=str)[:160] + "; a fixed choice is needed")
        elif k == "Nothing":
            pass
        else:
            gh_parts.append(f"{k}: {v}")
            notes.append(f"UNTRANSLATED Gloomhaven effect {k}")

    for eff in effects:
        for k, v in (eff or {}).items():
            one(k, v)
    # keep each note once, in order
    seen = set()
    notes = [n for n in notes if not (n in seen or seen.add(n))]
    return ", ".join(gh_parts) or "nothing", fh, notes


def build(gh, cls):
    """The class's EVENTS entries, status proposed."""
    events = chain(gh, cls)
    ids = {e: f"GH.{cls}-{i}" for i, e in enumerate(events, 1)}
    out = []
    for e in events:
        ev = gh.load(gh.event_files[e])
        kind = "City" if "_City_" in e else "Road"
        intro = ev["Screens"]["Decision"]["Text"]
        entry = {"id": ids[e], "deck": "SummerOutpost" if kind == "City" else "SummerRoad",
                 "start": "retire" if e in gh.retirement[cls].values() else "chain", "gh": event_label(e),
                 "front": gh.text(intro),
                 "edits": "none yet: Gloomhaven's place names stay until the text pass",
                 "options": []}
        for o in options(gh, ev):
            gh_text, fh, notes = translate(o["effects"], ids)
            if o["nested"]:
                notes.insert(0, "Gloomhaven asks a second question after this choice; flattened into one option per answer")
            entry["options"].append({"text": o["text"], "outcome": o["outcome"], "gh": gh_text, "fh": fh,
                                     "note": "; ".join(notes) or "a straight copy", "status": "proposed"})
        out.append(entry)
    return out


def all_events(gh):
    """Every class with Gloomhaven retirement events: the hand-written entry where gh_events.py has one, else built."""
    out = {}
    for cls in sorted(gh.retirement):
        hand = gh_events.EVENTS.get(cls)
        out[cls] = ("hand-written", hand) if hand else ("generated", build(gh, cls))
    return out


def workbook(per_class, path):
    import gh_testsheet as ts
    from openpyxl import Workbook
    from openpyxl.styles import Alignment, Font, PatternFill
    from openpyxl.worksheet.datavalidation import DataValidation
    from openpyxl.formatting.rule import FormulaRule

    items = ts._items()
    costs = {i: c for i, (_, c) in items.items() if c is not None}
    wb = Workbook()
    guide = wb.active
    guide.title = "How to use"
    review = wb.create_sheet("Review")
    rules = wb.create_sheet("Guidelines")
    text = wb.create_sheet("Event text")
    ts._sheet(review, ["#", "Class", "Event", "Deck", "From Gloomhaven", "Option", "Gloomhaven gives", "Frosthaven version",
                       "Inside the guidelines?", "How common in Frosthaven", "Notes on the translation", "Status", "Your verdict", "Your notes"],
              [4, 13, 14, 16, 14, 40, 26, 30, 20, 30, 40, 10, 12, 34])
    n = 0
    flagged = 0
    for cls, (source, evs) in per_class.items():
        deck_of = {e["id"]: ts.DECK_NAMES[e["deck"]] for e in evs}
        for e in evs:
            how = " (from retirement)" if e["start"] == "retire" else " (from another event)"
            for i, o in enumerate(e["options"], 1):
                n += 1
                fh = "\n".join(ts._effect_text(k, ps, items, deck_of) for k, *ps in o["fh"]) or "nothing"
                common = "\n".join(ts._baseline(k, ps) for k, *ps in o["fh"])
                why = gh_events.check(o, costs)
                needs = any(w in o["note"] for w in ("ITEM TO CHOOSE", "RANDOM TABLE", "UNTRANSLATED"))
                r = ts._row(review, [n, cls, e["id"], deck_of[e["id"]] + how, e["gh"], f"{i}. {o['text']}", o["gh"], fh,
                                     "Yes" if not why else "No: " + why, common, o["note"], o.get("status", "proposed"), None, None],
                            inputs=(13, 14))
                if why:
                    review.cell(r, 9).fill = ts.RETEST
                if needs:
                    review.cell(r, 11).fill = ts.RETEST
                    flagged += 1
    last = review.max_row
    dv = DataValidation(type="list", formula1='"' + ",".join(ts.VERDICTS) + '"', allow_blank=True, promptTitle="Verdict",
                        prompt="Approve: ship it. Change: say what in the notes. Reject: no reward for this option.")
    review.add_data_validation(dv)
    dv.add(f"M2:M{last}")
    for value, colour in (("Approve", "C6EFCE"), ("Change", "FFEB9C"), ("Reject", "FFC7CE")):
        review.conditional_formatting.add(f"M2:M{last}", FormulaRule(formula=[f'$M2="{value}"'], fill=PatternFill("solid", fgColor=colour)))
    review.auto_filter.ref = f"A1:N{last}"
    review.freeze_panes = "C2"

    ts._sheet(rules, ["Guideline"], [120])
    for gl in gh_events.GUIDELINES:
        ts._row(rules, [gl])
    ts._row(rules, ["Per-option limits: " + ", ".join(f"{k} {v}" for k, v in gh_events.LIMITS.items())
                    + f"; items up to {gh_events.ITEM_COST_LIMIT} gold. Never: " + ", ".join(sorted(gh_events.NEVER)) + "."])

    ts._sheet(text, ["Class", "Event", "Part", "Text"], [13, 14, 26, 110])
    for cls, (source, evs) in per_class.items():
        for e in evs:
            ts._row(text, [cls, e["id"], f"Card front (changed from Gloomhaven: {e.get('edits', 'none')})", e["front"]])
            for i, o in enumerate(e["options"], 1):
                ts._row(text, [cls, e["id"], f"Option {i}", o["text"]])
                ts._row(text, [cls, e["id"], f"Option {i} outcome", o["outcome"]])

    guide.column_dimensions["A"].width = 110
    classes = ", ".join(f"{c} ({len(evs)} events, {'reviewed before' if src == 'hand-written' else 'new'})" for c, (src, evs) in per_class.items())
    lines = [
        ("Gloomhaven classes: retirement rewards review (all classes)", Font(name=ts.FONT, size=14, bold=True)),
        (f"Built {datetime.date.today():%d %b %Y} for mods {ts.version()}.", None),
        ("", None),
        ("When a ported class retires, its Gloomhaven city and road events go into Frosthaven's event decks, the way "
         "Frosthaven rewards five of its own classes. Each option's Gloomhaven effect has to be translated; this sheet "
         "holds every class at once so the whole set can be judged together. The Jaws of the Lion classes have no "
         "retirement events in Gloomhaven and are not here.", None),
        (f"Classes: {classes}.", None),
        ("1. Review sheet, one row per event option: what Gloomhaven gives, the proposed Frosthaven version, whether it "
         "fits the Guidelines sheet, how often Frosthaven's own events give the same thing, and the translation notes. "
         "A yellow note means the rules could not settle it (an item to choose, a random table): those rows need you most.", None),
        ("2. Pick a verdict in the yellow column for every row (Approve, Change or Reject) and add notes where you want "
         "something different, including the deck (season).", None),
        ("3. The Event text sheet has the full Gloomhaven text; place names are changed in a later text pass.", None),
        ("4. Save and tell Claude the review is done. Until an option is approved it ships with its text and no effect.", None),
    ]
    for line, font in lines:
        guide.append([line])
        c = guide.cell(guide.max_row, 1)
        c.font = font or Font(name=ts.FONT, size=11)
        c.alignment = Alignment(wrap_text=True)
    wb.active = wb.sheetnames.index("Review")
    path.parent.mkdir(exist_ok=True)
    wb.save(path)
    return n, flagged


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--classes", action="store_true", help="list the classes and their event chains only")
    ap.add_argument("--out", type=Path, default=OUT)
    args = ap.parse_args()
    gh = GH()
    if args.classes:
        for cls in sorted(gh.retirement):
            print(f"{cls:14} {[event_label(e) for e in chain(gh, cls)]}")
        return
    per_class = all_events(gh)
    generated = {c: evs for c, (src, evs) in per_class.items() if src == "generated"}
    PROPOSED.write_text(json.dumps(generated, indent=1, ensure_ascii=False), encoding="utf-8")
    n, flagged = workbook(per_class, args.out)
    counts = {c: sum(len(e["options"]) for e in evs) for c, (_, evs) in per_class.items()}
    print(f"wrote {PROPOSED.name}: {len(generated)} generated classes; {args.out}: {n} options in {len(per_class)} classes, {flagged} need a choice")
    print(", ".join(f"{c} {k}" for c, k in counts.items()))


if __name__ == "__main__":
    main()
