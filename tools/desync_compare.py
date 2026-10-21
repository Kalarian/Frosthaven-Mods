"""Compare the Frosthaven Mods desync recordings of the players of one co-op session, to find where their games split.

    python tools/desync_compare.py <player1.zip or folder> <player2.zip or folder> [...] [--session N]

Each player presses Ctrl+F7 in game after a desync: it writes Frosthaven-desync-<name>-<time>.zip to their desktop
(the latest recorder sessions from <game>\\FrosthavenMods\\desync\\, coop-log.txt, Player.log). A folder can be given
instead of a zip (a session folder, or a desync folder holding sessions). By default each player's newest session is
used; --session N picks the Nth newest (1 = newest).

Report:
1. desync events each PC recorded (sync checks that failed, signals, the game's error, the game's one-line diff)
2. checkpoints: the game's state fingerprints at each turn / rest / round start, compared across players; the first
   checkpoint where they differ (important = HP, positions, conditions, cards, decks, elements; full = also the
   random number generators, whose positions are listed too)
3. scenario actions: the order each PC ran the players' inputs in, actions one PC ran and another didn't, actions the
   game's queue dropped; the first place the order differs
4. rules-engine steps: the first message where the PCs' sequences differ, with context
5. state dumps saved at the desync: the first lines that differ between players (same desync ID)
"""

import argparse
import difflib
import io
import re
import sys
import zipfile
from pathlib import Path

CHECK = re.compile(r"CHECKPOINT round=(?P<round>-?\d+) phase=(?P<phase>\S+) actor=(?P<actor>\S+) action#(?P<action>-?\d+) "
                   r"lastGameAction=(?P<last>-?\d+) important=(?P<important>\S+) full=(?P<full>\S+) rng=(?P<rng>.*)$")
# A logged game action: SENT / QUEUED / APPLY [player:n] GameAction([type] Name @ PHASE) ...
ACTION = re.compile(r" (?P<kind>SENT|QUEUED|APPLY) \[(?P<player>\d+):(?P<n>\d+)\] GameAction\(\[\d+\] (?P<name>\S+) @ (?P<phase>[^)]*)\)")
# Outpost commands: the host checks a client's command and re-sends it under its own ID, so IDs differ per PC
# (tools/coop_compare.py compares the outpost instead).
OUTPOST_ACTIONS = {"FHMultiplayerCommand"}


class Player:
    def __init__(self, label):
        self.label = label
        self.files = {}  # name -> text
        self.me = None  # player number

    def get(self, suffix):
        return next((t for n, t in self.files.items() if n.endswith(suffix)), "")


def sessions_from_zip(path):
    z = zipfile.ZipFile(path)
    found = {}
    for name in z.namelist():
        parts = name.split("/")
        if len(parts) == 3 and parts[0] == "desync":
            found.setdefault(parts[1], {})[parts[2]] = z.read(name).decode("utf-8", "replace")
    return found


def sessions_from_folder(path):
    path = Path(path)
    if (path / "timeline.txt").exists():
        return {path.name: {f.name: f.read_text("utf-8", "replace") for f in path.iterdir() if f.is_file()}}
    root = path / "desync" if (path / "desync").is_dir() else path
    return {d.name: {f.name: f.read_text("utf-8", "replace") for f in d.iterdir() if f.is_file()}
            for d in root.iterdir() if d.is_dir() and (d / "timeline.txt").exists()}


def load(path, session):
    found = sessions_from_zip(path) if zipfile.is_zipfile(path) else sessions_from_folder(path)
    if not found:
        sys.exit(f"{path}: no recorder sessions found")
    names = sorted(found, reverse=True)
    name = names[min(session - 1, len(names) - 1)]
    timeline = found[name].get("timeline.txt", "")
    # The header says me=? when the session started before joining; the PLAYER line (or any line's tag) has it then.
    me = re.search(r"me=((?:host|client)\d+)", timeline) or re.search(r"^\S+ \[((?:host|client)\d+)\]", timeline, re.M)
    p = Player(f"{Path(path).stem} [{me.group(1) if me else '?'}] session {name}")
    p.me = int(re.sub(r"\D", "", me.group(1))) if me else None
    p.files = found[name]
    return p


