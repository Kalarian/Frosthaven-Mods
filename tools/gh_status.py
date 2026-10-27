"""Port readiness of a Gloomhaven class: what the pipeline has for it and what is still missing, in one table, so a
new group starts from a checklist (2026-10-27). Reads the same inputs gh_port.py uses plus the mechanics map.

    python tools/gh_status.py <Class> [<Class> ...]      -> one block per class
    python tools/gh_status.py --all                       -> every Gloomhaven class, one line each

Rows (per class):
    extras      CLASS_EXTRAS entry (race, traits, complexity, stats)        gh_port.py
    icons       CLASS_ICONS entry (icons for its class-named actives)       gh_port.py  (needed only when a card asks)
    crossover   perks + masteries typed from Frosthaven's sheet            gh_crossover.py
    story       retirement story section                                   gh_story.py
    events      retirement events: hand-written / generated, approved n/m  gh_events.py (+ gh_events_proposed.json)
    codex       mechanics text written from the cards                      gh_codex_text.py
    tests       effects test sheet turns                                   tools/gh_tests/<Class>.json (optional)
    pack        converted into mods/gloomhaven (cards, model, sounds)      mods/gloomhaven/pack.json
    summons     summons its cards name: definitions / models               gh-card-halves + Gloomhaven's bundles
    mechanics   keys/behaviours no Frosthaven card uses, with the map's verdict (check / plugin / verified)
"""

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gh_codex_text  # noqa: E402
import gh_crossover  # noqa: E402
import gh_events  # noqa: E402
import gh_port  # noqa: E402
import gh_story  # noqa: E402
import mechanics  # noqa: E402
from mechanics_notes import NOTES  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
CARDS = ROOT / "docs" / "mechanics" / "cards.json"
PACK = ROOT / "mods" / "gloomhaven" / "pack.json"
GH_CLASSES = ["Brute", "Tinkerer", "Spellweaver", "Scoundrel", "Cragheart", "Mindthief", "Sunkeeper", "Quartermaster",
              "Summoner", "Nightshroud", "Plagueherald", "Berserker", "Soothsinger", "Doomstalker", "Sawbones",
              "Elementalist", "BeastTyrant", "Demolitionist", "Hatchet", "RedGuard", "Voidwarden"]


def fh_card_keys(cards):
    """Every key (and ActiveBonus behaviour) a Frosthaven card uses."""
    keys, behaviours = set(), set()

    def walk(ab):
        if not isinstance(ab, dict):
            return
        for k, v in ab.items():
            keys.add(str(k))
            if k == "ActiveBonus" and isinstance(v, dict) and v.get("Behaviour"):
                behaviours.add(str(v["Behaviour"]))
            if k == "SubAbilities" and isinstance(v, dict):
                for s in v.values():
                    walk(s)
    for c in cards:
        if c["game"] == "FH":
            for h in c["halves"].values():
                for ab in (h["abilities"] or {}).values():
                    walk(ab)
                for cons in ((h.get("extra") or {}).get("Consumes") or {}).values() if isinstance((h.get("extra") or {}).get("Consumes"), dict) else []:
                    walk({k: v for k, v in cons.items() if k != "Element"} if isinstance(cons, dict) else {})
    return keys, behaviours


def class_profile(cards, cls):
    """The class's keys, behaviours and summons."""
    keys, behaviours, summons = {}, {}, set()

    def walk(ab, label):
        if not isinstance(ab, dict):
            return
        for k, v in ab.items():
            k = str(k)
            if k in mechanics.COSMETIC or k.endswith("Enhancements"):
                continue
            keys.setdefault(k, label)
            if k == "ActiveBonus" and isinstance(v, dict) and v.get("Behaviour"):
                behaviours.setdefault(str(v["Behaviour"]), label)
            if k == "Summon":
                sid = v.get("ID") if isinstance(v, dict) else v
                summons.add(str(sid).removesuffix("ID"))
            if k == "SubAbilities" and isinstance(v, dict):
                for s in v.values():
                    walk(s, label)
    n = 0
    for c in cards:
        if c["game"] == "GH" and c["class"] == cls:
            n += 1
            for half, h in c["halves"].items():
                for ab in (h["abilities"] or {}).values():
                    walk(ab, f"{c['name']} {half}")
    return n, keys, behaviours, summons


