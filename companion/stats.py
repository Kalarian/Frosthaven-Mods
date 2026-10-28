"""The Stats tab's data: the mod's battle records (FrosthavenMods\\stats\\runs\\<campaign>\\*.json.gz, one per finished
scenario, and live-<campaign>.json.gz during one), each boiled down to a compact summary the page charts.

A record holds the game's own per-scenario event lists for every character, the summon pool and the monsters (see
plugins/FrosthavenMods/src/StatsExport.cs). Only finished scenarios are ever recorded (wins, losses, resigns; never
restarts), so every file here counts. Summaries are cached by file (path, size, time), so the page stays quick as a
campaign grows. Names (classes, monsters, ability cards, scenarios) come from the game's own Base.ruleset.
"""
import datetime as dt
import glob
import gzip
import io
import json
import os
import re
import threading
import zipfile
from collections import Counter, defaultdict
from pathlib import Path

import fh_ruleset
import game
import scenario_map

# conditions, as the game names the abilities that apply them: buffs help their target, debuffs hurt it
BUFFS = ("Bless", "Strengthen", "Invisible", "Regenerate", "Ward", "Safeguard", "Shield", "Retaliate")
DEBUFFS = ("Poison", "Wound", "Muddle", "Immobilize", "Disarm", "Stun", "Curse", "Brittle", "Bane", "Impair",
           "Infect", "Rupture")
CONDITIONS = BUFFS + DEBUFFS

NULL_X2_WORTH = 3       # what a null (and a x2) is worth in modifier points: about a typical attack's damage

_lock = threading.Lock()
_cache = {}             # path -> (key, summary)
_names = {"key": None}


# ---- names from the game's data

def _humanize(s):
    return re.sub(r"(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=\d)", " ", s or "").strip()


def class_name(cid):
    """'BannerSpearID' -> 'Banner Spear', 'AlgoxGuardEliteID' -> 'Algox Guard (elite)'."""
    s = (cid or "").removesuffix("ID")
    elite = s.endswith("Elite")
    s = _humanize(s.removesuffix("Elite"))
    s = re.sub(r"\s*S\s*0*(\d+)$", "", s)          # "CityGuardS01" (a scenario's own variant) -> "City Guard"
    return s + (" (elite)" if elite else "")


def names():
    """{'cards': {id: {'name', 'class', 'init'}}, 'scen': {number: name}} from Base.ruleset, re-read when it changes."""
    ruleset = game.ruleset_path()
    key = (ruleset, os.path.getmtime(ruleset)) if ruleset and os.path.exists(ruleset) else None
    with _lock:
        if _names["key"] == key and key is not None:
            return _names
        cards, scen = {}, {}
        if key:
            for e in fh_ruleset.read_ruleset(Path(ruleset).read_bytes()):
                kind = fh_ruleset.PARSER_TYPES.get(e["parser"])
                if kind not in ("AbilityCard", "FHScenario") or e["binary"]:
                    continue
                text = e["data"].decode("utf-8-sig", "replace")
                head = dict(re.findall(r"^(\w+):[ \t]*(.*?)\s*$", text, re.M))
                if kind == "AbilityCard" and head.get("ID", "").isdigit():
                    prefab = head.get("Prefab", "")
                    nm = _humanize(prefab.split("_", 2)[2]) if prefab.count("_") >= 2 else head.get("Name", "")
                    cards[int(head["ID"])] = {"name": nm, "class": head.get("Character", ""),
                                              "init": int(head["Initiative"]) if head.get("Initiative", "").isdigit() else None}
                elif kind == "FHScenario" and head.get("ID", "").isdigit():
                    scen[int(head["ID"])] = head.get("Name") or re.sub(r"^FS_\d+_", "", head.get("LevelID", "")).replace("_", " ")
        # cards of an installed content pack (the Gloomhaven classes) have no prefab and name their card by a text key
        # ("$ABILITY_CARD_EyeforanEye$") that the pack's own strings.tsv answers, not the game's text (the stats
        # page showed the raw keys, 2026-10-27)
        pack = _pack_strings(ruleset)
        for c in cards.values():
            m = re.fullmatch(r"\$([^$]+)\$", c["name"] or "")
            if m:
                k = m.group(1)
                c["name"] = pack.get(k) or _humanize(re.sub(r"^ABILITY_CARD_", "", k))
        _names.update(key=key, cards=cards, scen=scen)
        return _names


