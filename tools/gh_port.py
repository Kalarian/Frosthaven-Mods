"""Port Gloomhaven digital classes into Frosthaven digital, from the player's OWN Gloomhaven install.

Nothing from Gloomhaven is stored in this repo or shipped: this script reads the player's Gloomhaven rule files
(plain zip archives under <Gloomhaven>\\Rulebase) and English text (LanguageExport.csv), converts one class to
Frosthaven's format and writes the result as the "gloomhaven" content pack, a mod folder in this repo (mods\\gloomhaven\\)
that the release zip and the installer carry like every other mod:

    mod.json                                  the mod entry (name, enabled)
    files\\Global\\Characters\\<Class>\\...     new ruleset files (added to Base.ruleset like any mod's files)
    strings.tsv                               key<TAB>English text (the plugin answers keys the game lacks;
                                              also <Class>_FullName, CODEX_HeroInfo_<Class>, Event__Text)
    art\\<Class>\\*.png                         the class's pictures in Frosthaven's shapes (gh_art.py)
    pack.json                                 manifest: name, version, classes, data_sha (plugin + co-op check), source

strings.tsv, art\\ and pack.json are copied into the game folder as <Frosthaven>\\FrosthavenMods\\packs\\<name>\\ (by
fh_mods.py install here, by the installer on the friends' PCs through the zip's game/ files). A content pack is data
the game loads at startup: every PC needs the same one before launch, and a campaign that has used it (the
"Gloomhaven classes" house rule on, a character created) needs it for good. The author builds it from their own
Gloomhaven; the friends get the built pack with the release. See GHClasses.cs.

Conversion (see the research notes in the gloomhaven-class-port memory):
    - ability card IDs become 20000 + the Gloomhaven ID (Frosthaven uses 1-504; saves store card IDs, so this
      offset must never change); card files are renamed to match
    - Gloomhaven's plain "Enhancements: N" (Frosthaven reads it as diamond-plus) becomes the slot shape Frosthaven's
      own cards use for that ability: attack diamond, move circle, heal diamond-plus, everything else square
    - card-specific text keys ($013_B$) get a GH_ prefix so they can never meet a Frosthaven key
    - perk keys Frosthaven's parser doesn't know (IgnoreNegativeItemEffects: True) become Frosthaven perk Flags
    - the "- XP: N" layout element goes (Frosthaven shows a card's experience in the corner badge, from the data)
    - the character gets Frosthaven's extra fields from the official crossover sheet (race, traits). No MasteryIDs
      or ElementalAffinities until there are some: the game's YAML reader fails a whole file on an empty list ([]),
      and the plugin gives a class without masteries an empty list (null crashes the scenario results screen)

Usage:
    python tools/gh_port.py Brute [--gh <Gloomhaven folder>] [--out <dir>]
"""

import argparse
import csv
import hashlib
import io
import json
import re
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fh_mods  # noqa: E402
import fh_ruleset as rs  # noqa: E402

GH_APP_ID = "780290"
CARD_ID_OFFSET = 20000
PACK = "gloomhaven"
PACK_TITLE = "Gloomhaven classes (content pack)"
# Bump when the conversion changes in a way that matters in play (card data, IDs): co-op peers must match.
PACK_VERSION = 1
# The pack is a mod folder in the repo like any other (mod.json + files/), plus its extras (strings.tsv, art/, pack.json)
# that the release copies into the game folder under FrosthavenMods\packs\<name>\ for the plugin.
OUT = fh_mods.MODS / PACK

# Frosthaven-only character fields, per class. Race and traits from Cephalofair's crossover sheets; hero stats and
# complexity are our estimate for the class-select screen (0-6 and 1-5).
CLASS_EXTRAS = {
    "Brute": {
        "Race": "Inox",
        "Traits": "[ Armored, Intimidating, Strong ]",
        "Complexity": "1",
        "HeroStats": {"Melee": 5, "Ranged": 1, "Mobility": 3, "Support": 1, "Defense": 5, "Control": 3},
    },
}

# Slot shape for "Enhancements: N", by the ability key it sits under (from counting Frosthaven's own cards).
ENHANCEMENT_SLOT = {
    "Attack": "DiamondEnhancements",
    "Move": "CircleEnhancements",
    "Heal": "DiamondPlusEnhancements",
}
DEFAULT_SLOT = "SquareEnhancements"

