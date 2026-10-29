"""Card layout review: how Frosthaven lays out each card half, and which Frosthaven half a ported (Gloomhaven) half is
closest to, so the ports can follow Frosthaven's own layout rules.

    python tools/layout_review.py survey                 -> the layout vocabulary of Frosthaven's 1000-odd halves
    python tools/layout_review.py match <Class> [n]      -> each half of a ported class with its n closest Frosthaven halves
                                                            (abilities first, then layout), as JSON for the review page

Reads Frosthaven's extracted cards (extracted/Base/Global/Characters) and the pack's ported cards
(mods/gloomhaven/files/Global/Characters): the ported files are what the game draws, so those are compared, not
Gloomhaven's originals. Layouts are parsed with gh_layout.parse, the port's own reader.
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gh_layout  # noqa: E402
from mechanics import load_yaml  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
FH_DIR = ROOT / "extracted" / "Base" / "Global" / "Characters"
PACK_DIR = ROOT / "mods" / "gloomhaven" / "files" / "Global" / "Characters"
SHOTS = Path(r"C:\Claude\FrosthavenCardShots")

ABILITY_TYPES = {"Attack", "Move", "Heal", "Shield", "Retaliate", "Push", "Pull", "Loot", "Summon", "Infuse", "Damage",
                 "Teleport", "Create", "Trap", "Jump", "Bless", "Curse", "Strengthen", "Muddle", "Poison", "Wound",
                 "Immobilize", "Disarm", "Stun", "Invisible", "Regenerate", "Ward", "Brittle", "Bane", "Impair", "Fly",
                 "AddActiveBonus", "Recover", "Refresh", "ControlActor", "Swap", "Song", "Augment", "AddDoom", "NullHex",
                 "Null", "Pierce", "AddTarget", "RecoverDiscardedCards", "RecoverLostCards", "GiveSupplyCard",
                 "ChangeAllegiance", "Kill", "IncreaseCardLimit", "ExtraTurn", "Overheal", "MoveObstacle",
                 "DestroyObstacle", "Consume", "Choose", "RedistributeDamage", "ChooseAbility", "Teleport", "AttackersGainDisadvantage"}
CONDITIONS = {"Poison", "Wound", "Immobilize", "Disarm", "Stun", "Muddle", "Curse", "Bless", "Strengthen", "Invisible",
              "Regenerate", "Ward", "Brittle", "Bane", "Impair"}


def sections(text):
    """{'Top': [(indent, line)], 'Bottom': [...]} of the file's Layout block."""
    parts = re.split(r"^Layout:\s*$", text, maxsplit=1, flags=re.M)
    if len(parts) < 2:
        return {}
    out, cur = {}, None
    for raw in parts[1].splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        ln = raw.replace("\t", "    ")
        ind = len(ln) - len(ln.lstrip(" "))
        if ind <= 4 and re.match(r"^\s*(Top|Bottom):\s*$", ln):
            cur = ln.strip()[:-1]
            out[cur] = []
            continue
        if ind == 0:
            break
        if cur:
            out[cur].append((ind, ln.rstrip()))
    return out


def tokens(nodes, depth=0):
    """The layout as a flat list of structural tokens: element kinds with a few telling details."""
    out = []
    for n in nodes:
        k, v = n.key, n.text()
        if k == "Row" and not n.children:
            if re.fullmatch(r"\s*<size=\d+%>\s*", v):
                out.append("Spacer")
            elif "*" in v and "sprite name=" in v:
                out.append("Line(" + main_icon(v) + ")")
            elif v.startswith("$") or "$" in v:
                out.append("Text")
            else:
                out.append("Line(" + main_icon(v) + ")" if "sprite" in v else "Text")
        elif k == "Row":
            cols = [c for c in n.children if c.key == "Column"]
            widths = next((c.text() for c in n.children if c.key == "ColumnWidths"), "")
            out.append(f"Row[{len(cols)}|{widths}](" + ",".join(col_token(c) for c in cols) + ")")
        elif k in ("Column",):
            out.append(col_token(n))
        else:
            sub = [c.key for c in n.children if c.key not in ("Ability", "Icon", "ListLayout", "Layout")]
            out.append(k + ("(" + ",".join(sub) + ")" if sub else ""))
    return out


def col_token(c):
    if c.children:
        return "+".join(ch.key + (":" + main_icon(ch.text()) if "sprite" in ch.text() else "") for ch in c.children)
    v = c.text()
    if not v.strip():
        return "_"
    if "*" in v and "sprite" in v:
        return "Main(" + main_icon(v) + ")"
    return "Text" if "$" in v else ("Icons" if "sprite" in v else "T")


def main_icon(v):
    m = re.search(r"sprite name=\"?([A-Za-z0-9_+-]+)", v)
    return m.group(1) if m else "?"


