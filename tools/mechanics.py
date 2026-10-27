"""Card mechanics catalogue: every card half of Frosthaven (the game's extracted rules, extracted/Base) and of
Gloomhaven (the Gloomhaven install's rule archives, Jaws of the Lion included), the ability keys each half uses, and
the Gloomhaven -> Frosthaven mechanic map built from those keys (which Gloomhaven mechanics Frosthaven's own cards use,
which only its parser and code know, which it lacks). The map's judgement column (NOTES below) is hand-written; a key
without a note is printed as "(review)" so nothing is silently unmapped.

    python tools/mechanics.py   -> docs/mechanics/fh-card-halves.md, gh-card-halves.md, gh-to-fh-map.md, cards.json
"""

import collections
import json
import re
import sys
from pathlib import Path

import yaml

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gh_port as g  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
FH_CARDS = ROOT / "extracted" / "Base" / "Global" / "Characters"
OUT = ROOT / "docs" / "mechanics"
# every key Frosthaven's rule readers switch on: the ability reader (fh_ability_keys.txt, AbilityData) plus every other YML
# reader, the filters and the start requirements (fh_rule_keys.txt, all `case "X":` strings of ScenarioRuleLibrary.YML)
FH_KEYS = set()
for f in ("fh_ability_keys.txt", "fh_rule_keys.txt"):
    FH_KEYS |= {l.strip() for l in (ROOT / "tools" / f).read_text(encoding="utf-8").splitlines() if l.strip()}
# the classes already ported: every half of theirs showed its effect in Frosthaven (effects pass, 2026-10-27)
PORTED = {"Brute", "Scoundrel", "Sunkeeper", "Sawbones", "Quartermaster"}
# a key whose ability class is named differently
CLASS_ALIAS = {"Augment": "AddAugment", "Song": "AddSong", "RefreshItems": "RefreshItemCards", "AddDoomSlot": "AddDoomSlots",
               "AddModifiersToMonsterDeck": "AddModifierToMonsterDeck", "MergedDestroyObstacleAttack": "MergedDestroyAttack",
               "ChainAttack": "Attack", "SetHPTo": "Attack", "Condition": "AddCondition", "Conditions": "AddCondition"}
# keys that make an ability's shape (with its verbs) worth comparing between the games; the rest is detail
STRUCTURAL = {"AreaEffect", "AllTargetsOnMovePath", "AllTargetsAdjacentToParentTarget", "AllTargetsAdjacentToParentTargets",
              "AllTargetsOnAttackPath", "AllTargetsAdjacentToParentMovePath", "AllTargetsOnMovePathSameStartAndEnd", "StatIsBasedOnX",
              "PerformXTimesBasedOn", "ConsumeGroup", "AttackEffect", "CostAbility", "ActiveBonus", "StartAbilityRequirements",
              "UseParentTiles", "FilterContainer", "ResourcesToTakeFromTargets", "ResourcesToAddOnAbilityEnd", "XPPerTarget",
              "XPPerTargetPerEnemyCount", "ChainAttack", "GainXPPerXDamageDealt", "SlotToRefresh", "TreatAsMelee", "CanTargetInvisible",
              "ExhaustSelf", "ExactRange", "IgnorePreviousAbilityTargets", "PullType", "AdditionalPushEffect", "LootData",
              "AdditionalHealData", "Conditions", "Swap", "Targeting", "MoveRestrictionType", "MergedMoveAttack", "MergedCreateAttack",
              "MergedKillCreate", "MergedMoveObstacleAttack", "MergedDestroyObstacleAttack", "MergedDisarmTrapDestroyObstacle"}
