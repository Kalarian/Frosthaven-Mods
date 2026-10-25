"""The effects test workbook for a ported Gloomhaven class: what to play, turn by turn, what Gloomhaven shows for each
card half, and a column for what Frosthaven showed. Built from the pack itself, so it follows every re-port:

  - the class's cards (mods/gloomhaven/files/.../AbilityCards) and what each half plays (its AnimOverload, or the
    game's default for the ability: Attack, RangedAttack, Loot, Heal, PowerUp);
  - pack.json's animation table: the effects and the sound of each animator state;
  - tools/gh_tests/<Class>.json, hand-written: the test hand in play order ("turns"), a plain description of each
    state ("states"), what the last in-game run found ("status"), and the effects that are not on a card ("actions").
    Without it, the hand is picked to cover every state once and the descriptions are generated from the data.

    python tools/gh_testsheet.py build Brute        -> tests/Brute effects test.xlsx (an existing file is kept: --force)
    python tools/gh_testsheet.py read Brute         -> the rows whose result is not "Matches", with the notes
    python tools/gh_testsheet.py rewards Brute      -> tests/Brute rewards review.xlsx: the retirement events' rewards,
                                                       Gloomhaven's and the proposed Frosthaven version, for a verdict
    python tools/gh_testsheet.py read-rewards Brute -> the verdicts
    python tools/gh_testsheet.py party Scoundrel Sunkeeper Sawbones Quartermaster --name "Group 1"
                                                    -> tests/Group 1 test script.xlsx: one workbook for a whole test
                                                       party: setup, hands, the turn plan (turn x character), the
                                                       tests and the rewards review of every class in it
    python tools/gh_testsheet.py read-party --name "Group 1" Scoundrel   -> its results and verdicts

The workbook is the player's: tests/ is not published (the repo is a whitelist). Needs openpyxl.
"""

import argparse
import datetime
import json
import re
import subprocess
import sys
from pathlib import Path

from openpyxl import Workbook, load_workbook
from openpyxl.formatting.rule import FormulaRule
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.worksheet.datavalidation import DataValidation

sys.path.insert(0, str(Path(__file__).resolve().parent))   # gh_events, fh_mods, fh_ruleset beside this file

ROOT =Path(__file__).resolve().parent.parent
PACK = ROOT / "mods" / "gloomhaven"
SPECS = Path(__file__).resolve().parent / "gh_tests"
OUT = ROOT / "tests"

RESULTS = ["Matches", "Differs", "Missing", "Not seen"]
FONT = "Arial"

# ability kinds -> the animator state the game plays when the card names none
DEFAULT_STATE = {"Attack": "Attack", "Loot": "Loot", "Heal": "Heal", "Move": None}
KIND_NAMES = {"MergedMoveAttack": "Move and attack", "PreventDamage": "Prevent damage", "AddActiveBonus": "Active bonus",
              "Redirect": "Redirect attacks", "Kill": "Kill"}


# ---------------------------------------------------------------- the data

def strings():
    out = {}
    for line in (PACK / "strings.tsv").read_text(encoding="utf-8").splitlines():
        k, _, v = line.partition("\t")
        out[k] = v
    return out


def cards(cls):
    """[{key, name, level, initiative, halves: {Top: [ability], Bottom: [ability]}}] for the class, in card order.
    An ability: {kind, amount, range, area, jump, anim}."""
    names = strings()
    out = []
    for f in sorted((PACK / "files" / "Global" / "Characters" / cls / "AbilityCards").glob("*.yml")):
        text = f.read_text(encoding="utf-8")
        key = re.search(r"^Name:\s*\$ABILITY_CARD_(\w+)\$", text, re.M).group(1)
        card = {"key": key, "name": names.get("ABILITY_CARD_" + key, key),
                "level": re.search(r"^Level:\s*(\w+)", text, re.M).group(1),
                "initiative": int(re.search(r"^Initiative:\s*(\d+)", text, re.M).group(1)),
                "halves": {"Top": [], "Bottom": []}}
        half, ab = None, None
        for line in text.splitlines():
            m = re.match(r"^(\s*)([^\s:-][^:]*):\s*(.*?)\s*$", line)
            if not m:
                continue
            ind, k, v = len(m.group(1)), m.group(2), m.group(3)
            if ind == 0:
                half = None
            if ind == 4 and k in ("Top", "Bottom"):
                half = k
                continue
            if half is None:
                continue
            if ind == 12:
                ab = {"kind": None, "amount": None, "range": None, "area": False, "jump": False, "anim": None}
                card["halves"][half].append(ab)
            elif ab is not None and ind == 16:
                if k == "AnimOverload":
                    ab["anim"] = v
                elif k == "Range":
                    ab["range"] = v
                elif k == "AreaEffect":
                    ab["area"] = True
                elif k in ("ActiveBonus", "ActiveBonusDuration"):
                    ab["bonus"] = True
                elif ab["kind"] is None and k not in ("Filter", "Target", "Conditions", "ActiveBonus", "ActiveBonusDuration",
                                                      "SubAbilities", "MultiPassAttack", "XPPerTarget", "StatIsBasedOnX",
                                                      "AttackSourcesOnly", "StartAbilityRequirements", "Name", "Text",
                                                      "AreaEffectLayoutOverride", "PreviewEffectText", "AttackEffect",
                                                      "AbilityOverrides", "Elements"):
                    ab["kind"] = k
                    ab["amount"] = v or None
            elif ab is not None and ind > 16:
                if k == "Amount" and ab["amount"] is None:
                    ab["amount"] = v
                elif k == "Jump" and v.lower() == "true":
                    ab["jump"] = True
                elif k == "Move" and ab["kind"] == "MergedMoveAttack" and v:
                    ab["amount"] = v
        for h in card["halves"]:
            card["halves"][h] = [a for a in card["halves"][h] if a["kind"]]
        out.append(card)
    return out


