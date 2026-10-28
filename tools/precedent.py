"""Precedent census: which shapes in a pack's converted cards no Frosthaven card has.

    python tools/precedent.py [pack files dir] [--classes A B ...] [--md OUT]

Group 2's late port bugs all had one thing in common: the card used a shape no Frosthaven card uses, so nothing in
the engine had ever run it (Wings of the Night's StartActionAbility bonus threw; Rock Slide's damage under a Create
hit the rocks' own hexes; Virulent Strain's poison under a NullTargeting parent never applies). The packcheck
catches keys the parser doesn't know; this catches keys it knows but that Frosthaven never combines that way.

Features compared, per ability (sub-abilities included):
    pair       parent kind > sub kind (+ inline / all-adjacent-to-parent markers)
    behaviour  ActiveBonus Behaviour / Type values
    targeting  kind + Targeting value
    require    StartAbilityRequirements keys
    filter     kind + Filter value
A pack half with any feature Frosthaven's own cards (extracted/Base) never show is listed: bench it alone, first,
before a full pass. Exit 0 always (a report, not a gate).
"""

import argparse
import collections
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mechanics import FH_CARDS, load_yaml  # noqa: E402
from packcheck import kind_of  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
PACK = ROOT / "mods" / "gloomhaven" / "files"
SUB_MARKERS = ("IsInlineSubAbility", "AllTargetsAdjacentToParentTargets", "AllTargetsAdjacentToParentTarget",
               "TreatAsNonSubAbility", "UseParentTiles")


def features(props, parent=None):
    """Yield (feature tuple) for an ability and its subs."""
    if not isinstance(props, dict):
        return
    kind = kind_of(props) or ("Null" if None in props or "Null" in props else "?")
    if parent is not None:
        marks = "+".join(m for m in SUB_MARKERS if props.get(m))
        yield ("pair", f"{parent} > {kind}" + (f" [{marks}]" if marks else ""))
    ab = props.get("ActiveBonus")
    if isinstance(ab, dict):
        for k in ("Behaviour", "Type"):
            if ab.get(k) is not None:
                yield ("behaviour", f"{k}: {ab[k]}")
    if props.get("Targeting") is not None:
        yield ("targeting", f"{kind} Targeting: {props['Targeting']}")
    req = props.get("StartAbilityRequirements")
    if isinstance(req, dict):
        for k in req:
            yield ("require", str(k))
    if isinstance(props.get("Filter"), str):
        yield ("filter", f"{kind} Filter: {props['Filter']}")
    for sp in (props.get("SubAbilities") or {}).values():
        yield from features(sp, kind)
    ctl = props.get("ControlActor")
    if isinstance(ctl, dict):
        for sp in (ctl.get("Abilities") or {}).values():
            yield from features(sp, "ControlActor")


def halves(path):
    d = load_yaml(path.read_text(encoding="utf-8-sig"))
    if not d or d.get("Parser") != "AbilityCard":
        return
    data = d.get("Data") or {}
    for half in ("Top", "Bottom"):
        h = data.get(half) or {}
        abilities = h.get("Abilities") if isinstance(h, dict) else None
        feats = set()
        for props in (abilities or {}).values():
            feats.update(features(props))
        yield half.lower(), feats


def perk_features(path):
    """A perk's shape: its flags and its abilities' features (Grave Defense, 2026-10-28: a perk shape the census never
    read put the game on its error screen at start-up)."""
    d = load_yaml(path.read_text(encoding="utf-8-sig"))
    if not d or d.get("Parser") != "Perk":
        return set()
    feats = set()
    flags = d.get("Flags")
    for fl in (flags if isinstance(flags, list) else str(flags or "").strip("[]").split(",")):
        if str(fl).strip():
            feats.add(("perk flag", str(fl).strip()))
    for props in (d.get("Abilities") or {}).values():
        feats.update(features(props))
    return feats


def census(files):
    seen = collections.Counter()
    for p in files:
        try:
            for _, feats in halves(p):
                seen.update(feats)
        except Exception:
            continue
    return seen


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", type=Path, nargs="?", default=PACK, help="a pack's files/ folder (default: the gloomhaven pack)")
    ap.add_argument("--classes", nargs="*", help="only these classes")
    ap.add_argument("--md", type=Path, help="also write the list as markdown")
    a = ap.parse_args()
    fh = census(sorted(FH_CARDS.glob("*/AbilityCards/*.yml")))
    rows = []
    for p in sorted((a.files / "Global" / "Characters").glob("*/AbilityCards/*.yml")):
        cls = p.parent.parent.name
        if a.classes and cls not in a.classes:
            continue
        for half, feats in halves(p):
            new = sorted(f for f in feats if f not in fh)
            if new:
                rows.append((cls, p.stem, half, new))
    fh_perks = collections.Counter()
    for p in sorted((FH_CARDS.parent / "Perks").glob("*/*.yml")):
        try:
            fh_perks.update(perk_features(p))
        except Exception:
            continue
    for p in sorted((a.files / "Global" / "Perks").glob("*/*.yml")):
        cls = p.parent.name
        if a.classes and cls not in a.classes:
            continue
        try:
            new = sorted(f for f in perk_features(p) if f not in fh_perks and f not in fh)
        except Exception as e:
            new = [("unreadable", str(e)[:60])]
        if new:
            rows.append((cls, p.stem, "perk", new))
    lines = [f"# Shapes no Frosthaven card or perk has ({len(rows)} halves and perks)", "",
             "Bench each alone before the full pass; a shape Frosthaven never runs is where Group 2's port bugs hid.", "",
             "| Class | Card | Half | New to Frosthaven |", "|---|---|---|---|"]
    for cls, card, half, new in rows:
        lines.append(f"| {cls} | {card} | {half} | {'; '.join(f'{k}: {v}' for k, v in new)} |")
    text = "\n".join(lines)
    print(text)
    if a.md:
        a.md.write_text(text + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
