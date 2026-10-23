// The Stats tab: the whole party's battle records for the campaign picked in the header (one shared campaign, so the
// views are about the party, not one player). Data: GET /api/stats?campaign= -> {runs: [summary...], live: summary|null},
// built by companion/stats.py from the mod's records (only finished scenarios: wins, losses, resigns).
// Charts are small inline SVGs following the companion's dataviz rules: thin marks, 2px surface gaps, a legend for 2+
// series, text in text colours (never the series colour), a hover tooltip on every chart, and a table under each one.
(function () {
  "use strict";
  // categorical slots (dark steps, validated on the card surface #1b2027): one per character, in order of first appearance
  const COLORS = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];
  const OTHER = "#4f5762";        // "other"/"lost" fills: separates from every slot (validated); recessive by design
  const MONSTER_LINE = "#9aa1ab";  // the monsters' luck line: a lighter grey, so a 2px line stays visible
  const TOP = "#9085e9", BOTTOM = "#199e70";   // a card's top / bottom half (validated with OTHER)
  const SURFACE = "#1b2027", GRID = "#2c333d", AXIS = "#8d877b", INK = "#e8e2d4", DIM = "#a59f92";
  const ELEMENTS = ["Fire", "Ice", "Air", "Earth", "Light", "Dark"];
  const VIEWS = [["overview", "Overview"], ["scenario", "Scenario"], ["luck", "Luck"], ["damage", "Damage"],
    ["cards", "Cards & elements"], ["monsters", "Monsters"], ["records", "Records"]];

  const $ = s => document.querySelector(s);
  const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({"&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"}[c]));
  const fmt = v => v == null ? "—" : Math.abs(v) >= 10000 ? (v / 1000).toFixed(1).replace(/\.0$/, "") + "K" : (Math.round(v * 10) / 10).toLocaleString();
  const signed = v => (v > 0 ? "+" : v < 0 ? "−" : "") + fmt(Math.abs(v));
  const sum = xs => xs.reduce((a, b) => a + (b || 0), 0);

  let D = null, stamp = null, view = "overview", runKey = null, outcome = "all", loading = false;
  let notice = null;           // {tone, html}: the result of an export or import, shown above the view
  try { view = localStorage.getItem("stats-view") || "overview"; } catch (e) {}
  if (/^#stats\/\w+/.test(location.hash)) view = location.hash.split("/")[1];      // /#stats/luck opens that view

  // ---- characters: one identity per class + name across the campaign, coloured in order of first appearance
  let charOrder = [];
  const ckey = p => p.cls + "|" + p.name;
  function indexCharacters(runs) {
    const seen = new Map();
    for (const r of runs) for (const p of r.party || []) if (!seen.has(ckey(p))) seen.set(ckey(p), {key: ckey(p), cls: p.cls, name: p.name, className: p.className});
    charOrder = [...seen.values()];
    charOrder.forEach((c, i) => { c.color = COLORS[i] || OTHER; });
  }
  const charOf = key => charOrder.find(c => c.key === key) || {key, name: key, color: OTHER};
  const who = c => `${c.name} (${c.className || ""})`;

  // ---- the tooltip (one element; rows are [value, label, colour])
  const tip = document.createElement("div");
  tip.className = "stip"; tip.hidden = true; document.body.appendChild(tip);
  function showTip(ev, title, rows) {
    tip.textContent = "";
    if (title) { const h = document.createElement("div"); h.className = "stip-h"; h.textContent = title; tip.appendChild(h); }
    for (const [value, label, color] of rows) {
      const row = document.createElement("div"); row.className = "stip-r";
      if (color) { const k = document.createElement("span"); k.className = "stip-k"; k.style.background = color; row.appendChild(k); }
      const v = document.createElement("b"); v.textContent = value; row.appendChild(v);
      const l = document.createElement("span"); l.textContent = label; row.appendChild(l);
      tip.appendChild(row);
    }
    tip.hidden = false;
    const x = Math.min(ev.clientX + 14, window.innerWidth - tip.offsetWidth - 8);
    const y = Math.min(ev.clientY + 14, window.innerHeight - tip.offsetHeight - 8);
    tip.style.left = x + "px"; tip.style.top = y + "px";
  }
  const hideTip = () => { tip.hidden = true; };
  const TIPS = [];            // tooltip data for the current render, referenced by data-t on hit targets
  document.addEventListener("pointermove", ev => {
    const t = ev.target.closest && ev.target.closest("[data-t]");
    if (!t || !document.getElementById("stats").contains(t)) { hideTip(); return; }
    const d = TIPS[+t.dataset.t];
    if (d) showTip(ev, d.title, d.rows);
  });
  const tipRef = (title, rows) => { TIPS.push({title, rows}); return TIPS.length - 1; };

  // ---- chart helpers (SVG strings; every name escaped)
  // a clean axis top and step: whole-number steps when the data are counts
  function scale(v, whole) {
    if (v <= 0) return {max: 1, step: whole ? 1 : 0.25};
    const raw = v / 4, p = Math.pow(10, Math.floor(Math.log10(raw)));
    let step = [1, 2, 2.5, 5, 10].map(m => m * p).find(st => st >= raw);
    if (whole) step = Math.max(1, Math.ceil(step));
    let max = step * Math.ceil(v / step);
    if (max - v < step * 0.1) max += step;          // a little headroom above the tallest mark
    return {max, step};
  }
  function niceMax(v) {
    if (v <= 0) return 1;
    const p = Math.pow(10, Math.floor(Math.log10(v))), n = v / p;
    return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * p;
  }
  function legend(series, kind) {
    if (series.length < 2) return "";
    return `<div class="slegend">${series.map(s => `<span><i class="${kind === "line" ? "ln" : "sw"}" style="background:${s.color}"></i>${esc(s.name)}</span>`).join("")}</div>`;
  }
  function yAxis(W, H, L, T, PH, max, unit, step) {
    let g = "";
    step = step || max / 4;
    for (let v = 0; v <= max + 1e-9; v += step) {
      const y = T + PH - PH * v / max;
      g += `<line x1="${L}" x2="${W - 8}" y1="${y}" y2="${y}" stroke="${GRID}" stroke-width="1"/>` +
        `<text x="${L - 6}" y="${y + 4}" text-anchor="end" class="tick">${fmt(v)}</text>`;
    }
    return g + (unit ? `<text x="${L}" y="${T - 8}" class="tick">${esc(unit)}</text>` : "");
  }
  // stacked columns: cats along x, one stack per cat; the whole column is the hit target, listing every series
  const cap = (x, yy, bw, hh, color) => `<path d="M${x},${yy + hh} V${yy + Math.min(4, hh)} Q${x},${yy} ${x + Math.min(4, bw / 2)},${yy} H${x + bw - Math.min(4, bw / 2)} Q${x + bw},${yy} ${x + bw},${yy + Math.min(4, hh)} V${yy + hh} Z" fill="${color}"/>`;
  // columns: stacked (parts of one total) or grouped (side by side, for measures that aren't parts of a whole)
  function columns({cats, series, unit, height = 240, catTitle = c => c, grouped = false}) {
    const W = 900, H = height, L = 44, T = 22, B = 30, PW = W - L - 8, PH = H - T - B;
    const totals = cats.map((_, i) => sum(series.map(s => s.values[i] || 0)));
    const whole = series.every(s => s.values.every(v => !v || Number.isInteger(v)));
    const {max, step} = scale(grouped ? Math.max(0, ...series.flatMap(s => s.values.map(v => v || 0))) : Math.max(...totals, 0), whole);
    const band = PW / Math.max(cats.length, 1);
    const bw = grouped ? Math.min(24, (band * 0.7 - 2 * (series.length - 1)) / series.length) : Math.min(24, band * 0.62);
    let marks = "", hits = "", labels = "";
    const every = Math.ceil(cats.length / 24);
    cats.forEach((c, i) => {
      if (grouped) {
        const gw = bw * series.length + 2 * (series.length - 1);
        series.forEach((s, k) => {
          const v = s.values[i] || 0; if (v <= 0) return;
          const h = PH * v / max;
          marks += cap(L + band * i + (band - gw) / 2 + k * (bw + 2), T + PH - h, bw, h, s.color);
        });
      }
      const x = L + band * i + (band - bw) / 2;
      let y = T + PH;
      const visible = grouped ? [] : series.filter(s => (s.values[i] || 0) > 0);
      visible.forEach((s, k) => {
        const h = PH * (s.values[i] || 0) / max, top = k === visible.length - 1;
        const hh = Math.max(h - (top ? 0 : 2), 0.5);
        const yy = y - h;
        marks += top ? cap(x, yy, bw, hh, s.color) : `<rect x="${x}" y="${yy + 2}" width="${bw}" height="${hh}" fill="${s.color}"/>`;
        y -= h;
      });
      const t = tipRef(catTitle(c, i), [...(grouped ? [] : [[fmt(totals[i]), "total", null]]), ...series.filter(s => s.values[i]).map(s => [fmt(s.values[i]), s.name, s.color])]);
      hits += `<rect class="hit" data-t="${t}" x="${L + band * i}" y="${T}" width="${band}" height="${PH}"/>`;
      if (i % every === 0) labels += `<text x="${L + band * i + band / 2}" y="${H - 10}" text-anchor="middle" class="tick">${esc(c)}</text>`;
    });
    return legend(series, "bar") + `<svg viewBox="0 0 ${W} ${H}" class="schart" role="img">${yAxis(W, H, L, T, PH, max, unit, step)}` +
      `<line x1="${L}" x2="${W - 8}" y1="${T + PH}" y2="${T + PH}" stroke="${AXIS}" stroke-width="1"/>${marks}${labels}${hits}</svg>`;
  }
  // lines: series of possibly different lengths over x = 1..n; a crosshair column per x lists every series
  function lines({series, unit, height = 260, xTitle = i => `#${i + 1}`, zero = false}) {
    const W = 900, H = height, L = 44, T = 22, B = 26, PW = W - L - 100, PH = H - T - B;
    const n = Math.max(...series.map(s => s.values.length), 1);
    const all = series.flatMap(s => s.values);
    let lo = Math.min(0, ...all), hi = Math.max(0, ...all);
    if (hi === lo) hi = lo + 1;
    const span = niceMax(Math.max(Math.abs(lo), Math.abs(hi)));
    let step;
    if (lo < 0) { lo = -span; hi = hi > 0 ? span : 0; step = (hi - lo) / 4; }
    else { const sc = scale(hi, all.every(v => Number.isInteger(v))); lo = 0; hi = sc.max; step = sc.step; }
    const X = i => L + (n === 1 ? PW / 2 : PW * i / (n - 1)), Y = v => T + PH - PH * (v - lo) / (hi - lo);
    let g = "";
    for (let v = lo; v <= hi + 1e-9; v += step) {
      const y = Y(v);
      g += `<line x1="${L}" x2="${L + PW}" y1="${y}" y2="${y}" stroke="${Math.abs(v) < 1e-9 ? AXIS : GRID}" stroke-width="1"/><text x="${L - 6}" y="${y + 4}" text-anchor="end" class="tick">${v > 0 && lo < 0 ? "+" : ""}${fmt(v)}</text>`;
    }
    if (unit) g += `<text x="${L}" y="${T - 8}" class="tick">${esc(unit)}</text>`;
    let paths = "", ends = "";
    const endYs = series.filter(s => s.values.length).map(s => Y(s.values[s.values.length - 1])).sort((a, b) => a - b);
    const labelsFit = series.length <= 4 && endYs.every((y, i) => !i || y - endYs[i - 1] >= 14);   // else the legend carries identity
    for (const s of series) {
      if (!s.values.length) continue;
      paths += `<path d="${s.values.map((v, i) => (i ? "L" : "M") + X(i) + "," + Y(v)).join(" ")}" fill="none" stroke="${s.color}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>`;
      const i = s.values.length - 1;
      ends += `<circle cx="${X(i)}" cy="${Y(s.values[i])}" r="4" fill="${s.color}" stroke="${SURFACE}" stroke-width="2"/>`;
      if (labelsFit) ends += `<text x="${X(i) + 8}" y="${Y(s.values[i]) + 4}" class="lbl">${esc(s.short || s.name)}</text>`;
    }
    let hits = "";
    const colW = n > 1 ? PW / (n - 1) : PW;
    for (let i = 0; i < n; i++) {
      const rows = series.filter(s => i < s.values.length).map(s => [(zero ? signed : fmt)(s.values[i]), s.name, s.color]);
      hits += `<rect class="hit col" data-t="${tipRef(xTitle(i), rows)}" x="${X(i) - colW / 2}" y="${T}" width="${colW}" height="${PH}"/>`;
    }
    return legend(series, "line") + `<svg viewBox="0 0 ${W} ${H}" class="schart" role="img">${g}${paths}${ends}${hits}</svg>`;
  }
  // turn position by round: x = round, y = place in that round's turn order (1st at the top); a dot per turn
  const ordinal = n => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : ["th", "st", "nd", "rd"][n % 10] || "th");
  function bump({rounds, series, height = 240}) {
    const W = 900, H = height, L = 44, T = 22, B = 26, PW = W - L - 100, PH = H - T - B;
    const maxPos = Math.max(2, ...series.flatMap(s => s.points.map(p => p.of)));
    const X = r => L + (rounds === 1 ? PW / 2 : PW * (r - 1) / (rounds - 1)), Y = pos => T + PH * (pos - 1) / (maxPos - 1);
    const {step} = scale(maxPos, true);
    let g = "";
    for (let v = 1; v <= maxPos; v += Math.max(1, step)) g += `<line x1="${L}" x2="${L + PW}" y1="${Y(v)}" y2="${Y(v)}" stroke="${GRID}" stroke-width="1"/><text x="${L - 6}" y="${Y(v) + 4}" text-anchor="end" class="tick">${ordinal(v)}</text>`;
    let marks = "", hits = "";
    for (const s of series) {
      const pts = s.points.filter(p => p.r >= 1 && p.r <= rounds);
      if (!pts.length) continue;
      marks += `<path d="${pts.map((p, i) => (i ? "L" : "M") + X(p.r) + "," + Y(p.pos)).join(" ")}" fill="none" stroke="${s.color}" stroke-width="2" stroke-linejoin="round" opacity="0.55"/>`;
      for (const p of pts) {
        marks += `<circle cx="${X(p.r)}" cy="${Y(p.pos)}" r="4" fill="${s.color}" stroke="${SURFACE}" stroke-width="2"/>`;
        hits += `<circle class="hit" data-t="${tipRef(`Round ${p.r}`, [[`${ordinal(p.pos)} of ${p.of} turns`, s.name, s.color], [p.inits.join(" and ") || "—", "initiatives of the two cards", null]])}" cx="${X(p.r)}" cy="${Y(p.pos)}" r="12"/>`;
      }
    }
    let labels = "";
    const every = Math.ceil(rounds / 24);
    for (let r = 1; r <= rounds; r += every) labels += `<text x="${X(r)}" y="${H - 6}" text-anchor="middle" class="tick">R${r}</text>`;
    return legend(series, "line") + `<svg viewBox="0 0 ${W} ${H}" class="schart" role="img">${g}${marks}${labels}${hits}</svg>`;
  }
  // horizontal stacked bars: one row per label, value at the tip
  function hbars({rows, series, unit = ""}) {
    const max = niceMax(Math.max(...rows.map(r => sum(r.values)), 0));
    return legend(series, "bar") + `<div class="hbars">${rows.map(r => {
      const total = sum(r.values);
      const segs = r.values.map((v, k) => v > 0 ? `<i style="width:${100 * v / max}%;background:${series[k].color}"></i>` : "").join("");
      const t = tipRef(r.label, [[fmt(total) + (unit ? " " + unit : ""), "total", null], ...series.map((s, k) => r.values[k] ? [fmt(r.values[k]), s.name, s.color] : null).filter(Boolean)]);
      return `<div class="hrow" data-t="${t}"><span class="hl">${esc(r.label)}</span><span class="hb">${segs}</span><span class="hv">${fmt(total)}</span></div>`;
    }).join("")}</div>`;
  }
  function table(head, rows, numeric = []) {
    return `<table class="stable"><tr>${head.map((h, i) => `<th${numeric.includes(i) ? ' class="n"' : ""}>${esc(h)}</th>`).join("")}</tr>` +
      rows.map(r => `<tr>${r.map((c, i) => `<td${numeric.includes(i) ? ' class="n"' : ""}>${c}</td>`).join("")}</tr>`).join("") + `</table>`;
  }
  const card = (title, sub, body, tableHtml) => `<div class="card scard"><h3>${esc(title)}</h3>${sub ? `<p class="note">${sub}</p>` : ""}${body}` +
    (tableHtml ? `<details class="stbl"><summary>Table</summary>${tableHtml}</details>` : "") + `</div>`;
  const tiles = ts => `<div class="tiles">${ts.map(([label, value, sub]) => `<div class="tile"><div class="tl">${esc(label)}</div><div class="tv">${esc(value)}</div>${sub ? `<div class="ts">${esc(sub)}</div>` : ""}</div>`).join("")}</div>`;
  const dot = c => `<i class="sw" style="background:${c.color}"></i>`;
  const outcomePill = o => `<span class="pill ${o === "Win" ? "win" : o === "InProgress" ? "live" : "loss"}">${esc(o === "InProgress" ? "In progress" : o === "Resign" ? "Resigned" : o === "Lose" ? "Lost" : o)}</span>`;
  const runTitle = r => `#${r.num ?? "?"} ${r.name}`;

  // ---- the data
  function runs() {
    const rs = (D && D.runs || []).filter(r => !r.error);
    return outcome === "all" ? rs : rs.filter(r => outcome === "win" ? r.outcome === "Win" : r.outcome !== "Win");
  }
  const runId = r => r.live ? "live" : r.file;
  function pickRun() {
    const all = [...(D.live ? [D.live] : []), ...[...(D.runs || [])].reverse()];
    return all.find(r => runId(r) === runKey) || all[0] || null;
  }
  // per character totals over a set of runs
  function totals(rs) {
    const t = new Map();
    for (const r of rs) for (const p of r.party) {
      const c = r.chars[p.cls]; if (!c) continue;
      const k = ckey(p);
      const a = t.get(k) || {key: k, runs: 0, rounds: 0, dealt: 0, taken: 0, healed: 0, kills: 0, hits: 0, biggest: 0, overkill: 0, melee: 0, ranged: 0, summons: 0, poison: 0, shielded: 0, noFields: 0, exhausted: 0, draws: [], deckN: 0, deckNulls: 0, deckX2: 0, luck: 0, lostToAvoid: 0, xp: 0, infused: {}, consumed: {}, cards: {}, cardDamage: {}};
      a.runs++; a.rounds += r.rounds || 0; a.exhausted += p.exhausted ? 1 : 0;
      for (const f of ["dealt", "taken", "healed", "kills", "hits", "overkill", "summons", "lostToAvoid"]) a[f] += c[f] || 0;
      a.xp += c.xpEarned || 0;
      if (c.melee == null) a.noFields++; else { a.melee += c.melee; a.ranged += c.ranged; a.poison += c.poison; a.shielded += c.shielded; }
      a.biggest = Math.max(a.biggest, c.biggest || 0);
      for (const d of c.draws) { a.draws.push(d); a.luck += d.luck; if (p.deck.n) { a.deckNulls += p.deck.nulls / p.deck.n; a.deckX2 += p.deck.x2s / p.deck.n; } }
      for (const [e, n] of Object.entries(c.infused || {})) a.infused[e] = (a.infused[e] || 0) + n;
      for (const [e, n] of Object.entries(c.consumed || {})) a.consumed[e] = (a.consumed[e] || 0) + n;
      for (const [id, v] of Object.entries(c.cards || {})) { const x = a.cards[id] || (a.cards[id] = {name: v.name, plays: 0, lost: 0}); x.plays += v.plays; x.lost += v.lost; }
      for (const [id, v] of Object.entries(c.cardDamage || {})) { const x = a.cardDamage[id] || (a.cardDamage[id] = {name: v.name, top: 0, bottom: 0, other: 0, hits: 0}); for (const f of ["top", "bottom", "other", "hits"]) x[f] += v[f] || 0; }
      t.set(k, a);
    }
    return charOrder.filter(c => t.has(c.key)).map(c => Object.assign(t.get(c.key), {c}));
  }
  const empty = msg => `<div class="card"><p class="note" style="margin:0">${msg}</p></div>`;

  // ---- views
  function viewOverview() {
    const rs = runs();
    if (!rs.length) return empty("No finished scenarios recorded for this campaign yet. With the <b>Battle stats</b> rule on (Options, Custom Rules), every won, lost or resigned scenario is recorded when the game saves its outcome.");
    const ts = totals(rs);
    const won = rs.filter(r => r.outcome === "Win").length;
    const kills = sum(ts.map(t => t.kills)), dealt = sum(ts.map(t => t.dealt)), rounds = sum(rs.map(r => r.rounds));
    const luck = sum(ts.map(t => t.luck));
    let h = tiles([["Scenarios", String(rs.length), `${won} won · ${rs.length - won} lost or resigned`], ["Rounds played", fmt(rounds), `${fmt(rounds / rs.length)} per scenario`],
      ["Damage dealt", fmt(dealt), `${fmt(dealt / Math.max(rounds, 1))} per round`], ["Monsters killed", fmt(kills), ""],
      ["Party luck", signed(luck), "modifier points vs. your decks"]]);
    const series = charOrder.map(c => ({name: who(c), color: c.color, values: rs.map(r => { const p = r.party.find(p => ckey(p) === c.key); return p ? (r.chars[p.cls] || {}).dealt || 0 : 0; })})).filter(s => sum(s.values));
    h += card("Damage dealt, scenario by scenario", "Each column is one finished scenario, split by who dealt the damage (summons count for their owner).",
      columns({cats: rs.map(r => "#" + (r.num ?? "?")), series, unit: "damage", catTitle: (c, i) => runTitle(rs[i]) + " · " + rs[i].outcome}),
      table(["Scenario", ...series.map(s => s.name)], rs.map((r, i) => [esc(runTitle(r)), ...series.map(s => fmt(s.values[i]))]), series.map((_, i) => i + 1)));
    h += `<div class="card scard"><h3>Scenarios</h3><p class="note">Click one to open it.</p>` + table(["When", "Scenario", "Level", "Outcome", "Rounds", "Damage dealt", "Damage taken", "Kills", "Exhausted"],
      [...rs].reverse().map(r => [esc((r.written || "").replace("T", " ").slice(0, 16)), `<a data-run="${esc(runId(r))}">${esc(runTitle(r))}</a>`, fmt(r.level), outcomePill(r.outcome), fmt(r.rounds),
        fmt(sum(Object.values(r.chars).map(c => c.dealt))), fmt(sum(Object.values(r.chars).map(c => c.taken))), fmt(sum(Object.values(r.chars).map(c => c.kills))),
        esc(r.party.filter(p => p.exhausted).map(p => p.name).join(", ") || "none")]), [2, 4, 5, 6, 7]) + `</div>`;
    return h;
  }

  function viewScenario() {
    const r = pickRun();
    if (!r) return empty("No scenarios recorded for this campaign yet.");
    const all = [...(D.live ? [D.live] : []), ...[...(D.runs || [])].reverse()];
    let h = `<div class="sbar2"><select id="srun">${all.map(x => `<option value="${esc(runId(x))}"${runId(x) === runId(r) ? " selected" : ""}>${esc((x.live ? "Live now: " : "") + runTitle(x) + (x.live ? ` (round ${x.rounds})` : " · " + (x.written || "").slice(0, 10)))}</option>`).join("")}</select>` +
      ` ${outcomePill(r.outcome)} <span class="note">Level ${fmt(r.level)} · ${fmt(r.rounds)} rounds${r.week != null ? " · week " + r.week : ""}</span></div>`;
    if (r.live) h += `<p class="note">This scenario is still being played. It updates at each of the game's autosaves (every round) and joins the campaign's stats once its outcome is saved.</p>`;
    const party = r.party.map(p => Object.assign({}, charOf(ckey(p)), {p, c: r.chars[p.cls] || {}}));
    const per = f => party.map(x => ({name: who(x), color: x.color, values: (x.c.byRound || {})[f] || []}));
    const rounds = Array.from({length: r.rounds || 1}, (_, i) => "R" + (i + 1));
    const T = f => sum(party.map(x => x.c[f] || 0));
    h += tiles([["Damage dealt", fmt(T("dealt")), `${fmt(T("dealt") / Math.max(r.rounds, 1))} per round`], ["Damage taken", fmt(T("taken")), ""], ["Healing", fmt(T("healed")), ""], ["Monsters killed", fmt(T("kills")), ""],
      ["Party luck", signed(sum(party.map(x => sum((x.c.draws || []).map(d => d.luck))))), "modifier points vs. your decks"]]);
    const roundTable = f => table(["Round", ...party.map(x => x.name)], rounds.map((rn, i) => [rn, ...party.map(x => fmt(((x.c.byRound || {})[f] || [])[i]))]), party.map((_, i) => i + 1));
    h += card("Damage dealt, round by round", "Who did the damage each round (summons count for their owner).", columns({cats: rounds, series: per("dealt"), unit: "damage", catTitle: c => "Round " + c.slice(1)}), roundTable("dealt"));
    h += card("Damage taken, round by round", "", columns({cats: rounds, series: per("taken"), unit: "damage", catTitle: c => "Round " + c.slice(1)}), roundTable("taken"));
    const hand = party.map(x => ({name: who(x), short: x.name, color: x.color, values: (x.c.hand || []).map(hh => hh.available ?? hh.hand)})).filter(s => s.values.length);
    if (hand.length) h += card("Cards still available", "Hand plus discard pile each round, after its two cards were picked. Resting only moves cards between them, so this drops as cards are lost; below 2, the character exhausts.", lines({series: hand, unit: "cards", xTitle: i => "Round " + (i + 1)}),
      table(["Round", ...hand.map(s => s.short)], Array.from({length: Math.max(...hand.map(s => s.values.length))}, (_, i) => [String(i + 1), ...hand.map(s => fmt(s.values[i]))]), hand.map((_, i) => i + 1)));
    const order = party.filter(x => (x.c.turnOrder || []).length);
    if (order.length) h += card("Turn order", "Where each character's turn fell in each round, counting every monster's and summon's turn too. Hover for that round's two initiatives.",
      bump({rounds: r.rounds || 1, series: order.map(x => ({name: who(x), short: x.name, color: x.color, points: x.c.turnOrder}))}),
      table(["Round", ...order.map(x => x.name)], Array.from({length: r.rounds || 1}, (_, i) => [String(i + 1), ...order.map(x => { const t = x.c.turnOrder.find(t => t.r === i + 1); return t ? `${t.pos} of ${t.of} <span class="note">(${t.inits.join(", ")})</span>` : "—"; })]), order.map((_, i) => i + 1)));
    h += `<div class="card scard"><h3>The party</h3>` + table(["", "Character", "Level", "Damage dealt", "Taken", "Healing", "Kills", "Biggest hit", "Overkill", "Luck", "Cards lost to avoid damage", "XP"],
      party.map(x => [dot(x), esc(who(x)) + (x.p.exhausted ? ' <span class="pill loss">exhausted</span>' : ""), fmt(x.p.level), fmt(x.c.dealt), fmt(x.c.taken), fmt(x.c.healed), fmt(x.c.kills), fmt(x.c.biggest), fmt(x.c.overkill),
        signed(sum((x.c.draws || []).map(d => d.luck))), fmt(x.c.lostToAvoid), fmt(x.p.xp)]), [2, 3, 4, 5, 6, 7, 8, 9, 10, 11]) + `</div>`;
    return h;
  }

  function viewLuck() {
    const rs = runs();
    if (!rs.length) return empty("No scenarios recorded yet.");
    const ts = totals(rs).filter(t => t.draws.length);
    const run = []; let acc = 0;
    const series = ts.map(t => { let a = 0; return {name: who(t.c), short: t.c.name, color: t.c.color, values: t.draws.map(d => (a += d.luck))}; });
    for (const r of rs) for (const d of r.monsters.draws || []) run.push((acc += d.luck));
    if (run.length) series.push({name: "Monsters", short: "Monsters", color: MONSTER_LINE, values: run});
    let h = `<div class="card"><p class="note" style="margin:0">Luck compares every modifier card drawn with the average card of the deck it came from. A <b>+1</b> drawn from a deck averaging +0 is +1 lucky. A null counts as −3 and a x2 as +3, about what they're worth on a typical attack. Above 0 means the draws beat the deck; it's not a skill score. Blesses and curses count as x2 and null draws.</p></div>`;
    h += card("Running luck over the campaign", "Each line adds up one character's draws, in order. The monsters' deck is the grey line.", lines({series, unit: "modifier points vs. the deck", zero: true, xTitle: i => `Draw ${i + 1}`}));
    const rows = ts.map(t => {
      const nulls = t.draws.filter(d => d.kind === "null").length, x2s = t.draws.filter(d => d.kind === "x2").length;
      return [dot(t.c), esc(who(t.c)), fmt(t.draws.length), `${nulls} <span class="note">(expected ${fmt(t.deckNulls)})</span>`, `${x2s} <span class="note">(expected ${fmt(t.deckX2)})</span>`, signed(t.luck / Math.max(t.draws.length, 1)), signed(t.luck)];
    });
    h += `<div class="card scard"><h3>Draws</h3>` + table(["", "Character", "Draws", "Nulls", "x2s", "Luck per draw", "Total luck"], rows, [2, 3, 4, 5, 6]) + `</div>`;
    return h;
  }

  function viewDamage() {
    const rs = runs();
    if (!rs.length) return empty("No scenarios recorded yet.");
    const ts = totals(rs);
    const kinds = [{name: "Melee", color: COLORS[0]}, {name: "Ranged", color: COLORS[1]}, {name: "Summons", color: COLORS[2]}, {name: "Other (retaliate, traps, effects)", color: OTHER}];
    const byCard = ts.map(t => {
      const cs = Object.values(t.cardDamage).filter(c => c.top + c.bottom + c.other > 0).sort((a, b) => (b.top + b.bottom + b.other) - (a.top + a.bottom + a.other)).slice(0, 10);
      if (!cs.length) return "";
      return `<div class="card scard"><h3>${dot(t.c)} ${esc(who(t.c))}</h3><p class="note">Damage by card, split by the half that dealt it.</p>` +
        hbars({rows: cs.map(c => ({label: c.name, values: [c.top, c.bottom, c.other]})), series: [{name: "Top", color: TOP}, {name: "Bottom", color: BOTTOM}, {name: "No card half (basic attack, summons, effects)", color: OTHER}], unit: "damage"}) +
        `<details class="stbl"><summary>Table</summary>${table(["Card", "Top", "Bottom", "Other", "Hits"], cs.map(c => [esc(c.name), fmt(c.top), fmt(c.bottom), fmt(c.other), fmt(c.hits)]), [1, 2, 3, 4])}</details></div>`;
    }).join("");
    const rows = ts.map(t => { const other = Math.max(0, t.dealt - t.melee - t.ranged - t.summons); return {label: who(t.c), values: [t.melee, t.ranged, t.summons, other]}; });
    const old = ts.some(t => t.noFields);
    let h = card("How the damage was dealt", (old ? "Scenarios recorded before melee/ranged were tracked count under Other. " : "") + "Summons count for their owner.", hbars({rows, series: kinds, unit: "damage"}),
      table(["Character", ...kinds.map(k => k.name)], rows.map(r => [esc(r.label), ...r.values.map(fmt)]), [1, 2, 3, 4]));
    h += `<div class="card scard"><h3>Per character</h3>` + table(["", "Character", "Scenarios", "Damage dealt", "Per round", "Hits", "Per hit", "Biggest hit", "Overkill (wasted)", "Poison bonus", "Damage taken", "Shield blocked"],
      ts.map(t => [dot(t.c), esc(who(t.c)), fmt(t.runs), fmt(t.dealt), fmt(t.dealt / Math.max(t.rounds, 1)), fmt(t.hits), fmt(t.dealt / Math.max(t.hits, 1)), fmt(t.biggest), fmt(t.overkill), t.noFields === t.runs ? "—" : fmt(t.poison), fmt(t.taken), t.noFields === t.runs ? "—" : fmt(t.shielded)]),
      [2, 3, 4, 5, 6, 7, 8, 9, 10, 11]) + `</div>`;
    if (byCard) h += `<div class="cgrid">${byCard}</div>`;
    return h;
  }

  function viewCards() {
    const rs = runs();
    if (!rs.length) return empty("No scenarios recorded yet.");
    const ts = totals(rs);
    let h = `<div class="cgrid">` + ts.map(t => {
      const cs = Object.values(t.cards).sort((a, b) => b.plays - a.plays).slice(0, 10);
      if (!cs.length) return "";
      return `<div class="card scard"><h3>${dot(t.c)} ${esc(who(t.c))}</h3><p class="note">Most played cards (${fmt(t.runs)} scenario${t.runs === 1 ? "" : "s"}).</p>` +
        hbars({rows: cs.map(c => ({label: c.name, values: [c.plays - c.lost, c.lost]})), series: [{name: "Discarded", color: t.c.color}, {name: "Lost", color: OTHER}], unit: "plays"}) + `</div>`;
    }).join("") + `</div>`;
    const party = [{name: "Infused", color: COLORS[2]}, {name: "Consumed", color: COLORS[3]}];
    const inf = e => sum(ts.map(t => t.infused[e] || 0)), con = e => sum(ts.map(t => t.consumed[e] || 0));
    h += card("Elements", "How often the party made and used each element.", columns({cats: ELEMENTS, series: [{name: "Infused", color: COLORS[2], values: ELEMENTS.map(inf)}, {name: "Consumed", color: COLORS[3], values: ELEMENTS.map(con)}], unit: "times", height: 220, grouped: true}),
      table(["Character", ...ELEMENTS.flatMap(e => [e + " made", e + " used"])], ts.map(t => [esc(who(t.c)), ...ELEMENTS.flatMap(e => [fmt(t.infused[e] || 0), fmt(t.consumed[e] || 0)])]), ELEMENTS.flatMap((_, i) => [2 * i + 1, 2 * i + 2])));
    // who fed whom: each element a party member used, credited to whoever made it last (party, summons or monsters)
    const flow = new Map();
    const nameOf = (id, r) => { if (id === "_monsters") return "Monsters"; if (id === "_summons") return "Summons"; if (id === "_unknown") return "Already on the board"; const p = r.party.find(p => p.cls === id); return p ? who(charOf(ckey(p))) : id; };
    for (const r of rs) for (const f of r.elementFlow || []) {
      const k = JSON.stringify([nameOf(f.from, r), nameOf(f.to, r), f.element]);
      flow.set(k, (flow.get(k) || 0) + f.n);
    }
    const frows = [...flow.entries()].sort((a, b) => b[1] - a[1]).map(([k, n]) => { const [from, to, el] = JSON.parse(k); return [esc(from), esc(to), esc(el), fmt(n)]; });
    h += `<div class="card scard"><h3>Who fed whom</h3><p class="note">Every element a character used, credited to whoever made it last: another character, a summon, or the monsters.</p>` +
      (frows.length ? table(["Made by", "Used by", "Element", "Times"], frows, [3]) : `<p class="note">No elements used yet.</p>`) + `</div>`;
    return h;
  }

  function viewMonsters() {
    const rs = runs();
    if (!rs.length) return empty("No scenarios recorded yet.");
    const hurt = new Map(), killed = new Map();
    for (const r of rs) {
      for (const x of r.monsters.hurt || []) {
        const p = r.party.find(p => p.cls === x.target); if (!p) continue;
        const m = hurt.get(x.monster) || new Map(); m.set(ckey(p), (m.get(ckey(p)) || 0) + x.damage); hurt.set(x.monster, m);
      }
      for (const [m, n] of Object.entries(r.monsters.killed || {})) killed.set(m, (killed.get(m) || 0) + n);
    }
    const chars = charOrder.filter(c => [...hurt.values()].some(m => m.has(c.key)));
    const rows = [...hurt.entries()].map(([m, by]) => ({label: m, values: chars.map(c => by.get(c.key) || 0)})).sort((a, b) => sum(b.values) - sum(a.values)).slice(0, 15);
    let h = card("Nemeses: who hurt the party most", "Damage each kind of monster dealt, split by who took it.", rows.length ? hbars({rows, series: chars.map(c => ({name: who(c), color: c.color})), unit: "damage"}) : `<p class="note">No monster damage recorded.</p>`,
      table(["Monster", ...chars.map(c => c.name)], rows.map(r => [esc(r.label), ...r.values.map(fmt)]), chars.map((_, i) => i + 1)));
    const krows = [...killed.entries()].sort((a, b) => b[1] - a[1]).slice(0, 15);
    h += card("Monsters killed", "", hbars({rows: krows.map(([m, n]) => ({label: m, values: [n]})), series: [{name: "Killed", color: COLORS[0]}], unit: "killed"}),
      table(["Monster", "Killed"], krows.map(([m, n]) => [esc(m), fmt(n)]), [1]));
    return h;
  }

  function viewRecords() {
    const rs = runs();
    if (!rs.length) return empty("No scenarios recorded yet.");
    const recs = [];
    const best = (title, unit, pick) => {
      let top = null;
      for (const r of rs) for (const p of r.party) { const c = r.chars[p.cls]; if (!c) continue; const v = pick(c, p, r); if (v != null && (!top || v > top.v)) top = {v, p, r}; }
      if (top && top.v > 0) recs.push([title, `${fmt(top.v)}${unit}`, `${who(charOf(ckey(top.p)))} · ${runTitle(top.r)}`, charOf(ckey(top.p)).color]);
    };
    best("Biggest hit", " damage", c => c.biggest);
    best("Most damage in one round", " damage", c => Math.max(0, ...(c.byRound.dealt || [])));
    best("Most damage in a scenario", " damage", c => c.dealt);
    best("Most kills in a scenario", " kills", c => c.kills);
    best("Most damage taken in a scenario", " damage", c => c.taken);
    best("Most healing in a scenario", " healed", c => c.healed);
    best("Most overkill in a scenario", " wasted", c => c.overkill);
    best("Luckiest scenario", " luck", c => sum(c.draws.map(d => d.luck)));
    best("Unluckiest scenario", " bad luck", c => -sum(c.draws.map(d => d.luck)));
    best("Most nulls in a scenario", " nulls", c => c.draws.filter(d => d.kind === "null").length);
    best("Most x2s in a scenario", " x2s", c => c.draws.filter(d => d.kind === "x2").length);
    const wins = rs.filter(r => r.outcome === "Win");
    if (wins.length) {
      const fast = wins.reduce((a, b) => (b.rounds < a.rounds ? b : a));
      recs.push(["Fastest win", `${fast.rounds} rounds`, runTitle(fast), null]);
    }
    const long = rs.reduce((a, b) => (b.rounds > a.rounds ? b : a));
    recs.push(["Longest scenario", `${long.rounds} rounds`, runTitle(long), null]);
    return `<div class="recs">${recs.map(([t, v, s, color]) => `<div class="card rec"><div class="tl">${esc(t)}</div><div class="tv">${esc(v)}</div><div class="ts">${color ? `<i class="sw" style="background:${color}"></i>` : ""}${esc(s)}</div></div>`).join("")}</div>`;
  }

  // ---- shell
  function render() {
    const root = $("#statsbody");
    if (!root) return;
    TIPS.length = 0;
    if (!D) { root.innerHTML = `<div class="empty">Loading…</div>`; return; }
    indexCharacters([...(D.runs || []), ...(D.live ? [D.live] : [])]);
    const nav = `<div class="sfilters"><div class="spills">${VIEWS.map(([k, l]) => `<button data-sv="${k}" class="${k === view ? "on" : ""}">${esc(l)}${k === "scenario" && D.live ? ' <span class="livedot" title="A scenario is being played"></span>' : ""}</button>`).join("")}</div>` +
      (view !== "scenario" ? `<label class="sfil">Scenarios <select id="soutcome"><option value="all">All</option><option value="win">Won</option><option value="other">Lost or resigned</option></select></label>` : "") +
      `<span class="sgrow"></span><button class="btn small" id="sexport" title="Save this campaign's records as one zip, to send to a friend who missed a scenario">Export records</button>` +
      `<button class="btn small" id="simport" title="Add the records from a friend's export: scenarios you already have are skipped">Import records</button>` +
      `<input type="file" id="simportfile" accept=".zip,.gz,.json" hidden></div>` +
      (notice ? `<div class="banner ${notice.tone}" style="margin:0 0 16px">${notice.html} <a id="snoticex">Dismiss</a></div>` : "");
    const body = {overview: viewOverview, scenario: viewScenario, luck: viewLuck, damage: viewDamage, cards: viewCards, monsters: viewMonsters, records: viewRecords}[view] || viewOverview;
    let html;
    try { html = body(); } catch (e) { html = empty("This view couldn't be drawn: " + esc(e.message)); console.error(e); }
    root.innerHTML = nav + html;
    const so = $("#soutcome"); if (so) so.value = outcome;
  }
  document.addEventListener("click", e => {
    const b = e.target.closest("[data-sv]");
    if (b) { view = b.dataset.sv; try { localStorage.setItem("stats-view", view); } catch (x) {} render(); return; }
    const a = e.target.closest("[data-run]");
    if (a) { runKey = a.dataset.run; view = "scenario"; render(); }
  });
  async function exportRecords() {
    const r = await fetch("/api/stats/export?campaign=" + encodeURIComponent(D.campaign || ""), {method: "POST", headers: {"X-Companion": "1"}}).then(r => r.json()).catch(() => ({ok: false, message: "The companion didn't answer."}));
    notice = r.ok ? {tone: "ok", html: `Saved ${r.count} scenario record${r.count === 1 ? "" : "s"} to <b>${esc(r.path)}</b>. It's open in Explorer: send it to whoever missed a scenario, and they click <b>Import records</b>.`}
                  : {tone: "bad", html: esc(r.message)};
    render();
  }
  async function importRecords(file) {
    notice = {tone: "warn", html: `<span class="spin"></span>Importing ${esc(file.name)}…`};
    render();
    const body = await file.arrayBuffer();
    const r = await fetch(`/api/stats/import?campaign=${encodeURIComponent(D.campaign || "")}&name=${encodeURIComponent(file.name)}`,
      {method: "POST", headers: {"X-Companion": "1", "Content-Type": "application/octet-stream"}, body}).then(r => r.json()).catch(() => ({ok: false, message: "The companion didn't answer."}));
    if (!r.ok) { notice = {tone: "bad", html: esc(r.message)}; render(); return; }
    const parts = [];
    parts.push(r.added.length ? `Added ${r.added.length} scenario${r.added.length === 1 ? "" : "s"} to <b>${esc(D.campaign)}</b>: ${r.added.map(esc).join(", ")}.` : `Nothing new: you already had ${r.skipped === 1 ? "that scenario" : "all of those scenarios"}.`);
    if (r.added.length && r.skipped) parts.push(`Skipped ${r.skipped} you already had.`);
    if (r.otherCampaigns.length) parts.push(`Note: the records say they're from <b>${r.otherCampaigns.map(esc).join(", ")}</b>; they were added to the campaign picked at the top.`);
    if (r.bad.length) parts.push(`Couldn't read: ${r.bad.map(esc).join("; ")}.`);
    notice = {tone: r.bad.length ? "warn" : "ok", html: parts.join(" ")};
    await load(D.campaign, true);
    render();
  }
  document.addEventListener("click", e => {
    if (e.target.id === "sexport") exportRecords();
    if (e.target.id === "simport") $("#simportfile").click();
    if (e.target.id === "snoticex") { notice = null; render(); }
  });
  document.addEventListener("change", e => {
    if (e.target.id === "simportfile" && e.target.files.length) importRecords(e.target.files[0]);
    if (e.target.id === "soutcome") { outcome = e.target.value; render(); }
    if (e.target.id === "srun") { runKey = e.target.value; render(); }
  });

  async function load(campaign, force) {
    if (loading) return;
    loading = true;
    try {
      const st = await fetch("/api/stats-stamp?campaign=" + encodeURIComponent(campaign || ""), {cache: "no-store"}).then(r => r.json());
      if (force || st.stamp !== stamp || !D || D.campaign !== campaign) {
        D = await fetch("/api/stats?campaign=" + encodeURIComponent(campaign || ""), {cache: "no-store"}).then(r => r.json());
        stamp = st.stamp;
        render();
      }
    } catch (e) { /* the next poll tries again */ }
    loading = false;
  }
  window.Stats = {load, render, reset: () => { D = null; stamp = null; runKey = null; notice = null; render(); }};
})();
