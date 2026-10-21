"""Build a campaign stats page from the Frosthaven Mods battle stats.

    python tools/campaign_stats.py [campaign file or stats folder] [-o page.html]

The mod's Battle stats rule keeps one file per campaign in <game>\\FrosthavenMods\\stats\\campaign-<name>.txt: running
totals per character, and (since the update that added this page) every scenario's numbers. With no argument, every campaign file in
the default Steam folder gets a page next to it (campaign-<name>.html). The page is one self-contained file: it works
offline and can be shared as is.

Each player's PC keeps its own file; the host's covers every co-op scenario.
"""

import argparse
import html
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

DEFAULT_STATS = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Frosthaven\FrosthavenMods\stats")

# The game's own 8 rows, if the file has no "label" lines for them (files from before history was kept).
VANILLA = {
    "v:GUI_RESULTS_ENEMIES": "Enemies killed",
    "v:GUI_RESULTS_DAMAGE_DONE": "Damage dealt",
    "v:GUI_RESULTS_DAMAGE_TAKEN": "Damage taken",
    "v:GUI_RESULTS_HEALING": "Healing done",
    "v:GUI_RESULTS_LOOTCOLECTED": "Loot collected",
    "v:GUI_RESULTS_CHESTS": "Chests opened",
    "v:GUI_RESULTS_XP": "XP gained",
    "v:GUI_RESULTS_ITEMS_SPENT": "Items used",
}


def num(text):
    try:
        return float(text)
    except ValueError:
        return None


def pretty_class(class_id):
    base = re.sub(r"ID$", "", class_id or "?")
    return re.sub(r"(?<=[a-z])(?=[A-Z])", " ", base)


def scenario_name(level_id):
    """FS_006_Avalanche -> (6, 'Avalanche')."""
    m = re.match(r"^[A-Za-z]+_(\d+)_?(.*)$", level_id or "")
    if not m:
        return None, (level_id or "?").replace("_", " ")
    name = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", m.group(2).replace("_", " ")).strip()
    return int(m.group(1)), name or level_id


def parse(path):
    labels, rows, totals, games, perf = dict(VANILLA), [], defaultdict(dict), [], defaultdict(dict)
    counted = 0
    for line in Path(path).read_text("utf-8", "replace").splitlines():
        p = line.split("\t")
        kind = p[0]
        if kind == "scenario":
            counted += 1
        elif kind == "stat" and len(p) == 4 and num(p[3]) is not None:
            totals[p[1]][p[2]] = num(p[3])
        elif kind == "label" and len(p) == 3:
            labels[p[1]] = p[2]
        elif kind == "row" and len(p) == 6:
            rows.append({"key": p[1], "label": p[1], "section": p[2], "agg": p[3], "format": p[4], "ratio": p[5] == "ratio"})
        elif kind == "game" and len(p) == 7:
            n, name = scenario_name(p[3])
            games.append({"key": p[1], "time": p[2], "id": p[3], "num": n, "name": name, "result": p[4],
                          "rounds": int(num(p[5]) or 0), "level": int(num(p[6]) or 0)})
        elif kind == "perf" and len(p) == 7:
            entry = perf[p[1]].setdefault(p[2], {"level": int(num(p[3]) or 0), "v": {}})
            entry["v"][p[4]] = [num(p[5]) or 0, num(p[6]) if p[6] != "" else None]

    vanilla_keys = sorted({k for t in totals.values() for k in t if k.startswith("v:")} |
                          {k for g in perf.values() for c in g.values() for k in c["v"] if k.startswith("v:")},
                          key=lambda k: list(VANILLA).index(k) if k in VANILLA else 99)
    all_rows = [{"key": k, "label": labels.get(k, k[2:]), "section": "THE GAME'S ROWS", "agg": "Sum", "format": "0",
                 "ratio": False} for k in vanilla_keys]
    if not rows:  # a file from before "row" lines: rebuild the list from the totals (no sections or formats)
        names = {k for t in totals.values() for k in t if not k.startswith("v:") and not k.endswith("/den") and k != "scenarios"}
        rows = [{"key": k, "label": k, "section": "MOD ROWS", "agg": "Max" if k.startswith("Biggest") else "Sum",
                 "format": "0.0" if any(k + "/den" in t for t in totals.values()) else "0",
                 "ratio": any(k + "/den" in t for t in totals.values())} for k in sorted(names)]
    all_rows += rows

    chars = []
    for key in sorted(set(totals) | {c for g in perf.values() for c in g}):
        cls, _, name = key.partition("|")
        chars.append({"key": key, "cls": pretty_class(cls), "name": name or pretty_class(cls)})
    in_history = {g["key"] for g in games}
    return {
        "campaign": Path(path).stem.replace("campaign-", "", 1),
        "rows": all_rows, "chars": chars, "totals": totals, "games": games,
        "perf": {k: v for k, v in perf.items() if k in in_history},
        "counted": counted, "without_history": max(0, counted - len(in_history)),
    }


