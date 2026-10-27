"""Port Gloomhaven digital classes into Frosthaven digital, from the player's OWN Gloomhaven install.

Nothing from Gloomhaven is stored in this repo or shipped: this script reads the player's Gloomhaven rule files
(plain zip archives under <Gloomhaven>\\Rulebase) and English text (LanguageExport.csv), converts one class to
Frosthaven's format and writes the result as the "gloomhaven" content pack, a mod folder in this repo (mods\\gloomhaven\\)
that the release zip and the installer carry like every other mod:

    mod.json                                  the mod entry (name, enabled)
    files\\Global\\Characters\\<Class>\\...     new ruleset files (added to Base.ruleset like any mod's files)
    files\\Outpost\\Sections\\...               the class's retirement story section (gh_story.py)
    strings.tsv                               key<TAB>English text (the plugin answers keys the game lacks;
                                              also <Class>_FullName, CODEX_Hero_<Class>_Title, CODEX_HeroInfo_<Class>
                                              from Gloomhaven digital's role/difficulty/play notes, the story's
                                              Event_GH.<Class>_Text, and Event__Text for a class without a story)
    art\\<Class>\\*.png                         the class's pictures in Frosthaven's shapes (gh_art.py)
    sounds\\<Class>\\*.wav                      the class's own sounds, under their Gloomhaven event names (gh_sound.py)
    bundles\\hero_<class>_assets_all.bundle     the class's 3D model, Gloomhaven's own asset bundle copied as is
    bundles\\pcg_materials_assets_*.bundle etc.  the materials of the class's ability effects (gh_vfx.py), as is
                                              (gh_model.py; 83 MB for the Brute, so git-ignored: the release carries it)
    pack.json                                 manifest: name, version, classes, data_sha (plugin + co-op check), source,
                                              "unlock" (which classes open at once, whether the rest come through
                                              retirement envelopes) and a "model" entry per class for the plugin

strings.tsv, art\\, bundles\\ and pack.json are copied into the game folder as <Frosthaven>\\FrosthavenMods\\packs\\<name>\\
(by fh_mods.py install here, by the installer on the friends' PCs through the zip's game/ files). A content pack is data
the game loads at startup: every PC needs the same one before launch, and a campaign that has used it (the
"Gloomhaven classes" house rule on, a character created) needs it for good. The author builds it from their own
Gloomhaven; the friends get the built pack with the release. See GHClasses.cs.

Conversion (see the research notes in the gloomhaven-class-port memory):
    - ability card IDs become 20000 + the Gloomhaven ID (Frosthaven uses 1-504; saves store card IDs, so this
      offset must never change); card files are renamed to match
    - Gloomhaven's plain "Enhancements: N" (Frosthaven reads it as diamond-plus) becomes the slot shape Frosthaven's
      own cards use for that ability: attack diamond, move circle, heal diamond-plus, everything else square
    - card-specific text keys ($013_B$) get a GH_ prefix so they can never meet a Frosthaven key
    - a class on Frosthaven's official crossover sheet gets that sheet's perks and masteries (gh_crossover.py) in place
      of Gloomhaven's perks; otherwise Gloomhaven's perks are converted (keys Frosthaven's parser doesn't know, such
      as IgnoreNegativeItemEffects: True, become Frosthaven perk Flags)
    - the card's Layout is rewritten in the form Frosthaven's own cards use (gh_layout.py: icons without the word,
      modifiers beside the ability, dividers between abilities, Frosthaven's active-bonus icons)
    - the "- XP: N" layout element goes (Frosthaven shows a card's experience in the corner badge, from the data)
    - a card's AnimOverload (a Gloomhaven animator state) becomes the Drifter-animator state the plugin plays the
      matching Gloomhaven clip in (ANIM_OVERLOADS); unknown names fall back to the game's default animation
    - a class with a retirement story (gh_story.py) gets Frosthaven's RetireCampaignSection and the section file, so
      its first retirement plays a story and leaves a mark on the campaign like Frosthaven's own classes do
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
import shutil
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fh_mods  # noqa: E402
import fh_ruleset as rs  # noqa: E402
import fh_sprites  # noqa: E402
import gh_crossover  # noqa: E402
import gh_layout  # noqa: E402
import gh_story  # noqa: E402
import gh_events  # noqa: E402

GH_APP_ID = "780290"
CARD_ID_OFFSET = 20000
PACK = "gloomhaven"
PACK_TITLE = "Gloomhaven classes (content pack)"
# Bump when the conversion changes in a way that matters in play (card data, IDs): co-op peers must match.
PACK_VERSION = 1
# Which classes a campaign opens the moment the pack's switch is on. The others wait in envelopes: each retirement in
# the campaign opens one, and the party picks one of two (plugin: GHEnvelopes.cs). ALL_CLASSES_OPEN hands every class
# out at once (while we test the classes themselves); set it False to deal the rest through retirements. Both go into
# data_sha, so co-op peers must agree on them like on the cards.
ALL_CLASSES_OPEN = False
STARTING_CLASSES = {"Demolitionist", "Hatchet", "RedGuard", "Voidwarden"}   # Jaws of the Lion: Gloomhaven digital's own starters
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
    # Group 1 (2026-10-25). Race and traits from the crossover sheets; complexity from Gloomhaven digital's own
    # difficulty line (Low 1, Medium 2); hero stats our estimate from its role line and the cards.
    "Scoundrel": {   # Melee Damage, Low
        "Race": "Human",
        "Traits": "[ Chaotic, Nimble, Resourceful ]",
        "Complexity": "1",
        "HeroStats": {"Melee": 6, "Ranged": 2, "Mobility": 4, "Support": 1, "Defense": 1, "Control": 2},
    },
    "Sunkeeper": {   # Melee Tank/Support, Low
        "Race": "Valrath",
        "Traits": "[ Armored, Nimble, Persuasive ]",
        "Complexity": "1",
        "ElementalAffinities": "[ Light ]",   # 29 of her card abilities infuse Light (see AFFINITY_MIN)
        "HeroStats": {"Melee": 4, "Ranged": 1, "Mobility": 2, "Support": 4, "Defense": 6, "Control": 1},
    },
    "Sawbones": {   # Melee Damage/Support, Low
        "Race": "Human",
        "Traits": "[ Educated, Persuasive, Resourceful ]",
        "Complexity": "1",
        "HeroStats": {"Melee": 4, "Ranged": 1, "Mobility": 2, "Support": 6, "Defense": 2, "Control": 2},
    },
    "Quartermaster": {   # Melee Damage/Support, Medium
        "Race": "Valrath",
        "Traits": "[ Armored, Resourceful, Strong ]",
        "Complexity": "2",
        "HeroStats": {"Melee": 5, "Ranged": 2, "Mobility": 2, "Support": 4, "Defense": 4, "Control": 2},
    },
}

# Frosthaven's element affinities (the codex and class screens' "Affinities") name the elements a class's cards infuse
# often: every Frosthaven class that lists one infuses it 4 or more times, and the classes that infuse little (Drifter,
# Trapper, Blinkblade: two each at most) list none. The port warns when CLASS_EXTRAS' ElementalAffinities and the
# converted cards disagree by that rule.
AFFINITY_MIN = 4
ELEMENTS = ("Fire", "Ice", "Air", "Earth", "Light", "Dark")


def infused_often(card_texts):
    """The elements the cards infuse AFFINITY_MIN or more times, in Frosthaven's element order."""
    counts = dict.fromkeys(ELEMENTS, 0)
    for s in card_texts:
        for m in re.finditer(r"^\s*(?:Infuse|ElementsToInfuse|InfuseElements?)\s*:\s*\[?([^\]\n#]*)", s, re.M):
            for e in m.group(1).split(","):
                if e.strip() in counts:
                    counts[e.strip()] += 1
    return [e for e in ELEMENTS if counts[e] >= AFFINITY_MIN]