def _pack_strings(ruleset):
    """{key: English text} from every installed content pack's strings.tsv (<game>/FrosthavenMods/packs/*/)."""
    out = {}
    if not ruleset:
        return out
    gdir = Path(ruleset).parents[3]   # <game>/Frosthaven_Data/StreamingAssets/Rulebase/Base.ruleset
    for f in sorted((gdir / "FrosthavenMods" / "packs").glob("*/strings.tsv")):
        try:
            for line in f.read_text(encoding="utf-8-sig", errors="replace").splitlines():
                k, sep, v = line.partition("\t")
                if sep and k and k not in out:
                    out[k] = v.replace("\\n", " ").strip()
        except OSError:
            continue
    return out


# ---- one record -> one summary

def _plain(s):
    return re.sub(r"<[^>]+>", "", s or "").strip()


def _value(card):
    """A modifier as drawn or in a deck ('+1', '-2', 'x2', 'x0'/'null', '*2', 'R+1') -> (label, points, kind)."""
    c = _plain(card).lower().replace(" ", "")
    rolling = c.startswith("r") and len(c) > 1 and c[1] in "+-*x"
    c = c[1:] if rolling else c
    if c in ("x2", "*2", "2x"):
        return "x2", NULL_X2_WORTH, "x2"
    if c in ("x0", "*0", "0x", "null", "miss"):
        return "null", -NULL_X2_WORTH, "null"
    m = re.fullmatch(r"([+-]\d+)", c)
    if m:
        return m.group(1), int(m.group(1)), "rolling" if rolling else "num"
    return c or "?", 0, "other"


def _deck(cards):
    vals = [_value(c) for c in cards or []]
    settling = [v for v in vals if v[2] != "rolling"]
    n = len(settling)
    return {"n": n, "mean": round(sum(v[1] for v in settling) / n, 3) if n else 0,
            "nulls": sum(v[2] == "null" for v in settling), "x2s": sum(v[2] == "x2" for v in settling)}


def _per_round(events, rounds, amount):
    out = [0] * max(rounds, 1)
    for e in events:
        r = e.get("Round", 1) or 1
        if 1 <= r <= len(out):
            out[r - 1] += amount(e)
    return out


def _hand(abilities, rounds):
    """Cards still available each round (hand + discard pile, after that round's two were picked), from the
    character's first ability of the round: the game records both counts on every ability (its Hand list holds only
    the scenario's end). Resting moves cards between the two, so only losses lower the total; under 2, exhaustion."""
    first = {}
    for e in sorted(abilities, key=lambda e: e.get("EventOrder") or 0):
        r = e.get("Round")
        if r and r not in first and e.get("CardsInHandCount") is not None and not e.get("PerformedBySummons"):
            first[r] = (e["CardsInHandCount"], e.get("CardsInDiscardCount") or 0)
    return [{"r": r, "hand": first[r][0], "discard": first[r][1], "available": sum(first[r])}
            for r in sorted(first) if r <= max(rounds, 1)]


def _card_damage(dealt, cards):
    """Damage by where it came from: each ability card (split top / bottom: the game names the half, TAttack / BAttack),
    the basic Attack 2 (DefaultAttack), summons' own attacks, and anything else (retaliate, traps, effects)."""
    out = {}
    for e in dealt:
        amt = e.get("FinalDamageAmount", 0) or 0
        cid, act = e.get("CardID") or 0, (e.get("ActingAbilityName") or "")
        if e.get("PerformedBySummons") and act == "DefaultAttack":
            key, name, half = "summons", "Summons' own attacks", "other"
        elif cid and cid != 2147483647 and e.get("CardType") == "CharacterAbility":
            key, name = str(cid), (cards.get(cid) or {}).get("name") or f"card {cid}"
            half = "top" if act.startswith("T") else "bottom" if act.startswith("B") else "other"
        elif act == "DefaultAttack":
            key, name, half = "basic", "Basic attack (Attack 2)", "other"
        else:
            key, name, half = "other", "Other (retaliate, traps, effects)", "other"
        d = out.setdefault(key, {"name": name, "top": 0, "bottom": 0, "other": 0, "hits": 0})
        d[half] += amt
        d["hits"] += 1
    return out


