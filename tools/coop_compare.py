"""Compare two players' Frosthaven Mods co-op logs after a test session.

    python tools/coop_compare.py <host coop-log.txt> <client coop-log.txt> [--session N]

The logs are <game>\\FrosthavenMods\\coop-log.txt on each PC (the mod appends to it every launch). By default the last
session in each file is used; --session N picks the Nth session (1 = first) in both.

Checks:
- checkpoints (Ctrl+F8, pressed together by both players): each player's "local" snapshot must match the other's,
  and on the host the "local" copy must match the "host-check" copy (the authoritative one)
- lists failed commands (a failure on host-check = rolled back for everyone), rejected/applied gifts (resources and
  items), duplicate loot, rule changes
Exit code 0 when every compared checkpoint matches.
"""

import argparse
import re
import sys
from pathlib import Path

SNAP = re.compile(r"SNAPSHOT (?P<label>.+?) \[(?P<copy>[^\]]+)\] role=(?P<role>\S+) me=(?P<me>\S+) hash=(?P<hash>[0-9A-F]+)(?P<body>.*)$")


def sessions(path):
    out, cur = [], None
    for line in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
        if "===== session start" in line:
            cur = {"header": line.split("=====")[1].strip(), "lines": []}
            out.append(cur)
        elif cur is not None:
            cur["lines"].append(line)
    return out


def parse(lines):
    checkpoints, events = {}, []
    for line in lines:
        m = SNAP.search(line)
        if m:
            if m["label"].startswith("checkpoint #"):
                checkpoints.setdefault(m["label"], {})[m["copy"]] = m
            continue
        if any(k in line for k in ("FAILED", "GIVE ", "RULES ", "DUPLICATE LOOT")):
            events.append(line)
    return checkpoints, events


def fields(body):
    """' | A(..) gold=1 ... | herbs=... | rules=..' -> {part key: part}"""
    parts = [p.strip() for p in body.split("|") if p.strip()]
    return {p.split(" ")[0] if not p.startswith(("herbs=", "rules=")) else p.split("=")[0]: p for p in parts}


def diff(a, b):
    fa, fb = fields(a), fields(b)
    return [(k, fa.get(k, "(missing)"), fb.get(k, "(missing)")) for k in sorted(set(fa) | set(fb)) if fa.get(k) != fb.get(k)]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("host_log")
    ap.add_argument("client_log")
    ap.add_argument("--session", type=int, help="Nth session (1-based) in both files instead of the last")
    args = ap.parse_args()

    picked = []
    for path in (args.host_log, args.client_log):
        ss = sessions(path)
        if not ss:
            sys.exit(f"{path}: no sessions found (is it a coop-log.txt?)")
        s = ss[(args.session - 1) if args.session else -1]
        picked.append(s)
        print(f"{path}: {len(ss)} session(s); using '{s['header']}'")
    (host_cp, host_ev), (client_cp, client_ev) = parse(picked[0]["lines"]), parse(picked[1]["lines"])

    ok = True
    labels = sorted(set(host_cp) | set(client_cp), key=lambda l: int(l.split("#")[1]))
    print(f"\nCheckpoints: host {len(host_cp)}, client {len(client_cp)}")
    for label in labels:
        h, c = host_cp.get(label, {}), client_cp.get(label, {})
        hl, cl, hc = h.get("local"), c.get("local"), h.get("host-check")
        if hl is None or cl is None:
            print(f"  {label}: only on {'host' if hl else 'client'} (press Ctrl+F8 together)")
            ok = False
            continue
        problems = []
        if hl["hash"] != cl["hash"]:
            problems.append(("host vs client", diff(hl["body"], cl["body"])))
        if hc is not None and hc["hash"] != hl["hash"]:
            problems.append(("host local vs host-check", diff(hl["body"], hc["body"])))
        if not problems:
            print(f"  {label}: MATCH ({hl['hash']})")
            continue
        ok = False
        print(f"  {label}: MISMATCH")
        for what, d in problems:
            print(f"    {what}:")
            for key, a, b in d:
                print(f"      {key}\n        {a}\n        {b}")

    for name, ev in (("host", host_ev), ("client", client_ev)):
        print(f"\n{name} events ({len(ev)}):")
        for line in ev:
            print("  " + line)
    fails = [l for l in host_ev + client_ev if "FAILED" in l]
    print(f"\nFailed commands: {len(fails)} (a failure on host-check was rolled back for everyone; on the client it can mean it")
    print("tried something the host then refused - check the matching host line)")
    print("\nRESULT:", "PASS" if ok and labels else ("NO CHECKPOINTS" if not labels else "FAIL"))
    sys.exit(0 if ok and labels else 1)


if __name__ == "__main__":
    main()