# Active-bonus icons for the actives gh_layout's general mapping (Shield, Retaliate...) doesn't fit: {ability: icon},
# from the Frosthaven icon names in misc_globalsettings (ActiveAbilityDataSO)
CLASS_ICONS = {
    "Brute": {
        "BRedirect": "Positive_Redirect_Ally_Attack_Aura",   # Provoking Roar: attacks on adjacent allies target you
        "BAbilityOverrideAbilityType": "Positive_Damage_Move",  # Immovable Phalanx: moves count as attacks
    },
    # Gloomhaven's class-named actives (its class icon), by what the card does
    "Sunkeeper": {"BInfuse": "Positive_Infusion"},                       # Beacon of Light: infuse Light each turn
    "Sawbones": {"TAbilityOverrideAbilityType": "Positive_Heal"},        # Prep for Surgery: moves and attacks as heals
    "Quartermaster": {"TActive": "Refresh"},                             # Refreshment: refresh an item on each long rest
}

# Slot shape for "Enhancements: N", by the ability key it sits under, so a ported card enhances the way Frosthaven's
# own cards do (counted over Frosthaven's ~700 slots: attack diamond, move circle, heal diamond-plus; shield, retaliate,
# range, target, push and pull square; its Stun and Bane abilities diamond, Bless, Ward and Regenerate diamond-plus).
# A condition ability has no number, so a square there (+1 only) would offer nothing: negative conditions get a
# diamond, positive ones a diamond-plus.
ENHANCEMENT_SLOT = {
    "Attack": "DiamondEnhancements",
    "Move": "CircleEnhancements",
    "Heal": "DiamondPlusEnhancements",
    **dict.fromkeys(("Stun", "Immobilize", "Disarm", "Wound", "Poison", "Muddle", "Curse", "Bane"), "DiamondEnhancements"),
    **dict.fromkeys(("Bless", "Strengthen", "Invisible", "Ward", "Regenerate"), "DiamondPlusEnhancements"),
}
DEFAULT_SLOT = "SquareEnhancements"
# Frosthaven's own moves granted to an ally take a square (8 of 9)
ALLY_MOVE_SLOT = "SquareEnhancements"

