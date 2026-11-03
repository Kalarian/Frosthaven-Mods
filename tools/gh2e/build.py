"""Gloomhaven 2nd edition: assemble the "virtual Gloomhaven install" that gh_port.py reads with --edition 2.

    python tools/gh2e/build.py            (all classes that have card files)
    python tools/gh2e/build.py Bruiser    (one class)

There is no digital 2nd edition, so its rule files are written here in Gloomhaven digital's own format, from:
  - tools/gh2e/cards/<Class>/*.yml   one Gloomhaven-format AbilityCard file per card (hand-written from the class's card
                                     sheet, tools/gh2e/sheets/<Class>.json; the image is the deciding source)
  - tools/gh2e/cards/<Class>/strings.tsv   key<TAB>English for every $GH2E_...$ text key those cards use
  - tools/gh2e/cards/<Class>/summons/*.yml  the class's HeroSummon files (2e stats), if any
  - tools/gh2e/classes.json          hit points and hand size (class mats)
  - the player's Gloomhaven install  the 1st edition character file of the same model (template: model, skin, companion)

Output: tools/gh2e/build/ (git-ignored), laid out like Gloomhaven's own archives (Character/, AbilityCard/,
HeroSummon/) plus strings.tsv (English for the 2e keys).

Fixed for good (saves store card and class IDs):
  - class names and IDs: CLASSES below. A class whose name the 1st edition also uses gets a "2" (Tinkerer2ID), so the
    two editions can share one ruleset (Frosthaven allows several classes per model: ModelInstanceID).
  - card IDs: 12000 + 100 x class index + the card's position in its sheet (1-based). gh_port adds 20000 (32001..).
    Gloomhaven's own cards end at 11252.
The build rewrites each card's header (ID, Character, Name, Level, Initiative) from the sheet, so a card file can't
carry a wrong number.
"""
import json
import os
import re
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
sys.path.insert(0, str(TOOLS))
import os
OUT = Path(os.environ.get("GH2E_BUILD") or HERE / "build")   # check.py gives each class its own folder
CARDS = HERE / "cards"
SHEETS = HERE / "sheets"
BASE_ID = 12000

# internal class name: (sheet name, 1st edition model, class index for card IDs, starting class)
CLASSES = {
    "Bruiser": ("Bruiser", "Brute", 0, True),
    "Cragheart2": ("Cragheart", "Cragheart", 1, True),
    "Mindthief2": ("Mindthief", "Mindthief", 2, True),
    "SilentKnife": ("SilentKnife", "Scoundrel", 3, True),
    "Spellweaver2": ("Spellweaver", "Spellweaver", 4, True),
    "Tinkerer2": ("Tinkerer", "Tinkerer", 5, True),
    "Berserker2": ("Berserker", "Berserker", 6, False),
    "Doomstalker2": ("Doomstalker", "Doomstalker", 7, False),
    "Elementalist2": ("Elementalist", "Elementalist", 8, False),
    "Nightshroud2": ("Nightshroud", "Nightshroud", 9, False),
    "Plagueherald2": ("Plagueherald", "Plagueherald", 10, False),
    "Quartermaster2": ("Quartermaster", "Quartermaster", 11, False),
    "Sawbones2": ("Sawbones", "Sawbones", 12, False),
    "Sunkeeper2": ("Sunkeeper", "Sunkeeper", 13, False),
    "Soothsinger2": ("Soothsinger", "Soothsinger", 14, False),
    "Soultether": ("Soultether", "Summoner", 15, False),
    "Wildfury": ("Wildfury", "BeastTyrant", 16, False),
}
DISPLAY = {"SilentKnife": "Silent Knife"}
GHS = Path(r"C:\Claude\Frosthaven\local\gh2e-data\ghs\character")
GHS_STEM = {"Bruiser": "bruiser", "Cragheart2": "cragheart", "Mindthief2": "mindthief", "SilentKnife": "silent-knife",
            "Spellweaver2": "spellweaver", "Tinkerer2": "tinkerer", "Berserker2": "lightning", "Doomstalker2": "angry-face",
            "Elementalist2": "triangles", "Nightshroud2": "eclipse", "Plagueherald2": "squidface",
            "Quartermaster2": "three-spears", "Sawbones2": "saw", "Sunkeeper2": "sun", "Soothsinger2": "music-note",
            "Soultether": "circles", "Wildfury": "two-mini"}


def display(cls):
    return DISPLAY.get(cls, CLASSES[cls][0])