def animation(cls):
    return json.loads((PACK / "pack.json").read_text(encoding="utf-8"))["model"][cls]["animation"]


def states_of(ab):
    """The animator states an ability plays, in order ("Movement" for a plain move)."""
    kind, anim = ab["kind"], ab["anim"]
    if anim == "None":
        return []
    if kind in ("Move",):
        return ["Jump" if ab["jump"] else "Movement"]
    if kind == "MergedMoveAttack":
        return ["Jump" if ab["jump"] else "Movement", "Attack"]
    if anim:
        return [anim]
    if kind == "Attack":
        return ["RangedAttack" if ab["range"] else "Attack"]
    return [DEFAULT_STATE.get(kind, "PowerUp")]


def plays(anim, state):
    """(clip, sound event, [effect names]) the plugin plays for <state>, from the pack's animation table."""
    for table in ("gh_effects", "lent_states"):
        pass
    if state in anim.get("gh_states", {}):
        fx = anim.get("gh_effects", {}).get(state) or {}
        return anim["gh_states"][state], anim.get("gh_audio", {}).get(state), _fx(fx)
    if state in anim.get("lent_states", {}):
        lent = anim["lent_states"][state]
        return lent.get("clip"), lent.get("audio"), _fx(lent.get("effects") or {})
    t = anim.get("states", {}).get("Base Layer." + state)
    if t is not None:
        return anim.get("clips", {}).get(anim.get("state_clips", {}).get(state)), t.get("audio"), _fx(t.get("effects") or {})
    return None, None, []


def _fx(play):
    names = [c["effect"] for c in play.get("caster", [])]
    for k in ("target", "area", "central", "aura"):
        names += [e["effect"] for e in (play.get(k) or {}).get("effects", [])]
    return names


def _pretty(name, cls):
    if not name:
        return ""
    n = re.sub(r"^(PlaySound_|P_|HE_)", "", name)
    n = re.sub(rf"^{cls}_?|_v\d+$", "", n)
    return re.sub(r"(?<=[a-z])(?=[A-Z])|_", " ", n).strip()


def generated(anim, state, cls):
    clip, audio, fx = plays(anim, state)
    parts = [f"Animation: {_pretty(clip, cls) or state}."]
    if fx:
        parts.append("Effects: " + ", ".join(_pretty(f, cls) for f in fx) + ".")
    if audio:
        parts.append(f"Sound: {_pretty(audio, cls)}.")
    return " ".join(parts)


def summary(abilities):
    out = []
    for a in abilities:
        name = KIND_NAMES.get(a["kind"], a["kind"])
        text = name + (f" {a['amount']}" if a["amount"] and a["amount"].lower() not in ("true",) else "")
        if a["range"]:
            text += f", range {a['range']}"
        if a["area"]:
            text += ", area"
        if a["jump"]:
            text += ", jump"
        if a.get("bonus"):
            text += " (bonus)"
        out.append(text)
    return "; ".join(out)


def pick_hand(all_cards):
    """Without a hand-written plan: cards picked greedily until every state any half plays is covered, ten at most."""
    want = {s for c in all_cards for h in c["halves"].values() for a in h for s in states_of(a)} - {"Movement"}
    hand, covered = [], set()
    while want - covered and len(hand) < 10:
        best = max(all_cards, key=lambda c: len({s for h in c["halves"].values() for a in h for s in states_of(a)} - covered))
        hand.append(best)
        covered |= {s for h in best["halves"].values() for a in h for s in states_of(a)}
    if len(hand) % 2:   # a turn needs two cards: the lowest-level card not yet taken
        hand.append(next(c for c in all_cards if c not in hand))
    return [{"top": hand[i]["key"], "bottom": hand[i + 1]["key"], "order": "", "setup": ""} for i in range(0, len(hand), 2)]