# Gloomhaven perk keys Frosthaven's perk parser rejects -> Frosthaven perk flag
PERK_FLAG_KEYS = ("IgnoreNegativeItemEffects", "IgnoreNegativeScenarioEffects")

KEY_RE = re.compile(r"\$([A-Za-z0-9_]+)\$")
CARD_TEXT_KEY = re.compile(r"\$(\d{3}_[TB][A-Za-z0-9_]*)\$")


def find_gloomhaven():
    steam = fh_mods.GAME.parent.parent.parent  # ...\Steam
    libraries = [steam]
    vdf = steam / "steamapps" / "libraryfolders.vdf"
    if vdf.exists():
        for m in re.finditer(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="replace")):
            libraries.append(Path(m.group(1).replace("\\\\", "\\")))
    for lib in libraries:
        manifest = lib / "steamapps" / f"appmanifest_{GH_APP_ID}.acf"
        folder = lib / "steamapps" / "common" / "Gloomhaven"
        if manifest.exists() and (folder / "Rulebase" / "Global.ruleset").exists():
            build = re.search(r'"buildid"\s+"(\d+)"', manifest.read_text(encoding="utf-8", errors="replace"))
            return folder, build.group(1) if build else ""
    sys.exit("Gloomhaven (Steam) isn't installed on this PC; pass --gh <folder>")


def gh_files(gh):
    """Every Gloomhaven rule file, by its path inside the archives (base game, then the Jaws of the Lion DLC)."""
    files = {}
    rulebase = gh / "Rulebase"
    for archive in sorted(rulebase.rglob("*.ruleset")):
        with zipfile.ZipFile(archive) as z:
            for info in z.infolist():
                if not info.is_dir():
                    files.setdefault(info.filename, z.read(info))
    return files


def text(data):
    return data.decode("utf-8-sig")


def eol_of(s):
    return "\r\n" if "\r\n" in s else "\n"


def convert_enhancements(s, warn):
    lines = s.splitlines(keepends=True)
    for i, ln in enumerate(lines):
        m = re.match(r"^(\s*)Enhancements:(.*)$", ln.rstrip("\r\n"))
        if not m:
            continue
        indent = len(m.group(1))
        parent = None
        for j in range(i - 1, -1, -1):
            pm = re.match(r"^(\s*)([A-Za-z]+):", lines[j])
            if pm and len(pm.group(1)) < indent:
                parent = pm.group(2)
                break
        slot = ENHANCEMENT_SLOT.get(parent, DEFAULT_SLOT)
        if parent not in ENHANCEMENT_SLOT and parent not in ("Shield", "Retaliate", "Push", "Pull", "Target", "Range"):
            warn(f"enhancement under {parent!r}: using {slot}")
        lines[i] = ln.replace("Enhancements:", slot + ":", 1)
    return "".join(lines)


def convert_layout(s):
    """Gloomhaven layouts place an "- XP: N" element under an ability; Frosthaven draws that element as its XP
    tracker bar (teal pips) and shows a card's experience in the corner badge from the card data instead, as none
    of its own layouts use it. Drop the lines (an element list left empty is fine: Gloomhaven's own layouts have
    empty columns)."""
    m = re.search(r"^Layout:\s*$", s, re.M)
    if not m:
        return s
    head, layout = s[:m.end()], s[m.end():]
    layout = re.sub(r"^[ \t]*- XP:[^\n]*(\n|$)", "", layout, flags=re.M)  # also the file's last line
    return head + layout


def convert_card(name, s, warn):
    m = re.search(r"^ID:\s*(\d+)\s*$", s, re.M)
    if not m:
        raise ValueError(f"{name}: no card ID")
    new_id = CARD_ID_OFFSET + int(m.group(1))
    s = s[:m.start(1)] + str(new_id) + s[m.end(1):]
    s = convert_enhancements(s, lambda w: warn(f"{name}: {w}"))
    s = CARD_TEXT_KEY.sub(lambda k: f"$GH_{k.group(1)}$", s)
    s = convert_layout(s)
    return new_id, s


