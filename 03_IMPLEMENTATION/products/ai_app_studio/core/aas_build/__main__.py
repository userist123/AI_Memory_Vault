"""`python -m aas_build`: the per-project preview server on 127.0.0.1:<preview port> (default 17880).

Serves ``<data>/preview/<project>/current`` for ``Host: p-<id16>.localhost:<port>`` only. Runs until it is killed
(scripts/run_studio.ps1 stops it) or its stdin is closed.
"""
from __future__ import annotations

import os
import sys
import threading

from studio_core.config import load_settings

from .preview_server import PreviewServer
from .service import site_root_for


def main() -> None:
    settings = load_settings()
    dev = [o.strip() for o in os.environ.get("AAS_DEV_ORIGINS", "").split(",") if o.strip()]
    srv = PreviewServer(site_root_for(settings), port=settings.preview_port, extra_frame_ancestors=dev).start()
    print(f"preview server on http://127.0.0.1:{srv.port}", flush=True)
    stop = threading.Event()

    def watch_stdin() -> None:
        try:
            while sys.stdin.read(1024):
                pass
        except (OSError, ValueError):
            pass
        stop.set()

    if sys.stdin is not None and not sys.stdin.isatty():
        threading.Thread(target=watch_stdin, daemon=True).start()
    try:
        stop.wait()
    except KeyboardInterrupt:
        pass
    srv.stop()


if __name__ == "__main__":
    main()
