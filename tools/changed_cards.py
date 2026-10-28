"""Which ported card halves changed their rules since a release: the halves a regression must replay.

    python tools/changed_cards.py [v0.9.0] [--classes A B ...]   -> one "Class=Card:half" line per changed half

A card file is compared half by half, rules only (its Data block: abilities, consumes, infusions, XP); a change to the
card's layout or text alone is not a rule change and is listed separately as "text only". New cards (a group ported
after the release) are left out: they get their own full pass.
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mechanics import load_yaml  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
CARDS = "mods/gloomhaven/files/Global/Characters"


def halves(text):
    d = load_yaml(text) or {}
    data = d.get("Data") or {}
    return {h.lower(): data.get(h) for h in ("Top", "Bottom")}, re.search(r"^Name:\s*\$ABILITY_CARD_(\w+)\$", text, re.M)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("since", nargs="?", default="v0.9.0")
    ap.add_argument("--classes", nargs="*")
    a = ap.parse_args()
    changed = subprocess.run(["git", "diff", "--name-only", a.since, "--", CARDS], cwd=ROOT, capture_output=True, text=True).stdout.split()
    rules, text_only = [], []
    for rel in changed:
        if "/AbilityCards/" not in rel or not rel.endswith(".yml"):
            continue
        cls = rel.split("/")[5]   # mods/gloomhaven/files/Global/Characters/<Class>/AbilityCards/<card>.yml
        if a.classes and cls not in a.classes:
            continue
        old = subprocess.run(["git", "show", f"{a.since}:{rel}"], cwd=ROOT, capture_output=True, text=True)
        if old.returncode != 0 or not (ROOT / rel).exists():
            continue   # a card new since the release, or removed
        (h_old, _), (h_new, m) = halves(old.stdout), halves((ROOT / rel).read_text(encoding="utf-8"))
        name = m.group(1) if m else Path(rel).stem
        diff = [h for h in ("top", "bottom") if h_old.get(h) != h_new.get(h)]
        if diff:
            rules += [f"{cls}={name}:{h}" for h in diff]
        else:
            text_only.append(f"{cls}={name}")
    sys.stdout.reconfigure(encoding="utf-8")
    print("\n".join(rules))
    print(f"# {len(rules)} half(s) with changed rules since {a.since}; {len(text_only)} card(s) changed text or layout only"
          + (f": {', '.join(text_only)}" if text_only else ""), file=sys.stderr)


if __name__ == "__main__":
    main()
