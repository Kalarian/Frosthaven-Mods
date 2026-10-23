"""The Scenario map tab: tools/scenario_map.py's page, built fresh from the campaign's progress file on every load,
so a browser refresh (or the page itself, when the game autosaves) always shows the current campaign. The game's
scenario definitions are read from its Base.ruleset once, and again only when that file changes (a game or mod
update)."""
import html
import os
import threading
from pathlib import Path

import game
import scenario_map

_lock = threading.Lock()
_graph = {"key": None, "graph": None}

# Reloads the page when the progress file changes (the game autosaved), keeping the selected scenario or tab.
# `selected` and `tab` are the map page's own top-level variables.
LIVE = """<script>
(function () {
  const campaign = new URLSearchParams(location.search).get("campaign") || "";
  let stamp = null;
  async function poll() {
    try {
      const r = await fetch("/api/map-stamp?campaign=" + encodeURIComponent(campaign), {cache: "no-store"});
      const j = await r.json();
      if (stamp === null) stamp = j.stamp;
      else if (j.stamp !== stamp) {
        if (typeof selected !== "undefined" && selected !== null) location.hash = String(selected);
        else if (typeof tab !== "undefined" && tab !== "map") location.hash = tab;
        location.reload();
        return;
      }
    } catch (e) {}
    setTimeout(poll, 4000);
  }
  setTimeout(poll, 4000);
})();
</script>
"""


def _graph_for(ruleset):
    key = (ruleset, os.path.getmtime(ruleset), os.path.getsize(ruleset))
    with _lock:
        if _graph["key"] != key:
            _graph.update(key=key, graph=scenario_map.Graph(scenario_map.load_defs(Path(ruleset))))
        return _graph["graph"]


def find(campaign):
    """The campaign's progress file entry, by name (default: the most recently saved one), or None."""
    found = game.campaigns()
    if campaign:
        return next((c for c in found if game._key(c["name"]) == game._key(campaign)), None)
    return found[0] if found else None


def stamp(campaign):
    c = find(campaign)
    return f"{c['name']}:{c['mtime']}" if c else ""


def render(campaign):
    """The map page for a campaign (the blank map when there is none), or a short error page."""
    ruleset = game.ruleset_path()
    if not ruleset or not os.path.exists(ruleset):
        return _message("Frosthaven wasn't found", "The scenario map reads the game's own scenario data, so it needs "
                                                   "Frosthaven installed through Steam.")
    try:
        g = _graph_for(ruleset)
        c = find(campaign)
        prog = scenario_map.read_progress(Path(c["path"])) if c else None
        page = scenario_map.build(scenario_map.page_data(g, scenario_map.Analysis(g, prog), prog))
    except Exception as ex:
        return _message("The scenario map couldn't be built", f"{type(ex).__name__}: {ex}")
    return page.replace("</body>", LIVE + "</body>", 1)


def _message(title, text):
    return (f"<!doctype html><meta charset='utf-8'><body style='font:15px system-ui;background:#14171c;color:#e8e2d4;"
            f"padding:32px'><h2>{html.escape(title)}</h2><p>{html.escape(text)}</p></body>")