def version():
    v = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    try:
        sha = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()
    except OSError:
        sha = ""
    return f"{v} + {sha}" if sha else v


# ---------------------------------------------------------------- the workbook

THIN = Side(style="thin", color="BFBFBF")
BOX = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
HEAD = PatternFill("solid", fgColor="1F3864")
INPUT = PatternFill("solid", fgColor="FFF2CC")
RETEST = PatternFill("solid", fgColor="FCE4D6")


def _sheet(ws, headers, widths):
    ws.append(headers)
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[ws.cell(1, i).column_letter].width = w
        c = ws.cell(1, i)
        c.font = Font(name=FONT, bold=True, color="FFFFFF")
        c.fill = HEAD
        c.alignment = Alignment(wrap_text=True, vertical="center")
        c.border = BOX
    ws.freeze_panes = "A2"


def _row(ws, values, inputs=()):
    ws.append(values)
    r = ws.max_row
    for i in range(1, len(values) + 1):
        c = ws.cell(r, i)
        c.font = Font(name=FONT, size=10)
        c.alignment = Alignment(wrap_text=True, vertical="top")
        c.border = BOX
        if i in inputs:
            c.fill = INPUT
    return r


def build(cls, force=False):
    spec_file = SPECS / f"{cls}.json"
    spec = json.loads(spec_file.read_text(encoding="utf-8")) if spec_file.exists() else {}
    all_cards = cards(cls)
    by_key = {c["key"].lower(): c for c in all_cards}
    anim = animation(cls)
    turns = spec.get("turns") or pick_hand(all_cards)
    notes, status = spec.get("states", {}), spec.get("status", {})
    built = version()

    def expected(state):
        return notes.get(state) or generated(anim, state, cls)

    wb = Workbook()
    guide = wb.active
    guide.title = "How to use"
    plan = wb.create_sheet("Turn plan")
    tests = wb.create_sheet("Tests")

    # Tests: one row per card half of the hand, in play order, then the effects that are not on a card
    _sheet(tests, ["#", "Turn", "Card", "Half", "Ability", "Plays", "Expected (what Gloomhaven shows)",
                   "Status before this run", "Result in Frosthaven", "Notes"], [5, 6, 20, 8, 22, 18, 60, 26, 14, 40])
    seen, n = {}, 0
    for t, turn in enumerate(turns, 1):
        for half, key in (("Top", turn["top"]), ("Bottom", turn["bottom"])):
            card = by_key[key.lower()]
            abilities = card["halves"][half]
            states = [s for a in abilities for s in states_of(a) if s != "Movement"]
            n += 1
            lines, st = [], []
            for s in dict.fromkeys(states):
                if s in seen:
                    lines.append(f"Same {s} as row {seen[s]}.")
                else:
                    seen[s] = n
                    lines.append(expected(s))
                    if status.get(s):
                        st.append(status[s])
            if any(states_of(a) == ["Movement"] for a in abilities):
                lines.append("The move: see the Movement row.")
            r = _row(tests, [n, t, card["name"], half, summary(abilities), ", ".join(dict.fromkeys(states)) or "(move only)",
                             "\n".join(lines) or "Nothing of its own.", "\n".join(st), None, None], inputs=(9, 10))
            if any(x.startswith("RETEST") for x in st):
                tests.cell(r, 8).fill = RETEST
    for action in spec.get("actions") or [{"name": "Movement", "how": "Any move.", "expected": "Run cycle, footstep sounds and dust."}]:
        n += 1
        st = status.get(action["name"], "")
        r = _row(tests, [n, "", action["name"], "", action["how"], "", action["expected"], st, None, None], inputs=(9, 10))
        if st.startswith("RETEST"):
            tests.cell(r, 8).fill = RETEST
    last = tests.max_row
    tests.auto_filter.ref = f"A1:J{last}"
    dv = DataValidation(type="list", formula1='"' + ",".join(RESULTS) + '"', allow_blank=True,
                        promptTitle="Result", prompt="Matches, Differs, Missing (nothing played) or Not seen (didn't get to it)")
    tests.add_data_validation(dv)
    dv.add(f"I2:I{last}")
    for value, colour in (("Matches", "C6EFCE"), ("Differs", "FFC7CE"), ("Missing", "FFC7CE"), ("Not seen", "EDEDED")):
        tests.conditional_formatting.add(f"I2:I{last}", FormulaRule(formula=[f'$I2="{value}"'], fill=PatternFill("solid", fgColor=colour)))

    # Turn plan
    _sheet(plan, ["Turn", "Top", "Bottom", "Order", "Setup"], [6, 22, 22, 55, 40])
    for t, turn in enumerate(turns, 1):
        _row(plan, [t, by_key[turn["top"].lower()]["name"], by_key[turn["bottom"].lower()]["name"], turn.get("order", ""), turn.get("setup", "")])

    # How to use
    guide.column_dimensions["A"].width = 110
    lines = [
        (f"{cls}: effects test", Font(name=FONT, size=14, bold=True)),
        (f"Built {datetime.date.today():%d %b %Y} for mods {built}. Rebuilt from the pack when the class changes.", None),
        ("", None),
        ("1. Take the ten cards on the Turn plan sheet into a scenario with plenty of normal enemies, at a low scenario level.", None),
        ("2. Play the turns in order. Each Tests row is one card half; the moves are checked once, on the Movement row.", None),
        ("3. For each row, fill the yellow cells: Result (pick from the list) and Notes (what was different, louder or quieter, colours).", None),
        ("   Matches: looks and sounds like Gloomhaven.  Differs: something is off.  Missing: nothing played.  Not seen: didn't get to it.", None),
        ("4. 'Status before this run' says what the last run found. Rows marked RETEST had a fix since; the others only need a glance.", None),
        ("5. Save the file and tell Claude the results are in. Claude reads the sheet directly; no need to paste anything.", None),
        ("", None),
        ("Compare against Gloomhaven digital when unsure (Ctrl+F8 and Ctrl+F9 there unlock and max out the class).", None),
    ]
    for text, font in lines:
        guide.append([text])
        c = guide.cell(guide.max_row, 1)
        c.font = font or Font(name=FONT, size=11)
        c.alignment = Alignment(wrap_text=True)

    wb.active = wb.sheetnames.index("Tests")
    OUT.mkdir(exist_ok=True)
    path = OUT / f"{cls} effects test.xlsx"
    if path.exists() and not force:
        sys.exit(f"{path} exists (it may hold results); pass --force to replace it")
    wb.save(path)
    print(f"wrote {path}: {n} tests, {len(turns)} turns, for mods {built}")
    return path