def convert_perk(s):
    eol = eol_of(s)
    flags = []
    out = []
    for ln in s.splitlines():
        m = re.match(r"^(\w+):\s*(True|False)\s*$", ln)
        if m and m.group(1) in PERK_FLAG_KEYS:
            if m.group(2) == "True":
                flags.append(m.group(1))
            continue
        out.append(ln)
    if flags:
        existing = next((i for i, ln in enumerate(out) if ln.startswith("Flags:")), None)
        if existing is None:
            out.append(f"Flags: [Passive, {', '.join(flags)}]")
        else:
            inner = out[existing].split("[", 1)[1].rsplit("]", 1)[0]
            names = [x.strip() for x in inner.split(",") if x.strip()] + flags
            out[existing] = f"Flags: [{', '.join(dict.fromkeys(names))}]"
    return eol.join(out) + eol


def convert_character(cls, s):
    extras = CLASS_EXTRAS.get(cls)
    if extras is None:
        sys.exit(f"no Frosthaven extras (race, traits...) written down for {cls} yet: add it to CLASS_EXTRAS")
    eol = eol_of(s)
    lines = s.rstrip("\r\n").splitlines()
    present = {ln.split(":", 1)[0] for ln in lines if ":" in ln and not ln.startswith((" ", "#"))}
    at = next(i for i, ln in enumerate(lines) if ln.startswith("Model:")) + 1
    todo = {k: v for k, v in extras.items() if k not in present}
    lines[at:at] = [f"{k}: {v}" for k, v in todo.items() if not isinstance(v, dict)]
    for key, value in todo.items():
        if isinstance(value, dict):  # nested blocks go last, where they can't swallow the next key
            lines.append(f"{key}:")
            lines += [f"    {k}: {v}" for k, v in value.items()]
    return eol.join(lines) + eol