def build(data):
    payload = json.dumps(data, separators=(",", ":")).replace("</", "<\\/")
    title = f"{data['campaign']}: campaign stats"
    return PAGE.replace("__TITLE__", html.escape(title)).replace("__DATA__", payload)


PAGE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
:root { --bg:#f6f4ef; --card:#fff; --ink:#1d1d20; --muted:#666; --line:#ddd6c8; --accent:#7a5a1e; --good:#2e8b45; --bad:#c0392b; --hit:#ffe58a; --bar:#e9e3d6;
  --c0:#3a6fb0; --c1:#c0392b; --c2:#2e8b45; --c3:#b8860b; --c4:#7d4ab0; --c5:#1f8a8a; --c6:#c2185b; --c7:#5d6d7e; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg:#16171a; --card:#202226; --ink:#e8e6e1; --muted:#9a9a9a; --line:#33363c; --accent:#d9b56a; --hit:#6b5a12; --bar:#2c2f35;
  --c0:#6aa0e0; --c1:#e5735f; --c2:#5cc27a; --c3:#e0b440; --c4:#ad84e0; --c5:#4fc0c0; --c6:#ec6a9c; --c7:#9aa8b6; } }
:root[data-theme="dark"] { --bg:#16171a; --card:#202226; --ink:#e8e6e1; --muted:#9a9a9a; --line:#33363c; --accent:#d9b56a; --hit:#6b5a12; --bar:#2c2f35;
  --c0:#6aa0e0; --c1:#e5735f; --c2:#5cc27a; --c3:#e0b440; --c4:#ad84e0; --c5:#4fc0c0; --c6:#ec6a9c; --c7:#9aa8b6; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--ink); font:15px/1.5 system-ui, Segoe UI, sans-serif; }
.bar { position:sticky; top:0; z-index:5; background:var(--bg); border-bottom:1px solid var(--line); padding:12px 16px; }
.bar h1 { font-size:18px; margin:0 0 4px; } .bar h1 small { color:var(--muted); font-weight:400; }
.sub { color:var(--muted); font-size:13px; }
nav { display:flex; flex-wrap:wrap; gap:6px; margin-top:8px; }
nav button, .seg button { font:inherit; font-size:14px; padding:4px 12px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--ink); cursor:pointer; }
nav button.on, .seg button.on { background:var(--accent); border-color:var(--accent); color:var(--bg); }
.who { display:flex; flex-wrap:wrap; gap:10px; margin-top:8px; font-size:14px; }
.who label { display:flex; align-items:center; gap:5px; cursor:pointer; }
.dot { display:inline-block; width:11px; height:11px; border-radius:50%; background:var(--c); flex:0 0 auto; }
main { padding:12px 16px 60px; max-width:1200px; margin:0 auto; }
section.view { display:none; } section.view.on { display:block; }
.card { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:12px 16px; margin:12px 0; }
h2 { font-size:16px; margin:18px 0 6px; color:var(--accent); } h3 { margin:0 0 6px; font-size:17px; }
.tools { display:flex; flex-wrap:wrap; gap:10px; align-items:center; margin:8px 0; }
input[type=search], select { font:inherit; font-size:14px; padding:5px 8px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--ink); }
input[type=search] { width:100%; max-width:320px; }
.seg { display:inline-flex; gap:0; } .seg button { border-radius:0; } .seg button:first-child { border-radius:6px 0 0 6px; } .seg button:last-child { border-radius:0 6px 6px 0; }
.chars { display:grid; grid-template-columns:repeat(auto-fill, minmax(260px, 1fr)); gap:12px; }
.chars .card { margin:0; border-top:4px solid var(--c); }
.kv { display:grid; grid-template-columns:1fr auto; gap:2px 12px; font-size:14px; } .kv span:nth-child(even) { text-align:right; font-variant-numeric:tabular-nums; }
.muted { color:var(--muted); } .small { font-size:13px; }
.lb-sec { margin:16px 0 4px; font-weight:600; font-size:13px; letter-spacing:.04em; color:var(--muted); }
.lb { display:grid; grid-template-columns:minmax(150px, 220px) 1fr; gap:4px 14px; align-items:start; padding:6px 0; border-top:1px solid var(--line); }
.lb .name { font-size:14px; } .bars { display:flex; flex-direction:column; gap:3px; }
.b { display:grid; grid-template-columns:90px 1fr 64px; gap:8px; align-items:center; font-size:13px; }
.b .track { background:var(--bar); border-radius:3px; height:12px; overflow:hidden; } .b .fill { height:100%; background:var(--c); border-radius:3px; }
.b .val { text-align:right; font-variant-numeric:tabular-nums; } .b.lead .val { font-weight:700; }
.b .who2 { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
table { border-collapse:collapse; width:100%; font-size:14px; }
th, td { padding:5px 8px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; }
th { font-size:13px; color:var(--muted); font-weight:600; cursor:pointer; user-select:none; white-space:nowrap; }
td.n, th.n { text-align:right; font-variant-numeric:tabular-nums; }
.tablewrap { overflow-x:auto; }
.win { color:var(--good); font-weight:600; } .lose { color:var(--bad); font-weight:600; }
mark { background:var(--hit); color:inherit; padding:0 2px; border-radius:2px; }
svg text { fill:var(--muted); font-size:11px; } .grid line { stroke:var(--line); }
.empty { color:var(--muted); font-style:italic; padding:20px 0; }
@media (max-width:640px) { .lb { grid-template-columns:1fr; } .b { grid-template-columns:70px 1fr 54px; } }
</style></head>
<body>
<div class="bar">
  <h1 id="title"></h1>
  <div class="sub" id="sub"></div>
  <nav id="nav"></nav>
  <div class="who" id="who"></div>
</div>
<main>
  <section class="view" id="v-party"></section>
  <section class="view" id="v-boards">
    <div class="tools"><input type="search" id="lbq" placeholder="Find a stat..."><span class="seg" id="lbmode"></span></div>
    <div id="lb"></div>
  </section>
  <section class="view" id="v-records">
    <div class="tools"><input type="search" id="rq" placeholder="Find a stat..."></div>
    <div class="card tablewrap"><table id="rec"></table></div>
  </section>
  <section class="view" id="v-history">
    <div class="tools"><label class="small muted">Stat <select id="hstat"></select></label><input type="search" id="hq" placeholder="Find a scenario..."></div>
    <div class="card tablewrap"><table id="hist"></table></div>
  </section>
  <section class="view" id="v-trends">
    <div class="tools"><label class="small muted">Stat <select id="tstat"></select></label><span class="seg" id="tmode"></span></div>
    <div class="card"><div id="chart"></div></div>
  </section>
</main>
<script>
const D = __DATA__;
const $ = s => document.querySelector(s);
const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]));
const ROW = Object.fromEntries(D.rows.map(r => [r.key, r]));
const store = { get(k, d) { try { const v = localStorage.getItem("fhstats:" + k); return v === null ? d : JSON.parse(v); } catch { return d; } },
                set(k, v) { try { localStorage.setItem("fhstats:" + k, JSON.stringify(v)); } catch {} } };
