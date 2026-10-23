"""The companion's window: the local page in a WebView2 window that owns the main thread (pywebview needs it on
Windows), adapted from MnM Meter's window.py. Without pywebview, pythonnet or the WebView2 Runtime, or when the
window stays blank, the page opens in the system browser instead and a message box says why. Closing the window
ends the program. Also: the log file, message boxes and the single-instance lock."""
import datetime as dt
import logging
import os
import re
import sys
import threading

import paths

TITLE = paths.APP_NAME
MB_ICONINFORMATION = 0x40
MB_ICONERROR = 0x10
MB_TOPMOST = 0x40000
ERROR_ALREADY_EXISTS = 183
LOAD_TIMEOUT = 30.0
APP_ID = "FrosthavenCompanion.App"
SIZE, MIN_SIZE = (1400, 900), (900, 600)
WEBVIEW2_KEYS = (r"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                 r"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                 r"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}")
WEBVIEW2_URL = "https://developer.microsoft.com/microsoft-edge/webview2/"
HELP = {
    "runtime": "The companion's window needs the Microsoft WebView2 Runtime (Windows 11 already has it). Install it "
               f"from {WEBVIEW2_URL} and start the companion again. It has opened in your browser instead.",
    "blocked": "Windows has blocked the companion's files because the zip came from the internet. Right-click the zip, "
               "Properties, tick Unblock, and unzip it again. It has opened in your browser instead.",
    "failed": "The companion's window couldn't start (the reason is in {log}). It has opened in your browser instead. "
              f"Installing or repairing the WebView2 Runtime from {WEBVIEW2_URL} usually fixes it.",
    "stuck": "The companion's window stayed blank (details in {log}). It has opened in your browser instead; closing "
             f"the blank window quits. Repairing the WebView2 Runtime from {WEBVIEW2_URL} usually fixes it.",
}

_mutex = None
_log_lock = threading.Lock()
_log_started = False
unavailable_kind = ""


def log_path():
    return os.path.join(paths.LOGS_DIR, "companion.log")


def log(*args):
    """One line in logs/companion.log (the exe has no console)."""
    global _log_started
    try:
        os.makedirs(paths.LOGS_DIR, exist_ok=True)
        with _log_lock, open(log_path(), "a", encoding="utf-8") as f:
            if not _log_started:
                f.write(f"{dt.datetime.now():%Y-%m-%d %H:%M:%S} ---- pid {os.getpid()}: {paths.APP_NAME} "
                        f"{paths.VERSION} ({'exe' if paths.FROZEN else 'source'})\n")
                _log_started = True
            f.write(dt.datetime.now().strftime("%H:%M:%S ") + " ".join(str(a) for a in args) + "\n")
    except OSError:
        pass


def message_box(text, flags=MB_ICONINFORMATION):
    try:
        import ctypes
        ctypes.windll.user32.MessageBoxW(0, text, TITLE, flags)
    except Exception:
        pass


def single_instance():
    """True when this process holds the companion's mutex (False: another companion is running)."""
    global _mutex
    if _mutex is not None:
        return True
    try:
        import ctypes
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        k32.CreateMutexW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_wchar_p]
        k32.CreateMutexW.restype = ctypes.c_void_p
        k32.CloseHandle.argtypes = [ctypes.c_void_p]
        h = k32.CreateMutexW(None, False, paths.MUTEX)
        if ctypes.get_last_error() == ERROR_ALREADY_EXISTS:
            if h:
                k32.CloseHandle(h)
            return False
    except Exception:
        return True
    _mutex = h
    return True


def webview2_installed():
    try:
        import winreg
    except ImportError:
        return False
    for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
        for key in WEBVIEW2_KEYS:
            try:
                with winreg.OpenKey(hive, key) as k:
                    if winreg.QueryValueEx(k, "pv")[0]:
                        return True
            except OSError:
                continue
    return False


def download_marked(path=None):
    """Whether pythonnet's Python.Runtime.dll carries Windows' download mark (Zone 3 or 4), which makes .NET refuse it."""
    if path is None:
        try:
            import pythonnet
            path = os.path.join(os.path.dirname(pythonnet.__file__), "runtime", "Python.Runtime.dll")
        except Exception:
            return False
    try:
        with open(path + ":Zone.Identifier", encoding="utf-8", errors="replace") as f:
            m = re.search(r"ZoneId\s*=\s*(\d+)", f.read())
    except OSError:
        return False
    return bool(m) and int(m.group(1)) >= 3


class _ToLog(logging.Handler):
    def emit(self, record):
        try:
            log(self.format(record))
        except Exception:
            pass


def _route_pywebview_log():
    lg = logging.getLogger("pywebview")
    if not any(isinstance(h, _ToLog) for h in lg.handlers):
        h = _ToLog(logging.WARNING)
        h.setFormatter(logging.Formatter("pywebview %(levelname)s: %(message)s"))
        lg.addHandler(h)


def webview_available():
    """True when pywebview and pythonnet import and the WebView2 Runtime is installed; else unavailable_kind says why
    ("" for a source run without pywebview: no box)."""
    global unavailable_kind
    unavailable_kind = ""
    try:
        import webview  # noqa: F401
        _route_pywebview_log()
        import clr  # noqa: F401
    except ImportError as ex:
        unavailable_kind = ("blocked" if download_marked() else "failed") if paths.FROZEN else ""
        log(f"no native window: {type(ex).__name__}: {ex}")
        return False
    except Exception as ex:
        unavailable_kind = "blocked" if download_marked() else "failed"
        log(f"no native window: {type(ex).__name__}: {ex}")
        return False
    if not webview2_installed():
        unavailable_kind = "runtime"
        log("no native window: the WebView2 Runtime is not installed")
        return False
    return True


def set_app_id():
    try:
        import ctypes
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_ID)
    except Exception:
        pass


def show_help(kind):
    text = HELP.get(kind or "")
    if text:
        threading.Thread(target=message_box, args=(text.format(log=log_path()), MB_ICONINFORMATION | MB_TOPMOST),
                         daemon=True).start()


def _watch_load(w, on_stuck):
    events = getattr(w, "events", None)
    loaded = getattr(events, "loaded", None)
    if loaded is None or not hasattr(loaded, "wait"):
        return

    def watch():
        if loaded.wait(LOAD_TIMEOUT):
            return
        closed = getattr(events, "closed", None)
        if closed is not None and hasattr(closed, "is_set") and closed.is_set():
            return
        log(f"window: the page did not load in {LOAD_TIMEOUT:.0f} s")
        on_stuck()
    threading.Thread(target=watch, name="window-watchdog", daemon=True).start()


def close_main_window():
    """Close the window from another thread (the self-update's restart); the program ends when it's gone."""
    try:
        import webview
        for w in list(getattr(webview, "windows", [])):
            w.destroy()
    except Exception as ex:
        log(f"window: couldn't close ({type(ex).__name__}: {ex})")


def run(url, on_stuck=None):
    """Show `url` in a WebView2 window on this thread; return when it's closed. Raises when it can't open."""
    import webview
    _route_pywebview_log()
    storage = os.path.join(paths.DATA_DIR, "webview2")
    os.makedirs(storage, exist_ok=True)
    set_app_id()
    w = webview.create_window(TITLE, url, width=SIZE[0], height=SIZE[1], min_size=MIN_SIZE, text_select=True)
    if on_stuck is not None and w is not None:
        _watch_load(w, on_stuck)
    icon = paths.app_icon()
    extra = {"icon": icon} if icon else {}
    webview.start(gui="edgechromium", storage_path=storage, private_mode=False, **extra)