def gh_strings(gh):
    with open(gh / "LanguageExport.csv", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        col = next(i for i, h in enumerate(header) if h.startswith("English"))
        return {row[0]: row[col] for row in reader if len(row) > col and row[0]}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("classes", nargs="+", help="Gloomhaven class name(s), e.g. Brute")
    ap.add_argument("--gh", type=Path, help="Gloomhaven install folder (default: found through Steam)")
    ap.add_argument("--out", type=Path, default=OUT)
    args = ap.parse_args()

    if args.gh:
        gh, build = args.gh, ""
    else:
        gh, build = find_gloomhaven()
    files = gh_files(gh)
    strings = gh_strings(gh)
    fh = rs.read_ruleset(fh_mods.vanilla_backup())
    fh_names = {e["name"].lower() for e in fh}
    fh_amcs = {Path(e["name"]).stem for e in fh if "/AttackModifiers/" in e["name"]}
    fh_card_ids = set()
    for e in fh:
        if e["parser"] == rs.PARSER_IDS["AbilityCard"]:
            m = re.search(rb"^ID:\s*(-?\d+)", e["data"], re.M)
            if m:
                fh_card_ids.add(int(m.group(1)))

    out_ruleset = args.out / "files"
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "mod.json").write_text(json.dumps({
        "name": PACK_TITLE,
        "description": "Gloomhaven's classes (" + ", ".join(args.classes) + ") converted from the author's own Gloomhaven; "
                       "unlocked in a campaign by the house rule of the same name.",
        "enabled": True,
    }, indent=2) + "\n", encoding="utf-8")
    written, warnings, used_keys = [], [], set()

    def write(rel, s):
        if rel.lower() in fh_names:
            sys.exit(f"{rel} already exists in Frosthaven's ruleset")
        if re.search(r"^[^#\n]*:\s*\[\s*\]", s, re.M):
            sys.exit(f"{rel}: empty list ([]): the game's YAML reader rejects the whole file")
        p = out_ruleset / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(s.encode("utf-8"))
        written.append(rel)
        used_keys.update(KEY_RE.findall(s))
        for m in re.finditer(r"^(?:Name|Description):\s*([A-Za-z0-9_]+)\s*$", s, re.M):
            used_keys.add(m.group(1))

    for cls in args.classes:
        char_path = f"Character/{cls}.yml"
        if char_path not in files:
            sys.exit(f"no Gloomhaven class {cls!r} (looked for {char_path})")
        base = f"Global/Characters/{cls}"
        write(f"{base}/{cls}.yml", convert_character(cls, text(files[char_path])))

        char_id = re.search(r"^ID:\s*(\S+)", text(files[char_path]), re.M).group(1)
        cards = sorted(n for n, d in files.items() if n.startswith("AbilityCard/") and
                       re.search(rf"^Character:\s*{re.escape(char_id)}\s*$", text(d), re.M))
        for n in cards:
            new_id, s = convert_card(n, text(files[n]), warnings.append)
            if new_id in fh_card_ids:
                sys.exit(f"{n}: card ID {new_id} is taken in Frosthaven")
            stem = re.sub(r"^\d+", str(new_id), Path(n).name)
            write(f"{base}/AbilityCards/{stem}", s)

        perks = sorted(n for n, d in files.items() if n.startswith("Perk/") and
                       re.search(rf"^CharacterID:\s*{re.escape(char_id)}\s*$", text(d), re.M))
        for n in perks:
            s = convert_perk(text(files[n]))
            for amc in re.findall(r"AMC_\w+", s):
                if amc not in fh_amcs:
                    warnings.append(f"{n}: attack modifier {amc} isn't in Frosthaven")
            write(f"Global/Perks/{cls}/{Path(n).name}", s)

        print(f"{cls}: 1 character, {len(cards)} cards (IDs {CARD_ID_OFFSET + 1}..), {len(perks)} perks")

        # pictures, composed into Frosthaven's shapes (see gh_art.py); optional, the plugin keeps stand-in art without
        try:
            import gh_art
        except ImportError as e:
            warnings.append(f"{cls}: no pictures ({e})")
        else:
            try:
                files, roles = gh_art.export(gh, cls, args.out / "art" / cls)
                print(f"{cls}: {len(files)} pictures from Gloomhaven's {len(roles)}")
            except Exception as e:  # noqa: BLE001 - art is cosmetic, never block the data
                warnings.append(f"{cls}: pictures failed: {e}")

    table = {}
    for key in sorted(used_keys):
        gh_key = key[3:] if key.startswith("GH_") else key
        if gh_key in strings:
            table[key] = strings[gh_key]
    # Keys Frosthaven derives from a class ID and expects in its own text: the full class name (FHMapView), the codex
    # hero description (FHUICodexHeroEntry), both from Gloomhaven's own text; and the retirement story of a class with
    # no Frosthaven retirement section, whose key comes out as "Event_" + "" + "_Text" (our line, no Gloomhaven text).
    for cls in args.classes:
        if cls in strings:
            table.setdefault(f"{cls}_FullName", strings[cls])
        if f"{cls}_DESCRIPTION" in strings:
            table.setdefault(f"CODEX_HeroInfo_{cls}", strings[f"{cls}_DESCRIPTION"])
    table.setdefault("Event__Text", "Their personal quest complete, the mercenary leaves Frosthaven for good. "
                                    "Someone else will take up the fight.")
    missing = sorted(k for k in used_keys if k not in table)
    lines = [f"{k}\t{v.replace(chr(13), '').replace(chr(10), chr(92) + 'n')}" for k, v in table.items()]
    (args.out / "strings.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    # The pack manifest the plugin and installer read. data_sha covers everything that matters in play (the ruleset
    # files and the text), so two PCs with the same sha have identical packs whatever Gloomhaven build they came from.
    digest = hashlib.sha256()
    for rel in sorted(written, key=str.lower):
        digest.update(rel.lower().encode("utf-8") + b"\0" + (out_ruleset / rel).read_bytes() + b"\0")
    digest.update((args.out / "strings.tsv").read_bytes())
    (args.out / "pack.json").write_text(json.dumps({
        "name": PACK, "title": PACK_TITLE, "version": PACK_VERSION, "classes": args.classes, "files": "files",
        "data_sha": digest.hexdigest(), "source": {"gloomhaven": str(gh), "buildid": build, "files": len(written)},
    }, indent=2) + "\n", encoding="utf-8")

    for w in warnings:
        print("warning:", w)
    if missing:
        print(f"{len(missing)} text keys not in Gloomhaven's English text either (Frosthaven may have them): "
              + ", ".join(missing))
    print(f"wrote {len(written)} files + {len(table)} strings to {args.out}")


if __name__ == "__main__":
    main()