def _turn_order(actors, rounds):
    """Each round's turns in the order they happened (every actor's turn start, by event order): for each party member,
    its place in that order, out of how many turns, and its two cards' initiatives (stored low to high, so which one
    led isn't known: a Blinkblade that went slow, say)."""
    turns = defaultdict(list)
    for who, a in actors.items():
        for e in a.get("StartTurn", []):
            if e.get("Round"):
                turns[e["Round"]].append((e.get("EventOrder") or 0, who, e.get("MainActorClassID"), e.get("RoundCardsInitiative") or []))
    out = defaultdict(list)
    for r, ts in turns.items():
        ts.sort()
        for pos, (_, who, cls, inits) in enumerate(ts, 1):
            if not who.startswith("_") and cls == who:
                out[who].append({"r": r, "pos": pos, "of": len(ts), "inits": inits})
    return {k: sorted(v, key=lambda x: x["r"]) for k, v in out.items()}


def _element_flow(actors, party_ids):
    """Who fed whom: each time a party member consumed an element, the last infusion of that element before it, by
    anyone (a party member, a summon, a monster). [{'from', 'to', 'element', 'n'}]."""
    events = []
    for who, a in actors.items():
        src = who if who in party_ids else "_summons" if who == "_summons" else "_monsters"
        for e in a.get("Infusions", []):
            events.append((e.get("EventOrder") or 0, 0, e.get("Element"), src))
        for e in a.get("Consumed", []):
            events.append((e.get("EventOrder") or 0, 1, e.get("Element"), src))
    last, flow = {}, Counter()
    for _, consumed, el, who in sorted(events):
        if not consumed:
            last[el] = who
        elif who in party_ids:
            flow[(last.get(el, "_unknown"), who, el)] += 1
    return [{"from": f, "to": t, "element": el, "n": n} for (f, t, el), n in flow.most_common()]


def run_key(rec):
    """One play-through, the same on every PC that recorded it: the scenario, its random seed (shared in co-op),
    rounds, outcome, the party and its totals. (Files differ between PCs: a few event timestamps are local times.)"""
    sc = rec.get("scenario") or {}
    actors = rec.get("actors") or {}
    party = sorted(p.get("class") or "" for p in rec.get("party") or [])
    totals = []
    for cls in party:
        a = actors.get(cls) or {}
        totals.append(sum(e.get("FinalDamageAmount", 0) or 0 for e in a.get("DamageDealt", [])))
        totals.append(sum(e.get("FinalDamageAmount", 0) or 0 for e in a.get("DamageReceived", [])))
        totals.append(len(a.get("Modifiers", [])))
    return "|".join(str(x) for x in [sc.get("id"), sc.get("seed"), sc.get("rounds"), rec.get("outcome"), ",".join(party), *totals])


def _conditions(actors, party_ids):
    """Conditions applied, from every ability that applies one (cards, and attack-modifier perks: those show as the
    game's 'dummy' cards). Per party member: what they applied, split by target (an enemy, themselves, an ally, a
    summon), and what they received, with who applied it (a monster type, an ally, themselves)."""
    applied = {c: defaultdict(lambda: defaultdict(int)) for c in party_ids}
    received = {c: defaultdict(lambda: defaultdict(int)) for c in party_ids}
    for who, a in actors.items():
        for e in a.get("Abilities", []):
            cond = e.get("AbilityType")
            if cond not in CONDITIONS:
                continue
            caster, target = e.get("MainActorClassID"), e.get("SecondaryActorClassID")
            ttype, ctype = e.get("SecondaryActorType"), e.get("MainActorType")
            if who in party_ids and caster == who:
                kind = ("self" if target == who else "enemy" if ttype == "Enemy" else "summon" if ttype == "HeroSummon"
                        else "ally" if ttype == "Player" else "other")
                applied[who][cond][kind] += 1
            if ttype == "Player" and target in party_ids:
                src = ("self" if caster == target else class_name(caster) if ctype == "Enemy" else "ally:" + (caster or "?"))
                received[target][cond][src] += 1
    plain = lambda d: {k: dict(v) for k, v in d.items()}
    return {c: {"applied": plain(applied[c]), "received": plain(received[c])} for c in party_ids}


