"""Where the companion lives, reads and writes, for the source tree and the packaged build alike.

Packaged (PyInstaller one-folder), the exe sits in APP_DIR with the bundled Python and resources in APP_DIR\\_internal
(RES_DIR); logs and WebView2's profile go next to the exe, unless that folder is read-only, in which case they go to
%LOCALAPPDATA%\\Frosthaven Companion. From source, resources come from the repo itself (the guides, the installer
in dist\\, tools\\ for the scenario map) and logs go to companion\\logs.
"""
import os
import sys

APP_NAME = "Frosthaven Companion"
VERSION = "0.1.0"
MUTEX = r"Local\FrosthavenCompanion"
PORT = 7788

FROZEN = bool(getattr(sys, "frozen", False))
SOURCE_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_DIR = os.path.dirname(SOURCE_DIR)
APP_DIR = os.path.dirname(os.path.abspath(sys.executable)) if FROZEN else SOURCE_DIR
RES_DIR = (getattr(sys, "_MEIPASS", None) or os.path.join(APP_DIR, "_internal")) if FROZEN else SOURCE_DIR

if not FROZEN:
    # the scenario map's analysis lives in the repo's tools (bundled into the exe by the spec's pathex)
    sys.path.insert(0, os.path.join(REPO_DIR, "tools"))


def _writable(d):
    try:
        os.makedirs(d, exist_ok=True)
        probe = os.path.join(d, ".write-test")
        with open(probe, "w") as f:
            f.write("ok")
        os.remove(probe)
        return True
    except OSError:
        return False


DATA_DIR = APP_DIR if _writable(APP_DIR) else os.path.join(os.environ.get("LOCALAPPDATA", APP_DIR), APP_NAME)
LOGS_DIR = os.path.join(DATA_DIR, "logs")
WEB_DIR = os.path.join(RES_DIR, "web")


def _res(frozen_rel, source_path):
    """A bundled resource's path: under RES_DIR in the exe, the repo's own file from source."""
    return os.path.join(RES_DIR, frozen_rel) if FROZEN else source_path


MOD_GUIDE = _res(r"guides\mod-guide.html", os.path.join(REPO_DIR, "docs", "guide", "guide.html"))
PLAY_GUIDE = _res(r"guides\play-guide.html", r"C:\Claude\FrosthavenGuide\frosthaven-play-guide.html")
INSTALLER = _res(r"installer\Install Frosthaven Mods.exe", os.path.join(REPO_DIR, "dist", "Install Frosthaven Mods.exe"))
PACK_VERSION_FILE = _res("VERSION", os.path.join(REPO_DIR, "VERSION"))


def pack_version():
    """The mod pack version this companion was built with (the mod guide's __VERSION__)."""
    try:
        with open(PACK_VERSION_FILE, encoding="utf-8") as f:
            return f.read().strip()
    except OSError:
        return "?"


def app_icon():
    path = os.path.join(WEB_DIR, "icon.ico")
    return path if os.path.exists(path) else None