def read(cls):
    path = OUT / f"{cls} effects test.xlsx"
    ws = load_workbook(path, data_only=True)["Tests"]
    head = [c.value for c in ws[1]]
    col = {h: i for i, h in enumerate(head)}
    counts, open_rows = {}, []
    for row in ws.iter_rows(min_row=2, values_only=True):
        result = row[col["Result in Frosthaven"]] or "(blank)"
        counts[result] = counts.get(result, 0) + 1
        if result != "Matches":
            open_rows.append(row)
    print(f"{path.name}: " + ", ".join(f"{k} {v}" for k, v in counts.items()))
    for row in open_rows:
        print(f"  #{row[col['#']]} {row[col['Card']]} {row[col['Half']] or ''}: {row[col['Result in Frosthaven']] or '(blank)'}"
              f" | {row[col['Notes']] or ''}")


# ---------------------------------------------------------------- the reward review (gh_events.py)

VERDICTS = ["Approve", "Change", "Reject"]
DECK_NAMES = {"SummerOutpost": "summer outpost", "WinterOutpost": "winter outpost", "SummerRoad": "summer road",
              "WinterRoad": "winter road"}


def _items():
    """{item ID: (name, gold cost or None)} from the player's vanilla Base.ruleset."""
    import fh_mods
    import fh_ruleset as rs
    out = {}
    for e in rs.read_ruleset(fh_mods.vanilla_backup()):
        if not e["name"].startswith("Outpost/Items/"):
            continue
        t = e["data"].decode("utf-8", "replace")
        i = re.search(r"^ID:\s*(\d+)", t, re.M)
        n = re.search(r"^Name:\s*(.+?)\s*$", t, re.M)
        c = re.search(r"^GoldCost:\s*(\d+)", t, re.M)
        if i:
            out[int(i.group(1))] = (n.group(1) if n else "?", int(c.group(1)) if c else None)
    return out


def _effect_text(kind, params, items, deck_of):
    p = params[0] if params else None
    if kind == "GainItem":
        name, cost = items.get(int(p), ("?", None))
        return f"gain {name} (item {p}, {cost if cost is not None else '?'} gold)"
    words = {
        "GainMorale": f"+{p} morale", "LoseMorale": f"-{p} morale", "AddProsperity": f"+{p} prosperity",
        "LoseProsperity": f"-{p} prosperity", "GainInspiration": f"+{p} inspiration", "GainGold": f"+{p} gold",
        "LoseGold": f"-{p} gold", "PartyDiscardCards": f"each character discards {p} ability cards",
        "PartyDamage": f"each character suffers {p} damage", "PartyBless": f"each character gains {p} bless",
        "PartyCurse": f"each character gains {p} curse",
        "EnableEvent": f"adds event {p} to the {deck_of.get(p, '?')} deck",
    }
    return words.get(kind, f"{kind} {' '.join(str(x) for x in params)}")


