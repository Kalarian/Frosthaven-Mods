"""Static checks over a content pack's converted cards, before anything is installed or released.

    python tools/packcheck.py [mods/gloomhaven] [--strict]

Run by tools/gh_port.py at the end of a conversion and by tools/publish.sh before packing. Checks every ability
card file under <pack>/files/Global/Characters/*/AbilityCards:
    - every property under an ability is one Frosthaven's ability parser knows (tools/fh_ability_keys.txt, the
      case labels of ScenarioRuleLibrary.YML.AbilityData; an unknown key is silently dropped by the game)
    - a "Condition:" names an ability type the game has
    - every enhancement slot can take at least one sticker: a square takes only "+1", so one on an ability with no
      number (a condition, a loot, a control) is a dead slot nobody can use (the 2026-10-26 enhancement check found
      15 such slots shipped in v0.8.0); a hex slot needs an area effect with hexes to add
    - every half has at least one ability the game can perform (or is a rest)
    - 2nd edition pack only: every half against its printed card (tools/gh2e/sheets): enhancement mark shapes, the main
      numbers, corner XP, lost / persistent / round icons, initiative and level (printed_vs_shipped; intentional or still
      open differences only through PRINTED_EXCEPTIONS, each with its reason)
Prints one line per problem and a count; exit 1 on any problem (--strict also fails on warnings).
"""

import argparse
import json
import re
import sys
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parent.parent
KEYS = {l.strip() for l in (ROOT / "tools" / "fh_ability_keys.txt").read_text(encoding="utf-8").splitlines() if l.strip()}
# keys the converter or the pack use that the parser handles outside the ability property switch
EXTRA_KEYS = {"Condition", "Conditions", "Filter", "Target", "Range", "AnimOverload", "SubAbilities", "MiscVars", "Vars",
              "SquareEnhancements", "CircleEnhancements", "DiamondEnhancements", "DiamondPlusEnhancements",
              "HexEnhancements", "PierceEnhancements", "Enhancements"}
# ability kinds Frosthaven sells a "+1" (square) sticker for: Outpost/Enhancements/Enhancement_*.yml ForEffect values
PLUS_ONE = {"Attack", "Move", "Heal", "Range", "Target", "Shield", "Retaliate", "Pierce", "Push", "Pull", "Teleport",
            "Damage", "SummonHP", "SummonMove", "SummonAttack", "SummonRange",
            "Teleport2",   # Frosthaven's Deepwraith Skewer the Flesh has a square there
            "Trap"}   # a trap's damage (Frosthaven's Boar Catcher puts its square slot inside the Trap block)
CONDITIONS = {"Stun", "Immobilize", "Disarm", "Wound", "Poison", "Muddle", "Curse", "Brittle", "Bane", "Impair",
              "Bless", "Strengthen", "Invisible", "Regenerate", "Ward"}
ABILITY_TYPES = {
    "Move", "Attack", "Heal", "Damage", "Loot", "PreventDamage", "Shield", "Retaliate", "Trap", "Create", "RecoverLostCards",
    "Invisible", "Poison", "Wound", "Muddle", "Immobilize", "Disarm", "Curse", "Stun", "Strengthen", "Bless", "Consume",
    "Infuse", "RefreshItemCards", "RecoverDiscardedCards", "Summon", "Advantage", "Teleport", "Kill", "DisarmTrap",
    "DestroyObstacle", "DestroyTerrain", "Redirect", "AddTarget", "Push", "Pull", "AddHeal", "AddRange", "ControlActor", "AddAugment",
    "MoveObstacle", "AttackersGainDisadvantage", "AddCondition", "AddSong", "PlaySong", "AddActiveBonus", "Null",
    "ImmunityTo", "ConsumeElement", "ChooseAbility", "LoseCards", "IncreaseCardLimit", "Augment", "StopFlying", "ExtraTurn",
    "ImprovedShortRest", "Overheal", "GainXP", "AdjustInitiative", "Swap", "RedistributeDamage", "ChangeModifier",
    "Immovable", "ChangeCondition", "RemoveConditions", "ShuffleModifierDeck", "DiscardCards", "NullTargeting", "AddDoom",
    "AddDoomSlots", "TransferDooms", "Cure", "Choose", "GiveSupplyCard", "LongRest", "ShortRest", "Fear", "DeactivateSpawner",
    "RemoveActorFromMap", "ConsumeItemCards", "Invulnerability", "PierceInvulnerability", "Sleep", "ActivateSpawner",
    "Untargetable", "ItemLock", "RecoverResources", "MoveTrap", "BlockHealing", "NeutralizeShield", "ChangeCharacterModel",
    "NullHex", "HealthReduction", "ChangeAllegiance", "Revive", "DisableCardAction", "Regenerate", "Bane", "Ward", "Brittle",
    "Impair", "PlayDiscardedCard", "CreateTerrain", "ShiftForm", "MirrorDamage", "RepeatAbility", "ExploitableVulnerability",
    "DuplicateCondition", "MoveActiveBonusTracker", "RefreshItems", "GiveAbilityCardToActor",
    "MergedCreateAttack", "MergedDestroyObstacleAttack", "MergedMoveAttack", "MergedMoveObstacleAttack",
    "MergedDisarmTrapDestroyObstacles", "MergedKillCreate",
}
# the kind of an ability entry: the first property that names an ability type (the converter writes it first)
KIND_KEYS = ABILITY_TYPES | {"RefreshItems", "MergedMoveAttack", "MergedCreateAttack", "MergedKillCreate", "MergedMoveObstacleAttack",
                             "MergedDestroyObstacleAttack", "MergedDisarmTrapDestroyObstacle",
                             "AddModifiersToMonsterDeck",   # the Voidwarden's Taunting Fate (CAbilityAddModifierToMonsterDeck reads it)
                             "AddDoomSlot", "OverrideAugmentAttackType",   # Doomstalker / Mindthief actives the engine keeps
                             "Teleport2",   # Frosthaven's second teleport form (Banner Spear Hail of Spears)
                             "CreateShadow", "RemoveShadow",   # the Deathwalker's shadows (2e Nightshroud Tears)
                             "SelectAndPlayCard"}   # play a card from a pile (Frosthaven: HIVE Recall, Low Tide; 2e Spellweaver)


