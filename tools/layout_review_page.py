"""The layout review page for one ported class: each half beside its closest Frosthaven halves, with the rule breaks.

    python tools/layout_review_page.py <Class>   -> C:\\Claude\\FrosthavenCardShots\\review\\<class>-layout-review.html

A local page (the card images are the game's art; they stay on this machine). The half crops come from the card
images in C:\\Claude\\FrosthavenCardShots (tools/cardshots_all.py in the harness repo).
"""
import html
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import layout_review as L  # noqa: E402

OUT = L.SHOTS / "review"
# the two half panels of a 504x876 card image
HALF_BOX = {"top": (12, 186, 492, 492), "bottom": (12, 560, 492, 866)}

RULES = [
    ("R1", "One modifier pill per ability",
     "Every modifier of an ability (target, range, pierce, push, pull, jump, conditions) sits in one pill on that "
     "ability's own line, after its value and enhancement slot, comma-separated, each value with its own slot. Never "
     "stacked in a column, never on rows of their own, never half pill and half plain.",
     ["Trapper/Exploding Decoy", "Infuser/Obsidian Spear", "Boneshaper/Twisted Decree", "Drifter/Deadly Shot",
      "Shattersong/Illuminative Tone", "CrashingTide/Tidal Blast"]),
    ("R2", "Targets go in the pill; the default goes unsaid",
     "\"All adjacent enemies\" is the pill's target entry (◎ all adjacent ☠). A single adjacent target is the default "
     "and isn't printed (no \"Target one adjacent enemy\"). Targets that aren't a count (\"all enemies moved through\") "
     "become a rider sentence under the ability.",
     ["Deathwalker/Call of Doom", "Snowdancer/Blizzard", "Snowdancer/Chilling Impact", "Drifter/Bloodletting"]),
    ("R3", "Frosthaven's words",
     "Enemy and ally are icons (red skull, blue figure), not words. \"+1⚔\", never \"Add +1 Attack ⚔\". A condition "
     "is its icon alone, never its name in capitals.",
     ["CrashingTide/Crush Armor", "BannerSpear/Resolved Courage", "PainConduit/Shared Affliction", "BannerSpear/Lead from Afar"]),
    ("R4", "Consumes in the dashed box",
     "An element consume is the dashed box: the element (crossed), a colon, then what it adds.",
     ["BannerSpear/Deflecting Maneuver", "Snowdancer/White Winds", "Deathwalker/Medium"]),
    ("R5", "Infusions at the bottom",
     "The elements a half infuses are centred at the bottom of the half, apart from the abilities.",
     ["Snowdancer/Blizzard", "FrozenFist/Voice from Below", "Boneshaper/Twisted Decree"]),
    ("R6", "Dividers between abilities",
     "A thin divider separates two abilities (Move, then Attack); a rider sentence follows its own ability without one. "
     "The Brute already does this.", ["Drifter/No Remorse", "Deepwraith/Tumultuous Panic"]),
    ("R7", "Self-targeted abilities are outlined (to probe)",
     "Frosthaven draws a heal or shield on yourself inside an outlined box instead of a \"self\" label. Its cards are "
     "hand-made prefabs, so which layout element gives the same outline on a generated card needs a probe.",
     ["HIVE/Launch Pod", "HIVE/Interference"]),
]
ALREADY = ["Main lines: icon, value at 130%, enhancement slot right after the value",
           "Area hexes to the right of the attack", "Persistent and round symbols, XP stars, lost marks bottom right",
           "Use trackers as boxes under the text", "\"where X is …\" directly under an X value"]


def crop(src, half, dst):
    if not src:
        return None
    dst.parent.mkdir(parents=True, exist_ok=True)
    if not dst.exists():
        Image.open(src).convert("RGB").crop(HALF_BOX[half]).save(dst, quality=90)
    return dst.relative_to(OUT).as_posix()


