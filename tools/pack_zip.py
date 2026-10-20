"""Build the mod pack zip the installer embeds / downloads: version.txt + mods/<Name>/{mod.json, patches, files}.

    python tools/pack_zip.py [--mod NAME ...] [--out PATH]

Default: every enabled mod, written to dist/Frosthaven-Mods-pack.zip with the version from VERSION.
Before writing, the pack is built against the vanilla backup, so a patch that no longer applies stops the release.
The zip is deterministic (fixed timestamps, sorted entries) so an unchanged pack has an unchanged checksum.
"""

import argparse
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fh_mods  # noqa: E402

ROOT = fh_mods.ROOT
DEFAULT_OUT = ROOT / "dist" / "Frosthaven-Mods-pack.zip"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mod", action="append", help="pack only these mods (also disabled ones; for testing)")
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    args = ap.parse_args()

    version = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    mods = fh_mods.load_mods(args.mod)
    fh_mods.build(fh_mods.vanilla_backup(), mods)  # raises if any patch no longer applies

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    stamp = (2026, 1, 1, 0, 0, 0)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr(zipfile.ZipInfo("version.txt", stamp), version + "\n")
        for d, meta in mods:
            files = [d / "mod.json"] + fh_mods.sorted_files(d / "patches", ".diff") + fh_mods.sorted_files(d / "files")
            for p in files:
                info = zipfile.ZipInfo(f"mods/{d.name}/{p.relative_to(d).as_posix()}", stamp)
                info.compress_type = zipfile.ZIP_DEFLATED
                z.writestr(info, p.read_bytes())
    print(f"wrote {out} (v{version}, {len(mods)} mod(s): {', '.join(d.name for d, _ in mods) or 'none'})")


if __name__ == "__main__":
    main()
