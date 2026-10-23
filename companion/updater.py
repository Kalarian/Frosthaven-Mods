"""The companion updating itself from the latest GitHub release (Frosthaven-Companion-<version>.zip).

A running program can't overwrite its own files, so an update takes three steps, all inside the exe:
1. start(): download the zip, check it against the release's SHA256SUMS.txt, unpack it next to the app
   ("<app folder>.new"), start that new copy with --finish-update <app folder> <this process id>, and close.
2. finish(), in the new copy before any window: wait for the old process to end, replace the app folder's program
   files with its own (logs\\, webview2\\ and cache\\ are kept), start the app from its usual folder with
   --cleanup-update <the .new folder>, and exit.
3. cleanup(), in the restarted app: remove the .new folder (retrying while the copy that made it finishes exiting).
Only the packaged exe updates itself (a source run has nothing to replace). One update at a time; the page polls job().
"""
import ctypes
import hashlib
import os
import shutil
import subprocess
import sys
import threading
import time
import urllib.request
import zipfile

import game
import paths

ZIP_PREFIX, ZIP_SUFFIX = "Frosthaven-Companion-", ".zip"
KEEP = {"logs", "webview2", "cache"}             # the app folder's own data: never replaced or removed
_lock = threading.Lock()
_job = {"state": None, "message": "", "version": None}


def job():
    with _lock:
        return dict(_job)


def _set(**kw):
    with _lock:
        _job.update(kw)


def available():
    """(version, zip url, sums url) of a newer companion on the latest release, or None."""
    latest = game.latest_release()
    assets = latest.get("assets") or {}
    best = None
    for name, url in assets.items():
        if name and name.startswith(ZIP_PREFIX) and name.endswith(ZIP_SUFFIX):
            v = name[len(ZIP_PREFIX):-len(ZIP_SUFFIX)]
            if game.version_key(v) > game.version_key(paths.VERSION) and (not best or game.version_key(v) > game.version_key(best[0])):
                best = (v, url, name)
    if not best:
        return None
    return best[0], best[1], best[2], assets.get("SHA256SUMS.txt")


def can_update():
    """'' when this copy can update itself, else why not (shown instead of the button)."""
    if not paths.FROZEN:
        return "This is a source run: update it with git."
    probe = os.path.join(os.path.dirname(paths.APP_DIR), ".fhc-write-test")
    try:
        with open(probe, "w") as f:
            f.write("ok")
        os.remove(probe)
        with open(os.path.join(paths.APP_DIR, ".fhc-write-test"), "w") as f:
            f.write("ok")
        os.remove(os.path.join(paths.APP_DIR, ".fhc-write-test"))
    except OSError:
        return "The companion's folder can't be written to (is it under Program Files?): download the new zip instead."
    return ""


def _get(url, dest=None):
    req = urllib.request.Request(url, headers={"User-Agent": "FrosthavenCompanion"})
    with urllib.request.urlopen(req, timeout=120) as r:
        if dest is None:
            return r.read()
        with open(dest, "wb") as f:
            shutil.copyfileobj(r, f)
    return None


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def expected_hash(sums_text, name):
    """The hash SHA256SUMS.txt lists for a file ("<hash> *<name>" or "<hash>  <name>"), or None."""
    for line in sums_text.splitlines():
        parts = line.strip().split(None, 1)
        if len(parts) == 2 and parts[1].lstrip("*").strip() == name:
            return parts[0].lower()
    return None


def start(on_quit):
    """Begin updating on a thread; on_quit() closes this app once the new copy is running. Returns (ok, message)."""
    why = can_update()
    if why:
        return False, why
    found = available()
    if not found:
        return False, "This is already the newest companion."
    with _lock:
        if _job["state"] in ("downloading", "checking", "unpacking", "restarting"):
            return False, "Already updating."
        _job.update(state="downloading", message="Downloading…", version=found[0])
    threading.Thread(target=_run, args=(found, on_quit), name="self-update", daemon=True).start()
    return True, "started"