def kind_of(props):
    if not isinstance(props, dict):
        return None
    for k in props:
        if k in KIND_KEYS:
            return k
    if None in props:   # "Null: True": the YAML reader reads the key Null as None (2e Quartermaster)
        return "Null"
    if isinstance(props.get("Condition"), str):
        return props["Condition"]
    return None


def check_ability(path, where, props, problems, warnings):
    if not isinstance(props, dict):
        return
    kind = kind_of(props)
    for k, v in props.items():
        if k is None:
            continue   # a bare "~:" line (Gloomhaven's empty Null abilities; Frosthaven's own cards have 64 of them)
        if k not in KEYS and k not in EXTRA_KEYS and k not in ABILITY_TYPES:
            problems.append(f"{path}: {where}: unknown ability property {k!r} (the game drops it)")
        if k == "Condition":
            for c in (v if isinstance(v, list) else [v]):
                if c not in ABILITY_TYPES:
                    problems.append(f"{path}: {where}: Condition {c!r} is no ability type")
        if k in ("SquareEnhancements", "HexEnhancements", "CircleEnhancements", "DiamondEnhancements", "DiamondPlusEnhancements", "Enhancements"):
            problems.append(f"{path}: {where}: {k} sits on the ability itself; a slot belongs under its number (Attack: {{Amount, {k}}}) or is ignored")
    # slots under a number: {Amount: n, SquareEnhancements: 1}
    for k, v in props.items():
        if not isinstance(v, dict):
            continue
        for slot, n in v.items():
            if slot == "SquareEnhancements" and k not in PLUS_ONE:
                problems.append(f"{path}: {where}: a square slot on {k} (no +1 sticker exists for it: dead slot)")
            if slot == "HexEnhancements" and not isinstance(props.get("AreaEffect"), str):
                problems.append(f"{path}: {where}: a hex slot without an area effect")
            if slot == "Enhancements":
                warnings.append(f"{path}: {where}: plain 'Enhancements' on {k} (the game reads it as diamond-plus)")
    if kind is None and not any(k in props for k in ("SubAbilities", "ControlActor", "ActiveBonus", "AddActiveBonus", "Null")) and any(k is not None for k in props):
        warnings.append(f"{path}: {where}: no ability kind found (keys: {', '.join(str(k) for k in list(props)[:4])})")
    for sub, sp in (props.get("SubAbilities") or {}).items():
        check_ability(path, f"{where}/{sub}", sp, problems, warnings)
    ctl = props.get("ControlActor")
    if isinstance(ctl, dict):
        for sub, sp in (ctl.get("Abilities") or {}).items():
            check_ability(path, f"{where}/{sub}", sp, problems, warnings)


ID_LINE = re.compile(r'^\s*ID:\s*"?([A-Za-z0-9_.]+)"?\s*$', re.M)


def _ids(folders):
    """Every 'ID:' line (top level, or under a named entry of a multi-entry file) in the rule files of those folders."""
    out = set()
    for folder in folders:
        for f in Path(folder).glob("**/*.yml"):
            out.update(ID_LINE.findall(f.read_text(encoding="utf-8-sig", errors="replace")))
    return out


