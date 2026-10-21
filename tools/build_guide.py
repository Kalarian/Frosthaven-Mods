"""Build the player guide PDF (docs/Frosthaven-Mods-Guide.pdf) from docs/guide/guide.html.

Prints the page with Chrome or Edge in headless mode (no window), with the version from VERSION filled in.
tools/publish.sh runs it for every release and attaches the PDF to the GitHub release.

    python tools/build_guide.py [--out PATH]
"""

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs" / "guide" / "guide.html"
OUT = ROOT / "docs" / "Frosthaven-Mods-Guide.pdf"
BROWSERS = [
    Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
    Path(r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"),
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=str(OUT))
    args = ap.parse_args()
    browser = next((b for b in BROWSERS if b.exists()), None)
    if browser is None:
        sys.exit("build_guide: no Chrome or Edge found to print the PDF")
    version = (ROOT / "VERSION").read_text("utf-8").strip()
    out = Path(args.out).resolve()
    with tempfile.TemporaryDirectory() as tmp:
        page = Path(tmp) / "guide.html"
        page.write_text(SOURCE.read_text("utf-8").replace("__VERSION__", version), "utf-8")
        if out.exists():
            out.unlink()
        subprocess.run([str(browser), "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                        f"--user-data-dir={Path(tmp) / 'profile'}", f"--print-to-pdf={out}", page.as_uri()],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
    if not out.exists() or out.stat().st_size < 10_000:
        sys.exit(f"build_guide: the PDF wasn't written ({out})")
    print(f"wrote {out} (v{version}, {out.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
