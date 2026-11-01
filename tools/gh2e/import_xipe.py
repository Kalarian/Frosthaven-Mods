"""Start a class's 2e card files from Xipe's Gloomhaven digital mod (Steam Workshop 3587771315, used with credit).

    python tools/gh2e/import_xipe.py Bruiser [--force]

Writes tools/gh2e/cards/<Class>/<NN>_<Card>.yml (one per card Xipe has, matched to the sheet by name, else by
initiative) and strings.tsv with English for his text keys, renamed GH2E_X_<key> unless Gloomhaven's own text has the
key. The files are a starting point only: each card is then corrected against its sheet (the image is the deciding
source; Xipe simplified on purpose).
"""
import csv
import difflib
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))
import build  # noqa: E402

XIPE = Path(r"C:\Program Files (x86)\Steam\steamapps\workshop\content\780290\3587771315")
XIPE_CLASS = {"Bruiser": "Bruiser", "Cragheart2": "Cragheart2", "Mindthief2": "Mindthief2", "SilentKnife": "Scoundrel2",
              "Spellweaver2": "Spellweaver2", "Tinkerer2": "Tinkerer2"}


def xipe_strings():
    out = {}
    raw = (XIPE / "LangUpdates" / "LangUpdate.csv").read_bytes()
    lines = []
    for ln in raw.split(b"\n"):   # the file mixes UTF-8 and Windows-1252 lines
        try:
            lines.append(ln.decode("utf-8"))
        except UnicodeDecodeError:
            lines.append(ln.decode("cp1252", errors="replace"))
    data = "\n".join(lines).lstrip("﻿")
    import io
    with io.StringIO(data, newline="") as f:
        r = csv.reader(f)
        next(r)
        for row in r:
            if len(row) > 1 and row[0]:
                out[row[0]] = row[1]
    return out


def main(cls, force):
    import gh_port
    gh, _ = gh_port.find_gloomhaven()
    gh_text = gh_port.gh_strings(gh)
    xs = xipe_strings()
    dest = HERE / "cards" / cls
    if dest.exists() and any(dest.glob("*.yml")) and not force:
        sys.exit(f"{dest} has card files already (--force to overwrite)")
    dest.mkdir(parents=True, exist_ok=True)
    sh = build.sheet(cls)["cards"]
    names = {c["name"].lower(): (i, c) for i, c in enumerate(sh, start=1)}
    used, strings, done = set(), {}, []
    for p in sorted((XIPE / "ModdedYML" / "Ability Cards").glob(f"*_{XIPE_CLASS[cls]}_*.yml")):
        xname = p.stem.split("_", 2)[2]
        s = p.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
        hit = names.get(xname.lower())
        if hit is None:
            close = difflib.get_close_matches(xname.lower(), list(names), n=1, cutoff=0.75)
            hit = names[close[0]] if close else None
        if hit is None:
            ini = re.search(r"^Initiative:\s*(\d+)", s, re.M)
            hit = next(((i, c) for i, c in names.values() if ini and c["initiative"] == int(ini.group(1))), None)
        if hit is None:
            print(f"  {p.name}: no sheet card matches; skipped")
            continue
        i, c = hit
        body = [ln for ln in s.split("\n") if ln.split(":", 1)[0] not in build.HEADER_KEYS]

        def key(m):
            k = m.group(1)
            if k in gh_text or k.startswith("GH2E_"):
                return m.group(0)
            if k in xs:
                strings[f"GH2E_X_{k}"] = xs[k]
                return f"$GH2E_X_{k}$"
            used.add(k)
            return m.group(0)
        text = re.sub(r"\$([A-Za-z0-9_]+)\$", key, "\n".join(body))
        out = f"# card: {c['name']}\n# from Xipe's {p.name}; corrected against tools/gh2e/sheets/{build.CLASSES[cls][0]}.json\n" + text.strip("\n") + "\n"
        (dest / f"{i:02d}_{re.sub(r'[^A-Za-z0-9]+', '', c['name'])}.yml").write_text(out, encoding="utf-8")
        done.append(c["name"])
    with open(dest / "strings.tsv", "w", encoding="utf-8") as f:
        f.write("# key<TAB>English (GH2E_X_*: Xipe's text, used with credit)\n")
        for k, v in sorted(strings.items()):
            f.write(f"{k}\t{v.replace(chr(10), chr(92) + 'n')}\n")
    missing = [c["name"] for c in sh if c["name"] not in done]
    print(f"{cls}: {len(done)} cards from Xipe; not in his mod: {', '.join(missing) or 'none'}")
    if used:
        print("  keys found in neither text table:", ", ".join(sorted(used)))


if __name__ == "__main__":
    main(sys.argv[1], "--force" in sys.argv)