def applied_order(p):
    """The scenario actions in the order this PC ran them. A PC runs its own actions directly (only actions from others
    go through the game's action queue), so its own actions come from its SENT lines; an own action the host checks
    first comes back and is applied from the queue, and that APPLY is where it ran. Returns (order, dropped): order is a
    list of (id, label); dropped lists actions from others that were queued but never applied (the game's queue drops
    actions it finds out of phase)."""
    seen, sent, queued, order = set(), {}, {}, []
    lines = p.get("timeline.txt").splitlines()
    applied = {f"{m['player']}:{m['n']}" for m in map(ACTION.search, lines) if m and m["kind"] == "APPLY"}
    for line in lines:
        m = ACTION.search(line)
        if not m or m["name"] in OUTPOST_ACTIONS:
            continue
        aid, label = f"{m['player']}:{m['n']}", f"[{m['player']}:{m['n']}] {m['name']}@{m['phase']}"
        if m["kind"] == "QUEUED":
            queued.setdefault(aid, label)
        elif m["kind"] == "SENT":
            sent.setdefault(aid, label)
            if p.me is not None and int(m["player"]) == p.me and aid not in applied and aid not in seen:
                seen.add(aid)
                order.append((aid, label))
        elif aid not in seen:
            seen.add(aid)
            order.append((aid, label))
    dropped = [label for aid, label in queued.items() if aid not in seen]
    return order, dropped