def references(pack, problems):
    """Every piece of data the pack's files name must exist: in Frosthaven's own rules (extracted/Base), in the pack, or
    in the mod's code. A name that resolves nowhere is found by the game only when the card is played (The Favorite,
    2026-10-28: its resource was defined in a Gloomhaven file the port never carried; the first play crashed the rules
    engine). Checked: character resources, hero summons, attack modifier cards a perk adds, and the mastery tags the
    plugin must answer."""
    base = ROOT / "extracted" / "Base" / "Global"
    files = pack / "files"
    resources = _ids([base / "Characters", files / "Global" / "Characters"])
    summons = _ids([base / "HeroSummons", files / "Global" / "HeroSummons"])
    modifiers = {p.stem for p in (base / "AttackModifiers").glob("*.yml")} | {p.stem for p in (files / "Global" / "AttackModifiers").glob("*.yml")}
    plugin = "\n".join(p.read_text(encoding="utf-8", errors="replace") for p in (ROOT / "plugins" / "FrosthavenMods" / "src").glob("*.cs"))
    resource_block = re.compile(r"^(\s*)(ResourcesToAddOnAbilityEnd|ResourcesToTakeFromTargets|ResourcesToGiveToTargets):\s*\r?\n((?:\1\s+-.*\r?\n?)+)", re.M)
    for f in sorted(files.glob("**/*.yml")):
        rel = f.relative_to(pack).as_posix()
        text = f.read_text(encoding="utf-8-sig", errors="replace")
        # the game (YMLLoading) and fh_mods read the FIRST line as "Parser: <kind>"; a comment above it makes the file
        # unreadable (2026-11-02 night: the Quartermaster's class items, fh_mods install failed "unknown parser type")
        if not text.lstrip("﻿").startswith("Parser:"):
            problems.append(f"{rel}: the first line must be 'Parser: <kind>' (the game and the installer read it there)")
        for blk in resource_block.finditer(text):
            for rid in re.findall(r'\[\s*"?([A-Za-z0-9_]+)"?\s*,', blk.group(3)):
                if rid not in resources:
                    problems.append(f"{rel}: {blk.group(2)} names resource {rid!r}, which no CharacterResources file defines (the game throws when the card makes it)")
        for lst in re.findall(r"TargetHasCharacterResource:\s*\[([^\]]*)\]", text):
            for rid in re.findall(r'"?([A-Za-z0-9_]+)"?', lst):
                if rid not in resources:
                    problems.append(f"{rel}: a filter names resource {rid!r}, which no CharacterResources file defines")
        for sid in re.findall(r"^\s*Summon:\s*([A-Za-z0-9_]+)\s*$", text, re.M):
            if not ({sid, sid + "ID", sid.removesuffix("ID")} & summons):
                problems.append(f"{rel}: summon {sid!r} has no HeroSummon definition in Frosthaven or the pack")
        for lst in re.findall(r"CardsToAdd:\s*\[([^\]]*)\]", text):
            for card in re.findall(r"[A-Za-z0-9_]+", lst):
                if card not in modifiers:
                    problems.append(f"{rel}: CardsToAdd names attack modifier {card!r}, which neither Frosthaven nor the pack defines")
        for tag in set(re.findall(r"FrosthavenMods_[A-Za-z0-9_]+", text)):
            if tag not in plugin:
                problems.append(f"{rel}: mastery tag {tag} is answered nowhere in the plugin (the mastery could never complete)")


def _modifier_kinds(path):
    """The ability kinds of an attack modifier file's Abilities block."""
    try:
        d = yaml.safe_load(path.read_text(encoding="utf-8-sig"))
    except yaml.YAMLError:
        return set()
    out = set()
    for props in ((d or {}).get("Abilities") or {}).values() if isinstance(d, dict) else []:
        k = kind_of(props)
        if k:
            out.add(k)
    return out


def modifier_abilities(pack, problems):
    """An attack modifier card runs only the ability kinds the game's modifier code knows: one it doesn't is the error
    screen the moment the card is drawn ("Unsupported attackAbility modifier ability type": the 2e Cragheart's obstacle
    modifier, 2026-11-01). Allowed: the kinds Frosthaven's own modifier cards and the 1st edition pack's (played) use."""
    # the switch in CAbilityAttack that runs a drawn modifier's abilities (decompiled ScenarioRuleLibrary; its default
    # case is the error screen). Conditions go on the attack (AbilityOverrides ... Conditions), not as abilities.
    allowed = {"Consume", "Infuse", "ModeTransfer", "Push", "Pull", "Attack", "Heal", "Damage", "Shield", "Retaliate",
               "Trap", "Strengthen", "RefreshItems", "RefreshItemCards", "AddActiveBonus", "NullTargeting", "Choose",
               "NullHex", "Brittle", "CreateTerrain", "Teleport2", "MoveActiveBonusTracker",
               "CreateLimitedHazardousTerrain", "Kill", "ControlActor"}
    # kinds the plugin runs after the attack on a pack's own card (AMC_GH2E_..., GHModifierAbilities.cs, 2026-11-02)
    plugin_run = {"Create", "CreateShadow", "GiveSupplyCard", "RecoverDiscardedCards", "Ward", "Bless", "Invisible"}
    for f in sorted((pack / "files" / "Global" / "AttackModifiers").glob("*.yml")):
        if f.stem.startswith("AMC_GH2E_"):
            allowed_here = allowed | plugin_run
        else:
            allowed_here = allowed
        # a Choose is allowed on a modifier (Frosthaven's AMC_PushOrPull2_R), but our own options inside one were never
        # proven: the 2e Sedation's self-Disarm option stopped the game ("Invalid targeting type", 2026-11-01)
        if "Choose" in _modifier_kinds(f):
            problems.append(f"{f.relative_to(pack).as_posix()}: a Choose on a pack's own attack modifier: an unproven shape "
                            f"(one stopped the game when drawn); use a Frosthaven modifier, or bench it first")
        for k in sorted(_modifier_kinds(f) - allowed_here):
            problems.append(f"{f.relative_to(pack).as_posix()}: a {k} ability on an attack modifier card: no modifier of "
                            f"Frosthaven's runs one (the game stops with an error when the card is drawn)")


