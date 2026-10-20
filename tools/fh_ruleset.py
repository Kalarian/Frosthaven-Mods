"""Unpack / repack Frosthaven (Snapshot Games, Steam) .ruleset archives.

Format (from FH.YML.RulebaseFile in Assembly-CSharp.dll):
    Header (16 bytes):  char Check[12] = "02392fe1" (NUL padded), uint32 Entries
    Entry  (144 bytes): char FileName[128] (NUL padded, '/' separators)
                        int32 ParserType (YMLLoading.EYMLParserType)
                        int32 IsBinaryData (bool)
                        uint32 DataSectionOffset (relative to end of index)
                        uint32 FileSize
    Data: file bodies back to back. No checksum, no compression.

The game loads entries in index order, batching consecutive entries of the same
ParserType, and its own compiler sorts by (ParserType, FileName). Pack keeps the
original order from the manifest and slots new files into their parser group.

Usage:
    python fh_ruleset.py info   <file.ruleset>
    python fh_ruleset.py unpack <file.ruleset> <out_dir>
    python fh_ruleset.py pack   <unpacked_dir> <out.ruleset>
    python fh_ruleset.py verify <file.ruleset>      (unpack + repack in memory, byte compare)
"""

import argparse
import json
import os
import struct
import sys
from collections import Counter
from pathlib import Path, PurePosixPath

MAGIC = b"02392fe1"
HEADER = struct.Struct("<12sI")
ENTRY = struct.Struct("<128siiII")
MANIFEST = "_manifest.json"

PARSER_TYPES = {
    0: "None", 1: "RemoveYML", 2: "HeroSummon", 3: "AttackModifier", 4: "AttackModifierDeck",
    5: "Character", 6: "AbilityCard", 7: "ItemCard", 8: "Perk", 9: "Enhancements",
    10: "IconGlossary", 11: "EnemyCard", 12: "Enemy", 13: "MonsterData", 14: "TreasureTable",
    15: "Rooms", 16: "ScenarioAbility", 17: "Scenario", 18: "Difficulty", 19: "RoadEvent",
    20: "VisibilitySphere", 21: "Temple", 22: "Village", 23: "Quest", 24: "Message",
    25: "Headquarters", 26: "CityEvent", 27: "InitialEvents", 28: "BattleGoal",
    29: "PersonalQuest", 30: "StoreLocation", 31: "ItemConfig", 32: "AbilityCardConfig",
    33: "CharacterConfig", 34: "MonsterConfig", 35: "MapConfig", 36: "CharacterResources",
    37: "GameMode", 38: "FHScenario", 39: "FHItem", 40: "CampaignSection",
    41: "FHEnhancement", 42: "Mastery", 43: "PetCard", 255: "CustomLevel",
}
PARSER_IDS = {v: k for k, v in PARSER_TYPES.items()}
CUSTOM_LEVEL = 255


def read_ruleset(data):
    check, count = HEADER.unpack_from(data, 0)
    if check.rstrip(b"\0") != MAGIC:
        raise ValueError(f"not a Rulebase file (header {check!r})")
    base = HEADER.size + count * ENTRY.size
    entries = []
    for i in range(count):
        raw_name, ptype, is_bin, off, size = ENTRY.unpack_from(data, HEADER.size + i * ENTRY.size)
        name = raw_name.split(b"\0", 1)[0].decode("utf-8")
        start = base + off
        if start + size > len(data):
            raise ValueError(f"entry {name} runs past end of file")
        entries.append({"name": name, "parser": ptype, "binary": bool(is_bin),
                        "data": data[start:start + size]})
    return entries


def write_ruleset(entries):
    out = [HEADER.pack(MAGIC, len(entries))]
    offset = 0
    for e in entries:
        name = e["name"].encode("utf-8")
        if len(name) >= 128:
            raise ValueError(f"file name too long for the format (max 127 bytes): {e['name']}")
        out.append(ENTRY.pack(name, e["parser"], int(e["binary"]), offset, len(e["data"])))
        offset += len(e["data"])
    out.extend(e["data"] for e in entries)
    return b"".join(out)


def safe_rel_path(name):
    p = PurePosixPath(name)
    if p.is_absolute() or ".." in p.parts or ":" in name or not p.parts:
        raise ValueError(f"refusing unsafe entry path: {name!r}")
    return Path(*p.parts)


def parser_from_file(path):
    if path.suffix.lower() == ".lvldat":
        return CUSTOM_LEVEL
    with open(path, "r", encoding="utf-8-sig", errors="replace") as f:
        first = f.readline()
    if ":" not in first:
        raise ValueError(f"{path}: first line must be 'Parser: <Type>', got {first.strip()!r}")
    name = first.split(":", 1)[1].strip()
    if name not in PARSER_IDS or name == "None":
        raise ValueError(f"{path}: unknown parser type {name!r}")
    return PARSER_IDS[name]