def _baseline(kind, params):
    import gh_events
    b = gh_events.FH_BASELINE.get(kind)
    if not b:
        return "Not among Frosthaven's common event effects"
    if kind == "GainItem":
        return f"{b['a specific item']} Frosthaven events give a specific item"
    if kind == "EnableEvent":
        return "Structure, not a reward (Frosthaven's events chain the same way)"
    n = b.get(params[0] if params else None)
    sizes = ", ".join(f"{k}: {v}" for k, v in b.items())
    return (f"{n} Frosthaven events give exactly this" if n else "No Frosthaven event gives this size") + f" (by size: {sizes})"


def build_rewards(cls, force=False):
    import gh_events
    evs = gh_events.events(cls)
    if not evs:
        sys.exit(f"{cls} has no events in tools/gh_events.py")
    items = _items()
    costs = {i: c for i, (_, c) in items.items() if c is not None}
    deck_of = {e["id"]: DECK_NAMES[e["deck"]] for e in evs}
    wb = Workbook()
    guide = wb.active
    guide.title = "How to use"
    review = wb.create_sheet("Review")
    rules = wb.create_sheet("Guidelines")
    text = wb.create_sheet("Event text")

    _sheet(review, ["#", "Event", "Deck", "From Gloomhaven", "Option", "Gloomhaven gives", "Frosthaven version",
                    "Inside the guidelines?", "How common in Frosthaven", "Why", "Status", "Your verdict", "Your notes"],
           [4, 12, 16, 14, 34, 22, 30, 20, 30, 34, 10, 12, 34])
    n = 0
    for e in evs:
        how = " (from retirement)" if e["start"] == "retire" else " (from another event)"
        for i, o in enumerate(e["options"], 1):
            n += 1
            fh = "\n".join(_effect_text(k, ps, items, deck_of) for k, *ps in o["fh"]) or "nothing"
            common = "\n".join(_baseline(k, ps) for k, *ps in o["fh"])
            why = gh_events.check(o, costs)
            r = _row(review, [n, e["id"], deck_of[e["id"]] + how, e["gh"], f"{i}. {o['text']}", o["gh"], fh,
                              "Yes" if not why else "No: " + why, common, o["note"], o.get("status", "proposed"), None, None],
                     inputs=(12, 13))
            if why:
                review.cell(r, 8).fill = RETEST
    last = review.max_row
    dv = DataValidation(type="list", formula1='"' + ",".join(VERDICTS) + '"', allow_blank=True, promptTitle="Verdict",
                        prompt="Approve: ship it. Change: say what in the notes. Reject: no reward for this option.")
    review.add_data_validation(dv)
    dv.add(f"L2:L{last}")
    for value, colour in (("Approve", "C6EFCE"), ("Change", "FFEB9C"), ("Reject", "FFC7CE")):
        review.conditional_formatting.add(f"L2:L{last}", FormulaRule(formula=[f'$L2="{value}"'], fill=PatternFill("solid", fgColor=colour)))
    review.auto_filter.ref = f"A1:M{last}"

    _sheet(rules, ["Guideline"], [120])
    for g in gh_events.GUIDELINES:
        _row(rules, [g])
    _row(rules, ["Per-option limits: " + ", ".join(f"{k} {v}" for k, v in gh_events.LIMITS.items())
                 + f"; items up to {gh_events.ITEM_COST_LIMIT} gold. Never: " + ", ".join(sorted(gh_events.NEVER)) + "."])

    _sheet(text, ["Event", "Part", "Text"], [12, 26, 110])
    for e in evs:
        _row(text, [e["id"], f"Card front (changed from Gloomhaven: {e.get('edits', 'none')})", e["front"]])
        for i, o in enumerate(e["options"], 1):
            _row(text, [e["id"], f"Option {i}", o["text"]])
            _row(text, [e["id"], f"Option {i} outcome", o["outcome"]])

    guide.column_dimensions["A"].width = 110
    lines = [
        (f"{cls}: retirement rewards review", Font(name=FONT, size=14, bold=True)),
        (f"Built {datetime.date.today():%d %b %Y} for mods {version()}.", None),
        ("", None),
        (f"When a {cls} retires, his Gloomhaven city and road events go into Frosthaven's event decks, the way Frosthaven "
         "rewards five of its own classes. Each option's Gloomhaven effect has to be translated; this sheet is where you "
         "decide each translation.", None),
        ("1. Read the Review sheet, one row per event option: what Gloomhaven gives, the proposed Frosthaven version, "
         "whether it fits the Guidelines sheet, and how often Frosthaven's own events give the same thing.", None),
        ("2. Pick a verdict in the yellow column for every row (Approve, Change or Reject) and add notes where you want "
         "something different, including the deck (season) if you'd rather have it elsewhere.", None),
        ("3. The Event text sheet has the full text, with any wording changed from Gloomhaven noted.", None),
        ("4. Save and tell Claude the review is done. Until an option is approved it ships with its text and no effect.", None),
    ]
    for line, font in lines:
        guide.append([line])
        c = guide.cell(guide.max_row, 1)
        c.font = font or Font(name=FONT, size=11)
        c.alignment = Alignment(wrap_text=True)
    wb.active = wb.sheetnames.index("Review")
    OUT.mkdir(exist_ok=True)
    path = OUT / f"{cls} rewards review.xlsx"
    if path.exists() and not force:
        sys.exit(f"{path} exists (it may hold your verdicts); pass --force to replace it")
    wb.save(path)
    print(f"wrote {path}: {n} options in {len(evs)} events")


