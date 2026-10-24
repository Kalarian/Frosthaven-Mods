"""Frosthaven mod pack pipeline: author mods as small patches, build a modded Base.ruleset, install/restore.

A mod is a folder mods/<Name>/ with
    mod.json             {"name", "description", "enabled": true|false}
    patches/<path>.diff  unified diff against the vanilla file at <path> inside the ruleset
    files/<path>         a brand-new file added to the ruleset (first line 'Parser: <Type>')
Patches carry only the changed lines plus context, so no full game files are published, and a patch that no
longer fits after a game update fails loudly instead of silently reverting the game's changes.

Authoring (vanilla files come from extracted/Base, the read-only extract):
    python tools/fh_mods.py edit <Mod> <ruleset/path.yml>...   copy vanilla files into work/<Mod>/ to edit
    python tools/fh_mods.py diff <Mod>                         turn work/<Mod>/ into the mod's patches + files
Building and testing:
    python tools/fh_mods.py build <out.ruleset> [--mod NAME ...]   (default: every enabled mod, by name)
    python tools/fh_mods.py install [--mod NAME ...]               build + install into the game, vanilla backed up
    python tools/fh_mods.py restore                                put the vanilla Base.ruleset back
    python tools/fh_mods.py status
    A content pack (a mod folder with a pack.json, e.g. mods/gloomhaven from tools/gh_port.py) is installed like any
    mod; its extras (pack.json, strings.tsv, art/) also go to <game>\\FrosthavenMods\\packs\\<folder>\\ for the plugin.
"""

import argparse
import difflib
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fh_ruleset as rs  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
MODS = ROOT / "mods"
WORK = ROOT / "work"
VANILLA_EXTRACT = ROOT / "extracted" / "Base"
GAME = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Frosthaven")
# Shared with the installer exe: vanilla backup + state live in the game folder, so both tools always agree.
BACKUP = GAME / "FrosthavenMods"
PACKS = BACKUP / "packs"  # the installed content packs' plugin files (pack.json, strings.tsv, art), per mod folder
STATE = BACKUP / "state.json"
GAME_RULESET = GAME / "Frosthaven_Data" / "StreamingAssets" / "Rulebase" / "Base.ruleset"
APP_MANIFEST = GAME.parent.parent / "appmanifest_2347080.acf"
NO_EOL = b"\\ No newline at end of file\n"


# ---------------------------------------------------------------- unified diffs on raw bytes (CRLF-safe)

def split_lines(data):
    return re.findall(rb"[^\n]*\n|[^\n]+$", data)


def make_diff(old, new, path):
    out = []
    lines = list(difflib.diff_bytes(difflib.unified_diff, split_lines(old), split_lines(new),
                                    fromfile=b"a/" + path.encode(), tofile=b"b/" + path.encode(), n=3))
    for i, line in enumerate(lines):
        if i >= 2 and not line.startswith(b"@@") and not line.endswith(b"\n"):
            out.append(line + b"\n")
            out.append(NO_EOL)
        else:
            out.append(line)
    return b"".join(out)


def parse_diff(diff):
    hunks, cur = [], None
    for line in diff.split(b"\n")[2:]:
        if not line:
            continue
        line += b"\n"  # split on \n only, so a CRLF line keeps its \r in the body
        if line.startswith(b"@@"):
            m = re.match(rb"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", line)
            if not m:
                raise ValueError(f"bad hunk header {line!r}")
            cur = {"old_start": int(m.group(1)), "old_len": int(m.group(2) or 1), "old": [], "new": []}
            hunks.append(cur)
        elif line == NO_EOL:
            last = cur["_last"]
            for side in last:
                cur[side][-1] = cur[side][-1][:-1]
        elif cur is not None and line[:1] in (b" ", b"-", b"+"):
            body, kind = line[1:], line[:1]
            sides = {b" ": ("old", "new"), b"-": ("old",), b"+": ("new",)}[kind]
            for side in sides:
                cur[side].append(body)
            cur["_last"] = sides
    return hunks