def styles(nodes):
    """The text styles used: sizes and bold on main lines."""
    found = Counter()
    for n in walk(nodes):
        for m in re.finditer(r"<size=(\d+)%>", n.value):
            found["size" + m.group(1)] += 1
        if "<b>" in n.value:
            found["bold"] += 1
        if "<voffset" in n.value:
            found["voffset"] += 1
        if "<mark" in n.value:
            found["mark"] += 1
    return found


def walk(nodes):
    for n in nodes:
        yield n
        yield from walk(n.children)


def abilities(half):
    """[(type, set of details)] of a half's abilities, in order."""
    out = []
    abil = (half or {}).get("Abilities") or {}
    for name, a in abil.items():
        if not isinstance(a, dict):
            continue
        typ = next((k for k in a if k in ABILITY_TYPES), None) or next(iter(a), "?")
        det = set()
        for k, v in a.items():
            if k in ("Range",) and v not in (None, 0, 1):
                det.add("Range")
            if k == "Target" and isinstance(v, int) and (v > 1 or v < 0):
                det.add("MultiTarget")
            if k in ("Area", "AreaEffect", "AreaEffectLayoutOverride"):
                det.add("Area")
            if k in CONDITIONS and k != typ:
                det.add("Cond")
            if k in ("Push", "Pull", "Pierce", "Jump") and k != typ:
                det.add(k)
            if k == "ActiveBonus" and isinstance(v, dict):
                det.add("Bonus:" + str(v.get("Duration", "?")))
                if v.get("Tracker") or v.get("XPTracker"):
                    det.add("Tracker")
            if k == "SubAbilities":
                det.add("Sub")
            if k in ("Conditions", "NegativeConditions", "PositiveConditions"):
                det.add("Cond")
        if isinstance(a.get(typ), dict):
            for k in a[typ]:
                if k in CONDITIONS:
                    det.add("Cond")
        out.append((typ, det))
    if (half or {}).get("Consumes"):
        out.append(("Consume", set()))
    if (half or {}).get("Infuse"):
        out.append(("Infuse", set()))
    return out


def load(path, game, cls):
    text = path.read_text(encoding="utf-8-sig")
    d = load_yaml(text) or {}
    if d.get("Parser") != "AbilityCard":
        return []
    lay = sections(text)
    out = []
    for half in ("Top", "Bottom"):
        try:
            nodes = gh_layout.parse(lay.get(half, []))
        except gh_layout.Unsupported:
            nodes = []
        data = (d.get("Data") or {}).get(half) or {}
        out.append({"game": game, "class": cls, "file": path.name, "card": card_name(path, d), "half": half.lower(),
                    "abilities": [[t, sorted(det)] for t, det in abilities(data)],
                    "layout": tokens(nodes), "styles": dict(styles(nodes)),
                    "lost": "Lost" in str((d.get("Discard") or ["", ""])[0 if half == "Top" else -1]),
                    "xp": data.get("XP"), "layout_text": "\n".join(l for _, l in lay.get(half, []))})
    return out


def card_name(path, d):
    parts = path.stem.split("_", 2)
    return re.sub(r"(?<=[a-z])(?=[A-Z])", " ", parts[2] if len(parts) > 2 else path.stem)


def fh_halves():
    for p in sorted(FH_DIR.glob("*/AbilityCards/*.yml")):
        yield from load(p, "FH", p.parent.parent.name)


def pack_halves(cls):
    for p in sorted((PACK_DIR / cls / "AbilityCards").glob("*.yml")):
        yield from load(p, "GH", cls)


def similarity(a, b):
    """Abilities weigh most (the same kinds in the same order, then their details), the layout breaks ties."""
    ta, tb = [x[0] for x in a["abilities"]], [x[0] for x in b["abilities"]]
    if not ta and not tb:
        s_types = 1.0
    else:
        common = sum((Counter(ta) & Counter(tb)).values())
        s_types = common / max(len(ta), len(tb))
        if ta[:1] == tb[:1]:
            s_types += 0.25
    da = Counter(f"{t}:{d}" for t, ds in a["abilities"] for d in ds) | Counter(t for t in ta)
    db = Counter(f"{t}:{d}" for t, ds in b["abilities"] for d in ds) | Counter(t for t in tb)
    inter, union = sum((da & db).values()), sum((da | db).values()) or 1
    s_det = inter / union
    la, lb = Counter(re.sub(r"\(.*", "", t) for t in a["layout"]), Counter(re.sub(r"\(.*", "", t) for t in b["layout"])
    s_lay = sum((la & lb).values()) / (sum((la | lb).values()) or 1)
    return round(2 * s_types + 1.5 * s_det + 0.5 * s_lay, 3)


def shot(h):
    folder = SHOTS / h["class"]
    if not folder.exists():
        return None
    want = re.sub(r"[^a-z0-9]", "", h["card"].lower())
    for f in folder.glob("*.png"):
        if re.sub(r"[^a-z0-9]", "", f.stem.lower()) == want:
            return str(f)
    return None