D.chars.forEach((c, i) => { c.color = `var(--c${i % 8})`; c.label = c.name !== c.cls ? `${c.name} (${c.cls})` : c.cls; });
const CH = Object.fromEntries(D.chars.map(c => [c.key, c]));
let shown = new Set(store.get("shown", D.chars.map(c => c.key)).filter(k => CH[k]));
if (!shown.size) shown = new Set(D.chars.map(c => c.key));
const visible = () => D.chars.filter(c => shown.has(c.key));

function fmt(v, f) {
  if (v === null || v === undefined || !isFinite(v)) return "-";
  const dec = /\.0/.test(f || "") ? 1 : 0;
  let s = Math.abs(v) < 1e-9 ? (0).toFixed(dec) : v.toFixed(dec);
  if ((f || "").startsWith("+") && v > 0) s = "+" + s;
  return Number(s).toLocaleString(undefined, {minimumFractionDigits: dec, maximumFractionDigits: dec, signDisplay: (f || "").startsWith("+") ? "exceptZero" : "auto"});
}
const tot = (ch, k) => (D.totals[ch] || {})[k] ?? 0;
const played = ch => tot(ch, "scenarios");
function total(ch, r) { return r.ratio ? (tot(ch, r.key + "/den") > 0 ? tot(ch, r.key) / tot(ch, r.key + "/den") : null) : tot(ch, r.key); }
function perScenario(ch, r) { if (r.ratio || r.agg === "Max") return total(ch, r); const n = played(ch); return n ? tot(ch, r.key) / n : null; }
function gameValue(g, ch, k) { const e = (D.perf[g.key] || {})[ch]; if (!e || !e.v[k]) return null; const [n, d] = e.v[k]; return ROW[k]?.ratio ? (d > 0 ? n / d : null) : n; }
const scen = g => g.num !== null && g.num !== undefined ? `#${g.num} ${g.name}` : g.name;
const resClass = r => r === "Win" ? "win" : (r === "Lose" || r === "Resign") ? "lose" : "";