def context_counts(h):
    """Leading/trailing context lines shared by both sides of a hunk."""
    old, new = h["old"], h["new"]
    lead = 0
    while lead < min(len(old), len(new)) and old[lead] == new[lead]:
        lead += 1
    trail = 0
    while trail < min(len(old), len(new)) - lead and old[-1 - trail] == new[-1 - trail]:
        trail += 1
    return lead, trail


def apply_diff(data, diff, label, warn=print):
    lines = split_lines(data)
    shift = 0
    for n, h in enumerate(parse_diff(diff), 1):
        expected = (h["old_start"] - 1 if h["old_len"] else h["old_start"]) + shift
        lead, trail = context_counts(h)
        # Like GNU patch's default fuzz 2: if the exact hunk is gone, drop up to 2 outer context lines per side.
        for fuzz in range(3):
            a, b = min(fuzz, lead), min(fuzz, trail)
            old, new = h["old"][a:len(h["old"]) - b], h["new"][a:len(h["new"]) - b]
            if not old and fuzz:
                break
            candidates = [i for i in range(len(lines) - len(old) + 1) if lines[i:i + len(old)] == old]
            if candidates:
                break
        else:
            candidates = []
        if not candidates:
            raise ValueError(f"{label}: hunk {n} no longer matches the game file (game update?)")
        if fuzz:
            warn(f"warning: {label}: hunk {n} applied with fuzz {fuzz} (the game changed nearby lines)")
        pos = min(candidates, key=lambda i: abs(i - (expected + a)))
        lines[pos:pos + len(old)] = new
        shift += len(new) - len(old)
    return b"".join(lines)


# ---------------------------------------------------------------- mods

def load_mods(names=None):
    mods = []
    # Order: mod folder names, then paths, compared lowercased (the installer uses the same rule)
    for d in sorted((p for p in MODS.iterdir() if (p / "mod.json").exists()), key=lambda p: p.name.lower()) if MODS.exists() else []:
        meta = json.loads((d / "mod.json").read_text(encoding="utf-8"))
        if names is None and not meta.get("enabled", True):
            continue
        if names is not None and d.name not in names:
            continue
        mods.append((d, meta))
    if names is not None:
        missing = set(names) - {d.name for d, _ in mods}
        if missing:
            sys.exit(f"unknown mod(s): {', '.join(sorted(missing))}")
    return mods


def sorted_files(folder, suffix=""):
    if not folder.exists():
        return []
    files = [p for p in folder.rglob("*") if p.is_file() and p.name.endswith(suffix)]
    return sorted(files, key=lambda p: p.relative_to(folder).as_posix().lower())


def pack_extras(mod_dir):
    """A content pack's files for the plugin, beside its ruleset files: [(path relative to the pack folder, file)] for
    pack.json, strings.tsv, art/**, bundles/** (the 3D models, git-ignored, built by gh_port.py) and sounds/**; empty for an
    ordinary mod (no pack.json). They go to <game>\\FrosthavenMods\\packs\\<mod folder>\\ (here: install_pack_extras;
    friends: the zip's game/ files)."""
    if not (mod_dir / "pack.json").exists():
        return []
    out = [(p.name, p) for p in (mod_dir / "pack.json", mod_dir / "strings.tsv") if p.exists()]
    out += [(p.relative_to(mod_dir).as_posix(), p) for p in sorted_files(mod_dir / "art")]
    out += [(p.relative_to(mod_dir).as_posix(), p) for p in sorted_files(mod_dir / "bundles")]
    out += [(p.relative_to(mod_dir).as_posix(), p) for p in sorted_files(mod_dir / "sounds")]
    return out


def install_pack_extras(mods):
    """Replace <game>\\FrosthavenMods\\packs with the extras of the installed mods (same result as the installer)."""
    if PACKS.exists():
        shutil.rmtree(PACKS)
    n = 0
    for d, _ in mods:
        for rel, p in pack_extras(d):
            dst = PACKS / d.name / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(p, dst)
            n += 1
    return n