def read_rewards(cls):
    ws = load_workbook(OUT / f"{cls} rewards review.xlsx", data_only=True)["Review"]
    head = [c.value for c in ws[1]]
    col = {h: i for i, h in enumerate(head)}
    for row in ws.iter_rows(min_row=2, values_only=True):
        print(f"  #{row[col['#']]} {row[col['Event']]} {str(row[col['Option']])[:50]}: {row[col['Your verdict']] or '(blank)'}"
              f" | {row[col['Your notes']] or ''}")


# ---------------------------------------------------------------- a party of classes in one workbook

def _spec(cls):
    f = SPECS / f"{cls}.json"
    return json.loads(f.read_text(encoding="utf-8")) if f.exists() else {}


def _plays(cls, spec, all_cards, anim):
    """Per turn (None for a rest), the card halves a class plays, and for each half its states and expected text."""
    by_key = {c["key"].lower(): c for c in all_cards}
    notes, status = spec.get("states", {}), spec.get("status", {})
    turns = spec.get("turns") or pick_hand(all_cards)
    out = []
    for turn in turns:
        if "rest" in turn:
            out.append(None)
            continue
        halves = []
        for half, key in (("Top", turn["top"]), ("Bottom", turn["bottom"])):
            card = by_key[key.lower()]
            abilities = card["halves"][half]
            states = [s for a in abilities for s in states_of(a) if s != "Movement"]
            halves.append({"card": card, "half": half, "abilities": abilities, "states": list(dict.fromkeys(states)),
                           "moveOnly": any(states_of(a) == ["Movement"] for a in abilities),
                           "expected": [notes.get(s) or generated(anim, s, cls) for s in dict.fromkeys(states)],
                           "status": [status[s] for s in dict.fromkeys(states) if status.get(s)]})
        out.append({"turn": turn, "halves": halves})
    return out