SHAPE_ALIAS = {"Condition": "Conditions", "ActiveBonusDuration": "ActiveBonus"}
# children of these keys are the card author's own names (TAttack, BCFireAttackBuff), not rule keys
NAMED_CHILDREN = {"Abilities", "AbilityOverrides", "ConditionalOverrides", "SubAbilities", "Consumes"}
# the decompiled rule library (scratchpad), for the ability classes Frosthaven's code implements
DECOMP = Path(r"C:\Users\my-mi\AppData\Local\Temp\claude\C--Claude-Frosthaven\a8c2980f-3410-47be-8772-f5ed98a78003\scratchpad\decomp-new\ScenarioRuleLibrary")

# keys that only steer presentation (animation, timing, layout) and never change what a half does
COSMETIC = {"AnimOverload", "ActiveBonusAnimOverload", "MoveObstacleAnimOverload", "MoveTrapAnimOverload", "SpawnDelay",
            "SkipAnim", "MiscVars", "AreaEffectLayoutOverride", "SkipConsumeCheck", "Prefab", "CustomImage"}
# ability keys that are the ability's verb (what it does); every other key under an ability modifies the verb
VERBS = {"Attack", "Move", "Heal", "Shield", "Retaliate", "Loot", "Push", "Pull", "Damage", "Kill", "Bless", "Curse",
         "Poison", "Wound", "Muddle", "Stun", "Immobilize", "Disarm", "Strengthen", "Invisible", "Regenerate", "Ward",
         "Brittle", "Bane", "Impair", "Rupture", "Summon", "Teleport", "Jump", "Fly", "Trap", "DestroyObstacle",
         "CreateObstacle", "MoveObstacle", "MoveTrap", "DisarmTrap", "ControlActor", "ControlSummonTurn", "AddActiveBonus",
         "RecoverLostCards", "RecoverDiscardedCards", "RefreshItems", "RefreshItemCards", "GiveSupplyCard", "Swap",
         "ChainAttack", "AddDoom", "AddDoomSlot", "Augment", "AddAugment", "Song", "AddSong", "PlaySong", "DiscardCards",
         "LoseCards", "ChangeCharacterModel", "AllowOverheal", "Infuse", "Consume", "ConsumeElement", "RemoveConditions",
         "ImmunityTo", "Advantage", "Disadvantage", "AddAttackModifier", "RemoveAttackModifier", "AddTarget", "AddRange",
         "Pierce", "Create", "CreateTerrain", "DestroyTerrain", "MoveTerrain", "CreateShadow", "MoveShadow", "RemoveShadow",
         "PerformFromShadow", "Redirect", "Capture", "ExtraTurn", "AdjustInitiative", "SetHPTo", "HealthReduction",
         "Overheal", "Invulnerability", "PreventDamage", "RepeatAbility", "PlayDiscardedCard", "Choose", "ChooseAbility",
         "ExploitableVulnerability", "MirrorDamage", "RedistributeDamage", "NeutralizeShield", "BlockHealing",
         "TransferDooms", "TransferAugments", "ChangeAllegiance", "ChangeCondition", "DuplicateCondition", "Fear",
         "ItemLock", "IncreaseCardLimit", "ImprovedShortRest", "ExcludeCardsFromShortRest", "PreventLoseCard",
         "RecoverResources", "RemoveActorFromMap", "ActivateSpawner", "DeactivateSpawner", "AddHeal", "AddModifiersToMonsterDeck",
         "ConsumeItemCards", "DisableCardAction", "ForgoActionsForCompanion", "MoveActiveBonusTracker", "NullHex",
         "PierceInvulnerability", "AttackersGainDisadvantage", "Compare", "Immovable", "OverrideAugmentAttackType",
         "RemoveTargeting", "Suffer", "Shadow", "Flip", "Reveal", "Time", "Doom", "Command"}

# ----------------------------------------------------------------------------------------------- hand-written map
# key -> (Frosthaven counterpart or verdict, note). Status is derived from the data (uses in each game, parser, code);
# this column says what it means for a port. Keys the data shows that are missing here print "(review)".
NOTES = {}
try:
    from mechanics_notes import NOTES  # noqa: F401,F811  (the judgement column lives in its own file)
