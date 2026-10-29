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
Prints one line per problem and a count; exit 1 on any problem (--strict also fails on warnings).
"""

import argparse
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
            "Damage", "SummonHP", "SummonMove", "SummonAttack", "SummonRange"}
CONDITIONS = {"Stun", "Immobilize", "Disarm", "Wound", "Poison", "Muddle", "Curse", "Brittle", "Bane", "Impair",
              "Bless", "Strengthen", "Invisible", "Regenerate", "Ward"}
ABILITY_TYPES = {
    "Move", "Attack", "Heal", "Damage", "Loot", "PreventDamage", "Shield", "Retaliate", "Trap", "Create", "RecoverLostCards",
    "Invisible", "Poison", "Wound", "Muddle", "Immobilize", "Disarm", "Curse", "Stun", "Strengthen", "Bless", "Consume",
    "Infuse", "RefreshItemCards", "RecoverDiscardedCards", "Summon", "Advantage", "Teleport", "Kill", "DisarmTrap",
    "DestroyObstacle", "Redirect", "AddTarget", "Push", "Pull", "AddHeal", "AddRange", "ControlActor", "AddAugment",
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
                             "AddDoomSlot", "OverrideAugmentAttackType"}   # Doomstalker / Mindthief actives the engine keeps


def kind_of(props):
    if not isinstance(props, dict):
        return None
    for k in props:
        if k in KIND_KEYS:
            return k
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