def summarize(rec, path=None):
    nm = names()
    sc = rec.get("scenario") or {}
    sid = sc.get("id") or ""
    m = re.match(r"FS_0*(\d+)", sid)
    num = int(m.group(1)) if m else None
    rounds = sc.get("rounds") or 0
    actors = rec.get("actors") or {}
    party = []
    for p in rec.get("party") or []:
        a = actors.get(p.get("class")) or {}
        party.append({"cls": p.get("class"), "className": class_name(p.get("class")), "name": p.get("name"),
                      "level": p.get("level"), "maxHp": p.get("maxHp"), "xp": p.get("xp"),
                      "exhausted": bool(p.get("exhausted")), "survived": bool(a.get("survived", not p.get("exhausted"))),
                      "deck": _deck(p.get("deck"))})
    decks = {p["cls"]: p["deck"] for p in party}
    fields = (rec.get("format") or 1) >= 2      # format 1 records lack the events' fields (melee, shields, poison)
    only = lambda v: v if fields else None

    chars = {}
    for p in party:
        cls = p["cls"]
        a = actors.get(cls) or {}
        dealt = [e for e in a.get("DamageDealt", []) if e.get("ActorWhoReceivedDamageType") != "Player"]
        taken = a.get("DamageReceived", [])
        kills = [k for k in a.get("Kills", []) if k.get("ActorWhoDiedType") != "Player"]
        draws, deck = [], decks.get(cls) or {"mean": 0}
        for d in a.get("Modifiers", []):
            for card in d.get("Modifier") or []:
                label, pts, kind = _value(card)
                if kind == "rolling":
                    continue
                draws.append({"r": d.get("Round"), "o": d.get("EventOrder"), "v": label, "kind": kind,
                              "luck": round(pts - deck["mean"], 3)})
        cards = defaultdict(lambda: {"plays": 0, "lost": 0})
        for e in a.get("DiscardCard", []):
            cid = e.get("DiscardedCardID")
            if cid is None:
                continue
            c = cards[cid]
            c["plays"] += 1
            c["lost"] += e.get("Pile") == "Lost"
        overkill = sum(max(0, (k.get("LastDamageAmount") or 0) - (k.get("PreDamageHealth") or 0)) for k in kills)
        chars[cls] = {
            "dealt": sum(e.get("FinalDamageAmount", 0) for e in dealt),
            "taken": sum(e.get("FinalDamageAmount", 0) for e in taken),
            "healed": sum(e.get("HealAmount", 0) for e in a.get("Heals", [])),
            "kills": len(kills),
            "hits": sum(1 for e in dealt if e.get("FinalDamageAmount", 0) > 0),
            "biggest": max((e.get("FinalDamageAmount", 0) for e in dealt), default=0),
            "biggestTaken": max((e.get("FinalDamageAmount", 0) for e in taken), default=0),
            "overkill": overkill,
            "shielded": only(sum(e.get("Shielded", 0) or 0 for e in taken)),
            "melee": only(sum(e.get("FinalDamageAmount", 0) for e in dealt if e.get("IsMelee") and not e.get("PerformedBySummons"))),
            "ranged": only(sum(e.get("FinalDamageAmount", 0) for e in dealt if not e.get("IsMelee") and not e.get("PerformedBySummons"))),
            "summons": sum(e.get("FinalDamageAmount", 0) for e in dealt if e.get("PerformedBySummons")),
            "poison": only(sum(e.get("PoisonDamage", 0) or 0 for e in dealt)),
            "byRound": {
                "dealt": _per_round(dealt, rounds, lambda e: e.get("FinalDamageAmount", 0)),
                "taken": _per_round(taken, rounds, lambda e: e.get("FinalDamageAmount", 0)),
                "healed": _per_round(a.get("Heals", []), rounds, lambda e: e.get("HealAmount", 0)),
                "kills": _per_round(kills, rounds, lambda e: 1),
            },
            "draws": draws,
            "infused": dict(Counter(e.get("Element") for e in a.get("Infusions", []) if e.get("Element"))),
            "consumed": dict(Counter(e.get("Element") for e in a.get("Consumed", []) if e.get("Element"))),
            "hand": _hand(a.get("Abilities", []), rounds),
            "initiative": [{"r": s.get("Round"), "cards": s.get("RoundCardsInitiative") or []}
                           for s in a.get("StartTurn", []) if s.get("RoundCardsInitiative")],
            "cards": {str(cid): {**v, "name": (nm["cards"].get(cid) or {}).get("name") or f"card {cid}"}
                      for cid, v in cards.items()},
            "cardDamage": _card_damage(dealt, nm["cards"]),
            "lostToAvoid": len(a.get("LoseCards", [])),
            "items": [e.get("ItemName") for e in a.get("Items", []) if e.get("ItemName")],
            "xpEarned": sum(e.get("XPEarned", 0) for e in a.get("XP", [])),
        }

    # monsters: who hurt the party (from each character's damage taken), who the party killed
    nemesis = Counter()
    for p in party:
        for e in (actors.get(p["cls"]) or {}).get("DamageReceived", []):
            if e.get("ActorWhoDealtDamageType") == "Enemy" and e.get("FinalDamageAmount", 0) > 0:
                nemesis[(class_name(e.get("ActorWhoDealtDamageClassID")), p["cls"])] += e["FinalDamageAmount"]
    killed = Counter(class_name(k.get("ActorWhoDiedClassID")) for k in (actors.get("_monsters") or {}).get("Deaths", [])
                     if k.get("ActorWhoDiedType") == "Enemy")
    monster_draws = []
    mdeck = _deck(rec.get("monsterDeck"))
    for d in (actors.get("_monsters") or {}).get("Modifiers", []):
        for card in d.get("Modifier") or []:
            label, pts, kind = _value(card)
            if kind != "rolling":
                monster_draws.append({"r": d.get("Round"), "v": label, "kind": kind, "luck": round(pts - mdeck["mean"], 3)})
    summon_dealt = sum(e.get("FinalDamageAmount", 0) for e in (actors.get("_summons") or {}).get("DamageDealt", [])
                       if e.get("ActorWhoReceivedDamageType") == "Enemy")

    order = _turn_order(actors, rounds)
    conds = _conditions(actors, set(chars))
    for cls, c in chars.items():
        c["turnOrder"] = order.get(cls, [])
        c["conditions"] = conds.get(cls, {"applied": {}, "received": {}})

    key = run_key(rec)

    return {
        "key": key,
        "elementFlow": _element_flow(actors, set(chars)),
        "file": os.path.basename(path) if path else None,
        "live": bool(rec.get("inProgress")),
        "id": sid, "num": num, "name": nm["scen"].get(num) or re.sub(r"^FS_\d+_", "", sid).replace("_", " "),
        "level": sc.get("level"), "rounds": rounds, "outcome": rec.get("outcome"), "written": rec.get("written"),
        "week": rec.get("week"), "party": party, "chars": chars,
        "monsters": {"hurt": [{"monster": k[0], "target": k[1], "damage": v} for k, v in nemesis.most_common()],
                     "killed": dict(killed), "deck": mdeck, "draws": monster_draws},
        "summonPoolDamage": summon_dealt,
    }