def sheet(cls):
    return json.loads((SHEETS / f"{CLASSES[cls][0]}.json").read_text(encoding="utf-8"))


def card_id(cls, position):
    return BASE_ID + 100 * CLASSES[cls][2] + position


def card_ids(cls):
    """{card name (lower case): (ID, sheet card)}"""
    return {c["name"].lower(): (card_id(cls, i), c) for i, c in enumerate(sheet(cls)["cards"], start=1)}


def traits(cls):
    p = GHS / f"{GHS_STEM[cls]}.json"
    if not p.exists():
        return None
    t = json.loads(p.read_text(encoding="utf-8")).get("traits") or []
    return "[ " + ", ".join(x.capitalize() for x in t) + " ]" if t else None


def race(cls):
    p = GHS / f"{GHS_STEM[cls]}.json"
    return json.loads(p.read_text(encoding="utf-8")).get("characterClass", "").capitalize() if p.exists() else None


def character_file(cls, template):
    """The 2e character file: the 1st edition file of the same model with 2e ID, name, hit points and hand size."""
    mat = json.loads((HERE / "classes.json").read_text(encoding="utf-8"))["classes"][CLASSES[cls][0]]["mat"]
    keep = []
    for ln in template.replace("\r\n", "\n").split("\n"):
        k = ln.split(":", 1)[0].strip()
        if k in ("ID", "LocKey", "HealthTable", "NumberAbilityCardsInBattle", "Role", "Description", "Difficulty",
                 "Strengths", "Weaknesses", "Adventure_Description", "CompanionSummonData") or not ln.strip():
            continue
        keep.append(ln.rstrip())
    head = ["Parser: Character", f"ID: {cls}ID", f"LocKey: ${cls}$"]
    body = [ln for ln in keep if not ln.startswith("Parser:")]
    hp = mat["hp"]
    body += ["HealthTable: [ " + ", ".join(str(h) for h in hp) + " ]",
             f"NumberAbilityCardsInBattle: {mat['hand_size']}"]
    companion = (CARDS / cls / "companion.txt")
    if companion.exists():
        body.append("CompanionSummonData: " + companion.read_text(encoding="utf-8").strip())
    body.append(f"Description: ${cls}_DESCRIPTION$")
    extra = CARDS / cls / "character_extra.yml"   # more character-file keys (StartScenarioAbilities: the Plagueherald's supply)
    if extra.exists():
        body += [ln.rstrip() for ln in extra.read_text(encoding="utf-8").splitlines() if ln.strip()]
    return "\n".join(head + body) + "\n", mat


# The class mat notes (classes.json) name icons in brackets ("[Doom]", "[Lost]"); the class description and the codex
# text (CODEX_HeroInfo, built from them by gh_port) show Frosthaven's sprite where one exists, else the word.
ICON_TEXT = {
    "Doom": "<b><color=#39BEED>Doom</color></b>",   # the Doomstalker cards' own Doom markup
    "Lost": "<sprite name=Lost>", "Invisible": "<sprite name=Invisible>", "Teleport": "<sprite name=Teleport>",
    "Poison": "<sprite name=Poison>", "Stun": "<sprite name=Stun>", "Immobilize": "<sprite name=Immobilize>",
    "Disarm": "<sprite name=Disarm>", "Push": "<sprite name=Push>", "Pull": "<sprite name=Pull>",
    "Light": "<sprite name=Light>", "Recover": "<sprite name=RecoverIcon>",
    "Tear token": "Tear", "Tear hex": "Tear hex", "Plague token": "Plague", "High note": "High Note",
    "Low note": "Low Note", "Command": "COMMAND", "Spend supplies": "spend supplies", "Supplies": "supplies",
    "Quartermaster": "Quartermaster",
}


def icon_text(s):
    def one(m):
        name, before = m.group(2), s[:m.start()]
        icon = ICON_TEXT.get(name, name)
        if icon.startswith("<sprite"):
            return m.group(1) + icon
        # a word stand-in right after the same words ("Supplies [Supplies]", "high notes [High note]", "bear summon
        # [Bear]", "Doom token [Doom]"): the text already says it, drop the placeholder
        stem = lambda w: w.lower().rstrip("s")
        clause = re.split(r"[.;:!?\]]", before)[-1]   # the same clause only, and not across an earlier placeholder
        near = {stem(w) for w in re.findall(r"[A-Za-z]+", clause)[-3:]}
        if all(stem(w) in near for w in name.split()):
            return ""
        return m.group(1) + icon
    return re.sub(r"( ?)\[([A-Za-z][A-Za-z ]*)\]", one, s)