// ---- header
$("#title").innerHTML = `${esc(D.campaign)} <small>campaign stats</small>`;
const wins = D.games.filter(g => g.result === "Win").length;
$("#sub").textContent = `${D.counted} scenario${D.counted === 1 ? "" : "s"} recorded` +
  (D.games.length ? ` · ${wins} won, ${D.games.length - wins} lost · ${D.games[0].time.slice(0, 10)} to ${D.games[D.games.length - 1].time.slice(0, 10)}` : "") +
  (D.without_history ? ` · ${D.without_history} from before the mod kept scenario details (in totals only)` : "");
const VIEWS = [["party", "Party"], ["boards", "Leaderboards"], ["records", "Records"], ["history", "Scenarios"], ["trends", "Trends"]];
let view = VIEWS.some(([k]) => "#" + k === location.hash) ? location.hash.slice(1) : store.get("view", "party");
$("#nav").innerHTML = VIEWS.map(([k, t]) => `<button data-v="${k}">${t}</button>`).join("");
$("#nav").onclick = e => { const v = e.target.dataset.v; if (v) { view = v; store.set("view", v); try { history.replaceState(null, "", "#" + v); } catch {} render(); } };
$("#who").innerHTML = D.chars.map(c => `<label><input type="checkbox" value="${esc(c.key)}" ${shown.has(c.key) ? "checked" : ""}><span class="dot" style="--c:${c.color}"></span>${esc(c.label)}</label>`).join("");
$("#who").onchange = e => { e.target.checked ? shown.add(e.target.value) : shown.delete(e.target.value); store.set("shown", [...shown]); render(); };

function seg(el, options, key, def) {
  let cur = store.get(key, def);
  const draw = () => { el.innerHTML = options.map(([k, t]) => `<button data-k="${k}" class="${k === cur ? "on" : ""}">${t}</button>`).join(""); };
  el.onclick = e => { if (e.target.dataset.k) { cur = e.target.dataset.k; store.set(key, cur); draw(); render(); } };
  draw();
  return () => cur;
}
const lbMode = seg($("#lbmode"), [["total", "Campaign total"], ["avg", "Per scenario"]], "lbmode", "total");
const tMode = seg($("#tmode"), [["each", "Each scenario"], ["running", "Running total"]], "tmode", "each");
const statOptions = D.rows.map(r => `<option value="${esc(r.key)}">${esc(r.label)}</option>`).join("");
for (const [id, def] of [["#hstat", "v:GUI_RESULTS_DAMAGE_DONE"], ["#tstat", "Damage per round"]]) {
  const el = $(id); el.innerHTML = statOptions; const saved = store.get(id, def); el.value = ROW[saved] ? saved : D.rows[0]?.key;
  el.onchange = () => { store.set(id, el.value); render(); };
}
for (const id of ["#lbq", "#rq", "#hq"]) $(id).oninput = render;