# ---- files

def _read(path):
    opener = gzip.open if path.endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8") as f:
        return json.load(f)


def _summary(path):
    try:
        st = os.stat(path)
    except OSError:
        return None
    key = (st.st_size, st.st_mtime)
    with _lock:
        hit = _cache.get(path)
        if hit and hit[0] == key:
            return hit[1]
    try:
        s = summarize(_read(path), path)
    except Exception as ex:
        s = {"file": os.path.basename(path), "error": f"{type(ex).__name__}: {ex}"}
    with _lock:
        _cache[path] = (key, s)
    return s


def _folder(campaign):
    d = game.mods_dir()
    if not d or not campaign:
        return None, None
    stats = os.path.join(d, "stats")
    want = game._key(campaign)
    runs = next((f for f in glob.glob(os.path.join(stats, "runs", "*")) if game._key(os.path.basename(f)) == want), None)
    live = next((f for f in glob.glob(os.path.join(stats, "live-*.json*"))
                 if game._key(re.sub(r"^live-|\.json(\.gz)?$", "", os.path.basename(f))) == want), None)
    return runs, live


def campaign_stats(campaign):
    """{'runs': [summary, ...] oldest first, 'live': summary or None} for a campaign."""
    runs_dir, live = _folder(campaign)
    files = sorted(glob.glob(os.path.join(runs_dir, "*.json*"))) if runs_dir else []
    runs, seen = [], set()
    for s in (_summary(f) for f in files if not f.endswith(".tmp")):
        if not s or (s.get("key") and s["key"] in seen):
            continue                     # the same play-through recorded twice (an import of a friend's copy, say)
        seen.add(s.get("key"))
        runs.append(s)
    runs.sort(key=lambda r: r.get("written") or "")
    return {"campaign": campaign, "runs": runs, "live": _summary(live) if live else None}


