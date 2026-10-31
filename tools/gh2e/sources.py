"""Gloomhaven 2nd edition: gather every card of a class from the three sources into one file per class.

  python tools/gh2e/sources.py [Class ...]     -> tools/gh2e/sources/<Class>.json

Sources (credited in docs/plans/gloomhaven-2nd-edition.md):
  - card list + images: gloomhavencards.com (github.com/cmlenius/gloomhaven-card-browser), fetched to C:\\Claude\\GH2eData\\cards
  - structured actions: Gloomhaven Secretariat (github.com/Lurkars/gloomhavensecretariat, data/gh2e, AGPL-3.0),
    fetched to C:\\Claude\\GH2eData\\ghs
  - Gloomhaven digital YAML: Xipe's "Gloomhaven: Second Edition" Steam Workshop mod 3587771315 (local Steam folder)

The card site's list is the master index (all classes, names, levels, initiatives). GHS actions are rendered as one
readable line per top-level action; Xipe's cards are kept as their Data: block. The card image is the deciding source:
tools/gh2e/sheets/<Class>.json (written from the images) records what each card really says.
"""
import json
import os
import re
import sys

DATA = r"C:\Claude\GH2eData"
XIPE = r"C:\Program Files (x86)\Steam\steamapps\workshop\content\780290\3587771315\ModdedYML"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "sources")

# card-site code -> (our 2e class name, 1e class whose model it uses, GHS file stem, Xipe class name or None)
CLASSES = {
    "BR2": ("Bruiser", "Brute", "bruiser", "Bruiser"),
    "CH2": ("Cragheart", "Cragheart", "cragheart", "Cragheart2"),
    "MT2": ("Mindthief", "Mindthief", "mindthief", "Mindthief2"),
    "SC2": ("SilentKnife", "Scoundrel", "silent-knife", "Scoundrel2"),
    "SW2": ("Spellweaver", "Spellweaver", "spellweaver", "Spellweaver2"),
    "TI2": ("Tinkerer", "Tinkerer", "tinkerer", "Tinkerer2"),
    "BE2": ("Berserker", "Berserker", "lightning", "Berserker2"),
    "DS2": ("Doomstalker", "Doomstalker", "angry-face", None),
    "EL2": ("Elementalist", "Elementalist", "triangles", "Elementalist2"),
    "NS2": ("Nightshroud", "Nightshroud", "eclipse", None),
    "PH2": ("Plagueherald", "Plagueherald", "squidface", None),
    "QM2": ("Quartermaster", "Quartermaster", "three-spears", None),
    "SB2": ("Sawbones", "Sawbones", "saw", None),
    "SK2": ("Sunkeeper", "Sunkeeper", "sun", None),
    "SS2": ("Soothsinger", "Soothsinger", "music-note", None),
    "ST2": ("Soultether", "Summoner", "circles", None),
    "WF2": ("Wildfury", "BeastTyrant", "two-mini", None),
    # BS2 Bladeswarm: no model in Gloomhaven digital -> not ported (user 2026-10-31)
}


