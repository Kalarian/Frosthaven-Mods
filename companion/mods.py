"""Installing, updating and removing the mod pack, by running the pack's own installer in its no-window mode
("Install Frosthaven Mods.exe" <game folder> install | uninstall). The installer stays the one place that knows how
to install: it fetches the newest pack from GitHub when there is one, rebuilds Base.ruleset from the vanilla backup,
copies the plugins and writes FrosthavenMods\\install-log.txt. One job at a time, on a thread; the page polls job()."""
import os
import shutil
import subprocess
import threading
import time
import urllib.request

import game
import paths
import updater

INSTALLER_NAME = "Install Frosthaven Mods.exe"
_lock = threading.Lock()
_job = {"action": None, "running": False, "started": 0.0, "finished": 0.0, "code": None, "log": ""}


def job():
    with _lock:
        return dict(_job)


def _installer():
    """The installer to run: the one bundled with the companion, else one downloaded from the latest release."""
    if os.path.exists(paths.INSTALLER):
        return paths.INSTALLER
    # GitHub stores "Install Frosthaven Mods.exe" as "Install.Frosthaven.Mods.exe"
    assets = game.latest_release().get("assets") or {}
    url = next((u for n, u in assets.items() if (n or "").replace(".", " ") == INSTALLER_NAME.replace(".", " ")), None)
    if not url:
        raise RuntimeError("no installer bundled, and none found on the latest GitHub release")
    cache = os.path.join(paths.DATA_DIR, "cache")
    os.makedirs(cache, exist_ok=True)
    dest = os.path.join(cache, INSTALLER_NAME)
    req = urllib.request.Request(url, headers={"User-Agent": "FrosthavenCompanion"})
    with urllib.request.urlopen(req, timeout=60) as r, open(dest + ".part", "wb") as f:
        shutil.copyfileobj(r, f)
    os.replace(dest + ".part", dest)
    return dest


def start(action):
    """Start 'install' (also updates and repairs) or 'uninstall'. Returns (ok, message)."""
    if action not in ("install", "uninstall"):
        return False, f"unknown action {action}"
    gdir = game.game_dir()
    if not gdir:
        return False, "Frosthaven wasn't found. Is it installed through Steam?"
    if game.is_running():
        return False, "Close Frosthaven first: the mods can't be changed while the game is running."
    # the companion installs the pack it carries (releases have no separate pack or installer since v0.9.5): a newer
    # companion is the newer pack, so an install from this one would put the old pack back
    if action == "install":
        try:
            newer = updater.available()
        except Exception:
            newer = None
        if newer:
            return False, f"Update the app first (Update the app, below): companion v{newer[0]} carries the newest mods."
    with _lock:
        if _job["running"]:
            return False, "Already working on it."
        _job.update(action=action, running=True, started=time.time(), finished=0.0, code=None, log="")
    threading.Thread(target=_run, args=(action, gdir), name=f"mods-{action}", daemon=True).start()
    return True, "started"


def _run(action, gdir):
    code, text = None, ""
    try:
        exe = _installer()
        proc = subprocess.run([exe, gdir, action], capture_output=True, timeout=600,
                              creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        code = proc.returncode
        try:
            with open(os.path.join(game.mods_dir(gdir), "install-log.txt"), encoding="utf-8", errors="replace") as f:
                text = f.read()
        except OSError:
            text = "(the installer wrote no log)"
    except Exception as ex:
        code, text = -1, f"{type(ex).__name__}: {ex}"
    with _lock:
        _job.update(running=False, finished=time.time(), code=code, log=text)