def section(title):
    print("\n" + "=" * 100 + "\n" + title + "\n" + "=" * 100)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("--session", type=int, default=1)
    args = ap.parse_args()
    players = [load(p, args.session) for p in args.inputs]
    for i, p in enumerate(players):
        print(f"P{i + 1}: {p.label}")

    section("1. Desync events")
    for i, p in enumerate(players):
        events = [l for l in p.get("timeline.txt").splitlines()
                  if any(k in l for k in ("DESYNC", "GAME'S DIFF", "GAME DESYNC ERROR"))]
        print(f"P{i + 1}: {len(events)}")
        for l in events[:30]:
            print("   " + l[:300])
        for name, text in p.files.items():
            if name.startswith("game-diff"):
                print(f"   {name}:\n      " + "\n      ".join(text.splitlines()[:12]))

    section("2. Checkpoints (state fingerprints)")
    tables = []
    for p in players:
        cps = {}
        for l in p.get("timeline.txt").splitlines():
            m = CHECK.search(l)
            if m:
                cps.setdefault((int(m["round"]), m["phase"], m["actor"], int(m["action"])), m.groupdict())
        tables.append(cps)
    shared = sorted(set.intersection(*(set(t) for t in tables))) if tables else []
    print(f"checkpoints per player: {[len(t) for t in tables]}, in common: {len(shared)}")
    first = None
    for key in shared:
        rows = [t[key] for t in tables]
        diffs = [f for f in ("important", "full", "rng") if len({r[f] for r in rows if r[f] not in ("", "None")}) > 1]
        if diffs:
            first = (key, diffs, rows)
            break
    if first is None:
        print("all shared checkpoints match" if shared else "no checkpoints in common (were all players in the same scenario?)")
    else:
        (rnd, phase, actor, action), diffs, rows = first
        print(f"FIRST DIFFERENCE: round {rnd}, {phase}, {actor}, action #{action}: differs in {', '.join(diffs)}")
        for i, r in enumerate(rows):
            print(f"   P{i + 1}: important={r['important']} full={r['full']}\n        rng: {r['rng']}")
        previous = [k for k in shared if k < first[0]]
        if previous:
            print(f"   last matching checkpoint: {previous[-1]}")

    section("3. Scenario actions: the order each PC ran them in (outpost commands: see coop_compare.py)")
    if any(p.me is None for p in players):
        print("(a PC's player number is unknown: its own actions are left out)")
    results = [applied_order(p) for p in players]
    orders = [o for o, _ in results]
    print(f"actions run per player: {[len(o) for o in orders]}")
    for i, (_, dropped) in enumerate(results):
        if dropped:
            print(f"P{i + 1} QUEUED BUT NEVER RAN {len(dropped)} (dropped by the game's queue, or still queued at the end): "
                  + ", ".join(dropped[:15]))
    ids = [{aid for aid, _ in o} for o in orders]
    everywhere = set.intersection(*ids) if ids else set()
    # An action missing on one PC only matters if that PC carried on past it (otherwise its session just ended sooner).
    for i, o in enumerate(orders):
        last = max((k for k, (aid, _) in enumerate(o) if aid in everywhere), default=-1)
        only = [label for aid, label in o[:last + 1] if aid not in everywhere]
        if only:
            print(f"P{i + 1} RAN {len(only)} THAT ANOTHER PC NEVER RAN (before their last shared action): " + ", ".join(only[:15]))
    common = [[label for aid, label in o if aid in everywhere] for o in orders]
    n = len(everywhere)
    split = next((k for k in range(n) if len({c[k] for c in common}) > 1), None)
    if not n:
        print("no actions in common" if any(orders) else "no scenario actions recorded")
    elif split is None:
        print(f"the {n} actions every PC ran were run in the same order")
    else:
        print(f"ORDER DIFFERS at shared action {split + 1} of {n} (harmless if players acted at the same moment, e.g. "
              "picking cards; suspicious if the checkpoints split right after):")
        for i, c in enumerate(common):
            print(f"   P{i + 1}: " + " | ".join(c[max(0, split - 2): split + 3]))

    section("4. Rules-engine steps")
    steps = [[l for l in p.get("steps.txt").splitlines() if l.strip()] for p in players]
    print(f"steps per player: {[len(s) for s in steps]}")
    n = min(map(len, steps)) if steps else 0
    split = next((i for i in range(n) if len({s[i] for s in steps}) > 1), None)
    if split is None:
        print("same sequence on every PC (as far as the shortest one goes)")
    else:
        print(f"FIRST DIFFERENCE at step {split + 1}:")
        for i, s in enumerate(steps):
            print(f"   P{i + 1}:")
            for j in range(max(0, split - 5), min(len(s), split + 6)):
                print(f"      {'>>' if j == split else '  '} {j + 1}: {s[j]}")

    section("5. State dumps saved at the desync")
    dumps = [{re.sub(r"^state-[^-]+-", "", n): t for n, t in p.files.items() if n.startswith("state-")} for p in players]
    ids = sorted(set().union(*(set(d) for d in dumps))) if dumps else []
    if not ids:
        print("none")
    for did in ids:
        have = [(i, d[did]) for i, d in enumerate(dumps) if did in d]
        print(f"{did}: saved by {', '.join(f'P{i + 1}' for i, _ in have)}")
        for (i, a), (j, b) in zip(have, have[1:]):
            delta = [l for l in difflib.unified_diff(a.splitlines(), b.splitlines(), f"P{i + 1}", f"P{j + 1}", n=2, lineterm="")]
            print(f"   P{i + 1} vs P{j + 1}: " + ("identical" if not delta else f"{sum(1 for l in delta if l[:1] in '+-') - 2} lines differ; first:"))
            for l in delta[:40]:
                print("      " + l[:250])
    for i, p in enumerate(players):
        for name, text in p.files.items():
            if name.startswith("game-dump-"):
                print(f"P{i + 1} also has the game's own dump {name} ({len(text)} chars)")


if __name__ == "__main__":
    main()