def build(vanilla_bytes, mods):
    entries = rs.read_ruleset(vanilla_bytes)
    by_name = {e["name"].lower(): e for e in entries}
    for d, meta in mods:
        for p in sorted_files(d / "patches", ".diff"):
            rel = p.relative_to(d / "patches").as_posix()[:-len(".diff")]
            e = by_name.get(rel.lower())
            if e is None:
                raise ValueError(f"{d.name}: patch target {rel} is not in the game's ruleset")
            e["data"] = apply_diff(e["data"], p.read_bytes(), f"{d.name}: {rel}")
        files = d / meta.get("files", "files")
        for p in sorted_files(files):
            rel = p.relative_to(files).as_posix()
            e = by_name.get(rel.lower())
            if e is not None:
                e["data"] = p.read_bytes()  # whole-file replacement
            else:
                new = {"name": rel, "parser": rs.parser_from_file(p), "binary": False, "data": p.read_bytes()}
                rs.insert_by_parser(entries, new)
                by_name[rel.lower()] = new
    return rs.write_ruleset(entries)


def cmd_edit(args):
    mod = MODS / args.mod
    if not (mod / "mod.json").exists():
        mod.mkdir(parents=True, exist_ok=True)
        (mod / "mod.json").write_text(json.dumps({"name": args.mod, "description": "", "enabled": True}, indent=2) + "\n",
                                      encoding="utf-8")
    for rel in args.paths:
        src = VANILLA_EXTRACT / rel
        if not src.is_file():
            sys.exit(f"not in the vanilla extract: {rel}")
        dst = WORK / args.mod / rel
        if dst.exists():
            print(f"already in work: {dst}")
            continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dst)
        print(f"edit: {dst}")


def cmd_diff(args):
    work = WORK / args.mod
    mod = MODS / args.mod
    if not work.exists():
        sys.exit(f"nothing in {work}")
    for p in sorted(work.rglob("*")):
        if not p.is_file():
            continue
        rel = p.relative_to(work).as_posix()
        if not rel.isascii():
            sys.exit(f"{rel}: use ASCII-only file names (the ruleset index stores plain bytes)")
        vanilla = VANILLA_EXTRACT / rel
        patch = mod / "patches" / (rel + ".diff")
        if vanilla.is_file():
            old, new = vanilla.read_bytes(), p.read_bytes()
            if old == new:
                patch.unlink(missing_ok=True)
                print(f"  = {rel} (unchanged, no patch)")
                continue
            d = make_diff(old, new, rel)
            if apply_diff(old, d, rel) != new:
                sys.exit(f"internal error: patch for {rel} does not reproduce the edit")
            patch.parent.mkdir(parents=True, exist_ok=True)
            patch.write_bytes(d)
            print(f"  ~ {rel}")
        else:
            dst = mod / "files" / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(p, dst)
            print(f"  + {rel} (new file)")


# ---------------------------------------------------------------- install / restore with a vanilla backup

def sha(data):
    return hashlib.sha256(data).hexdigest()


def steam_build_id():
    m = re.search(r'"buildid"\s+"(\d+)"', APP_MANIFEST.read_text(encoding="utf-8", errors="replace"))
    return m.group(1) if m else "unknown"


def read_state():
    return json.loads(STATE.read_text(encoding="utf-8")) if STATE.exists() else {}