// ---- party
const HEADLINE = ["v:GUI_RESULTS_ENEMIES", "v:GUI_RESULTS_DAMAGE_DONE", "Damage per round", "Biggest hit", "v:GUI_RESULTS_DAMAGE_TAKEN",
                  "v:GUI_RESULTS_HEALING", "Average modifier", "Crits (x2)", "Nulls", "v:GUI_RESULTS_XP"];
function bestGame(ch, k) {
  let best = null;
  for (const g of D.games) { const v = gameValue(g, ch, k); if (v !== null && (best === null || v > best.v)) best = {g, v}; }
  return best;
}
function party() {
  const cards = visible().map(c => {
    const rows = HEADLINE.filter(k => ROW[k]).map(k => `<span>${esc(ROW[k].label)}</span><span>${fmt(total(c.key, ROW[k]), ROW[k].format)}</span>`).join("");
    const b = bestGame(c.key, "v:GUI_RESULTS_DAMAGE_DONE");
    const lvl = D.games.map(g => (D.perf[g.key] || {})[c.key]?.level).filter(x => x).pop();
    return `<div class="card" style="--c:${c.color}"><h3>${esc(c.name)}</h3>
      <div class="small muted">${esc(c.cls)}${lvl ? ` · level ${lvl}` : ""} · ${played(c.key)} scenario${played(c.key) === 1 ? "" : "s"}</div>
      <div class="kv" style="margin-top:8px">${rows}</div>
      ${b ? `<div class="small muted" style="margin-top:8px">Best scenario: ${esc(scen(b.g))}, ${fmt(b.v, "0")} damage (${esc(b.g.time.slice(0, 10))})</div>` : ""}</div>`;
  }).join("");
  return `<h2>The party</h2><div class="chars">${cards || '<div class="empty">No characters selected.</div>'}</div>`;
}

// ---- leaderboards
function boards() {
  const q = $("#lbq").value.trim().toLowerCase(), mode = lbMode(), cs = visible();
  let out = "", sec = null;
  for (const r of D.rows) {
    if (q && !r.label.toLowerCase().includes(q) && !r.section.toLowerCase().includes(q)) continue;
    const vals = cs.map(c => ({c, v: mode === "avg" ? perScenario(c.key, r) : total(c.key, r)}));
    const max = Math.max(0, ...vals.map(x => x.v ?? 0)), min = Math.min(0, ...vals.map(x => x.v ?? 0)), span = (max - min) || 1;
    const top = vals.reduce((a, x) => x.v !== null && (a === null || x.v > a) ? x.v : a, null);
    if (r.section !== sec) { sec = r.section; out += `<div class="lb-sec">${esc(sec)}</div>`; }
    const note = r.agg === "Max" ? " <span class='small muted'>(best in one scenario)</span>" : r.ratio ? " <span class='small muted'>(over the campaign)</span>" : "";
    out += `<div class="lb"><div class="name">${esc(r.label)}${note}</div><div class="bars">` + vals.map(({c, v}) =>
      `<div class="b ${v !== null && v === top && top !== 0 ? "lead" : ""}" style="--c:${c.color}"><span class="who2">${esc(c.name)}</span>
       <span class="track"><span class="fill" style="display:block;width:${v === null ? 0 : Math.max(2, 100 * (v - min) / span)}%"></span></span>
       <span class="val">${fmt(v, mode === "avg" && !r.ratio && r.agg !== "Max" ? "0.0" : r.format)}</span></div>`).join("") + `</div></div>`;
  }
  return out || '<div class="empty">No stat matches.</div>';
}