def _run(found, on_quit):
    version, url, name, sums_url = found
    work = os.path.join(paths.DATA_DIR, "cache", "update")
    try:
        shutil.rmtree(work, ignore_errors=True)
        os.makedirs(work, exist_ok=True)
        zpath = os.path.join(work, name)
        _get(url, zpath)
        _set(state="checking", message="Checking the download…")
        if not sums_url:
            raise RuntimeError("the release has no SHA256SUMS.txt to check the download against")
        want = expected_hash(_get(sums_url).decode("utf-8", "replace"), name)
        if not want or want != sha256(zpath):
            raise RuntimeError("the download doesn't match the release's checksum; nothing was changed")
        _set(state="unpacking", message="Unpacking…")
        unpacked = os.path.join(work, "unpacked")
        with zipfile.ZipFile(zpath) as z:
            z.extractall(unpacked)
        roots = [d for d in os.listdir(unpacked) if os.path.isdir(os.path.join(unpacked, d))]
        src = os.path.join(unpacked, roots[0]) if len(roots) == 1 else unpacked
        exe = os.path.join(src, os.path.basename(sys.executable))
        if not os.path.exists(exe):
            raise RuntimeError("the downloaded zip has no " + os.path.basename(sys.executable))
        staged = paths.APP_DIR.rstrip("\\/") + ".new"
        shutil.rmtree(staged, ignore_errors=True)
        shutil.move(src, staged)
        _set(state="restarting", message=f"Restarting as v{version}…")
        subprocess.Popen([os.path.join(staged, os.path.basename(sys.executable)), "--finish-update", paths.APP_DIR,
                          str(os.getpid())], cwd=staged, close_fds=True,
                         creationflags=getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))
        time.sleep(1.0)
        on_quit()
    except Exception as ex:
        _set(state="error", message=f"The update didn't work: {ex}")


# ---- steps 2 and 3, in the new copy and then the restarted app

def _wait_for_exit(pid, timeout=60.0):
    SYNCHRONIZE = 0x00100000
    try:
        k32 = ctypes.windll.kernel32
        h = k32.OpenProcess(SYNCHRONIZE, False, int(pid))
        if not h:
            return True                      # already gone
        try:
            return k32.WaitForSingleObject(h, int(timeout * 1000)) == 0
        finally:
            k32.CloseHandle(h)
    except Exception:
        time.sleep(3)
        return True


def looks_like_app(folder):
    """A companion folder: the exe next to its _internal folder. Nothing else is ever replaced."""
    return (os.path.isdir(os.path.join(folder, "_internal"))
            and os.path.exists(os.path.join(folder, os.path.basename(sys.executable))))


def replace_app(new_dir, old_dir):
    """Replace old_dir's program files with new_dir's: only the names the new version has (the exe, its config,
    README.txt, the _internal folder), each removed and copied over; the logs, webview2 and cache folders and
    anything else the user put there are left alone."""
    if not looks_like_app(old_dir):
        raise RuntimeError(f"{old_dir} doesn't look like the companion's folder; nothing was changed")
    for name in os.listdir(new_dir):
        if name in KEEP:
            continue
        s, d = os.path.join(new_dir, name), os.path.join(old_dir, name)
        for attempt in range(20):            # the old exe can take a moment to let go of its files
            try:
                if os.path.isdir(d):
                    shutil.rmtree(d)
                elif os.path.exists(d):
                    os.remove(d)
                break
            except OSError:
                time.sleep(0.5)
        if os.path.isdir(s):
            shutil.copytree(s, d, dirs_exist_ok=True)
        else:
            shutil.copy2(s, d)


def finish(old_dir, pid, restart=True, log=print):
    """Step 2 (runs in the .new copy): wait for the old app, replace its files, restart it from its folder."""
    log(f"update: waiting for the old companion (pid {pid}) to close")
    _wait_for_exit(pid)
    new_dir = paths.APP_DIR
    log(f"update: replacing {old_dir} with {new_dir}")
    replace_app(new_dir, old_dir)
    if restart:
        exe = os.path.join(old_dir, os.path.basename(sys.executable))
        subprocess.Popen([exe, "--cleanup-update", new_dir], cwd=old_dir, close_fds=True,
                         creationflags=getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))
    log("update: done")


def cleanup(new_dir, log=print):
    """Step 3 (runs in the restarted app, on a thread): remove the .new folder once its copy has exited."""
    def run():
        if not new_dir.rstrip("\\/").endswith(".new"):
            return                           # only ever the folder this updater made
        for attempt in range(60):
            shutil.rmtree(new_dir, ignore_errors=True)
            if not os.path.exists(new_dir):
                log("update: removed " + new_dir)
                return
            time.sleep(1)
    threading.Thread(target=run, name="update-cleanup", daemon=True).start()