def name_key(name):
    return re.sub(r"[^A-Za-z0-9]", "", name)


HEADER_KEYS = ("Parser", "Name", "Character", "ID", "Level", "Initiative")


def card_file(cls, path, ids, warn):
    s = path.read_text(encoding="utf-8").replace("\r\n", "\n")
    m = re.search(r"^#\s*card:\s*(.+?)\s*$", s, re.M)
    name = m.group(1) if m else None
    if name is None:
        warn(f"{path.name}: no '# card: <name>' line")
        return None
    # the file's position prefix first (several sheet cards may share a name: the Sawbones' six Medical Packs), then the name
    pos = re.match(r"^(\d+)_", path.name)
    cards = sheet(cls)["cards"]
    if pos and 1 <= int(pos.group(1)) <= len(cards) and cards[int(pos.group(1)) - 1]["name"].lower() == name.lower():
        cid, c = card_id(cls, int(pos.group(1))), cards[int(pos.group(1)) - 1]
    elif name.lower() in ids:
        cid, c = ids[name.lower()]
    else:
        warn(f"{path.name}: card {name!r} is not on the {cls} sheet")
        return None
    lines = [ln for ln in s.split("\n") if ln.split(":", 1)[0] not in HEADER_KEYS]
    level = c["level"] if str(c["level"]).isdigit() or c["level"] == "X" else "X"   # Gloomhaven writes supply cards (2e "M") as X
    # Gloomhaven's own name keys ($ABILITY_CARD_EyeforanEye$): a key's text is the card's name, so a 2e card sharing a
    # 1st edition card's name shares its key harmlessly (and the test harness reads both packs alike)
    head = ["Parser: AbilityCard", f"Name: $ABILITY_CARD_{name_key(c['name'])}$", f"Character: {cls}ID", f"ID: {cid}",
            f"Level: {level}", f"Initiative: {c['initiative']}"]
    return cid, c, "\n".join(head + lines).rstrip("\n") + "\n"


def build(classes):
    import gh_port   # the player's Gloomhaven (1st edition character files as templates)
    gh, _ = gh_port.find_gloomhaven()
    files = gh_port.gh_files(gh)
    if OUT.exists():
        shutil.rmtree(OUT)
    (OUT / "Character").mkdir(parents=True)
    (OUT / "AbilityCard").mkdir()
    (OUT / "HeroSummon").mkdir()
    strings, warnings, report = {}, [], []
    for cls in classes:
        sheet_name, model, _k, _start = CLASSES[cls]
        warn = lambda w, cls=cls: warnings.append(f"{cls}: {w}")
        text, mat = character_file(cls, gh_port.text(files[f"Character/{model}.yml"]))
        (OUT / "Character" / f"{cls}.yml").write_text(text, encoding="utf-8")
        strings[cls] = display(cls)
        notes = [icon_text(n) for n in mat.get("class_notes") or []]
        if notes:
            strings[f"{cls}_DESCRIPTION"] = notes[0]
            strings[f"{cls}_ADVENTURE_DESCRIPTION"] = "\n".join(notes)
        ids = card_ids(cls)
        done = set()
        for p in sorted(q for q in (CARDS / cls).glob("*.yml") if q.name[:1].isdigit()):   # card files only (NN_Name.yml)
            r = card_file(cls, p, ids, warn)
            if r is None:
                continue
            cid, c, s = r
            if cid in done:
                warn(f"{p.name}: card {c['name']} written twice")
                continue
            done.add(cid)
            (OUT / "AbilityCard" / f"{cid}_{cls}_{re.sub(r'[^A-Za-z0-9]+', '', c['name'])}.yml").write_text(s, encoding="utf-8")
            strings[f"ABILITY_CARD_{name_key(c['name'])}"] = c["name"]
        missing = [c["name"] for i, c in enumerate(sheet(cls)["cards"], start=1) if card_id(cls, i) not in done]
        if missing:
            warn(f"{len(missing)} cards without a file: {', '.join(missing)}")
        for p in sorted((CARDS / cls / "summons").glob("*.yml")) if (CARDS / cls / "summons").exists() else []:
            # a file named like a 1st edition summon would replace that summon in the game's ruleset (both packs go into
            # Base.ruleset by path): 2e summon files must have names of their own (Decoy2.yml)
            if f"HeroSummon/{p.name}" in files:
                warn(f"summons/{p.name}: a 1st edition summon file has this name; name it after its 2e ID ({p.stem}2.yml)")
                continue
            (OUT / "HeroSummon" / p.name).write_text(p.read_text(encoding="utf-8"), encoding="utf-8")
        st = CARDS / cls / "strings.tsv"
        if st.exists():
            for ln in st.read_text(encoding="utf-8").splitlines():
                if "\t" in ln and not ln.startswith("#"):
                    k, v = ln.split("\t", 1)
                    strings[k.strip()] = v.replace("\\n", "\n")
        report.append(f"{cls}: {len(done)} of {len(sheet(cls)['cards'])} cards")
    # class resources (cards/<Class>/resources.yml, Gloomhaven's CharacterResources format: the Plagueherald's Plague tokens),
    # added to Gloomhaven's own file, which the virtual install then replaces; gh_port carries the ones cards name
    blocks = [(CARDS / c / "resources.yml").read_text(encoding="utf-8").strip() for c in classes if (CARDS / c / "resources.yml").exists()]
    if blocks:
        base = gh_port.text(files["CharacterResources/CharacterResources.yml"]).rstrip()
        (OUT / "CharacterResources").mkdir(exist_ok=True)
        (OUT / "CharacterResources" / "CharacterResources.yml").write_text(base + "\n\n" + "\n\n".join(blocks) + "\n", encoding="utf-8")
    with open(OUT / "strings.tsv", "w", encoding="utf-8") as f:
        for k, v in strings.items():
            f.write(f"{k}\t{v.replace(chr(10), chr(92) + 'n')}\n")
    for w in warnings:
        print("warning:", w)
    for r in report:
        print(r)
    return warnings