def build_party(classes, name, force=False):
    """One workbook for a whole test party: setup, the hands, the turn plan (turn x character), the tests, and the
    retirement rewards review of every class in it."""
    import gh_events
    data = {}
    for cls in classes:
        all_cards = cards(cls)
        data[cls] = {"spec": _spec(cls), "cards": all_cards, "anim": animation(cls)}
        data[cls]["plays"] = _plays(cls, data[cls]["spec"], all_cards, data[cls]["anim"])
    built = version()
    wb = Workbook()
    guide = wb.active
    guide.title = "How to use"
    setup = wb.create_sheet("Setup")
    hands = wb.create_sheet("Hands")
    plan = wb.create_sheet("Turn plan")
    tests = wb.create_sheet("Tests")
    review = wb.create_sheet("Rewards review")

    # Setup: the characters, their level-up picks, what the scenario needs
    _sheet(setup, ["Character", "Create as", "Level-up picks the hand needs", "What the scenario needs for them"], [16, 22, 44, 70])
    for cls in classes:
        spec = data[cls]["spec"]
        _row(setup, [cls, f"a {cls}, any name", "\n".join(spec.get("picks", ["any"])), spec.get("setup", "")])
    _row(setup, ["Everyone", "", "Ctrl+F7 gives every character the experience for the top level; level them up in the "
                 "outpost's downtime, picking the cards listed. Ctrl+F9 gives gold for items. Ctrl+F3 gives 3 perk marks each "
                 "(optional: to try the perks).",
                 "A scenario with plenty of normal enemies, a trap if possible, at a low scenario level. Four characters in "
                 "the party. Everyone takes a long rest on the turn marked so."])

    # Hands: the ten cards per character and what each half is for
    _sheet(hands, ["Character", "Card", "Level", "Initiative", "Top", "Bottom", "In the plan"], [16, 24, 7, 10, 30, 30, 24])
    for cls in classes:
        used = {}
        for i, p in enumerate(data[cls]["plays"], 1):
            if p:
                for h in p["halves"]:
                    used.setdefault(h["card"]["key"], []).append(f"turn {i} {h['half'].lower()}")
        for key, where in used.items():
            card = next(c for c in data[cls]["cards"] if c["key"] == key)
            _row(hands, [cls, card["name"], card["level"], card["initiative"], summary(card["halves"]["Top"]), summary(card["halves"]["Bottom"]), ", ".join(where)])

    # Turn plan: one row per turn, two columns per character
    head = ["Turn"]
    widths = [6]
    for cls in classes:
        head += [f"{cls} top", f"{cls} bottom", f"{cls}: order and setup"]
        widths += [20, 20, 42]
    _sheet(plan, head, widths)
    rounds = max(len(d["plays"]) for d in data.values())
    for i in range(rounds):
        row = [i + 1]
        for cls in classes:
            plays = data[cls]["plays"]
            p = plays[i] if i < len(plays) else None
            if i < len(plays) and p is None:
                row += ["Long rest", "", "Everyone rests: the discarded cards come back (lost ones don't)."]
            elif p is None:
                row += ["-", "", "nothing more planned"]
            else:
                row += [p["halves"][0]["card"]["name"], p["halves"][1]["card"]["name"],
                        " ".join(x for x in (p["turn"].get("order", ""), p["turn"].get("setup", "")) if x)]
        _row(plan, row)

    # Tests: per character, one row per card half in play order, then the non-card actions
    _sheet(tests, ["#", "Character", "Turn", "Card", "Half", "Ability", "Plays", "Expected (what Gloomhaven shows)",
                   "Result in Frosthaven", "Notes"], [5, 14, 6, 22, 8, 24, 20, 60, 14, 40])
    n = 0
    for cls in classes:
        seen = {}
        for i, p in enumerate(data[cls]["plays"], 1):
            if p is None:
                continue
            for h in p["halves"]:
                n += 1
                lines = []
                for s, text in zip(h["states"], h["expected"]):
                    if s in seen:
                        lines.append(f"Same {s} as row {seen[s]}.")
                    else:
                        seen[s] = n
                        lines.append(text)
                if h["moveOnly"]:
                    lines.append("The move: see the Movement row.")
                _row(tests, [n, cls, i, h["card"]["name"], h["half"], summary(h["abilities"]), ", ".join(h["states"]) or "(move only)",
                             "\n".join(lines) or "Nothing of its own.", None, None], inputs=(9, 10))
        for action in data[cls]["spec"].get("actions", []):
            n += 1
            _row(tests, [n, cls, "", action["name"], "", action["how"], "", action["expected"], None, None], inputs=(9, 10))
    n += 1
    _row(tests, [n, "Everyone", "", "Scenario end", "", "Win the scenario.", "", "Each character's victory pose and sound.", None, None], inputs=(9, 10))
    last = tests.max_row
    tests.auto_filter.ref = f"A1:J{last}"
    dv = DataValidation(type="list", formula1='"' + ",".join(RESULTS) + '"', allow_blank=True,
                        promptTitle="Result", prompt="Matches, Differs, Missing (nothing played) or Not seen (didn't get to it)")
    tests.add_data_validation(dv)
    dv.add(f"I2:I{last}")
    for value, colour in (("Matches", "C6EFCE"), ("Differs", "FFC7CE"), ("Missing", "FFC7CE"), ("Not seen", "EDEDED")):
        tests.conditional_formatting.add(f"I2:I{last}", FormulaRule(formula=[f'$I2="{value}"'], fill=PatternFill("solid", fgColor=colour)))

    # Rewards review: every class's retirement events
    items = _items()
    costs = {i: c for i, (_, c) in items.items() if c is not None}
    _sheet(review, ["#", "Character", "Event", "Deck", "From Gloomhaven", "Option", "Gloomhaven gives", "Frosthaven version",
                    "Inside the guidelines?", "Why", "Status", "Your verdict", "Your notes"],
           [4, 14, 14, 16, 14, 34, 24, 30, 20, 34, 10, 12, 34])
    m = 0
    for cls in classes:
        evs = gh_events.events(cls)
        deck_of = {e["id"]: DECK_NAMES[e["deck"]] for e in evs}
        for e in evs:
            how = " (from retirement)" if e["start"] == "retire" else " (from another event)"
            for i, o in enumerate(e["options"], 1):
                m += 1
                fh = "\n".join(_effect_text(k, ps, items, deck_of) for k, *ps in o["fh"]) or "nothing"
                why = gh_events.check(o, costs)
                r = _row(review, [m, cls, e["id"], deck_of[e["id"]] + how, e["gh"], f"{i}. {o['text']}", o["gh"], fh,
                                  "Yes" if not why else "No: " + why, o["note"], o.get("status", "proposed"), None, None], inputs=(12, 13))
                if why:
                    review.cell(r, 9).fill = RETEST
    if m:
        dv2 = DataValidation(type="list", formula1='"' + ",".join(VERDICTS) + '"', allow_blank=True, promptTitle="Verdict",
                             prompt="Approve: ship it. Change: say what in the notes. Reject: no reward for this option.")
        review.add_data_validation(dv2)
        dv2.add(f"L2:L{review.max_row}")
        for value, colour in (("Approve", "C6EFCE"), ("Change", "FFEB9C"), ("Reject", "FFC7CE")):
            review.conditional_formatting.add(f"L2:L{review.max_row}", FormulaRule(formula=[f'$L2="{value}"'], fill=PatternFill("solid", fgColor=colour)))
        review.auto_filter.ref = f"A1:M{review.max_row}"

    guide.column_dimensions["A"].width = 110
    for line, font in [
        (f"{name}: test script", Font(name=FONT, size=14, bold=True)),
        (f"Built {datetime.date.today():%d %b %Y} for mods {built}. Classes: {', '.join(classes)}.", None),
        ("", None),
        ("1. Setup sheet: create the four characters, level them up with the dev keys and pick the listed cards.", None),
        ("2. Hands sheet: the ten cards each character takes into the scenario, and which turn uses which half.", None),
        ("3. Turn plan sheet: one row per turn; every character plays the two cards named, in the order given. The row "
         "marked Long rest is a rest for everyone.", None),
        ("4. Tests sheet: one row per card half played, grouped by character. Fill the yellow cells: Result (from the list) "
         "and Notes. Compare against Gloomhaven digital when unsure (Ctrl+F8 and Ctrl+F9 there).", None),
        ("5. Rewards review sheet: each character's retirement events and the proposed Frosthaven effects; give each option a "
         "verdict in the yellow column.", None),
        ("6. Save the file and tell Claude the results are in. Claude reads the sheet directly.", None),
    ]:
        guide.append([line])
        c = guide.cell(guide.max_row, 1)
        c.font = font or Font(name=FONT, size=11)
        c.alignment = Alignment(wrap_text=True)
    wb.active = wb.sheetnames.index("Turn plan")
    OUT.mkdir(exist_ok=True)
    path = OUT / f"{name} test script.xlsx"
    if path.exists() and not force:
        sys.exit(f"{path} exists (it may hold results); pass --force to replace it")
    wb.save(path)
    print(f"wrote {path}: {n} tests, {rounds} turns, {m} reward options, for mods {built}")