def key(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


LABELS = load(os.path.join(DATA, "ghs", "label", "en.json"))
try:
    LABELS_SPOILER = load(os.path.join(DATA, "ghs", "label", "spoiler", "en.json"))
except OSError:
    LABELS_SPOILER = {}


def label(path):
    """'data.custom.gh2e.bruiser.abilities.1.1' -> text, or None."""
    parts = path.split(".")
    if parts and parts[0] == "data":
        parts = parts[1:]
    for root in (LABELS, LABELS_SPOILER):
        o = root
        for p in parts:
            if isinstance(o, dict) and p in o:
                o = o[p]
            else:
                o = None
                break
        if isinstance(o, str):
            return o
    return None


def tokens(text):
    """Resolve GHS %...% placeholders into words."""
    def one(m):
        t = m.group(1)
        if t.startswith("data."):
            return tokens(label(t) or t)
        p = t.split(".")
        if p[0] == "game":
            # game.action.retaliate:2, game.action.move.valueSign:1, game.condition.stun, game.element.fire, game.itemSlot:body
            last = p[-1]
            name, _, val = last.partition(":")
            if name == "valueSign" and len(p) >= 2:
                name = p[-2] + " +"
            if p[1] == "action" and len(p) == 3:
                name = p[2].partition(":")[0]
                val = p[2].partition(":")[2]
            word = name.replace("valueSign", "").strip()
            return f"{word.capitalize()} {val}".strip() if val else word.capitalize()
        return t
    return re.sub(r"%([^%]+)%", one, text or "")


CONTAINERS = {"grid", "box", "forceBox", "concatenation", "concatenationSpacer", "fontSize", "boxFhSubActions"}


def render(a):
    t = a.get("type")
    v = a.get("value")
    vt = a.get("valueType")
    subs = [render(s) for s in (a.get("subActions") or [])]
    subs = [s for s in subs if s]
    enh = a.get("enhancementTypes")
    enh = f" [{'/'.join(enh)}]" if enh else ""
    if t in CONTAINERS:
        if t == "concatenationSpacer" or (t == "fontSize" and not subs):
            return ""
        return "(" + "; ".join(subs) + ")" if subs else ""
    if t == "custom":
        s = '"' + tokens(str(v)) + '"'
    elif t == "element":
        s = ("consume " if vt == "minus" else "infuse ") + str(v)
    elif t == "elementHalf":
        s = ("consume " if vt == "minus" else "infuse ") + str(v).replace(":", "/") + " (one of)"
    elif t == "condition":
        s = str(v).capitalize()
    elif t == "specialTarget":
        s = "target: " + str(v)
    elif t == "area":
        s = "area " + str(v)
    elif t == "card":
        s = "card: " + str(v)
    elif t == "summon":
        s = "Summon " + json.dumps(a.get("valueObject") or v, separators=(",", ":"))
    else:
        sign = {"plus": "+", "minus": "-", "add": "+"}.get(vt, "")
        s = f"{t.capitalize()} {sign}{v}".strip() if v not in (None, "") else t.capitalize()
    s += enh
    if subs:
        s += " {" + "; ".join(subs) + "}"
    return s


def ghs_card(c):
    flags = []
    for k in ("lost", "persistent", "round", "xp", "loss"):
        if c.get(k):
            flags.append(f"{k}={c[k]}" if c[k] is not True else k)
    bflags = []
    for k in ("bottomLost", "bottomPersistent", "bottomRound", "bottomXp", "bottomLoss"):
        if c.get(k):
            n = k[6:].lower()
            bflags.append(f"{n}={c[k]}" if c[k] is not True else n)
    return {
        "name": c.get("name"),
        "number": c.get("cardId"),
        "level": c.get("level"),
        "initiative": c.get("initiative"),
        "top": [r for r in (render(a) for a in c.get("actions") or []) if r],
        "top_flags": flags,
        "bottom": [r for r in (render(a) for a in c.get("bottomActions") or []) if r],
        "bottom_flags": bflags,
    }


def xipe_cards(cls):
    if not cls:
        return {}
    folder = os.path.join(XIPE, "Ability Cards")
    out = {}
    for fn in os.listdir(folder):
        m = re.match(r"(\d+)_([^_]+)_(.+)\.yml$", fn)
        if not m or m.group(2) != cls:
            continue
        with open(os.path.join(folder, fn), encoding="utf-8", errors="replace") as f:
            text = f.read()
        data = text.split("\nData:", 1)[1] if "\nData:" in text else text
        data = re.split(r"\n#?Layout:", data, maxsplit=1)[0]
        head = {k: (re.search(rf"^{k}:\s*(.+)$", text, re.M) or [None, None])[1] for k in ("Level", "Initiative", "Discard")}
        out[key(m.group(3))] = {"file": fn, **head, "data": "Data:" + data.rstrip()}
    return out


def build(code):
    name, model, stem, xcls = CLASSES[code]
    site = [c for c in load(os.path.join(DATA, "cards", "abilities.json")) if f"/{code}/" in c["image"]]
    deck_path = os.path.join(DATA, "ghs", "character", "deck", stem + ".json")
    ghs = {}
    if os.path.exists(deck_path):
        for c in load(deck_path)["abilities"]:
            ghs[key(c["name"])] = ghs_card(c)
    xipe = xipe_cards(xcls)
    cards = []
    for c in site:
        k = key(c["name"])
        cards.append({
            "name": c["name"],
            "level": c["level"],
            "initiative": c["initiative"],
            "image": os.path.join(DATA, "cards", "images", *c["image"].split("/")[-2:]),
            "ghs": ghs.pop(k, None),
            "xipe": xipe.pop(k, None),
        })
    # second pass: spelling differences between the sources ("leg fly"/"let fly", "upheavel"), checked by initiative
    import difflib
    for c in cards:
        k = key(c["name"])
        for src, pool in (("ghs", ghs), ("xipe", xipe)):
            if c[src] is None and pool:
                m = difflib.get_close_matches(k, list(pool), n=1, cutoff=0.7)
                if m:
                    cand = pool[m[0]]
                    ini = str(cand.get("initiative") or cand.get("Initiative") or "")
                    if not ini or ini == str(c["initiative"]):
                        c[src] = pool.pop(m[0])
                        c.setdefault("matched_by_spelling", []).append(f"{src}:{m[0]}")
    # third pass: a card renamed between sources ("Provoking Roar"/"Wall of Doom"): the only leftover with the same
    # initiative and level. Flagged, so the image check confirms it.
    for c in cards:
        for src, pool in (("ghs", ghs), ("xipe", xipe)):
            if c[src] is None and pool:
                same = [k2 for k2, v in pool.items()
                        if str(v.get("initiative") or v.get("Initiative") or "") == str(c["initiative"])
                        and str(v.get("level") or v.get("Level") or "") == str(c["level"])]
                if len(same) == 1:
                    c[src] = pool.pop(same[0])
                    c.setdefault("matched_by_initiative", []).append(f"{src}:{same[0]}")
    char = os.path.join(DATA, "ghs", "character", stem + ".json")
    out = {
        "class": name, "code": code, "model_from": model, "ghs_file": stem, "xipe_class": xcls,
        "character": load(char) if os.path.exists(char) else None,
        "cards": cards,
        "unmatched_ghs": sorted(ghs), "unmatched_xipe": sorted(xipe),
    }
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
    print(f"{name}: {len(cards)} cards, GHS {sum(1 for c in cards if c['ghs'])}, Xipe {sum(1 for c in cards if c['xipe'])}"
          + (f", unmatched GHS {out['unmatched_ghs']}" if out["unmatched_ghs"] else "")
          + (f", unmatched Xipe {out['unmatched_xipe']}" if out["unmatched_xipe"] else ""))


if __name__ == "__main__":
    want = sys.argv[1:]
    for code, (name, *_rest) in CLASSES.items():
        if not want or name in want or code in want:
            build(code)