def main():
    cls = sys.argv[1]
    data = json.loads(subprocess.run([sys.executable, str(Path(__file__).with_name("layout_review.py")), "match", cls, "2"],
                                     capture_output=True, text=True, encoding="utf-8").stdout)
    table = L.strings()
    rows, flagged = [], 0
    for e in data:
        p = e["port"]
        notes = L.check(p, table)
        flagged += bool(notes)
        mine = crop(p.get("shot"), p["half"], OUT / "img" / cls / f"{p['card']}-{p['half']}.jpg")
        fh = []
        for m in e["matches"]:
            img = crop(m.get("shot"), m["half"], OUT / "img" / m["class"] / f"{m['card']}-{m['half']}.jpg")
            fh.append(f'<figure><img src="{img}" alt=""><figcaption>{html.escape(m["class"])} · {html.escape(m["card"])} '
                      f'({m["half"]})</figcaption></figure>' if img else "")
        note_html = ("<ul>" + "".join(f"<li><b>{html.escape(n[:2])}</b> {html.escape(n[3:])}</li>" for n in notes) + "</ul>"
                     if notes else '<p class="ok">Matches Frosthaven.</p>')
        rows.append(f'<section class="{"flag" if notes else "ok"}"><h3>{html.escape(p["card"])} <span>{p["half"]}</span></h3>'
                    f'<div class="cmp"><figure class="mine"><img src="{mine}" alt=""><figcaption>Brute (ported)</figcaption></figure>'
                    f'{"".join(fh)}</div>{note_html}</section>')
    rules = "".join(f'<div class="rule"><h3>{r} · {html.escape(t)}</h3><p>{html.escape(d)}</p><p class="ev">Frosthaven: '
                    f'{html.escape(", ".join(x.split("/")[1] for x in ev))}</p></div>' for r, t, d, ev in RULES)
    page = f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{cls} layout review</title><style>
:root{{--bg:#16181c;--fg:#e8e6e1;--muted:#9a9790;--card:#20232a;--flag:#d9a441;--ok:#5fb37a;--line:#2f333b}}
body{{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,Segoe UI,sans-serif}}
main{{max-width:1200px;margin:0 auto;padding:24px 16px}} h1{{font-size:26px;margin:0 0 4px}} h2{{margin:32px 0 12px;font-size:20px}}
.sub{{color:var(--muted);margin:0 0 20px}} .rules{{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:12px}}
.rule,.already{{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 14px}} .rule h3{{margin:0 0 6px;font-size:15px}}
.rule p{{margin:0 0 6px}} .ev{{color:var(--muted);font-size:13px}}
section{{background:var(--card);border:1px solid var(--line);border-left:4px solid var(--ok);border-radius:8px;padding:12px 14px;margin:12px 0}}
section.flag{{border-left-color:var(--flag)}} section h3{{margin:0 0 8px;font-size:16px}} section h3 span{{color:var(--muted);font-weight:400}}
.cmp{{display:flex;gap:12px;flex-wrap:wrap}} figure{{margin:0;width:240px}} figure img{{width:100%;border-radius:6px;display:block}}
figure.mine img{{outline:2px solid var(--flag)}} figcaption{{color:var(--muted);font-size:12px;margin-top:4px}}
ul{{margin:10px 0 0;padding-left:20px}} .ok{{color:var(--ok);margin:8px 0 0}} .filters{{margin:8px 0}} label{{margin-right:14px}}
</style></head><body><main>
<h1>{cls}: card layout review</h1>
<p class="sub">{len(data)} halves · {flagged} break a rule · each Brute half beside the two closest Frosthaven halves (by what the half does, then its layout).
Frosthaven's cards are hand-made prefabs, so the rules come from how its cards look, not from its layout data.</p>
<h2>Rules</h2><div class="rules">{rules}<div class="already"><h3>Already the same as Frosthaven</h3><ul>{"".join(f"<li>{html.escape(a)}</li>" for a in ALREADY)}</ul></div></div>
<h2>Halves</h2><div class="filters"><label><input type="checkbox" id="only"> Only halves that break a rule</label></div>
{"".join(rows)}
</main><script>
document.getElementById('only').addEventListener('change',e=>{{for(const s of document.querySelectorAll('section.ok'))s.style.display=e.target.checked?'none':''}});
</script></body></html>"""
    OUT.mkdir(parents=True, exist_ok=True)
    f = OUT / f"{cls.lower()}-layout-review.html"
    f.write_text(page, encoding="utf-8")
    print(f, f"{flagged} of {len(data)} halves flagged")


if __name__ == "__main__":
    main()