def read_party(name):
    ws = load_workbook(OUT / f"{name} test script.xlsx", data_only=True)["Tests"]
    head = [c.value for c in ws[1]]
    col = {h: i for i, h in enumerate(head)}
    counts = {}
    for row in ws.iter_rows(min_row=2, values_only=True):
        result = row[col["Result in Frosthaven"]] or "(blank)"
        counts[result] = counts.get(result, 0) + 1
        if result != "Matches":
            print(f"  #{row[col['#']]} {row[col['Character']]} {row[col['Card']]} {row[col['Half']] or ''}: {result} | {row[col['Notes']] or ''}")
    print("totals: " + ", ".join(f"{k} {v}" for k, v in counts.items()))
    rv = load_workbook(OUT / f"{name} test script.xlsx", data_only=True)["Rewards review"]
    head = [c.value for c in rv[1]]
    col = {h: i for i, h in enumerate(head)}
    for row in rv.iter_rows(min_row=2, values_only=True):
        print(f"  reward #{row[col['#']]} {row[col['Character']]} {row[col['Event']]} {str(row[col['Option']])[:40]}: {row[col['Your verdict']] or '(blank)'} | {row[col['Your notes']] or ''}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["build", "read", "rewards", "read-rewards", "party", "read-party"])
    ap.add_argument("cls", nargs="+", help="class name(s); for party/read-party: --name names the workbook")
    ap.add_argument("--name", default="Party", help="party: the workbook's name (tests/<name> test script.xlsx)")
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()
    first = args.cls[0]
    {"build": lambda: build(first, args.force), "read": lambda: read(first),
     "rewards": lambda: build_rewards(first, args.force), "read-rewards": lambda: read_rewards(first),
     "party": lambda: build_party(args.cls, args.name, args.force), "read-party": lambda: read_party(args.name)}[args.command]()