except ImportError:
    pass


def load_yaml(s):
    """The game's reader forgives an indent that is off by a space or two and odd layout blocks; PyYAML doesn't. Cut
    the layout (not needed here) and nudge an offending line to the nearest 4-space level, a few times at most."""
    s = s.replace("\t", "    ")
    s = re.split(r"^Layout:\s*$", s, maxsplit=1, flags=re.M)[0]
    lines = s.splitlines()
    for _ in range(12):
        try:
            return yaml.safe_load("\n".join(lines))
        except yaml.YAMLError as e:
            mark = getattr(e, "problem_mark", None)
            if mark is None:
                raise
            ln = lines[mark.line]
            ind = len(ln) - len(ln.lstrip(" "))
            new = round(ind / 4) * 4
            if new == ind:
                new = ind - 1 if ind % 4 else ind - 4
            if new < 0:
                raise
            lines[mark.line] = " " * new + ln.lstrip(" ")
    raise ValueError("could not parse")


def camel(s):
    s = s.replace("_", " ")
    s = re.sub(r"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def fh_cards():
    for p in sorted(FH_CARDS.glob("*/AbilityCards/*.yml")):
        d = load_yaml(p.read_text(encoding="utf-8-sig"))
        if not d or d.get("Parser") != "AbilityCard":
            continue
        parts = p.stem.split("_", 2)
        name = camel(parts[2] if len(parts) > 2 else p.stem)
        yield card_record("FH", p.parent.parent.name, name, d, p.name)


def gh_cards():
    gh, _ = g.find_gloomhaven()
    files = g.gh_files(gh)
    strings = g.gh_strings(gh)
    for path in sorted(files):
        if not (path.startswith("AbilityCard/") and path.endswith(".yml")):
            continue
        d = load_yaml(g.text(files[path]))
        if not d or d.get("Parser") != "AbilityCard":
            continue
        cls = str(d.get("Character", "")).removesuffix("ID")
        key = re.sub(r"^\$|\$$", "", str(d.get("Name", "")))
        name = strings.get(key) or camel(key.removeprefix("ABILITY_CARD_"))
        yield card_record("GH", cls, name, d, path.split("/")[-1])


def card_record(game, cls, name, d, file):
    data = d.get("Data") or {}
    halves = {}
    for half in ("Top", "Bottom"):
        h = data.get(half) or {}
        if not isinstance(h, dict):
            h = {}
        halves[half.lower()] = {
            "abilities": h.get("Abilities") or {},
            "extra": {k: v for k, v in h.items() if k != "Abilities"},     # XP, Infuse, Consumes, ...
        }
    return {"game": game, "class": cls, "id": d.get("ID"), "name": name, "level": d.get("Level"),
            "initiative": d.get("Initiative"), "discard": d.get("Discard"), "file": file, "halves": halves}


# ----------------------------------------------------------------------------------------------- rendering

def short(v):
    if isinstance(v, str) and re.fullmatch(r"(-?\d+,-?\d+,[A-Z]\|?)+", v):
        return f"{v.count('|') + 1} hexes"
    if isinstance(v, dict):
        return "{" + ", ".join(f"{k}={short(x)}" for k, x in v.items() if not str(k).endswith("Enhancements") and k not in COSMETIC) + "}"
    if isinstance(v, list):
        return ", ".join(str(x) for x in v)
    return str(v)


def fmt(k, v):
    if isinstance(v, dict):
        amt = v.get("Amount")
        rest = {str(kk): vv for kk, vv in v.items() if kk != "Amount" and not str(kk).endswith("Enhancements") and kk not in COSMETIC}
        s = f"{k} {amt}" if amt is not None else k
        if rest:
            s += " {" + ", ".join(f"{kk}={short(vv)}" for kk, vv in rest.items()) + "}"
        return s
    if v is True:
        return k
    return f"{k} {short(v)}"


def render(ab):
    if not isinstance(ab, dict):
        return str(ab)
    verbs, mods = [], []
    for k, v in ab.items():
        k = str(k)      # a bare "~:" line in a few files reads as a None key
        if k in COSMETIC or k.endswith("Enhancements") or k == "SubAbilities":
            continue
        (verbs if k in VERBS else mods).append(fmt(k, v))
    out = " + ".join(verbs) or "(no verb)"
    if mods:
        out += " [" + "; ".join(mods) + "]"
    subs = ab.get("SubAbilities") or {}
    if isinstance(subs, dict) and subs:
        out += " → " + " | ".join(render(x) for x in subs.values())
    return out


def render_consume(c):
    """One element-consume alternative: the element(s), then what the consume adds (overrides of the half's abilities,
    extra abilities, XP)."""
    if not isinstance(c, dict):
        return str(c)
    el = c.get("Elements", c.get("Element", "?"))
    parts = []
    for k, v in c.items():
        if k in ("Elements", "Element") or k in COSMETIC:
            continue
        if k in ("AbilityOverrides", "Abilities") and isinstance(v, dict):
            parts.append(("overrides " if k == "AbilityOverrides" else "adds ") + ", ".join(
                f"{n}{'=' + short({kk: vv for kk, vv in o.items() if kk not in COSMETIC}) if isinstance(o, dict) else ''}" for n, o in v.items()))
        else:
            parts.append(fmt(k, v))
    return f"{short(el)} → " + "; ".join(parts)


def render_half(h):
    rows = [render(ab) for ab in (h["abilities"] or {}).values()]
    for k, v in h["extra"].items():
        if k == "XP":
            continue
        if k == "Consumes" and isinstance(v, dict):
            rows.append("Consume: " + " | ".join(render_consume(c) for c in v.values()))
        else:
            rows.append(fmt(k, v))
    return "<br>".join(rows) if rows else "—"


def halves_md(cards, title, intro):
    by_class = collections.defaultdict(list)
    for c in cards:
        by_class[c["class"]].append(c)
    n_halves = sum(1 for c in cards for h in c["halves"].values() if h["abilities"] or h["extra"])
    out = [f"# {title}", "", intro, "",
           f"{len(by_class)} classes, {len(cards)} cards, {n_halves} halves. Generated by `tools/mechanics.py`; do not edit by hand.", ""]
    for cls in sorted(by_class):
        cs = sorted(by_class[cls], key=lambda c: (str(c['level']).rjust(2), c['id'] or 0))
        out += [f"## {cls} ({len(cs)} cards)", "", "| ID | Card | Lvl | Init | Half | Abilities | XP | Discard |", "|---|---|---|---|---|---|---|---|"]
        for c in cs:
            for half in ("top", "bottom"):
                h = c["halves"][half]
                xp = h["extra"].get("XP", "")
                out.append(f"| {c['id']} | {c['name']} | {c['level']} | {c['initiative']} | {half} | {render_half(h).replace('|', '/')} | {xp} | {short(c['discard'])} |")
        out.append("")
    return "\n".join(out)


# ----------------------------------------------------------------------------------------------- the inventory

def walk(ab, where, inv):
    """Every key of an ability (and of its sub-abilities), its nested keys and its enum-like values."""
    if not isinstance(ab, dict):
        return
    keys = set()
    for k, v in ab.items():
        k = str(k)
        if k == "SubAbilities" and isinstance(v, dict):
            for sub in v.values():
                walk(sub, where, inv)
            inv["keys"]["SubAbilities"][where.game] += 1
            inv["ex"]["SubAbilities"][where.game].add(where.label)
            keys.add("SubAbilities")
            continue
        keys.add(k)
        if getattr(where, "ported", False):
            inv["ported"].add(k)
        inv["keys"][k][where.game] += 1
        inv["ex"][k][where.game].add(where.label)
        record_values(k, v, where, inv)
        if isinstance(v, dict):
            for kk, vv in v.items():
                kk = str(kk)
                if k in NAMED_CHILDREN:
                    continue
                if getattr(where, "ported", False):
                    inv["ported"].add(f"{k}.{kk}")
                inv["keys"][f"{k}.{kk}"][where.game] += 1
                inv["ex"][f"{k}.{kk}"][where.game].add(where.label)
                record_values(f"{k}.{kk}", vv, where, inv)
    combo = frozenset(SHAPE_ALIAS.get(k, k) for k in keys if k in VERBS or k in STRUCTURAL or k in SHAPE_ALIAS)
    if combo & VERBS:
        inv["combos"][combo][where.game] += 1
        inv["cex"][combo][where.game].add(where.label)


def record_values(k, v, where, inv):
    vals = v if isinstance(v, list) else [v]
    for x in vals:
        if isinstance(x, str) and not re.fullmatch(r"(-?\d+,-?\d+,[A-Z]\|?)+", x) and len(x) < 40:
            inv["vals"][k][x][where.game] += 1
        elif isinstance(x, bool):
            inv["vals"][k][str(x)][where.game] += 1
        elif isinstance(x, int) and k in ("Target", "Range"):
            inv["vals"][k][str(x)][where.game] += 1


class Where:
    def __init__(self, game, label):
        self.game, self.label = game, label


def inventory(cards):
    inv = {"keys": collections.defaultdict(lambda: collections.Counter()),
           "ex": collections.defaultdict(lambda: collections.defaultdict(set)),
           "vals": collections.defaultdict(lambda: collections.defaultdict(collections.Counter)),
           "combos": collections.defaultdict(lambda: collections.Counter()),
           "cex": collections.defaultdict(lambda: collections.defaultdict(set)),
           "card": collections.defaultdict(lambda: collections.Counter()),
           "ported": set()}
    for c in cards:
        for d in (c["discard"] if isinstance(c["discard"], list) else [c["discard"]]):
            inv["card"][f"Discard: {d}"][c["game"]] += 1
        inv["card"][f"Level: {c['level']}"][c["game"]] += 1
        for half, h in c["halves"].items():
            w = Where(c["game"], f"{c['class']} {c['name']} {half}")
            w.ported = c["game"] == "GH" and c["class"] in PORTED
            for ab in (h["abilities"] or {}).values():
                walk(ab, w, inv)
            for k, v in h["extra"].items():
                inv["keys"][f"half:{k}"][c["game"]] += 1
                inv["ex"][f"half:{k}"][c["game"]].add(w.label)
                if k == "Consumes" and isinstance(v, dict):
                    for cons in v.values():
                        if isinstance(cons, dict):
                            walk({kk: vv for kk, vv in cons.items() if kk != "Element"}, w, inv)
                            record_values("half:Consumes.Element", cons.get("Element"), w, inv)
                else:
                    record_values(f"half:{k}", v, w, inv)
    return inv


def code_classes():
    names = set()
    if DECOMP.exists():
        for p in DECOMP.rglob("CAbility*.cs"):
            names.add(p.stem.removeprefix("CAbility"))
    return names


def status(k, gh, fh, classes, ported=False):
    base = k.split(".")[0].removeprefix("half:")
    tail = " · **verified in game** (a ported class uses it)" if ported else ""
    if fh and gh:
        return "both games use it" + tail
    if fh:
        return "Frosthaven only"
    known = base in FH_KEYS or k.split(".")[-1] in FH_KEYS
    coded = base in classes or base.removeprefix("Add") in classes or CLASS_ALIAS.get(base) in classes
    if known and coded:
        return "GH only: FH parser + code know it (unused by FH cards)" + tail
    if known:
        return "GH only: FH parser knows it (no ability class of that name)" + tail
    return "GH only: UNKNOWN to FH's parser" + tail


def ex(s, n=3):
    return "; ".join(sorted(s)[:n])


def map_md(inv, classes, fh_n, gh_n):
    out = ["# Gloomhaven → Frosthaven mechanic map", "",
           "Every key Gloomhaven's and Frosthaven's ability cards use (ability keys, their nested keys, half-level keys), with how often "
           "each game uses it, whether Frosthaven's parser (`tools/fh_ability_keys.txt`, from the game's `AbilityData` reader) and code "
           "(`CAbility*` classes) know it, and what that means for a port. Counts are abilities (sub-abilities included), not cards. "
           f"Generated by `tools/mechanics.py` from {fh_n} Frosthaven and {gh_n} Gloomhaven cards; the **Port** column is hand-written in "
           "`tools/mechanics_notes.py` (\"(review)\" = not yet judged).", "",
           "Reading the status: *both games use it* = Frosthaven's own cards do this, the port is a straight copy; *FH parser + code know it* = "
           "Frosthaven kept Gloomhaven's implementation but none of its cards use it, so the mechanic must be checked in game (UI, trackers, "
           "animations) before a class relying on it ships; *UNKNOWN* = Frosthaven's reader drops the key, the plugin must implement it.", ""]
    keys = inv["keys"]
    # 1. ability keys (verbs first)
    def row(k):
        gh, fh = keys[k]["GH"], keys[k]["FH"]
        note = NOTES.get(k) or NOTES.get(k.split(".")[0])
        port = "—" if not gh else f"**{note[0]}** — {note[1]}" if note else "(review)"
        exs = ex(inv["ex"][k]["GH"]) if gh else ex(inv["ex"][k]["FH"])
        return f"| `{k}` | {gh} | {fh} | {status(k, gh, fh, classes, k in inv['ported'])} | {port} | {exs} |"
    hdr = ["| Key | GH uses | FH uses | Status | Port | Examples |", "|---|---|---|---|---|---|"]
    verbs = sorted(k for k in keys if "." not in k and not k.startswith("half:") and k in VERBS)
    mods = sorted(k for k in keys if "." not in k and not k.startswith("half:") and k not in VERBS)
    nested = sorted(k for k in keys if "." in k)
    halves = sorted(k for k in keys if k.startswith("half:"))
    out += ["## 1. Ability verbs (what an ability does)", ""] + hdr + [row(k) for k in verbs] + [""]
    out += ["## 2. Ability modifiers (keys that shape a verb)", ""] + hdr + [row(k) for k in mods] + [""]
    out += ["## 3. Half-level keys", ""] + hdr + [row(k) for k in halves] + [""]
    out += ["## 4. Nested keys (`Verb.Property`)", ""] + hdr + [row(k) for k in nested] + [""]
    # 2. values
    out += ["## 5. Values (enum-like settings, conditions, elements, behaviours)", "",
            "| Key | Value | GH | FH |", "|---|---|---|---|"]
    for k in sorted(inv["vals"]):
        vals = inv["vals"][k]
        if len(vals) > 40:
            continue
        for v in sorted(vals, key=lambda x: (-vals[x]["GH"] - vals[x]["FH"], x)):
            mark = "" if vals[v]["FH"] else " **GH only**"
            out.append(f"| `{k}` | {v}{mark} | {vals[v]['GH']} | {vals[v]['FH']} |")
    out.append("")
    # 3. card-level
    out += ["## 6. Card-level", "", "| Property | GH | FH |", "|---|---|---|"]
    for k in sorted(inv["card"]):
        out.append(f"| {k} | {inv['card'][k]['GH']} | {inv['card'][k]['FH']} |")
    out.append("")
    # 4. GH-only list
    out += ["## 7. Gloomhaven-only mechanics", "",
            "Keys no Frosthaven card uses, grouped by what Frosthaven still has for them, then ability shapes (key combinations) that only "
            "Gloomhaven cards have, even where each key on its own is common in Frosthaven (these are the places a port needs a decision or a "
            "set-up the test harness must know about).", ""]
    groups = collections.defaultdict(list)
    for k in sorted(keys):
        gh, fh = keys[k]["GH"], keys[k]["FH"]
        if gh and not fh:
            groups[status(k, gh, fh, classes).split(" · ")[0]].append(k)
    for st in sorted(groups):
        out += [f"### {st}", ""] + hdr + [row(k) for k in groups[st]] + [""]
    out += ["### Needs the plugin or a card decision", "",
            "Mechanics Frosthaven's reader drops or cannot run as Gloomhaven wrote them (verdicts *plugin* and *decision* in the notes).", "",
            "| Key | Verdict | GH uses | Note |", "|---|---|---|---|"]
    for k, (verdict, note) in sorted(NOTES.items()):
        if verdict.split()[0] in ("plugin", "decision") or "plugin fix" in verdict:
            out.append(f"| `{k}` | {verdict} | {keys[k]['GH']} | {note} |")
    out.append("")
    out += ["### Ability shapes only Gloomhaven cards have", "",
            "An ability's verbs plus its structural modifiers (area, path targeting, stat-based strength, active bonus, requirements...). "
            "Shapes every Frosthaven card lacks, however common each key is on its own.", "",
            "| Keys | GH abilities | Examples |", "|---|---|---|"]
    gh_only = [c for c in inv["combos"] if inv["combos"][c]["GH"] and not inv["combos"][c]["FH"]]
    for c in sorted(gh_only, key=lambda c: (-inv["combos"][c]["GH"], sorted(c))):
        out.append(f"| {', '.join(f'`{k}`' for k in sorted(c))} | {inv['combos'][c]['GH']} | {ex(inv['cex'][c]['GH'])} |")
    out.append("")
    out += ["### Frosthaven-only mechanics (for the reverse view)", "", "| Key | FH uses | Examples |", "|---|---|---|"]
    for k in sorted(keys):
        if keys[k]["FH"] and not keys[k]["GH"] and "." not in k:
            out.append(f"| `{k}` | {keys[k]['FH']} | {ex(inv['ex'][k]['FH'])} |")
    out.append("")
    return "\n".join(out)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    fh = list(fh_cards())
    gh = list(gh_cards())
    classes = code_classes()
    print(f"FH: {len(fh)} cards in {len({c['class'] for c in fh})} classes; GH: {len(gh)} cards in {len({c['class'] for c in gh})} classes; "
          f"{len(classes)} CAbility classes in the decompile")
    (OUT / "fh-card-halves.md").write_text(halves_md(fh, "Frosthaven card halves",
        "Every ability card of Frosthaven's 17 classes, both halves, as the game's own rule files define them (`Frosthaven_Data/StreamingAssets/Rulebase`, "
        "extracted to `extracted/Base`). Abilities read `Verb amount [modifiers] → sub-abilities`; `{...}` holds a verb's own settings."), encoding="utf-8")
    (OUT / "gh-card-halves.md").write_text(halves_md(gh, "Gloomhaven card halves",
        "Every ability card of Gloomhaven digital's 17 classes and the 4 Jaws of the Lion classes, both halves, as Gloomhaven's own rule files define "
        "them (`Gloomhaven/Rulebase/*.ruleset`). Same notation as the Frosthaven list; a key's meaning is in `gh-to-fh-map.md`."), encoding="utf-8")
    inv = inventory(fh + gh)
    (OUT / "gh-to-fh-map.md").write_text(map_md(inv, classes, len(fh), len(gh)), encoding="utf-8")
    (OUT / "cards.json").write_text(json.dumps(fh + gh, indent=0, default=str), encoding="utf-8")
    missing = [k for k in inv["keys"] if inv["keys"][k]["GH"] and "." not in k and k not in NOTES]
    print(f"keys: {len(inv['keys'])}, GH keys without a note: {len(missing)}")
    print(" ".join(sorted(missing)))


if __name__ == "__main__":
    main()
