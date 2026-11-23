"""preview_capture: Playwright screenshots of the project's preview origin in the studio device frames (DESIGN 4).

- URL is fixed: ``http://p-<id16>.localhost:<preview port>/`` (the project id comes from the validated tool input).
- Chromium gets the studio args of the preview WebView (dead proxy for every non-loopback request, WebRTC limited to
  proxied UDP) plus a route filter that aborts any request that is not same-origin, data: or blob: (M5, P7b).
- Frames: phone 390x844, tablet 834x1112, desktop 1440x900 (the UI's device frames). Each PNG is stored once under
  ``<data>/captures/<sha256>.png`` and announced with ``preview.captured {revision, frame, sha256}``; only digests go
  back to the agent. The owner's UI fetches an image by its sha256 over the authenticated REST API.
- Sync Playwright refuses to run on a thread with an asyncio loop (the MCP server's), so each capture runs on its own
  worker thread.
"""
from __future__ import annotations

import hashlib
import os
import re
import threading
from collections.abc import Callable
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

from studio_core import activity
from studio_core.errors import Unavailable, ValidationFailed

from . import service

FRAMES: dict[str, tuple[int, int]] = {"phone": (390, 844), "tablet": (834, 1112), "desktop": (1440, 900)}
BROWSER_ARGS = ("--proxy-server=http://127.0.0.1:9", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp",
                "--disable-extensions", "--no-first-run")
MAX_PNG_BYTES = 20 * 1024 * 1024
NAV_TIMEOUT_MS = 20_000
SETTLE_MS = 400
_SHA = re.compile(r"^[0-9a-f]{64}$")
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
_CAP_LOCK = threading.Lock()


def captures_dir(settings: Any) -> Path:
    return Path(settings.data_dir) / "captures"


def capture_file(settings: Any, sha256: str) -> Path | None:
    """The stored PNG for a digest, or None (bad id, missing, link)."""
    if not isinstance(sha256, str) or not _SHA.match(sha256):
        return None
    p = captures_dir(settings) / f"{sha256}.png"
    return p if p.is_file() and not p.is_symlink() else None


def _same_origin(url: str, origin: str) -> bool:
    a, b = urlsplit(url), urlsplit(origin)
    return (a.scheme, a.hostname, a.port) == (b.scheme, b.hostname, b.port)


def _shoot(url: str, frames: list[str]) -> dict[str, bytes]:
    from playwright.sync_api import sync_playwright

    origin = url.rstrip("/")
    shots: dict[str, bytes] = {}
    with sync_playwright() as pw:
        browser = pw.chromium.launch(headless=True, args=list(BROWSER_ARGS))
        try:
            for frame in frames:
                w, h = FRAMES[frame]
                ctx = browser.new_context(viewport={"width": w, "height": h}, device_scale_factor=1,
                                          service_workers="block", accept_downloads=False,
                                          is_mobile=frame == "phone", has_touch=frame != "desktop")
                try:
                    ctx.route("**/*", lambda route: route.continue_()
                              if _same_origin(route.request.url, origin) or route.request.url.startswith(("data:", "blob:"))
                              else route.abort("blockedbyclient"))
                    page = ctx.new_page()
                    page.goto(url, wait_until="load", timeout=NAV_TIMEOUT_MS)
                    page.wait_for_timeout(SETTLE_MS)
                    shots[frame] = page.screenshot(full_page=False, type="png")
                finally:
                    ctx.close()
        finally:
            browser.close()
    return shots


def capture(studio: Any, project_id: str, frames: list[str] | None = None, *, run_id: str | None = None,
            url: str | None = None, shoot: Callable[[str, list[str]], dict[str, bytes]] = _shoot) -> dict[str, Any]:
    if not isinstance(project_id, str) or not _PID.match(project_id):
        raise ValidationFailed("invalid project id")
    frames = list(frames or FRAMES)
    if not frames or any(f not in FRAMES for f in frames) or len(set(frames)) != len(frames):
        raise ValidationFailed("frames must be a subset of phone, tablet, desktop")
    if service.site_root_for(studio.settings)(project_id[4:]) is None:
        raise Unavailable("nothing to capture: build the web target first (app_build target web)")
    url = url or f"http://p-{project_id[4:]}.localhost:{studio.settings.preview_port}/"
    revision = studio.get_revision(project_id)
    if not _CAP_LOCK.acquire(blocking=False):
        raise Unavailable("a capture is already running")
    try:
        box: dict[str, Any] = {}

        def work() -> None:
            try:
                box["shots"] = shoot(url, frames)
            except BaseException as e:  # noqa: BLE001 - reported below
                box["error"] = e

        t = threading.Thread(target=work, name="aas-capture", daemon=True)
        t.start()
        t.join(timeout=120)
        if t.is_alive():
            raise Unavailable("the preview capture timed out")
        if "error" in box:
            raise Unavailable(f"the preview capture failed ({type(box['error']).__name__}: {str(box['error'])[:200]})")
        out: list[dict[str, Any]] = []
        d = captures_dir(studio.settings)
        d.mkdir(parents=True, exist_ok=True)
        for frame in frames:
            png = box["shots"].get(frame, b"")
            if not png.startswith(b"\x89PNG\r\n\x1a\n") or len(png) > MAX_PNG_BYTES:
                raise Unavailable(f"the {frame} capture is not a valid PNG")
            sha = hashlib.sha256(png).hexdigest()
            target = d / f"{sha}.png"
            if not target.is_file():
                tmp = d / f".{sha}.{os.getpid()}.partial"
                tmp.write_bytes(png)
                os.replace(tmp, target)
            try:
                activity.record(studio, "preview.captured", project_id=project_id, run_id=run_id, source="build",
                                artifact_sha256=sha, payload={"revision": revision, "frame": frame, "sha256": sha})
            except Exception:  # noqa: BLE001
                activity._note_dropped(studio)  # noqa: SLF001
            w, h = FRAMES[frame]
            out.append({"frame": frame, "sha256": sha, "width": w, "height": h, "bytes": len(png)})
        return {"revision": revision, "url": url, "captures": out}
    finally:
        _CAP_LOCK.release()