def status(cls, cards, fh_keys, fh_behaviours, pack):
    rows = []
    rows.append(("extras", "yes" if cls in gh_port.CLASS_EXTRAS else "MISSING (gh_port.CLASS_EXTRAS: race, traits, complexity, stats)"))
    rows.append(("icons", "yes" if cls in gh_port.CLASS_ICONS else "none (fine unless a card names a class icon: port warns)"))
    sheet = gh_crossover.CROSSOVER.get(cls)
    rows.append(("crossover", f"{len(sheet['perks'])} perks, {len(sheet['masteries'])} masteries" if sheet else "MISSING (gh_crossover.py: type the sheet)"))
    rows.append(("story", f"section {gh_story.section_id(cls)}" if gh_story.section_id(cls) else "MISSING (gh_story.STORIES: write it; the class retires with a generic line)"))
    evs = gh_events.events(cls)
    if evs:
        opts = [o for e in evs for o in e["options"]]
        approved = sum(1 for o in opts if o.get("status") == "approved")
        src = "hand-written" if cls in gh_events.EVENTS else "generated proposal"
        rows.append(("events", f"{len(evs)} events, {approved}/{len(opts)} options approved ({src})"))
    else:
        rows.append(("events", "none (Gloomhaven has no retirement events for it)" if cls in ("Hatchet", "RedGuard", "Voidwarden", "Demolitionist") else "MISSING (python tools/gh_rewards.py)"))
    rows.append(("codex", "yes" if cls in gh_codex_text.MECHANICS else "MISSING (gh_codex_text.MECHANICS: from every card half)"))
    tests = ROOT / "tools" / "gh_tests" / f"{cls}.json"
    rows.append(("tests", f"{len(json.loads(tests.read_text(encoding='utf-8')).get('turns', []))} turns" if tests.exists() else "none (the harness plans from the pack's cards; optional)"))
    if pack and cls in pack.get("classes", []):
        model = (pack.get("model") or {}).get(cls)
        rows.append(("pack", "converted" + (", model" if model else ", NO MODEL") + f"; data_sha {pack.get('data_sha', '')[:8]}"))
    else:
        rows.append(("pack", "not in mods/gloomhaven (python tools/gh_port.py <the pack's classes> " + cls + ")"))
    n, keys, behaviours, summons = class_profile(cards, cls)
    if summons:
        have = {p.stem for p in (ROOT / "mods" / "gloomhaven" / "files" / "Global" / "HeroSummons").glob("*.yml")} if pack else set()
        rows.append(("summons", f"{len(summons)}: {', '.join(sorted(summons))}; definitions {'in the pack' if summons <= have else 'come with the port'}; MODELS: none yet (docs/reports/2026-10-27-summons.md)"))
    else:
        rows.append(("summons", "none"))
    only = []
    for k, label in sorted(keys.items()):
        if k in fh_keys or k in ("SubAbilities", "XP", "None"):
            continue
        note = NOTES.get(k)
        verdict = note[0] if note else "(no note)"
        only.append(f"{k} [{verdict}] e.g. {label}")
    for b, label in sorted(behaviours.items()):
        if b not in fh_behaviours:
            only.append(f"ActiveBonus.Behaviour={b} [{'THROWS' if b == 'StartActionAbility' else 'check'}] e.g. {label}")
    rows.append(("mechanics", f"{len(only)} no Frosthaven card uses:\n" + "\n".join("              - " + o for o in only) if only else "nothing Frosthaven's own cards don't do"))
    return n, rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("classes", nargs="*")
    ap.add_argument("--all", action="store_true")
    args = ap.parse_args()
    cards = json.loads(CARDS.read_text(encoding="utf-8"))
    fh_keys, fh_behaviours = fh_card_keys(cards)
    pack = json.loads(PACK.read_text(encoding="utf-8")) if PACK.exists() else None
    classes = GH_CLASSES if args.all or not args.classes else args.classes
    for cls in classes:
        n, rows = status(cls, cards, fh_keys, fh_behaviours, pack)
        if args.all and not args.classes:
            missing = [name for name, v in rows if v.startswith("MISSING")]
            mech = next(v for name, v in rows if name == "mechanics").split(" no Frosthaven")[0]
            print(f"{cls:14} {n:2} cards  missing: {', '.join(missing) or '-':40} GH-only mechanics: {mech}")
            continue
        print(f"\n{cls} ({n} cards)")
        for name, v in rows:
            print(f"  {name:10} {v}")


if __name__ == "__main__":
    main()
