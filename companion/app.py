"""The entry point of "Frosthaven Companion.exe", also runnable from source as `py app.py` (add --browser to skip
the window and open the page in the system browser, --serve to only run the server, --check to load the window's
parts and log whether they work, both for tests).

One companion at a time: a second start opens the first one's page in the browser instead (the window may be
behind the game). The server runs on a thread; the window owns the main thread and closing it ends the program.
"""
import sys
import threading
import time
import webbrowser

import paths
import window


def _report_crash():
    import traceback
    ex_type, ex, tb = sys.exc_info()
    window.log("crash:\n" + "".join(traceback.format_exception(ex_type, ex, tb)).rstrip())
    first = "".join(traceback.format_exception_only(ex_type, ex)).strip().splitlines()[0]
    window.message_box(f"{paths.APP_NAME} stopped: {first}\n\nDetails are in {window.log_path()}.", window.MB_ICONERROR)


IDLE_QUIT = 90      # seconds without a request from the page (it pings every 15) before the browser fallback quits


def _serve_while_open():
    """In the browser, there's no window to close: serve until no page has been open for IDLE_QUIT seconds."""
    import server
    while time.time() - server.last_request < IDLE_QUIT:
        time.sleep(5)
    window.log("no page open any more: quitting")


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if "--check" in argv:
        # loads the window's parts (pywebview, pythonnet, WebView2) without opening anything, for testing a build
        ok = window.webview_available()
        window.log(f"check: window {'available' if ok else 'unavailable (' + (window.unavailable_kind or 'source run') + ')'}")
        return
    if "--serve" in argv:
        # tests: a server only, on its own port and without the one-companion lock, so it never touches (or opens
        # the browser for) a companion the user is running
        import server
        window.log(f"test server {server.start(paths.TEST_PORT)}")
        try:
            while True:
                time.sleep(3600)
        except KeyboardInterrupt:
            return
    if not window.single_instance():
        webbrowser.open(f"http://127.0.0.1:{paths.PORT}/")
        return
    import server
    url = server.start()
    window.log(f"serving {url}")
    if "--browser" in argv or not window.webview_available():
        webbrowser.open(url)
        window.show_help(window.unavailable_kind)
        _serve_while_open()
        return
    stuck = threading.Event()

    def on_stuck():
        stuck.set()
        webbrowser.open(url)
        window.show_help("stuck")
    try:
        window.run(url, on_stuck=on_stuck)
    except Exception as ex:
        window.log(f"window failed: {type(ex).__name__}: {ex}")
        webbrowser.open(url)
        window.show_help("failed")
        _serve_while_open()


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except KeyboardInterrupt:
        pass
    except BaseException:
        _report_crash()
