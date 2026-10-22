"""The game install and the mod pack's state: where Frosthaven is, whether it's running, which pack version is
installed, which one GitHub has, and the campaigns the Scenario map file rule has written. Mirrors the installer's
own lookups (installer\\Program.cs: FindGameFolder, SteamLibraries, Updater.Check) so both agree on the answers."""
import json
import os
import re
import string
import subprocess
import threading
import time
import urllib.request

GAME_EXE = "Frosthaven.exe"
PROCESS = "Frosthaven.exe"
STEAM_FOLDER = "Frosthaven"
REPO = "Kalarian/Frosthaven-Mods"
RELEASES_PAGE = f"https://github.com/{REPO}/releases/latest"
API = f"https://api.github.com/repos/{REPO}/releases/latest"
LATEST_TTL = 600            # seconds a GitHub answer is reused (the API allows 60 unauthenticated calls an hour)


def _steam_libraries():
    libs = []
    steam = None
    try:
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
                steam = winreg.QueryValueEx(k, "SteamPath")[0]
        except OSError:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam") as k:
                steam = winreg.QueryValueEx(k, "InstallPath")[0]
    except (OSError, ImportError):
        pass
    if not steam:
        return libs
    steam = steam.replace("/", "\\")
    libs.append(steam)
    vdf = os.path.join(steam, "steamapps", "libraryfolders.vdf")
    try:
        with open(vdf, encoding="utf-8", errors="replace") as f:
            for m in re.finditer(r'"path"\s+"([^"]+)"', f.read()):
                p = m.group(1).replace("\\\\", "\\")
                if os.path.isdir(p) and p.lower() not in (x.lower() for x in libs):
                    libs.append(p)
    except OSError:
        pass
    return libs


_game_dir = None


def game_dir():
    """The Frosthaven folder (Steam libraries first, then the usual places on each fixed drive), or None."""
    global _game_dir
    if _game_dir and os.path.exists(os.path.join(_game_dir, GAME_EXE)):
        return _game_dir
    candidates = [os.path.join(lib, "steamapps", "common", STEAM_FOLDER) for lib in _steam_libraries()]
    for drive in string.ascii_uppercase:
        root = f"{drive}:\\"
        if not os.path.exists(root):
            continue
        for rel in (r"Program Files (x86)\Steam", r"Program Files\Steam", "Steam", "SteamLibrary", r"Games\Steam",
                    r"Games\SteamLibrary"):
            candidates.append(os.path.join(root, rel, "steamapps", "common", STEAM_FOLDER))
    for c in candidates:
        if os.path.exists(os.path.join(c, GAME_EXE)):
            _game_dir = os.path.realpath(c)       # the folder's real letter case (Steam's registry path is lowercase)
            return _game_dir
    return None


def mods_dir(gdir=None):
    gdir = gdir or game_dir()
    return os.path.join(gdir, "FrosthavenMods") if gdir else None


def ruleset_path(gdir=None):
    gdir = gdir or game_dir()
    return os.path.join(gdir, "Frosthaven_Data", "StreamingAssets", "Rulebase", "Base.ruleset") if gdir else None


def is_running():
    """Whether Frosthaven.exe is running (tasklist, no console window)."""
    try:
        out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {PROCESS}", "/NH", "/FO", "CSV"], capture_output=True,
                             text=True, timeout=10, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)).stdout
        return PROCESS.lower() in out.lower()
    except Exception:
        return False


def installed_version(gdir=None):
    """The pack version the installer last installed (FrosthavenMods\\state.json), or None when it isn't installed."""
    d = mods_dir(gdir)
    if not d:
        return None
    try:
        with open(os.path.join(d, "state.json"), encoding="utf-8") as f:
            state = json.load(f)
    except (OSError, ValueError):
        return None
    plugin = os.path.join(gdir or game_dir(), "BepInEx", "plugins", "FrosthavenMods", "frosthaven.mods.dll")
    return state.get("version") if os.path.exists(plugin) else None


def version_key(v):
    """'0.6.8' -> (0, 6, 8) for comparing; anything unreadable sorts first."""
    try:
        return tuple(int(x) for x in str(v).strip().lstrip("vV").split("."))
    except ValueError:
        return ()


_latest = {"at": 0.0, "value": None}
_latest_lock = threading.Lock()


def latest_release(force=False):
    """{'version', 'notes', 'url', 'published', 'assets': {name: url}} for GitHub's latest release, or
    {'error': ...} when it can't be reached. Reused for LATEST_TTL seconds unless forced."""
    with _latest_lock:
        if not force and _latest["value"] and time.time() - _latest["at"] < LATEST_TTL:
            return _latest["value"]
        try:
            req = urllib.request.Request(API, headers={"User-Agent": "FrosthavenCompanion",
                                                       "Accept": "application/vnd.github+json",
                                                       "Cache-Control": "no-cache"})
            with urllib.request.urlopen(req, timeout=15) as r:
                data = json.load(r)
            value = {
                "version": str(data.get("tag_name", "")).lstrip("vV"),
                "notes": data.get("body") or "",
                "url": data.get("html_url") or RELEASES_PAGE,
                "published": data.get("published_at") or "",
                "assets": {a.get("name"): a.get("browser_download_url") for a in data.get("assets", [])},
            }
        except Exception as ex:
            value = {"error": f"{type(ex).__name__}: {ex}"}
            # an error is retried sooner than an answer
            _latest.update(at=time.time() - LATEST_TTL + 60, value=value)
            return value
        _latest.update(at=time.time(), value=value)
        return value


def campaigns(gdir=None):
    """The campaigns with a progress file (the Scenario map file rule), newest first: [{'name', 'path', 'mtime'}]."""
    d = mods_dir(gdir)
    folder = os.path.join(d, "scenarios") if d else None
    if not folder or not os.path.isdir(folder):
        return []
    out = []
    for name in os.listdir(folder):
        if name.startswith("progress-") and name.endswith(".txt"):
            path = os.path.join(folder, name)
            out.append({"name": name[len("progress-"):-len(".txt")], "path": path, "mtime": os.path.getmtime(path)})
    return sorted(out, key=lambda c: -c["mtime"])