# Gloomhaven perk keys Frosthaven's perk parser rejects -> Frosthaven perk flag
PERK_FLAG_KEYS = ("IgnoreNegativeItemEffects", "IgnoreNegativeScenarioEffects")

# A card's AnimOverload names a state of the hero's animator. The plugin drives a Gloomhaven model with the Drifter's
# animator (gh_model.py), so Gloomhaven state names are rewritten to the Drifter state whose clip slot gets the matching
# Gloomhaven clip: the heavy two-hand swing and the shield power-up then show. Names left out fall back to the game's
# defaults (Attack / PowerUp) exactly as an unknown name does. The shouts keep their Gloomhaven names on purpose (the
# Drifter's Push and DangerArea states play her plain cast clip): the plugin lends the roar clip to the state the game
# falls back to for that one play (GHModel.LendClip, from the model entry's gh_states).
ANIM_OVERLOADS = {
    "HeavyMeleeAttack": "Attack_AOE_Cone",
    "HeavyFatalAdvance": "Attack_AOE_Cone",
    "RadialAttack": "Attack_AOE_Radial",
    "PowerUpShield": "Persistent Buff",
    "Persistent": "Persistent Buff",      # the Sunkeeper's Divine Intervention names a state Gloomhaven itself lacks
}
ANIM_OVERLOAD_LINE = re.compile(r"^([ \t]*AnimOverload:[ \t]*)(\w+)(?=[ \t]*\r?$)", re.M)

