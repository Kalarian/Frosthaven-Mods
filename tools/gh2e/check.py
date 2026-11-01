"""Check one 2e class's card files end to end, in folders of its own (several classes can be checked at once):

    python tools/gh2e/check.py Bruiser

1. tools/gh2e/build.py for the class into <temp>/gh2e-check/<Class>/build
2. tools/gh_port.py --edition 2 <Class> into <temp>/gh2e-check/<Class>/pack, which runs packcheck (unknown keys, dead
   enhancement slots, empty halves, known engine traps) and rulecheck (Frosthaven's own rule loaders over the files)
3. text keys: every $key$ the converted files use has English (gh_port lists the ones it can't find)

Exit code 0 only when all of that is clean: no build warnings, no packcheck problem, rulecheck passes, no text key
missing. Prints the converted pack folder, so the Frosthaven-form files can be read.
"""
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent


def main(cls):
    root = Path(tempfile.gettempdir()) / "gh2e-check" / cls
    build_dir, pack = root / "build", root / "pack"
    env = dict(os.environ, GH2E_BUILD=str(build_dir), GH_PORT_RULECHECK_NO_BUILD="1", PYTHONIOENCODING="utf-8")
    ok = True
    r = subprocess.run([sys.executable, str(HERE / "build.py"), cls], env=env, capture_output=True, text=True, encoding="utf-8")
    print(r.stdout + r.stderr)
    if r.returncode != 0 or "warning:" in r.stdout:
        ok = False
    r = subprocess.run([sys.executable, str(TOOLS / "gh_port.py"), "--edition", "2", cls, "--out", str(pack)],
                       env=env, capture_output=True, text=True, encoding="utf-8")
    out = r.stdout + r.stderr
    print(out)
    if r.returncode != 0:
        ok = False
    m = re.search(r"(\d+) text keys not in Gloomhaven's English text either", out)
    if m:
        ok = False
        print(f"CHECK: {m.group(1)} text keys have no English: add them to tools/gh2e/cards/{cls}/strings.tsv")
    if "warning:" in out:
        print("CHECK: gh_port warnings above: fix them or note why each is fine in NOTES.md")
    print(f"converted pack: {pack}")
    print("CHECK", "PASSED" if ok else "FAILED", cls)
    return 0 if ok else 1


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    if not os.environ.get("GH2E_SKIP_RULECHECK_BUILD"):
        subprocess.run(["dotnet", "build", str(TOOLS / "rulecheck"), "-c", "Release", "-v", "q", "-nologo"],
                       capture_output=True)
    sys.exit(main(sys.argv[1]))