def vanilla_backup():
    """Return the vanilla ruleset bytes, refreshing the backup when Steam has put a new vanilla file in place."""
    state = read_state()
    game = GAME_RULESET.read_bytes()
    game_sha = sha(game)
    backup = BACKUP / f"Base.ruleset.{state.get('buildid', '')}"
    if state and game_sha == state.get("installed_sha"):
        data = backup.read_bytes()
        if sha(data) != state["vanilla_sha"]:
            sys.exit(f"backup {backup} is damaged; use Steam 'Verify integrity of game files', then run again")
        return data
    if state and game_sha != state.get("vanilla_sha") and steam_build_id() == state.get("buildid"):
        sys.exit("the game's Base.ruleset is neither vanilla nor our last install. Use Steam 'Verify integrity "
                 "of game files' to get a clean copy, then run again.")
    rs.read_ruleset(game)  # sanity: it is a ruleset
    build_id = steam_build_id()
    BACKUP.mkdir(exist_ok=True)
    (BACKUP / f"Base.ruleset.{build_id}").write_bytes(game)
    # keep the installer's own fields (game files it placed, loader ownership): those files are still in the game folder
    keep = {k: v for k, v in state.items() if k in ("files", "loader_owned")}
    STATE.write_text(json.dumps({"buildid": build_id, "vanilla_sha": game_sha, "installed_sha": None,
                                 "mods": [], "version": None, **keep}, indent=2), encoding="utf-8")
    print(f"backed up vanilla Base.ruleset (Steam build {build_id})")
    return game


def cmd_build(args):
    data = build(vanilla_backup(), load_mods(args.mod))
    Path(args.out).write_bytes(data)
    print(f"wrote {args.out} ({len(data):,} bytes)")


def cmd_install(args):
    mods = load_mods(args.mod)
    vanilla = vanilla_backup()
    data = build(vanilla, mods)
    rs.read_ruleset(data)  # sanity re-read
    tmp = GAME_RULESET.with_suffix(".ruleset.tmp")
    tmp.write_bytes(data)
    tmp.replace(GAME_RULESET)
    extras = install_pack_extras(mods)
    state = read_state()
    state.update(installed_sha=sha(data), mods=[d.name for d, _ in mods], version="dev")
    STATE.write_text(json.dumps(state, indent=2), encoding="utf-8")
    print(f"installed {len(mods)} mod(s): {', '.join(state['mods']) or '(none)'}" + (f"; {extras} content pack file(s)" if extras else ""))


def cmd_restore(args):
    state = read_state()
    if not state:
        sys.exit("no backup recorded; nothing to restore")
    if sha(GAME_RULESET.read_bytes()) == state["vanilla_sha"]:
        print("game already has the vanilla Base.ruleset")
        return
    data = vanilla_backup()
    GAME_RULESET.write_bytes(data)
    if PACKS.exists():
        shutil.rmtree(PACKS)  # a pack's plugin files without its rules would only confuse the plugin
    state.update(installed_sha=None, mods=[], version=None)
    STATE.write_text(json.dumps(state, indent=2), encoding="utf-8")
    print("restored vanilla Base.ruleset")


def cmd_status(args):
    state = read_state()
    game_sha = sha(GAME_RULESET.read_bytes())
    kind = ("vanilla" if game_sha == state.get("vanilla_sha") else
            "modded by us" if game_sha == state.get("installed_sha") else "unknown (not tracked)")
    print(f"Steam build {steam_build_id()}, backup for build {state.get('buildid', '-')}")
    print(f"game Base.ruleset: {kind}")
    print(f"installed mods: {', '.join(state.get('mods') or []) or '-'}")
    for d, meta in load_mods([p.name for p in MODS.iterdir() if (p / 'mod.json').exists()] if MODS.exists() else []):
        print(f"  {'on ' if meta.get('enabled', True) else 'off'}  {d.name}  {meta.get('description', '')}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("edit"); p.add_argument("mod"); p.add_argument("paths", nargs="+"); p.set_defaults(fn=cmd_edit)
    p = sub.add_parser("diff"); p.add_argument("mod"); p.set_defaults(fn=cmd_diff)
    for name, fn in (("build", cmd_build), ("install", cmd_install)):
        p = sub.add_parser(name)
        if name == "build":
            p.add_argument("out")
        p.add_argument("--mod", action="append", help="build only these mods (repeatable)")
        p.set_defaults(fn=fn)
    sub.add_parser("restore").set_defaults(fn=cmd_restore)
    sub.add_parser("status").set_defaults(fn=cmd_status)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
