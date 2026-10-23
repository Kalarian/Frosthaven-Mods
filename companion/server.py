"""The companion's local web server, bound to 127.0.0.1 only: the tabbed page (web/main.html), each tab's content
and a small JSON API the page calls. Requests are refused unless they name a local host (no DNS rebinding), and
every request that changes something must carry the X-Companion header, which a page on another site can't add
without the browser asking this server first (it never agrees), so only our own page can start an install."""
import json
import re
import mimetypes
import os
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import game
import mapview
import mods
import paths
import stats
import updater

LOCAL_HOSTS = ("127.0.0.1", "localhost")
on_quit = lambda: None            # app.py sets it: how the self-update closes this app once the new one runs
last_request = time.time()        # the browser fallback quits when no page has asked for anything in a while
OPENABLE = ("https://github.com/",)       # what /api/open may hand to the system browser


def status():
    latest = game.latest_release()
    installed = game.installed_version()
    gdir = game.game_dir()
    state = "no-game" if not gdir else "not-installed" if not installed else "unknown" if latest.get("error") else \
        "update" if game.version_key(latest.get("version")) > game.version_key(installed) else \
        "ahead" if game.version_key(latest.get("version")) < game.version_key(installed) else "current"
    # a newer companion rides on the same release as Frosthaven-Companion-<version>.zip
    app_latest = next((m.group(1) for n in (latest.get("assets") or {})
                       for m in [re.fullmatch(r"Frosthaven-Companion-([0-9.]+)\.zip", n or "")] if m), None)
    return {
        "app": {"name": paths.APP_NAME, "version": paths.VERSION, "latest": app_latest,
                "newer": bool(app_latest) and game.version_key(app_latest) > game.version_key(paths.VERSION),
                "canUpdate": updater.can_update(), "update": updater.job()},
        "game": {"dir": gdir, "running": game.is_running() if gdir else False},
        "installed": installed,
        "latest": latest,
        "state": state,
        "job": mods.job(),
        "campaigns": game.all_campaigns(),
        "playGuide": os.path.exists(paths.PLAY_GUIDE),
    }


# The mod guide is laid out for printing (the PDF). Inside the Mods tab it's shown below the status card, in the app's
# dark colours (its colours are CSS variables), without its own title (the tab has one) or its install steps (the
# tab's buttons do that), sized by the tab to fit.
EMBED_GUIDE_CSS = ("<style>:root { --ink:#e8e2d4; --muted:#a59f92; --line:#2f3742; --accent:#e3c98b; --soft:#1b2027; "
                   "--good:#7fc48a; --tag:#2a313b; } html, body { background: transparent; } "
                   "body { font-size: 14px; line-height: 1.5; margin: 0; } h1, .subtitle, .install { display: none; } "
                   ".mod { background: #1b2027; } .mod .tag.play { background: #3e2a1f; color: #f0b79c; } "
                   ".mod .tag.look { background: #1f3a26; color: #9fd8aa; } "
                   "kbd { background: #222832; border-color: #4a5360; color: #e8e2d4; } a { color: #8fc3e8; }</style>")


def guide(path, screen_css=""):
    try:
        with open(path, encoding="utf-8") as f:
            text = f.read()
    except OSError:
        return None
    text = text.replace("__VERSION__", game.installed_version() or paths.pack_version())
    return text.replace("</head>", screen_css + "</head>", 1) if screen_css else text


class Handler(BaseHTTPRequestHandler):
    server_version = "FrosthavenCompanion"

    def log_message(self, fmt, *args):
        pass

    def _local(self):
        host = (self.headers.get("Host") or "").rsplit(":", 1)[0].strip("[]").lower()
        if host in LOCAL_HOSTS:
            return True
        self._send(403, "text/plain", b"forbidden")
        return False

    def _send(self, code, ctype, body, extra=None):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def _json(self, value, code=200):
        self._send(code, "application/json; charset=utf-8", json.dumps(value).encode("utf-8"))

    def _html(self, text, code=200):
        self._send(code, "text/html; charset=utf-8", text.encode("utf-8"))

    def do_GET(self):
        global last_request
        if not self._local():
            return
        last_request = time.time()
        url = urlparse(self.path)
        q = {k: v[-1] for k, v in parse_qs(url.query).items()}
        p = url.path
        if p in ("/", "/index.html"):
            return self._static("main.html")
        if p.startswith("/web/"):
            return self._static(p[len("/web/"):])
        if p == "/api/status":
            return self._json(status())
        if p == "/api/ping":
            return self._json({"ok": True})
        if p == "/api/job":
            return self._json(mods.job())
        if p == "/api/map-stamp":
            return self._json({"stamp": mapview.stamp(q.get("campaign", ""))})
        if p == "/api/stats":
            return self._json(stats.campaign_stats(q.get("campaign", "")))
        if p == "/api/stats-stamp":
            return self._json({"stamp": stats.stamp(q.get("campaign", ""))})
        if p == "/map":
            return self._html(mapview.render(q.get("campaign", "")))
        if p == "/guide/mods":
            text = guide(paths.MOD_GUIDE, EMBED_GUIDE_CSS)
            return self._html(text) if text is not None else self._html(mapview._message(
                "No mod guide", "This copy of the companion was built without it."), 404)
        if p == "/guide/play":
            text = guide(paths.PLAY_GUIDE)
            return self._html(text) if text is not None else self._html(mapview._message(
                "No play guide", "This copy of the companion was built without it."), 404)
        self._send(404, "text/plain", b"not found")

    def do_POST(self):
        if not self._local():
            return
        if self.headers.get("X-Companion") != "1":
            return self._send(403, "text/plain", b"forbidden")
        url = urlparse(self.path)
        q = {k: v[-1] for k, v in parse_qs(url.query).items()}
        p = url.path
        if p in ("/api/mods/install", "/api/mods/uninstall"):
            ok, msg = mods.start(p.rsplit("/", 1)[1])
            return self._json({"ok": ok, "message": msg}, 200 if ok else 409)
        if p == "/api/app/update":
            ok, msg = updater.start(lambda: on_quit())
            return self._json({"ok": ok, "message": msg}, 200 if ok else 409)
        if p == "/api/check":
            game.latest_release(force=True)
            return self._json(status())
        if p == "/api/open":
            target = q.get("url", "")
            if not target.startswith(OPENABLE):
                return self._send(403, "text/plain", b"forbidden")
            webbrowser.open(target)
            return self._json({"ok": True})
        self._send(404, "text/plain", b"not found")

    def _static(self, name):
        root = os.path.realpath(paths.WEB_DIR)
        path = os.path.realpath(os.path.join(root, name))
        if not path.startswith(root + os.sep) or not os.path.isfile(path):
            return self._send(404, "text/plain", b"not found")
        with open(path, "rb") as f:
            body = f.read()
        self._send(200, mimetypes.guess_type(path)[0] or "application/octet-stream", body)


class Server(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = False


def start(port=paths.PORT):
    """Serve on 127.0.0.1 from `port` (or the next free one of ten) on a daemon thread; returns the page's URL."""
    for p in range(port, port + 10):
        try:
            srv = Server(("127.0.0.1", p), Handler)
        except OSError:
            continue
        threading.Thread(target=srv.serve_forever, name="server", daemon=True).start()
        return f"http://127.0.0.1:{p}/"
    raise OSError(f"no free port from {port} to {port + 9}")
