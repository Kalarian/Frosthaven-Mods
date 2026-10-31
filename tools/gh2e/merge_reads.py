"""Fold a checked second reading into a class sheet.

  python tools/gh2e/merge_reads.py <Class> [--take-b "Card name:top|bottom" ...]

Marks the sheet as read twice (read_by) and, for each --take-b, replaces that half's lines and corner icons with
reading B's (used after compare_reads.py showed a difference and a look at the image settled it in B's favour).
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main(cls, takes):
    pa = os.path.join(HERE, "sheets", cls + ".json")
    pb = os.path.join(HERE, "sheets", cls + ".B.json")
    with open(pa, encoding="utf-8") as f:
        a = json.load(f)
    with open(pb, encoding="utf-8") as f:
        b = json.load(f)
    bc = {c["name"].lower(): c for c in b["cards"]}
    for t in takes:
        name, side = t.rsplit(":", 1)
        ca = next(c for c in a["cards"] if c["name"].lower() == name.lower())
        ca[side] = bc[name.lower()][side]
        ca["notes"] = (ca.get("notes") or "") + f" [{side}: second reading's wording taken]"
    a["read_by"] = "two independent image readers (A, B), differences settled on the image, 2026-10-31"
    with open(pa, "w", encoding="utf-8") as f:
        json.dump(a, f, indent=1, ensure_ascii=False)
    print(cls, "merged;", len(takes), "halves from reading B")


if __name__ == "__main__":
    args = sys.argv[1:]
    takes = [args[i + 1] for i, x in enumerate(args) if x == "--take-b"]
    main(args[0], takes)