def cmd_info(args):
    entries = read_ruleset(Path(args.ruleset).read_bytes())
    print(f"{len(entries)} entries")
    counts = Counter(PARSER_TYPES.get(e["parser"], str(e["parser"])) for e in entries)
    for k, v in counts.most_common():
        print(f"  {v:5d}  {k}")
    print("binary-serialized entries:", sum(e["binary"] for e in entries))


def cmd_unpack(args):
    src = Path(args.ruleset)
    out = Path(args.out_dir)
    entries = read_ruleset(src.read_bytes())
    if out.exists() and any(out.iterdir()) and not args.force:
        sys.exit(f"{out} is not empty; pass --force to overwrite")
    seen = set()
    manifest = {"source": str(src.resolve()), "entries": []}
    for e in entries:
        rel = safe_rel_path(e["name"])
        key = str(rel).lower()  # Windows is case-insensitive
        if key in seen:
            raise ValueError(f"duplicate entry (case-insensitive): {e['name']}")
        seen.add(key)
        dest = out / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(e["data"])
        manifest["entries"].append({"name": e["name"], "parser": PARSER_TYPES.get(e["parser"], e["parser"]),
                                    "binary": e["binary"]})
    (out / MANIFEST).write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    print(f"unpacked {len(entries)} files to {out}")


def collect_entries(folder):
    """Build the entry list from an unpacked folder, keeping manifest order."""
    folder = Path(folder)
    manifest_path = folder / MANIFEST
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {"entries": []}

    on_disk = {}
    for p in folder.rglob("*"):
        if p.is_file() and p.suffix.lower() in (".yml", ".lvldat") and p.name != MANIFEST:
            on_disk[p.relative_to(folder).as_posix().lower()] = p

    entries, used, removed = [], set(), []
    for m in manifest["entries"]:
        key = m["name"].lower()
        if key not in on_disk:
            removed.append(m["name"])
            continue
        p = on_disk[key]
        ptype = m["parser"] if isinstance(m["parser"], int) else PARSER_IDS[m["parser"]]
        entries.append({"name": m["name"], "parser": ptype, "binary": m.get("binary", False),
                        "data": p.read_bytes()})
        used.add(key)

    added = []
    for key in sorted(set(on_disk) - used):
        p = on_disk[key]
        added.append({"name": p.relative_to(folder).as_posix(), "parser": parser_from_file(p),
                      "binary": False, "data": p.read_bytes()})

    for new in added:
        insert_by_parser(entries, new)
    return entries, [a["name"] for a in added], removed


def insert_by_parser(entries, new):
    """Insert after the last entry of the same parser type, or before the first entry of a
    higher type, so parser groups stay contiguous (the loader batches consecutive types)."""
    idx = None
    for i, e in enumerate(entries):
        if e["parser"] == new["parser"]:
            idx = i + 1
    if idx is None:
        idx = next((i for i, e in enumerate(entries) if e["parser"] > new["parser"]), len(entries))
    entries.insert(idx, new)


def cmd_pack(args):
    out = Path(args.out_file).resolve()
    if "steamapps" in str(out).lower() and not args.allow_game_dir:
        sys.exit("refusing to write into the Steam folder; pass --allow-game-dir (back up Base.ruleset first)")
    entries, added, removed = collect_entries(args.folder)
    data = write_ruleset(entries)
    out.write_bytes(data)
    print(f"wrote {out} ({len(entries)} entries, {len(data):,} bytes)")
    for n in added:
        print(f"  + {n}")
    for n in removed:
        print(f"  - {n}")


def cmd_verify(args):
    data = Path(args.ruleset).read_bytes()
    rebuilt = write_ruleset(read_ruleset(data))
    if rebuilt == data:
        print(f"OK: round-trip is byte-identical ({len(data):,} bytes)")
    else:
        sys.exit("MISMATCH: repacked file differs from the original")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("info"); p.add_argument("ruleset"); p.set_defaults(fn=cmd_info)
    p = sub.add_parser("unpack"); p.add_argument("ruleset"); p.add_argument("out_dir")
    p.add_argument("--force", action="store_true"); p.set_defaults(fn=cmd_unpack)
    p = sub.add_parser("pack"); p.add_argument("folder"); p.add_argument("out_file")
    p.add_argument("--allow-game-dir", action="store_true"); p.set_defaults(fn=cmd_pack)
    p = sub.add_parser("verify"); p.add_argument("ruleset"); p.set_defaults(fn=cmd_verify)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
