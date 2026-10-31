"""Compare two independent image readings of a class (for the classes with no other data source).

  python tools/gh2e/compare_reads.py <Class>      # tools/gh2e/sheets/<Class>.json vs tools/gh2e/sheets/<Class>.B.json

Prints every card where the readings differ in name, number, level, initiative, a half's lines or its corner icons,
after normalising case, spacing and punctuation. Each difference is then settled by looking at the image once more.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def norm(s):
    s = str(s).lower()
    s = s.replace("(! box)", "!").replace("’", "'").replace("“", '"').replace("”", '"')
    s = re.sub(r"[\s,;.]+", " ", s)
    return s.strip(' "')


def half(h):
    h = h or {}
    return [norm(x) for x in h.get("lines") or []], sorted(norm(x) for x in h.get("summary") or [])


def main(cls):
    with open(os.path.join(HERE, "sheets", cls + ".json"), encoding="utf-8") as f:
        a = json.load(f)
    with open(os.path.join(HERE, "sheets", cls + ".B.json"), encoding="utf-8") as f:
        b = json.load(f)
    bc = {norm(c["name"]): c for c in b["cards"]}
    diffs = 0
    for ca in a["cards"]:
        cb = bc.pop(norm(ca["name"]), None)
        if cb is None:
            print(f"- {ca['name']}: only in reading A")
            diffs += 1
            continue
        out = []
        for k in ("number", "level", "initiative"):
            if norm(ca.get(k)) != norm(cb.get(k)):
                out.append(f"{k}: A={ca.get(k)} B={cb.get(k)}")
        for side in ("top", "bottom"):
            la, sa = half(ca.get(side))
            lb, sb = half(cb.get(side))
            if la != lb:
                out.append(f"{side} lines:\n      A: " + " | ".join(la) + "\n      B: " + " | ".join(lb))
            if sa != sb:
                out.append(f"{side} corner: A={sa} B={sb}")
        if out:
            diffs += 1
            print(f"- {ca['name']}:")
            for o in out:
                print("    " + o)
    for name in bc:
        print(f"- {name}: only in reading B")
        diffs += 1
    print(f"{cls}: {len(a['cards'])} cards, {diffs} with differences")


if __name__ == "__main__":
    main(sys.argv[1])
