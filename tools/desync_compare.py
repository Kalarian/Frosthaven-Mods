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
3. applied actions: the order each PC applied the players' inputs in; the first place they differ
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
APPLY = re.compile(r"APPLY (\[\d+:\d+\] \S+\(\[[^\]]*\] \S+)")


class Player:
    def __init__(self, label):
        self.label = label
        self.files = {}  # name -> text

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
    header = timeline.splitlines()[0] if timeline else ""
    me = re.search(r"me=(\S+)", header)
    p = Player(f"{Path(path).stem} [{me.group(1) if me else '?'}] session {name}")
    p.files = found[name]
    return p


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

    section("3. Order of applied actions")
    orders = [[m.group(1) for m in map(APPLY.search, p.get("timeline.txt").splitlines()) if m] for p in players]
    print(f"actions applied per player: {[len(o) for o in orders]}")
    n = min(map(len, orders)) if orders else 0
    split = next((i for i in range(n) if len({o[i] for o in orders}) > 1), None)
    if split is None:
        print("same order on every PC" + (" (one PC applied more: the session continued or ended differently)" if len({len(o) for o in orders}) > 1 else ""))
    else:
        print(f"FIRST DIFFERENCE at action {split + 1}:")
        for i, o in enumerate(orders):
            print(f"   P{i + 1}: " + " | ".join(o[max(0, split - 2): split + 3]))

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