# ---------------------------------------------------------------------------------------------------------------------
# Printed vs shipped (2nd edition pack only): every card half's printed sheet (tools/gh2e/sheets/<Sheet>.json, read from
# the card images; the deciding source) against the shipped YAML. Compared per half:
#   - enhancement marks: the multiset of (stat, shape) printed ("Attack 3 [square]", "Strengthen [diamond+]", hex marks
#     in an area line) == the slots the shipped half carries (SquareEnhancements under Attack, ...; "E" hexes in an
#     AreaEffect). Conditions count as one stat ("Condition"); summon stat lines are left to the summon files.
#   - numbers: every printed "Attack 3" / "Move 4" / "Range 2" / "Target 2" / "Pierce 1" / "Push 2" ... (plain, not "+1")
#     must appear with that value somewhere in the shipped half, and every shipped number that carries a slot must be a
#     printed one;
#   - XP in the corner (half-level XP), lost / unrecoverable (Discard entry), persistent / round icons (a bonus of that
#     duration in the half), initiative and level (the card header).
# An intentional difference goes in PRINTED_EXCEPTIONS with the reason; nothing else passes.
PRINTED_STATS = ("attack", "move", "heal", "shield", "retaliate", "range", "target", "pierce", "push", "pull", "teleport", "loot")
PRINTED_CONDITIONS = ("stun", "immobilize", "disarm", "wound", "poison", "muddle", "curse", "brittle", "bane", "impair",
                      "bless", "strengthen", "invisible", "regenerate", "ward", "safeguard")
SHAPES = {"square": "square", "circle": "circle", "diamond": "diamond", "diamond+": "diamond+", "hex": "hex"}
SLOT_SHAPE = {"SquareEnhancements": "square", "CircleEnhancements": "circle", "DiamondEnhancements": "diamond",
              "DiamondPlusEnhancements": "diamond+", "HexEnhancements": "hex", "PierceEnhancements": "square"}
SHIPPED_STAT = {"Attack": "Attack", "Move": "Move", "Heal": "Heal", "Shield": "Shield", "Retaliate": "Retaliate",
                "Range": "Range", "RetaliateRange": "Range", "Target": "Target", "Pierce": "Pierce", "Push": "Push",
                "Pull": "Pull", "Teleport": "Teleport", "Teleport2": "Teleport", "Loot": "Loot",
                "Trap": "Damage"}   # a trap's damage: its slot sits in the Trap block (Boar Catcher)
# (card ID, "Top"/"Bottom", what) -> why the shipped card differs on purpose. 'what' is the problem's kind: "marks",
# "number <Stat> <value>", "slot-number <Stat> <value>", "xp", "pile", "persistent", "round".
PRINTED_EXCEPTIONS = {
    # --- intentional (the shipped number or shape is right for the engine) ---
    (32129, "Top", "number Attack 1"):     # Wave of Destruction: the stored base is 2 so printed 1 + distance comes out
        "Frosthaven's TargetDistanceRangeDiff gives distance - 1 for a melee area; base 2 makes 1 + distance (NOTES 29)",
    (33522, "Top", "number Heal 1"):       # Living Mountain: "at the end of each of this summon's turns it performs: Heal 1"
        "the summon's own end-of-turn Heal 1 lives in its summon file (summons/RockColossus2.yml), not on the card",
    (32530, "Top", "number Pull 2"):       # Gravity Bomb: the token performs the pull at the end of your next turn
        "the token's Pull 2 / damage 5 / damage 1 live in its summon file (summons/GravityBomb.yml, GHMapTokens.cs), not on the card",
    (33228, "Bottom", "round"):            # Secret of the Oak: the +4 Attack / +4 Move "that round" choice
        "the plugin's template BSecretChoice (GHSawbones2e) holds one-use round bonuses: printed 'that round', not a round icon",
    (32612, "Top", "number Heal 1"):       # Growing Rage: "whenever you retaliate, perform Heal 1, self"
        "the Heal 1 after each retaliate is performed by the plugin (GHRetaliate2e, the card's GH2E_GrowingRage marker)",
    (32717, "Bottom", "number Attack 1"):  # Walking Bastion: "grant that summon: Attack 1, Pierce 2" after its attack
        "the granted Attack 1, Pierce 2 is built by the plugin (GHDoomstalker2e WalkingBastion), not a card ability",
    (32717, "Bottom", "number Pierce 2"):
        "the granted Attack 1, Pierce 2 is built by the plugin (GHDoomstalker2e WalkingBastion), not a card ability",
    # --- OPEN rows (ALL.json): the printed rule needs a plugin system another task builds; remove the line when it ships ---
    (32327, "Top", "persistent"): "OPEN SilentKnife-11 Hired Help: the money-token cost and token are not built",
    (33416, "Bottom", "round"): "OPEN Soothsinger2-21 Soothing Lullaby: the token on the monster is not built",
    (33429, "Bottom", "round"): "OPEN Soothsinger2-26 Grand Finale: 'skip moving your token this round' is not built",
}


