"""Build the mod pack zip the installer embeds / downloads.

    python tools/pack_zip.py [--mod NAME ...] [--out PATH]

Zip layout:
    version.txt
    mods/<Name>/{mod.json, patches/..., files/...}   ruleset mods (every enabled mod by default)
    game/...                                        files copied into the game folder as-is: the BepInEx loader
                                                    (loader/BepInEx_*) and each code plugin (plugins/<Name>, built here)

Before writing, the ruleset mods are built against the vanilla backup, so a patch that no longer applies stops the
release. The zip is deterministic (fixed timestamps, sorted entries), so an unchanged pack has an unchanged checksum.
"""

import argparse
import subprocess
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fh_mods  # noqa: E402

ROOT = fh_mods.ROOT
DEFAULT_OUT = ROOT / "dist" / "Frosthaven-Mods-pack.zip"
LOADER = ROOT / "loader" / "BepInEx_5.4.23.5"
PLUGINS = ROOT / "plugins"


def build_plugins():
    """dotnet build every plugin; returns [(zip path under game/, file)]."""
    out = []
    for proj in sorted(PLUGINS.glob("*/*.csproj"), key=lambda p: p.parent.name.lower()):
        r = subprocess.run(["dotnet", "build", "-c", "Release", "--nologo", "-v", "q", str(proj)],
                           capture_output=True, text=True)
        if r.returncode != 0:
            sys.exit(f"plugin build failed: {proj.parent.name}\n{r.stdout[-3000:]}{r.stderr[-2000:]}")
        dlls = sorted((proj.parent / "bin" / "Release").glob("*.dll"))
        if not dlls:
            sys.exit(f"plugin {proj.parent.name}: no DLL in bin/Release")
        for dll in dlls:
            out.append((f"BepInEx/plugins/{proj.parent.name}/{dll.name}", dll))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mod", action="append", help="pack only these ruleset mods (also disabled ones; for testing)")
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    args = ap.parse_args()

    version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    mods = fh_mods.load_mods(args.mod)
    fh_mods.build(fh_mods.vanilla_backup(), mods)  # raises if any patch no longer applies

    game_files = []
    plugins = build_plugins()
    if plugins:
        game_files += [(p.relative_to(LOADER).as_posix(), p) for p in sorted(LOADER.rglob("*"))
                       if p.is_file() and p.suffix.lower() != ".xml" and p.name != "README.md"]
        game_files += plugins

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    stamp = (2026, 1, 1, 0, 0, 0)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        def add(name, data):
            info = zipfile.ZipInfo(name, stamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data)

        add("version.txt", version + "\n")
        for d, meta in mods:
            files = [d / "mod.json"] + fh_mods.sorted_files(d / "patches", ".diff") + fh_mods.sorted_files(d / "files")
            for p in files:
                add(f"mods/{d.name}/{p.relative_to(d).as_posix()}", p.read_bytes())
        for rel, p in sorted(game_files, key=lambda t: t[0].lower()):
            add("game/" + rel, p.read_bytes())
    print(f"wrote {out} (v{version}, ruleset mods: {', '.join(d.name for d, _ in mods) or 'none'}; "
          f"plugins: {', '.join(sorted({r.split('/')[2] for r, _ in plugins})) or 'none'}; {len(game_files)} game files)")


if __name__ == "__main__":
    main()