def files():
    """{archive path: bytes} of the virtual install (for gh_port)."""
    out = {}
    for p in OUT.rglob("*.yml"):
        out[p.relative_to(OUT).as_posix()] = p.read_bytes()
    return out


def build_strings():
    out = {}
    p = OUT / "strings.tsv"
    if p.exists():
        for ln in p.read_text(encoding="utf-8").splitlines():
            if "\t" in ln:
                k, v = ln.split("\t", 1)
                out[k] = v.replace("\\n", "\n")
    return out


def extras(cls):
    """Frosthaven's extra character fields for a 2e class: race and traits from Secretariat, the rest from the 1st
    edition class of the same model (complexity, hero stats; affinities are checked by gh_port's own count)."""
    import gh_port
    base = dict(gh_port.CLASS_EXTRAS.get(CLASSES[cls][1], {}))
    # the mat back, where one was read (Quartermaster, Soothsinger): complexity dots and role bars, on Frosthaven's own
    # scales (Complexity 1-5, HeroStats 0-5)
    back = json.loads((HERE / "classes.json").read_text(encoding="utf-8"))["classes"][CLASSES[cls][0]]["mat"].get("back") or {}
    m = re.match(r"\s*(\d)\s+of\s+5", str(back.get("complexity") or ""))
    if m:
        base["Complexity"] = m.group(1)
    if isinstance(back.get("role_bars_approx_0_to_5"), dict):
        # gh_port rates hero stats 0-6 and maps them onto Frosthaven's 0-5 web (HERO_STAT_TO_FH); the mat bars are
        # already 0-5, so they go in as the rating that maps back to the same bar
        to_ours = {0: 0, 1: 1, 2: 2, 3: 3, 4: 5, 5: 6}
        base["HeroStats"] = {k: to_ours[int(v)] for k, v in back["role_bars_approx_0_to_5"].items()}
    r, t = race(cls), traits(cls)
    if r:
        base["Race"] = r
    if t:
        base["Traits"] = t
    aff = HERE / "cards" / cls / "affinities.txt"
    if aff.exists():
        v = aff.read_text(encoding="utf-8").strip()
        if v:
            base["ElementalAffinities"] = v
        else:
            base.pop("ElementalAffinities", None)
    return base


def with_cards():
    return [c for c in CLASSES if (CARDS / c).exists() and any((CARDS / c).glob("*.yml"))]


if __name__ == "__main__":
    wanted = sys.argv[1:] or with_cards()
    bad = [c for c in wanted if c not in CLASSES]
    if bad:
        sys.exit(f"unknown class(es): {bad}; known: {', '.join(CLASSES)}")
    build(wanted)