// ---- records
function records() {
  if (!D.games.length) return '<tr><td class="empty">Records need scenario details. The mod keeps them from the first scenario played after the update that added this page.</td></tr>';
  const q = $("#rq").value.trim().toLowerCase(), cs = new Set(visible().map(c => c.key));
  let body = "";
  for (const r of D.rows) {
    if (q && !r.label.toLowerCase().includes(q)) continue;
    let best = null, worst = null;
    for (const g of D.games) for (const ch of Object.keys(D.perf[g.key] || {})) {
      if (!cs.has(ch)) continue;
      const v = gameValue(g, ch, r.key); if (v === null) continue;
      if (best === null || v > best.v) best = {g, ch, v, ties: 0}; else if (v === best.v) best.ties++;
      if (worst === null || v < worst.v) worst = {g, ch, v, ties: 0}; else if (v === worst.v) worst.ties++;
    }
    if (!best || (best.v === 0 && !r.ratio)) continue;
    const cell = x => `<td class="n">${fmt(x.v, r.format)}</td><td><span class="dot" style="--c:${CH[x.ch]?.color}"></span> ${esc(CH[x.ch]?.name)}</td><td>${esc(scen(x.g))} <span class="muted small">${esc(x.g.time.slice(0, 10))}${x.ties ? ` · first of ${x.ties + 1} times` : ""}</span></td>`;
    body += `<tr><td>${esc(r.label)}</td>${cell(best)}</tr>`;
    if (r.key === "Average modifier" && worst) body += `<tr><td>${esc(r.label)} <span class="muted small">(unluckiest)</span></td>${cell(worst)}</tr>`;
  }
  const longest = D.games.reduce((a, g) => !a || g.rounds > a.rounds ? g : a, null);
  if (longest && (!q || "longest scenario".includes(q))) body += `<tr><td>Longest scenario</td><td class="n">${longest.rounds} rounds</td><td>party</td><td>${esc(scen(longest))} <span class="muted small">${esc(longest.time.slice(0, 10))}</span></td></tr>`;
  return `<tr><th>Stat</th><th class="n">Record</th><th>Who</th><th>Scenario</th></tr>` + (body || '<tr><td colspan="4" class="empty">No stat matches.</td></tr>');
}

// ---- scenario history
let hsort = store.get("hsort", {col: 0, desc: true});
function history() {
  if (!D.games.length) return '<tr><td class="empty">No scenario details yet. The mod keeps them from the first scenario played after the update that added this page: play one with Battle stats on, then build the page again.</td></tr>';
  const k = $("#hstat").value, r = ROW[k], q = $("#hq").value.trim().toLowerCase(), cs = visible();
  const rows = D.games.map((g, i) => ({g, i, cells: cs.map(c => gameValue(g, c.key, k))})).filter(x => !q || scen(x.g).toLowerCase().includes(q));
  const keyOf = x => [x.i, scen(x.g), x.g.result, x.g.rounds, x.g.level, ...x.cells][hsort.col];
  rows.sort((a, b) => { const A = keyOf(a), B = keyOf(b); const c = (A ?? -1e18) < (B ?? -1e18) ? -1 : (A ?? -1e18) > (B ?? -1e18) ? 1 : 0; return hsort.desc ? -c : c; });
  const heads = ["Date", "Scenario", "Result", "Rounds", "Level", ...cs.map(c => c.name)];
  const th = heads.map((h, i) => `<th data-c="${i}" class="${i >= 3 ? "n" : ""}">${esc(h)}${hsort.col === i ? (hsort.desc ? " ▾" : " ▴") : ""}</th>`).join("");
  return `<tr>${th}</tr>` + rows.map(x => {
    const top = Math.max(...x.cells.filter(v => v !== null));
    return `<tr><td class="small">${esc(x.g.time)}</td><td>${esc(scen(x.g))}</td><td class="${resClass(x.g.result)}">${esc(x.g.result)}</td><td class="n">${x.g.rounds}</td><td class="n">${x.g.level}</td>` +
      x.cells.map(v => `<td class="n">${v !== null && v === top && top > 0 && cs.length > 1 ? `<mark>${fmt(v, r.format)}</mark>` : fmt(v, r.format)}</td>`).join("") + `</tr>`;
  }).join("") + (rows.length ? "" : '<tr><td colspan="9" class="empty">No scenario matches.</td></tr>');
}
$("#hist").onclick = e => { const c = e.target.closest("th")?.dataset.c; if (c !== undefined) { hsort = {col: +c, desc: hsort.col === +c ? !hsort.desc : true}; store.set("hsort", hsort); render(); } };