def _printed_items(lines):
    """(marks Counter of (stat, shape), plain numbers set of (stat, value), unparsed mark texts) from a half's sheet lines."""
    from collections import Counter
    marks, numbers, unparsed = Counter(), set(), []
    for line in lines:
        s = line.strip()
        if s.lower().startswith("slots:"):
            continue
        if re.match(r"^Summon\b", s) or " trait" in s:   # summon stats: the summon file's slots
            continue
        if re.search(r"\barea( becomes)?:", s):   # an area picture: its hex marks only
            m = re.search(r"\[(hex marks?[^\]]*)\]", s)
            if m:
                coords = re.findall(r"-?\d+,-?\d+", m.group(1))
                word = re.search(r"\b(both|two|three|four|\d) ", m.group(1))
                count = {"both": 2, "two": 2, "three": 3, "four": 4}.get(word.group(1), None) if word else None
                if word and word.group(1).isdigit():
                    count = int(word.group(1))
                marks[("Hex", "hex")] += len(coords) or count or (1 + m.group(1).count(" and ") if m.group(1).startswith("hex marks") else 1)
            continue
        # a mark inside rule text ("Create two obstacles within range 3 [square].")
        for q in re.findall(r'"[^"]*"', s):
            for m in re.finditer(rf"\b({'|'.join(PRINTED_STATS + ('damage',))})\s+(\d+)\s*\[(square|circle|diamond\+?|hex)\]", q, re.I):
                marks[(m.group(1).capitalize(), SHAPES[m.group(3)])] += 1
                numbers.add((m.group(1).capitalize(), int(m.group(2))))
        s = re.sub(r'"[^"]*"', "", s)               # quoted rule text
        s = re.sub(r"\([^)]*\)", "", s)             # remarks "(! box)", "(split icon)"
        for seg in re.split(r"[,:;]", s):
            seg = seg.strip().lstrip("!").strip()
            if not seg:
                continue
            mark = re.search(r"\[(square|circle|diamond\+?|hex)\]", seg)
            m = re.match(rf"^({'|'.join(PRINTED_STATS)})\s+([+-]?)(\d+|X)\b", seg, re.I)
            if m:
                stat = m.group(1).capitalize()
                if mark:
                    marks[(stat, SHAPES[mark.group(1)])] += 1
                if not m.group(2) and m.group(3).isdigit():
                    numbers.add((stat, int(m.group(3))))
                continue
            c = re.match(rf"^({'|'.join(PRINTED_CONDITIONS)})\b", seg, re.I)
            if c and mark:
                marks[("Condition", SHAPES[mark.group(1)])] += 1
            elif mark:
                unparsed.append(seg)
    return marks, numbers, unparsed


def _shipped_items(half):
    """(marks Counter, numbers set, slot numbers set) of a shipped half (its abilities, consumes and nested blocks)."""
    from collections import Counter
    marks, numbers, slotted = Counter(), set(), set()

    def walk(node, key=None):
        if isinstance(node, dict):
            if key in SHIPPED_STAT or key in CONDITIONS or key == "Safeguard":
                stat = SHIPPED_STAT.get(key, "Condition")
                amount = node.get("Damage") if key == "Trap" else node.get("Amount")
                has_slot = False
                for slot, shape in SLOT_SHAPE.items():
                    n = node.get(slot)
                    if isinstance(n, int) and n > 0:
                        st = "Pierce" if slot == "PierceEnhancements" else stat
                        marks[(st, shape)] += n
                        if slot == "PierceEnhancements" and isinstance(node.get("Pierce"), int):
                            slotted.add(("Pierce", node["Pierce"]))
                        elif slot != "PierceEnhancements":
                            has_slot = True
                if isinstance(amount, int) and stat != "Condition":
                    numbers.add((stat, amount))
                    if has_slot:
                        slotted.add((stat, amount))
            area = node.get("AreaEffect")
            if isinstance(area, str) and ",E" in area:
                marks[("Hex", "hex")] += sum(1 for h in area.split("|") if h.strip().endswith(",E"))
            for k, v in node.items():
                if k in SHIPPED_STAT and isinstance(v, int) and not isinstance(v, bool):
                    numbers.add((SHIPPED_STAT[k], v))
                if isinstance(k, str) and k.startswith("Summon"):
                    continue   # summon stats live in the summon file
                walk(v, k)
        elif isinstance(node, list):
            for v in node:
                walk(v, key)
    walk({k: v for k, v in half.items() if k != "XP"})
    numbers.add(("Target", 1))   # an ability without a Target value has one target
    return marks, numbers, slotted


# ability kinds that stay in play by themselves (Frosthaven draws their persistent icon from the kind, not a duration)
PERSISTENT_KINDS = {"Summon", "AddDoom", "AddAugment", "Augment", "AddSong", "PlaySong"}