# Cards Frosthaven can't run as Gloomhaven wrote them, changed by decision (each change was the author's choice), by the
# converted card ID: [(pattern, replacement)] on the converted file; a pattern that no longer matches stops the
# conversion, so a fix never silently stops applying. TEXT_FIXES replace the card text to match.
CARD_FIXES = {
    # Catastrophic Bomb, bottom: "you and all normal enemies within the targeted area suffer 7 damage". Frosthaven has
    # no aimed area for Damage abilities (no card of its own has one): its damage preview throws on it and the damage
    # can't be confirmed. Decided 2026-10-26: Frosthaven's own pattern instead, all adjacent normal enemies (up to six
    # hexes, where Gloomhaven's aimed area held two).
    20225: [
        (r'^([ \t]*)AreaEffect: "0,0,G\|2,0,R\|1,1,R"(\r?)$',
         lambda m: "\n".join(f"{m.group(1)}{line}{m.group(2)}" for line in
                             ("Range: 1", "Target: -1", "Targeting: Range", "AutoTrigger: True", "CanSkip: False"))),
        (r'^([ \t]*)- ColumnWidths: "65,30"\r?\n[ \t]*- Column:\r?\n[ \t]*- Row: "\$GH_225_B\$"\r?\n'
         r'[ \t]*- Row: "<size=50%> "\r?\n[ \t]*- Column: "£Area£"(?=\r?$)',
         lambda m: f'{m.group(1)}- Row: "$GH_225_B$"'),
    ],
}
TEXT_FIXES = {
    "GH_225_B": "You and all adjacent normal enemies suffer 7 damage.",
    # the ability's name, which Frosthaven looks up when it shows the ability (no card of its own gives supply cards)
    "GiveSupplyCard": "Give Medical Pack",
}


def apply_card_fixes(new_id, s):
    for pattern, repl in CARD_FIXES.get(new_id, []):
        s, n = re.subn(pattern, repl, s, flags=re.M)
        if n != 1:
            sys.exit(f"card {new_id}: a CARD_FIXES pattern matched {n} times, not once (the source changed?): {pattern[:60]}")
    return s


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
        parent = ability = None
        for j in range(i - 1, -1, -1):
            pm = re.match(r"^(\s*)(\w+):", lines[j])
            if pm and len(pm.group(1)) < indent:
                if parent is None:
                    parent = pm.group(2)
                else:
                    ability = pm.group(2)   # the ability's own name (TAllyMove), one level up
                    break
                indent = len(pm.group(1))
        slot = ENHANCEMENT_SLOT.get(parent, DEFAULT_SLOT)
        if parent == "Move" and ability and "Ally" in ability:
            slot = ALLY_MOVE_SLOT
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


def convert_anim_overloads(s):
    """Gloomhaven animator state names -> the Drifter-controller states the plugin fills with the Gloomhaven clips (see
    ANIM_OVERLOADS); each line keeps its own ending (the files are CRLF)."""
    return ANIM_OVERLOAD_LINE.sub(lambda m: m.group(1) + ANIM_OVERLOADS.get(m.group(2), m.group(2)), s)


