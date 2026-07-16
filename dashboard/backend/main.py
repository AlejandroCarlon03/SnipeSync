"""Desktop entry point.

Starts the Flask app on a loopback port and shows it in a native window. This is
what PyInstaller builds into SnipeSyncDashboard.exe.

The window uses the Edge WebView2 runtime, which ships with Windows 11 and
current Windows 10. If it is missing or the window can't open for any reason, we
fall back to the user's default browser rather than failing with a blank screen.
"""

from __future__ import annotations

import logging
import socket
import sys
import threading
import webbrowser
from wsgiref.simple_server import make_server

from app import create_app

WINDOW_TITLE = "SnipeSync Dashboard"


def _free_port() -> int:
    """Asks the OS for an unused loopback port, avoiding a hard-coded clash."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def _serve(port: int) -> None:
    # wsgiref rather than app.run(): no reloader, no debugger, no banner --
    # appropriate for a single-user desktop app bound to loopback.
    app = create_app()
    with make_server("127.0.0.1", port, app) as httpd:
        httpd.serve_forever()


def main() -> int:
    logging.basicConfig(level=logging.WARNING)

    port = _free_port()
    url = f"http://127.0.0.1:{port}"

    # Daemon: the server dies with the window rather than stranding a process.
    threading.Thread(target=_serve, args=(port,), daemon=True).start()

    try:
        import webview

        webview.create_window(
            WINDOW_TITLE,
            url,
            width=1440,
            height=920,
            min_size=(900, 620),
        )
        webview.start()
        return 0
    except Exception as exc:  # noqa: BLE001 -- last-resort fallback
        print(f"Native window unavailable ({exc}); opening in your browser.", file=sys.stderr)
        print(f"SnipeSync Dashboard is running at {url}  (close this window to stop)")
        webbrowser.open(url)
        try:
            threading.Event().wait()
        except KeyboardInterrupt:
            pass
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