// ---- trends
function trends() {
  if (D.games.length < 2) return '<div class="empty">Trends need at least two scenarios with details (the mod keeps them from the update that added this page on).</div>';
  const k = $("#tstat").value, r = ROW[k], running = tMode() === "running", cs = visible();
  const W = 900, H = 320, L = 48, R = 12, T = 12, B = 34, n = D.games.length;
  const series = cs.map(c => {
    let sum = 0, den = 0, pts = [];
    D.games.forEach((g, i) => {
      const e = (D.perf[g.key] || {})[c.key]; if (!e || !e.v[k]) return;
      const [nu, de] = e.v[k];
      let v;
      if (!running) v = gameValue(g, c.key, k);
      else if (r.ratio) { sum += nu; den += de || 0; v = den > 0 ? sum / den : null; }
      else if (r.agg === "Max") { sum = Math.max(sum, nu); v = sum; }
      else { sum += nu; v = sum; }
      if (v !== null) pts.push([i, v, g]);
    });
    return {c, pts};
  });
  const all = series.flatMap(s => s.pts.map(p => p[1]));
  if (!all.length) return '<div class="empty">No values for this stat yet.</div>';
  let lo = Math.min(0, ...all), hi = Math.max(...all); if (hi === lo) hi = lo + 1;
  const x = i => L + (n === 1 ? 0 : (W - L - R) * i / (n - 1)), y = v => T + (H - T - B) * (1 - (v - lo) / (hi - lo));
  const ticks = [0, .25, .5, .75, 1].map(t => lo + (hi - lo) * t);
  let svg = `<svg viewBox="0 0 ${W} ${H}" width="100%" role="img" aria-label="${esc(r.label)} by scenario"><g class="grid">` +
    ticks.map(v => `<line x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/><text x="${L - 6}" y="${y(v) + 4}" text-anchor="end">${fmt(v, r.format)}</text>`).join("") + `</g>`;
  const every = Math.max(1, Math.ceil(n / 12));
  D.games.forEach((g, i) => { if (i % every === 0) svg += `<text x="${x(i)}" y="${H - 12}" text-anchor="middle">${esc(g.num !== null && g.num !== undefined ? "#" + g.num : i + 1)}</text>`; });
  for (const s of series) {
    if (!s.pts.length) continue;
    svg += `<polyline fill="none" stroke="${s.c.color}" stroke-width="2.2" points="${s.pts.map(p => `${x(p[0])},${y(p[1])}`).join(" ")}"/>`;
    svg += s.pts.map(p => `<circle cx="${x(p[0])}" cy="${y(p[1])}" r="3.5" fill="${s.c.color}"><title>${esc(s.c.name)}: ${fmt(p[1], r.format)} · ${esc(scen(p[2]))} (${esc(p[2].time.slice(0, 10))})</title></circle>`).join("");
  }
  svg += `</svg>`;
  const legend = cs.map(c => `<span style="margin-right:14px"><span class="dot" style="--c:${c.color}"></span> ${esc(c.name)}</span>`).join("");
  return `<h3>${esc(r.label)} <span class="small muted">${running ? (r.ratio ? "campaign average so far" : r.agg === "Max" ? "best so far" : "running total") : "each scenario"}</span></h3>${svg}<div class="small">${legend}</div><div class="small muted">Hover a point for the scenario.</div>`;
}

function render() {
  document.querySelectorAll("#nav button").forEach(b => b.classList.toggle("on", b.dataset.v === view));
  document.querySelectorAll("section.view").forEach(s => s.classList.toggle("on", s.id === "v-" + view));
  if (view === "party") $("#v-party").innerHTML = party();
  if (view === "boards") $("#lb").innerHTML = boards();
  if (view === "records") $("#rec").innerHTML = records();
  if (view === "history") $("#hist").innerHTML = history();
  if (view === "trends") $("#chart").innerHTML = trends();
}
render();
</script>
</body></html>
"""


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("source", nargs="?", default=str(DEFAULT_STATS), help="a campaign-*.txt file or a stats folder")
    ap.add_argument("-o", "--out", help="output .html (only with a single campaign file)")
    args = ap.parse_args()
    src = Path(args.source)
    files = [src] if src.is_file() else sorted(src.glob("campaign-*.txt"))
    if not files:
        sys.exit(f"no campaign-*.txt files in {src}")
    if args.out and len(files) > 1:
        sys.exit("-o needs a single campaign file")
    for f in files:
        data = parse(f)
        out = Path(args.out) if args.out else f.with_suffix(".html")
        out.write_text(build(data), "utf-8")
        print(f"{out}  ({data['counted']} scenarios, {len(data['games'])} with details, {len(data['chars'])} characters)")


if __name__ == "__main__":
    main()