def _durations(node, out, skip=()):
    """The bonus durations in a block; keys in skip are not entered (a triggered ability's own bonus, inside an
    AddActiveBonus or a doom/bonus AbilityData, is what the trigger does, not the card's icon)."""
    if isinstance(node, dict):
        # the Safeguard stand-in (AUTHORING.md: a prevent-a-negative-condition bonus) is a condition kept until used,
        # printed without a persistent icon
        safeguard = node.get("Behaviour") == "GenericOnNegativeCondition" and len(node.get("XPTracker") or [0]) == 1 and \
            (isinstance(node.get("Vars"), dict) and node["Vars"].get("PreventCondition") or "GH2E_SAFEGUARD" in str(node.get("ListLayoutOverride")))
        hidden = node.get("Hidden") is True   # a hidden bookkeeping bonus (the Soothsinger's once-a-round Note marker)
        for k, v in node.items():
            if k in ("Duration", "ActiveBonusDuration") and isinstance(v, str) and not safeguard and not hidden:
                out.add(v)
            if k in PERSISTENT_KINDS:
                out.add("Persistent")
            if k == "ControlDurationType" and v == "ControlForOneTurn":   # "this round, during that enemy's turn"
                out.add("Round")
            if k == "ImprovedShortRest":   # "if you short rest at the end of this round": a round effect by itself
                out.add("Round")
            if k not in skip:
                _durations(v, out, skip)
    elif isinstance(node, list):
        for v in node:
            _durations(v, out, skip)
    return out


def _pack_name(pack):
    try:
        return json.loads((pack / "pack.json").read_text(encoding="utf-8")).get("name")
    except (OSError, ValueError):
        return None