def stamp(campaign):
    """Changes whenever a record or the live snapshot is written (the page reloads its data then)."""
    runs_dir, live = _folder(campaign)
    files = glob.glob(os.path.join(runs_dir, "*.json*")) if runs_dir else []
    return f"{len(files)}:{max((os.path.getmtime(f) for f in files), default=0)}:{os.path.getmtime(live) if live else 0}"


# ---- sharing records between players (Stats tab: Export / Import)

MAX_IMPORT = 200 * 1024 * 1024     # bytes a zip may unpack to


def _safe(name):
    return re.sub(r'[<>:"/\\|?*]', "_", name or "").strip() or "campaign"


def export_zip(campaign):
    """Every finished-scenario record of a campaign, as one zip in the Downloads folder. Returns its path."""
    runs_dir, _ = _folder(campaign)
    files = sorted(glob.glob(os.path.join(runs_dir, "*.json*"))) if runs_dir else []
    files = [f for f in files if not f.endswith(".tmp")]
    if not files:
        raise ValueError("This campaign has no recorded scenarios to export yet.")
    out_dir = os.path.join(os.environ.get("USERPROFILE", ""), "Downloads")
    if not os.path.isdir(out_dir):
        out_dir = os.path.expanduser("~")
    path = os.path.join(out_dir, f"Frosthaven stats - {_safe(campaign)} - {dt.date.today():%Y-%m-%d}.zip")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_STORED) as z:          # the records are gzipped already
        for f in files:
            z.write(f, os.path.basename(f))
    return path, len(files)


def _records_in(data, filename):
    """(name, record) for each record in an upload: a zip of records, or one .json.gz / .json record."""
    def parse(name, raw):
        try:
            if raw[:2] == b"\x1f\x8b":
                raw = gzip.decompress(raw)
            rec = json.loads(raw.decode("utf-8"))
        except (OSError, ValueError, EOFError):
            raise ValueError("not a readable battle record") from None
        if not isinstance(rec, dict) or not rec.get("scenario") or not rec.get("actors"):
            raise ValueError("not a battle record")
        if rec.get("inProgress"):
            raise ValueError("an unfinished scenario")
        return rec
    if data[:2] == b"PK":
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            if sum(i.file_size for i in z.infolist()) > MAX_IMPORT:
                raise ValueError("That zip is too big to be battle records.")
            for info in z.infolist():
                base = os.path.basename(info.filename.replace("\\", "/"))
                if info.is_dir() or not (base.endswith(".json.gz") or base.endswith(".json")):
                    continue
                try:
                    yield base, parse(base, z.read(info))
                except Exception as ex:
                    yield base, ex
    else:
        base = os.path.basename(filename or "record.json.gz")
        try:
            yield base, parse(base, data)
        except Exception as ex:
            yield base, ex


def import_records(campaign, data, filename):
    """Adds the records in an upload to a campaign, skipping play-throughs it already has. Returns a summary."""
    if not campaign:
        raise ValueError("Pick a campaign at the top first.")
    d = game.mods_dir()
    if not d:
        raise ValueError("Frosthaven wasn't found.")
    runs_dir, _ = _folder(campaign)
    runs_dir = runs_dir or os.path.join(d, "stats", "runs", _safe(campaign))
    os.makedirs(runs_dir, exist_ok=True)
    have = {s.get("key") for s in campaign_stats(campaign)["runs"]}
    added, skipped, bad, other = [], 0, [], set()
    for name, rec in _records_in(data, filename):
        if isinstance(rec, Exception):
            bad.append(f"{name}: {rec}")
            continue
        key = run_key(rec)
        if key in have:
            skipped += 1
            continue
        if rec.get("campaign") and game._key(rec["campaign"]) != game._key(campaign):
            other.add(rec["campaign"])
        base = re.sub(r"\.json(\.gz)?$", "", name)
        target = os.path.join(runs_dir, "imported-" + _safe(base) + ".json.gz")
        with gzip.open(target, "wt", encoding="utf-8") as f:
            json.dump(rec, f, separators=(",", ":"))
        have.add(key)
        s = summarize(rec)
        added.append(f"#{s.get('num') or '?'} {s.get('name')} ({s.get('outcome')})")
    return {"added": added, "skipped": skipped, "bad": bad, "otherCampaigns": sorted(other)}