def convert_card(name, s, warn, icons=None):
    # "ID: 417 # to 423": a supply card (the Sawbones' medical packs) notes the IDs of its copies after the ID
    m = re.search(r"^ID:\s*(\d+)\s*(?:#\s*to\s*(\d+)\s*)?$", s, re.M)
    if not m:
        raise ValueError(f"{name}: no card ID")
    new_id = CARD_ID_OFFSET + int(m.group(1))
    if m.group(2):
        s = s[:m.start(2)] + str(CARD_ID_OFFSET + int(m.group(2))) + s[m.end(2):]
    s = s[:m.start(1)] + str(new_id) + s[m.end(1):]
    # "GiveSupplyCard: 417" names a supply card by its card ID, which moves with the others: left as it was, the game
    # finds no supply card of that ID and hands over nothing (First Aid's Medical Pack never arrived)
    s = re.sub(r"^([ \t]*GiveSupplyCard:[ \t]*\[?)([0-9][0-9, ]*)",
               lambda g: g.group(1) + ", ".join(str(CARD_ID_OFFSET + int(x)) for x in re.findall(r"\d+", g.group(2))), s, flags=re.M)
    s = convert_enhancements(s, lambda w: warn(f"{name}: {w}"))
    s = convert_anim_overloads(s)
    s = CARD_TEXT_KEY.sub(lambda k: f"$GH_{k.group(1)}$", s)
    s = convert_layout(s)
    s = gh_layout.convert(s, icons, lambda w: warn(f"{name}: {w}"))
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
    masteries = gh_crossover.mastery_ids(cls)
    if masteries:
        extras = dict(extras, MasteryIDs="[ " + ", ".join(masteries) + " ]")
    section = gh_story.section_id(cls)
    if section:   # the class's own retirement story, played like Frosthaven's (gh_story.py)
        extras = dict(extras, RetireCampaignSection=section)
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
    strings_ci = {k.lower(): k for k in strings}   # Gloomhaven's card files and its English text disagree on case now and then
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
    if out_ruleset.exists():
        shutil.rmtree(out_ruleset)   # generated from scratch each run: nothing from an earlier conversion may linger
    (args.out / "mod.json").write_text(json.dumps({
        "name": PACK_TITLE,
        "description": "Gloomhaven's classes (" + ", ".join(args.classes) + ") converted from the author's own Gloomhaven; "
                       "unlocked in a campaign by the house rule of the same name.",
        "enabled": True,
    }, indent=2) + "\n", encoding="utf-8")
    written, warnings, used_keys, models, anim_names = [], [], set(), {}, set()

    def write(rel, s):
        s = fh_sprites.to_fh(s)   # Frosthaven's icons: item slots, no Aura icon, infused elements as CreateX
        for name in fh_sprites.check(s):
            warnings.append(f"{rel}: <sprite name={name}> isn't one of Frosthaven's icons (it would show as raw text)")
        for name in fh_sprites.check_icons(s):
            warnings.append(f"{rel}: active-bonus icon {name} isn't one of Frosthaven's (it would draw as a white box)")
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
        converted = []
        for n in cards:
            new_id, s = convert_card(n, text(files[n]), warnings.append, CLASS_ICONS.get(cls))
            s = apply_card_fixes(new_id, s)
            # Frosthaven can't aim a Damage ability's area (none of its own cards has one; the preview throws and
            # the damage can't be confirmed): such a card needs a CARD_FIXES entry
            for blk in re.finditer(r"^( +)\w+:\r?\n((?:\1 +.*\r?\n)+)", s.split("\nLayout:")[0], re.M):
                if re.search(r"^\s+Damage:", blk.group(2), re.M) and "AreaEffect" in blk.group(2):
                    warnings.append(f"{n}: a Damage ability with an AreaEffect: Frosthaven can't aim it (add a CARD_FIXES entry)")
            anim_names.update(m.group(2) for m in ANIM_OVERLOAD_LINE.finditer(s))
            # "ID: 20417 # to 20423": a supply card with copies (the Sawbones' medical packs). Frosthaven reads the range
            # as a comment, so one card stood for all of them and two in one hand shared an ID (the hand UI threw):
            # each copy is a card of its own, and the plugin hands out a copy nobody holds (GHSupplyCards)
            copies = re.search(r"^ID:\s*(\d+)\s*#\s*to\s*(\d+)\s*$", s, re.M)
            ids = range(int(copies.group(1)), int(copies.group(2)) + 1) if copies else [new_id]
            for cid in ids:
                if cid in fh_card_ids:
                    sys.exit(f"{n}: card ID {cid} is taken in Frosthaven")
                one = s[:copies.start()] + f"ID: {cid}" + s[copies.end():] if copies else s
                stem = re.sub(r"^\d+", str(cid), Path(n).name)
                write(f"{base}/AbilityCards/{stem}", one)
            converted.append(s)
        declared = re.findall(r"\w+", CLASS_EXTRAS[cls].get("ElementalAffinities", ""))
        often = infused_often(converted)
        if sorted(declared) != sorted(often):
            warnings.append(f"{cls}: ElementalAffinities {declared or 'none'} in CLASS_EXTRAS, but its cards infuse "
                            f"{often or 'no element'} {AFFINITY_MIN}+ times (Frosthaven's rule; see AFFINITY_MIN)")

        crossover = gh_crossover.files(cls)
        if crossover:
            # Frosthaven's own sheet for the class: its perks and masteries, not Gloomhaven's perks
            for rel, s in crossover:
                for amc in re.findall(r"AMC_\w+", s):
                    if amc not in fh_amcs:
                        warnings.append(f"{rel}: attack modifier {amc} isn't in Frosthaven")
                write(rel, s)
            perks = [rel for rel, _ in crossover if "/Perks/" in rel]
        else:
            perks = sorted(n for n, d in files.items() if n.startswith("Perk/") and
                           re.search(rf"^CharacterID:\s*{re.escape(char_id)}\s*$", text(d), re.M))
            for n in perks:
                s = convert_perk(text(files[n]))
                for amc in re.findall(r"AMC_\w+", s):
                    if amc not in fh_amcs:
                        warnings.append(f"{n}: attack modifier {amc} isn't in Frosthaven")
                write(f"Global/Perks/{cls}/{Path(n).name}", s)

        story = gh_story.files(cls)   # the retirement story: a campaign section like Frosthaven's "<Class> Retires"
        for rel, s in story:
            write(rel, s)

        print(f"{cls}: 1 character, {len(cards)} cards (IDs {CARD_ID_OFFSET + 1}..), {len(perks)} perks"
              + (f" and {len(crossover) - len(perks)} masteries from Frosthaven's crossover sheet" if crossover else " from Gloomhaven")
              + (f", retirement story (section {gh_story.section_id(cls)})" if story else ", no retirement story yet"))

        # pictures, composed into Frosthaven's shapes (see gh_art.py); optional, the plugin keeps stand-in art without
        try:
            import gh_art
        except ImportError as e:
            warnings.append(f"{cls}: no pictures ({e})")
        else:
            try:
                pictures, roles = gh_art.export(gh, cls, args.out / "art" / cls)
                print(f"{cls}: {len(pictures)} pictures from Gloomhaven's {len(roles)}")
            except Exception as e:  # noqa: BLE001 - art is cosmetic, never block the data
                warnings.append(f"{cls}: pictures failed: {e}")

        # the 3D model: Gloomhaven's own bundle plus what the plugin needs to know about it (see gh_model.py);
        # optional, the plugin keeps the Drifter's model without
        try:
            import gh_model
        except ImportError as e:
            warnings.append(f"{cls}: no 3D model ({e})")
        else:
            try:
                models[cls] = gh_model.export(gh, fh_mods.GAME, cls, args.out)
                anim = models[cls]["animation"]
                print(f"{cls}: 3D model from {models[cls]['bundle']} ({models[cls]['size'] // 1048576} MB, "
                      f"{len(models[cls]['materials'])} materials, {len(models[cls]['weapons'])} weapons, "
                      f"{len(anim['clips'])} clips swapped on the {anim['base']}'s animator)")
            except Exception as e:  # noqa: BLE001 - the model is cosmetic, never block the data
                warnings.append(f"{cls}: 3D model not copied: {e}")

        # the class's own ability effects (see gh_vfx.py); optional, the plugin keeps the Drifter's without
        if cls in models:
            try:
                import gh_vfx
            except ImportError as e:
                warnings.append(f"{cls}: no ability effects ({e})")
            else:
                try:
                    fx = gh_vfx.export(gh, cls, args.out, models[cls], warn=lambda m: warnings.append(m), wanted=anim_names)
                    models[cls].update(fx)
                    if fx:
                        mb = sum(b["size"] for b in fx["effect_bundles"]) / 1048576
                        print(f"{cls}: {len(fx['effects'])} ability effects, {len(fx['effect_materials'])} materials rebuilt "
                              f"on Frosthaven's shaders, {len(fx['effect_bundles'])} material bundles ({mb:.1f} MB)")
                except Exception as e:  # noqa: BLE001 - effects are cosmetic, never block the data
                    warnings.append(f"{cls}: ability effects not exported: {e}")

        # the names of its alternative looks, for the party screen's skin toggle ("Brute_SKIN_ALT": Boneguard)
        for skin in models.get(cls, {}).get("skins", {}):
            used_keys.add(f"{cls}_SKIN_{skin}")

        # the class's own sounds (see gh_sound.py); optional, the plugin keeps the Drifter's without
        if cls in models:
            try:
                import gh_sound
            except ImportError as e:
                warnings.append(f"{cls}: no sounds ({e})")
            else:
                try:
                    anim = models[cls]["animation"]
                    events = gh_sound.export(gh, cls, args.out, gh_sound.events_of(anim))
                    anim["gh_audio"] = {**anim.get("gh_audio", {}), **gh_sound.STATE_EVENTS.get(cls, {})}
                    models[cls]["sounds"] = events
                    print(f"{cls}: {sum(len(v) for v in events.values())} sounds for {len(events)} Gloomhaven events")
                except Exception as e:  # noqa: BLE001 - sounds are cosmetic, never block the data
                    warnings.append(f"{cls}: sounds not exported: {e}")

    table = {}
    for key in sorted(used_keys):
        gh_key = key[3:] if key.startswith("GH_") else key
        if gh_key in strings:
            table[key] = strings[gh_key]
        elif gh_key.lower() in strings_ci:   # a card file and the English text disagree on case (PrepForSurgery / PrepforSurgery)
            table[key] = strings[strings_ci[gh_key.lower()]]
    # Keys Frosthaven derives from a class ID and expects in its own text: the full class name (FHMapView), the codex
    # hero description (FHUICodexHeroEntry), both from Gloomhaven's own text; and the retirement story of a class with
    # no Frosthaven retirement section, whose key comes out as "Event_" + "" + "_Text" (our line, no Gloomhaven text).
    for cls in args.classes:
        table.update(gh_crossover.strings(cls))   # our English for the crossover perks and masteries
        table.update(gh_story.strings(cls))       # the retirement story (Event_<section>_Text and _Name)
        table.update(gh_events.strings(cls))      # its retirement events' text (<ID>_Front, _Option_<n>, _Outcome)
        if cls in strings:
            table.setdefault(f"{cls}_FullName", strings[cls])
            table.setdefault(f"CODEX_Hero_{cls}_Title", strings[cls])   # the codex page's title (GHCodex.cs)
        # Gloomhaven digital's own class guidance, from its mercenary screen. Its adventure description opens with the
        # lore paragraph (the class description for the classes without the separate one-liner: only the six starters
        # and the Elementalist have <Class>_DESCRIPTION); its role and difficulty, then any play-advice paragraphs, make
        # the codex "mechanics" text, CODEX_HeroInfo_<Class>, which the class info screen shows too.
        paragraphs = [p.strip() for p in strings.get(f"{cls}_ADVENTURE_DESCRIPTION", "").replace("\r", "").split("\n") if p.strip()]
        if paragraphs and f"{cls}_DESCRIPTION" not in strings:
            table.setdefault(f"{cls}_DESCRIPTION", paragraphs[0])
        head = [f"<b>{label}:</b> {strings[key]}" for label, key in (("Role", f"{cls}_ROLE"), ("Difficulty", f"{cls}_DIFFICULTY"))
                if strings.get(key)]
        # then its strengths and weaknesses lists (every class has both; Gloomhaven indents each "- " line with an
        # <indent=%15> tag, which Frosthaven's text doesn't read, so they become plain bullets)
        lists = []
        for label, key in (("Strengths", f"{cls}_STRENGTHS"), ("Weaknesses", f"{cls}_WEAKNESSES")):
            items = [re.sub(r"</?indent[^>]*>", "", ln).strip().lstrip("-").strip()
                     for ln in strings.get(key, "").replace("\r", "").split("\n")]
            items = [i for i in items if i]
            if items:
                lists.append(f"<b>{label}:</b>\n" + "\n".join(f"• {i}" for i in items))
        info = "\n\n".join((["\n".join(head)] if head else []) + paragraphs[1:] + lists)
        if info:
            table.setdefault(f"CODEX_HeroInfo_{cls}", info)
        elif f"{cls}_DESCRIPTION" in strings:
            table.setdefault(f"CODEX_HeroInfo_{cls}", strings[f"{cls}_DESCRIPTION"])
    table.setdefault("Event__Text", "Their personal quest complete, the mercenary leaves Frosthaven for good. "
                                    "Someone else will take up the fight.")
    table.update(TEXT_FIXES)   # card text that follows a CARD_FIXES change
    missing = sorted(k for k in used_keys if k not in table)
    table = {k: fh_sprites.to_fh(v) for k, v in table.items()}
    for k, v in table.items():
        for name in fh_sprites.check(v):
            warnings.append(f"strings.tsv {k}: <sprite name={name}> isn't one of Frosthaven's icons (it would show as raw text)")
    lines = [f"{k}\t{v.replace(chr(13), '').replace(chr(10), chr(92) + 'n')}" for k, v in table.items()]
    (args.out / "strings.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    # The pack manifest the plugin and installer read. data_sha covers everything that matters in play (the ruleset
    # files and the text), so two PCs with the same sha have identical packs whatever Gloomhaven build they came from.
    digest = hashlib.sha256()
    for rel in sorted(written, key=str.lower):
        digest.update(rel.lower().encode("utf-8") + b"\0" + (out_ruleset / rel).read_bytes() + b"\0")
    digest.update((args.out / "strings.tsv").read_bytes())
    # How the classes reach a campaign (see ALL_CLASSES_OPEN): part of what co-op peers must agree on.
    unlock = {"envelopes": not ALL_CLASSES_OPEN,
              "starting": [c for c in args.classes if ALL_CLASSES_OPEN or c in STARTING_CLASSES]}
    digest.update(json.dumps(unlock, sort_keys=True).encode("utf-8"))
    # the classes' retirement events (gh_events.py): which deck, which options, what they do
    events = [e for c in args.classes for e in gh_events.manifest(c)]
    digest.update(json.dumps(events, sort_keys=True).encode("utf-8"))
    # The "model" entries are visual only (not in data_sha): a PC without the bundle shows the Drifter's model instead.
    (args.out / "pack.json").write_text(json.dumps({
        "name": PACK, "title": PACK_TITLE, "version": PACK_VERSION, "classes": args.classes, "files": "files",
        "unlock": unlock,
        # the retirement-story sections the pack adds (gh_story.py); the plugin takes them out of a pack-free copy
        "sections": [s for s in (gh_story.section_id(c) for c in args.classes) if s],
        # the retirement events the pack adds (gh_events.py): the plugin defines them (GHEvents) and takes them out of a
        # pack-free copy
        "events": events,
        "data_sha": digest.hexdigest(), "source": {"gloomhaven": str(gh), "buildid": build, "files": len(written)},
        "model": models,
    }, indent=2) + "\n", encoding="utf-8")

    for w in warnings:
        print("warning:", w)
    if missing:
        print(f"{len(missing)} text keys not in Gloomhaven's English text either (Frosthaven may have them): "
              + ", ".join(missing))
    print(f"wrote {len(written)} files + {len(table)} strings to {args.out}")
    # the static checks (unknown ability keys, dead enhancement slots, empty halves) run on every conversion
    import subprocess
    if subprocess.call([sys.executable, str(Path(__file__).resolve().parent / "packcheck.py"), str(args.out)]) != 0:
        sys.exit("packcheck found problems in the converted pack (above)")


if __name__ == "__main__":
    main()