MODS = r"Range|Target|Push|Pull|Pierce|Jump|Poison|Wound|Stun|Disarm|Immobilize|Muddle|Curse|Bless|Strengthen|Invisible|Regenerate|Ward|Brittle|Bane|Impair"


def strings():
    out = {}
    for line in (ROOT / "mods" / "gloomhaven" / "strings.tsv").read_text(encoding="utf-8").splitlines():
        k, _, v = line.partition("\t")
        out[k] = v
    return out


def card_text(h, table):
    """The half's visible words: every $key$ in its layout, looked up."""
    return " ".join(table.get(k, "") for k in re.findall(r"\$([A-Za-z0-9_/]+)\$", h["layout_text"]))


# The rules, derived from Frosthaven's own card faces (the prefab-drawn images, not its layout data, which the
# prefabs ignore). Each check returns a short note when a ported half breaks the rule.
def check(h, table):
    t, words, notes = h["layout_text"], re.sub(r"<[^>]*>", "", card_text(h, table)), []
    # a main line: the ability's own icon, value and enhancement slot (<size=130%>); anything else carrying a modifier
    # icon is a modifier line. Only AddedEffect draws the pill on a generated card (<mark> draws nothing visible)
    main_rows = [l for l in t.splitlines() if "<size=130%>" in l and re.search(r"\*\w+\*", l)
                 and re.search(r"sprite name=(Attack|Move|Heal|Shield|Retaliate|Push|Pull|Loot)\b", l)]
    mod_lines = [l for l in t.splitlines() if re.search(r"sprite name=(" + MODS + r")\b", l) and l not in main_rows]
    plain = [l for l in mod_lines if "AddedEffect" not in l]
    if len(mod_lines) > 1 and any("AddedEffect" in l for l in mod_lines) and plain:
        notes.append("R1 modifiers split between a pill and plain icons: one pill")
    elif len(plain) > 1:
        notes.append("R1 modifiers stacked on separate lines: one pill on the ability's line")
    elif plain:
        notes.append("R1 modifier as a plain icon: put it in the ability's pill")
    if re.search(r"\$Target_(All|One)", t):
        if "OneAdjacent" in t:
            notes.append("R2 'Target one adjacent enemy' printed: Frosthaven leaves the default unsaid")
        elif "AllAdjacent" in t:
            notes.append("R2 'Target all adjacent enemies' as a text line: a target pill (◎ all adjacent ☠)")
        else:
            notes.append("R2 target as a text line: check Frosthaven's wording (pill or a rider sentence)")
    if re.search(r"\benem(y|ies)\b", words, re.I):
        notes.append("R3 the word 'enemy': Frosthaven's enemy icon")
    if re.search(r"\ball(y|ies)\b", words, re.I):
        notes.append("R3 the word 'ally': Frosthaven's ally icon")
    if re.search(r"\bAdd \+\d+ Attack", words):
        notes.append("R3 'Add +N Attack': Frosthaven writes '+N<attack icon>'")
    if re.search(r"\b(POISON|WOUND|STUN|DISARM|IMMOBILIZE|MUDDLE|CURSE|BLESS|STRENGTHEN|INVISIBLE|REGENERATE)\b", words):
        notes.append("R3 condition named in capitals: the icon alone")
    if re.search(r"^\s*- Consume:", t, re.M) and "Conditional" not in t:
        notes.append("R4 consume drawn bare: Frosthaven's dashed consume box")
    if re.search(r"^\s*- Infuse:", t, re.M) and "MandatoryTrigger" not in t:
        notes.append("R5 infuse placed under the ability: Frosthaven centres the elements at the bottom of the half")
    if "$Self$" in t or re.search(r"\bself\b", words, re.I):
        notes.append("R7 'self' pill: Frosthaven outlines a self-targeted ability (to probe)")
    return notes


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "survey"
    if cmd == "check":
        table = strings()
        for h in pack_halves(sys.argv[2]):
            n = check(h, table)
            print(f"{h['card']} {h['half']}: " + ("; ".join(n) if n else "ok"))
        return
    fh = list(fh_halves())
    if cmd == "survey":
        kinds, starts, sty = Counter(), Counter(), Counter()
        for h in fh:
            for t in h["layout"]:
                kinds[re.sub(r"\(.*|\[.*", "", t)] += 1
            starts[" ".join(re.sub(r"\(.*|\[.*", "", t) for t in h["layout"][:3])] += 1
            sty.update(h["styles"])
        print(f"{len(fh)} Frosthaven halves")
        print("element kinds:", kinds.most_common(40))
        print("styles:", sty.most_common(30))
        print("first three elements:", starts.most_common(25))
        return
    if cmd == "match":
        cls, n = sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 2
        out = []
        for g in pack_halves(cls):
            ranked = sorted(fh, key=lambda f: -similarity(g, f))[:n]
            g["shot"] = shot(g)
            out.append({"port": g, "matches": [dict(f, score=similarity(g, f), shot=shot(f)) for f in ranked]})
        print(json.dumps(out, indent=1))


if __name__ == "__main__":
    main()