def printed_vs_shipped(pack, problems, warnings):
    """The 2nd edition pack's shipped cards against the printed sheets (see the block comment above)."""
    sys.path.insert(0, str(ROOT / "tools" / "gh2e"))
    import build as gh2e
    by_index = {v[2]: (cls, v[0]) for cls, v in gh2e.CLASSES.items()}
    sheets, unparsed_total, used = {}, 0, set()
    for f in sorted((pack / "files" / "Global" / "Characters").glob("*/AbilityCards/*.yml")):
        rel = f.relative_to(pack).as_posix()
        try:
            d = yaml.safe_load(f.read_text(encoding="utf-8-sig"))
        except yaml.YAMLError:
            continue
        cid = d.get("ID") if isinstance(d, dict) else None
        if not isinstance(cid, int) or not 32000 <= cid < 34000:
            continue
        cls, sheet_name = by_index.get((cid - 32000) // 100, (None, None))
        pos = (cid - 32000) % 100
        if cls is None:
            problems.append(f"{rel}: card ID {cid} belongs to no 2nd edition class")
            continue
        if sheet_name not in sheets:
            sheets[sheet_name] = json.loads((ROOT / "tools" / "gh2e" / "sheets" / f"{sheet_name}.json").read_text(encoding="utf-8"))["cards"]
        cards = sheets[sheet_name]
        if not 1 <= pos <= len(cards):
            problems.append(f"{rel}: card ID {cid}: no card {pos} on the {sheet_name} sheet")
            continue
        c = cards[pos - 1]
        where = f"{rel} ({c['name']})"

        def problem(half, what, text):
            key = (cid, half, what)
            if key in PRINTED_EXCEPTIONS:
                used.add(key)
                return
            problems.append(f"{where}: {half}: printed vs shipped: {text}")
        if d.get("Initiative") != c["initiative"]:
            problem("card", "initiative", f"initiative printed {c['initiative']}, shipped {d.get('Initiative')}")
        # the engine takes a number or X only: a printed "M" (Medical Packs) ships as X. "Level: M" broke the outpost map
        # (FHMapView.InitUI NullReferenceException, no Edit Party: 2026-11-02 23:30)
        lvl = str(c["level"]) if str(c["level"]).isdigit() else "X"
        if not (str(d.get("Level")).isdigit() or str(d.get("Level")) == "X"):
            problems.append(f"{where}: card: Level {d.get('Level')!r} is not a number or X (the game's outpost map fails to build)")
        elif str(d.get("Level")) != lvl:
            problem("card", "level", f"level printed {c['level']}, shipped {d.get('Level')}")
        discard = d.get("Discard") or []
        if isinstance(discard, str):
            discard = [x.strip() for x in discard.strip("[]").split(",")]
        for i, half in enumerate(("Top", "Bottom")):
            sh = c[half.lower()]
            h = (d.get("Data") or {}).get(half) or {}
            summary = [s.lower() for s in sh.get("summary") or []]
            # the pile
            want = "Permanently Lost" if any(s in ("unrecoverable", "permanently lost") for s in summary) else \
                "Lost" if "lost" in summary else "Discard"
            got = discard[i].strip() if i < len(discard) else "Discard"
            if got != want:
                problem(half, "pile", f"printed {want}, shipped {got}")
            # XP in the corner
            xp = next((int(m.group(1)) for s in summary for m in [re.match(r"xp (\d+)$", s)] if m), 0)
            # (a half whose first ability is a required consume pays its corner XP with the consume: Gust Sigil)
            first = next(iter((h.get("Abilities") or {}).values()), None) if isinstance(h.get("Abilities"), dict) else None
            req = first.get("Consume") if isinstance(first, dict) and isinstance(first.get("Consume"), dict) \
                and (sh.get("lines") or [""])[0].startswith("! consume") else {}
            got_xp = h.get("XP") or req.get("XP") or 0
            if got_xp != xp:
                problem(half, "xp", f"XP printed {xp}, shipped {got_xp}")
            # persistent / round icons
            # (an icon inside a consume box is printed on its line and shipped inside the half's Consumes)
            durs = _durations(h, set())
            main_durs = _durations({k: v for k, v in h.items() if k != "Consumes"}, set(), skip=("AddActiveBonus", "AbilityData", "Consume"))
            for dur in ("persistent", "round"):
                printed = dur in summary or any(f"{dur} icon" in ln for ln in sh.get("lines") or [])
                if printed and dur.capitalize() not in durs:
                    problem(half, dur, f"{dur} icon printed, no {dur.capitalize()} bonus shipped")
                elif not printed and dur.capitalize() in main_durs:
                    problem(half, dur, f"{dur} icon not printed, a {dur.capitalize()} bonus shipped")
            # marks and numbers
            pm, pn, unparsed = _printed_items(sh.get("lines") or [])
            sm, sn, slotted = _shipped_items(h)
            unparsed_total += len(unparsed)
            if pm != sm:
                fmt = lambda cnt: ", ".join(f"{s} {sh_} x{n}" if n > 1 else f"{s} {sh_}" for (s, sh_), n in sorted(cnt.items())) or "none"
                problem(half, "marks", f"enhancement marks printed [{fmt(pm)}], shipped [{fmt(sm)}]")
            for stat, v in sorted(pn):
                if (stat, v) not in sn:
                    others = sorted(x for s, x in sn if s == stat)
                    problem(half, f"number {stat} {v}", f"{stat} {v} printed, shipped {stat} {others or 'none'}")
            for stat, v in sorted(slotted):
                if (stat, v) not in pn:
                    problem(half, f"slot-number {stat} {v}", f"shipped {stat} {v} carries an enhancement slot but no printed {stat} {v}")
    for key in sorted(set(PRINTED_EXCEPTIONS) - used, key=str):
        warnings.append(f"printed-vs-shipped exception {key} matched nothing (stale: remove it)")
    if unparsed_total:
        warnings.append(f"printed-vs-shipped: {unparsed_total} printed mark(s) in a form the check can't read")


def check_pack(pack):
    problems, warnings, n = [], [], 0
    for f in sorted((pack / "files" / "Global" / "Characters").glob("*/AbilityCards/*.yml")):
        n += 1
        rel = f.relative_to(pack).as_posix()
        try:
            d = yaml.safe_load(f.read_text(encoding="utf-8-sig"))
        except yaml.YAMLError as e:
            problems.append(f"{rel}: not readable as YAML: {str(e)[:80]}")
            continue
        if not isinstance(d, dict) or d.get("Parser") != "AbilityCard":
            problems.append(f"{rel}: not an AbilityCard file")
            continue
        # an area picture as a text value: the card generator can't localise it and the game stops at load with "an error
        # within one of the Rule Libraries" (Shield of the Desert's consume, 2026-10-30); an area is a Row of its own
        for m in re.finditer(r'^\s*(Text|Layout|ListLayout): "[^"]*£', f.read_text(encoding="utf-8-sig"), re.M):
            problems.append(f"{rel}: an area (£...£) inside a {m.group(1)} text: the card can't be drawn (make it a Row)")
        # the dashed consume box (Conditional) looks up each of its rows as text: only a text reward may go in it
        def boxed(node):
            if isinstance(node, dict):
                for it in (node.get("Conditional") or {}).get("Layout") or [] if isinstance(node.get("Conditional"), dict) else []:
                    if isinstance(it, dict) and isinstance(it.get("Consume"), dict) and not isinstance(it["Consume"].get("Text"), str):
                        problems.append(f"{rel}: a consume box (Conditional) around a reward that isn't text (the game stops at load)")
                    # a value of an ability ("*BCDarkCurse.Range*") isn't found from the box (Suggestion, 2026-10-30)
                    elif isinstance(it, dict) and isinstance(it.get("Consume"), dict) and re.search(r"\*[A-Za-z]\w*(\.\w+)?\*", it["Consume"]["Text"]):
                        problems.append(f"{rel}: a consume box (Conditional) whose text names an ability value (the game stops at load)")
                for v in node.values():
                    boxed(v)
            elif isinstance(node, list):
                for v in node:
                    boxed(v)
        boxed(d.get("Layout"))
        for half in ("Top", "Bottom"):
            h = (d.get("Data") or {}).get(half)
            if not isinstance(h, dict):
                if not d.get("SupplyCard"):
                    warnings.append(f"{rel}: no {half} data")
                continue
            abilities = h.get("Abilities") or {}
            if not abilities and not h.get("LongRest") and not h.get("ShortRest"):
                warnings.append(f"{rel}: {half} has no abilities")
            # the half's first ability must wait for the player's confirm (undo, confirm, skip), as Frosthaven's own do;
            # AutoTrigger plays it on the click (Supportive Chant bottom, Winged Congregation top: the user, 2026-10-28)
            first = next(iter(abilities.values()), None) if isinstance(abilities, dict) else None
            if isinstance(first, dict) and first.get("AutoTrigger") is True:
                problems.append(f"{rel}: {half}/{next(iter(abilities))}: AutoTrigger on the half's first ability (played on the click, no confirm, undo or skip)")
            # an attack or damage on "every enemy moved through" reads the move's path only when its Targeting isn't
            # Range (the default), and a merged move-attack picks its targets before the walk: only the enemy beside the
            # start or end hex is hit (Trample bottom, the user 2026-10-28)
            def path_targets(node, merged=False):
                if not isinstance(node, dict):
                    return
                for k, v in node.items():
                    if isinstance(v, dict):
                        if v.get("AllTargetsOnMovePath") is True and ("Attack" in v or "Damage" in v):
                            if merged:
                                problems.append(f"{rel}: {half}/{k}: a path attack inside a merged move-attack (targets picked before the walk)")
                            elif v.get("Targeting", "Range") == "Range":
                                problems.append(f"{rel}: {half}/{k}: AllTargetsOnMovePath with Range targeting (only adjacent enemies are hit): add Targeting: All")
                        path_targets(v, merged or k.startswith("MergedMove"))
            path_targets(abilities)
            # a consume's abilities hung on a Null placeholder that comes after a ControlActor in the half: switching the
            # element on after the controlled figure acted stopped the game (RefreshConsumeBar NullReference, the 2e
            # Arresting March, 2026-11-01); the placeholder goes before the control. Since the plugin's GHConsumeOnNull
            # guard loads (the public RefreshConsumeBar overload, commit 423dade) the crash can't happen: the shape is
            # allowed while that guard is in the plugin (the 2e Arresting March puts its heal back after the grant).
            guard = ROOT / "plugins" / "FrosthavenMods" / "src" / "GHConsumeOnNull.cs"
            guarded = guard.exists() and "typeof(CActor), typeof(CAbility), typeof(bool)" in guard.read_text(encoding="utf-8")
            names = list(abilities) if isinstance(abilities, dict) and not guarded else []
            for c in (h.get("Consumes") or {}).values() if isinstance(h.get("Consumes"), dict) else []:
                for a in ((c or {}).get("Abilities") or {}).values() if isinstance(c, dict) else []:
                    p = a.get("ParentName") if isinstance(a, dict) else None
                    if p in names and isinstance(abilities[p], dict) and (abilities[p].get("Null") or None in abilities[p]) \
                            and any(isinstance(abilities[n], dict) and "ControlActor" in abilities[n] for n in names[:names.index(p)]):
                        problems.append(f"{rel}: {half}/{p}: a consume's Null placeholder after a ControlActor (the game stops when the element is switched on): put it first")
            for ab, props in abilities.items():
                check_ability(rel, f"{half}/{ab}", props, problems, warnings)
                # a summon's card is drawn from the summon's definition (CreateLayout.CreateSummon throws without it)
                sid = props.get("Summon") if isinstance(props, dict) else None
                sid = sid.get("ID") if isinstance(sid, dict) else sid
                if isinstance(sid, str):
                    defs = list((pack / "files" / "Global" / "HeroSummons").glob("*.yml"))
                    if not any(re.search(rf"^ID:\s*{re.escape(sid)}(ID)?\s*$", d.read_text(encoding="utf-8-sig"), re.M) for d in defs):
                        problems.append(f"{rel}: {half}/{ab}: summon {sid} has no definition in files/Global/HeroSummons (its card cannot be drawn)")
    references(pack, problems)
    modifier_abilities(pack, problems)
    if _pack_name(pack) == "gloomhaven2e":
        printed_vs_shipped(pack, problems, warnings)
    # the effects' materials: a shader Frosthaven can't draw shows as a magenta block in game (tools/vfxcheck.py)
    if (pack / "pack.json").exists():
        import vfxcheck
        for cls, shader, mat, effs in vfxcheck.check(pack):
            problems.append(f"pack.json: {cls}: effect material {mat} on shader {shader!r} has no Frosthaven shader and no plugin stand-in (effects: {', '.join(effs) or '?'})")
    return n, problems, warnings


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("pack", nargs="?", default=str(ROOT / "mods" / "gloomhaven"))
    ap.add_argument("--strict", action="store_true", help="warnings fail too")
    args = ap.parse_args()
    n, problems, warnings = check_pack(Path(args.pack))
    for p in problems:
        print("PROBLEM " + p)
    for w in warnings:
        print("warning " + w)
    print(f"packcheck: {n} cards, {len(problems)} problem(s), {len(warnings)} warning(s)")
    sys.exit(1 if problems or (args.strict and warnings) else 0)


if __name__ == "__main__":
    main()
