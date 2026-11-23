"""preview_capture: Playwright screenshots of the project's preview origin in the studio device frames (DESIGN 4).

- URL is fixed: ``http://p-<id16>.localhost:<preview port>/`` (the project id comes from the validated tool input).
- Chromium gets the studio args of the preview WebView (dead proxy for every non-loopback request, WebRTC limited to
  proxied UDP) plus a route filter that aborts any request that is not same-origin, data: or blob: (M5, P7b).
- Frames: phone 390x844, tablet 834x1112, desktop 1440x900 (the UI's device frames). Each PNG is stored once under
  ``<data>/captures/<sha256>.png`` and announced with ``preview.captured {revision, frame, sha256}``; only digests go
  back to the agent. The owner's UI fetches an image by its sha256 over the authenticated REST API.
- Sync Playwright refuses to run on a thread with an asyncio loop (the MCP server's), so each capture runs on its own
  worker thread. On timeout the Playwright driver (and so Chromium) is killed and the worker joined BEFORE the capture
  lock is released (S7 review L3); a worker that survives the kill releases the lock itself when it ends.
- Limits (S7 review M3): per project at most MAX_FILES files / MAX_BYTES bytes (the oldest are pruned; a file another
  project still references is kept), a PNG larger than MAX_PNG_BYTES is refused, and a capture is refused when the data
  drive has less than MIN_FREE_BYTES free. The per-project index is ``captures/.index/<project>.json``.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
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
MAX_PNG_BYTES = 8 * 1024 * 1024
MAX_FILES = 200
MAX_BYTES = 200 * 1024 * 1024
MIN_FREE_BYTES = 5 * 1024 ** 3
CAPTURE_TIMEOUT_S = 120.0
KILL_JOIN_S = 15.0
NAV_TIMEOUT_MS = 20_000
SETTLE_MS = 400
_SHA = re.compile(r"^[0-9a-f]{64}$")
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
_CAP_LOCK = threading.Lock()
_STATE = threading.Lock()  # worker end vs. the abandon decision (who releases _CAP_LOCK)
_INDEX_LOCK = threading.Lock()
_ACTIVE: dict[str, Any] = {}  # the Playwright driver process of the running _shoot


def _free_bytes(path: Path) -> int:
    p = Path(path)
    while not p.exists() and p.parent != p:
        p = p.parent
    return shutil.disk_usage(p).free


def _kill_active() -> None:
    """Kill the running capture's Playwright driver and its Chromium children (best effort; the worker then fails fast)."""
    proc = _ACTIVE.get("proc")
    pid = getattr(proc, "pid", None)
    if pid and os.name == "nt":
        tk = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32", "taskkill.exe")
        try:
            subprocess.run([tk, "/PID", str(int(pid)), "/T", "/F"], capture_output=True, timeout=20, check=False)  # noqa: S603
        except (OSError, subprocess.SubprocessError):
            pass
    if proc is not None:
        try:
            proc.kill()
        except Exception:  # noqa: BLE001 - already gone / not killable from here
            pass


def _index_dir(settings: Any) -> Path:
    return captures_dir(settings) / ".index"


def _read_index(path: Path) -> list[dict[str, Any]]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return []
    if not isinstance(data, list):
        return []
    return [e for e in data if isinstance(e, dict) and _SHA.match(str(e.get("sha", ""))) and isinstance(e.get("bytes"), int)]


def _register_and_prune(settings: Any, project_id: str, added: list[tuple[str, int]]) -> None:
    """Record the stored PNGs in the project's index (newest last) and prune the oldest over the count/byte quota."""
    idx = _index_dir(settings)
    idx.mkdir(parents=True, exist_ok=True)
    path = idx / f"{project_id}.json"
    with _INDEX_LOCK:
        entries = _read_index(path)
        for sha, size in added:
            entries = [e for e in entries if e["sha"] != sha] + [{"sha": sha, "bytes": size}]
        pruned: list[str] = []
        while len(entries) > 1 and (len(entries) > MAX_FILES or sum(e["bytes"] for e in entries) > MAX_BYTES):
            pruned.append(entries.pop(0)["sha"])
        tmp = idx / f".{project_id}.{os.getpid()}.partial"
        tmp.write_text(json.dumps(entries), encoding="utf-8")
        os.replace(tmp, path)
        if pruned:
            held = {e["sha"] for f in idx.glob("prj_*.json") for e in _read_index(f)}
            for sha in pruned:
                if sha not in held:
                    (captures_dir(settings) / f"{sha}.png").unlink(missing_ok=True)


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
        _ACTIVE["proc"] = getattr(getattr(getattr(getattr(pw, "_impl_obj", None), "_connection", None), "_transport", None),
                                  "_proc", None)
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
            _ACTIVE.pop("proc", None)
    return shots


def capture(studio: Any, project_id: str, frames: list[str] | None = None, *, run_id: str | None = None,
            url: str | None = None, shoot: Callable[[str, list[str]], dict[str, bytes]] = _shoot,
            kill: Callable[[], None] | None = None, timeout: float = CAPTURE_TIMEOUT_S, join_timeout: float = KILL_JOIN_S,
            free_bytes: Callable[[], int] | None = None) -> dict[str, Any]:
    if not isinstance(project_id, str) or not _PID.match(project_id):
        raise ValidationFailed("invalid project id")
    frames = list(frames or FRAMES)
    if not frames or any(f not in FRAMES for f in frames) or len(set(frames)) != len(frames):
        raise ValidationFailed("frames must be a subset of phone, tablet, desktop")
    if service.site_root_for(studio.settings)(project_id[4:]) is None:
        raise Unavailable("nothing to capture: build the web target first (app_build target web)")
    url = url or f"http://p-{project_id[4:]}.localhost:{studio.settings.preview_port}/"
    revision = studio.get_revision(project_id)
    free = free_bytes() if free_bytes else _free_bytes(captures_dir(studio.settings))
    if free < MIN_FREE_BYTES:
        raise Unavailable(f"preview capture is disabled: less than 5 GiB free on the data drive ({free // 2**20} MiB)")
    if not _CAP_LOCK.acquire(blocking=False):
        raise Unavailable("a capture is already running")
    handed_off = False
    try:
        box: dict[str, Any] = {}

        def work() -> None:
            try:
                box["shots"] = shoot(url, frames)
            except BaseException as e:  # noqa: BLE001 - reported below
                box["error"] = e
            finally:
                with _STATE:
                    box["done"] = True
                    if box.get("abandoned"):
                        _CAP_LOCK.release()  # the caller gave up on us: the lock is ours to release

        t = threading.Thread(target=work, name="aas-capture", daemon=True)
        t.start()
        t.join(timeout=timeout)
        if t.is_alive():
            try:
                (kill or _kill_active)()
            except Exception:  # noqa: BLE001
                pass
            t.join(timeout=join_timeout)
            with _STATE:
                if not box.get("done"):
                    box["abandoned"] = True
                    handed_off = True
            raise Unavailable("the preview capture timed out")
        if "error" in box:
            raise Unavailable(f"the preview capture failed ({type(box['error']).__name__}: {str(box['error'])[:200]})")
        out: list[dict[str, Any]] = []
        d = captures_dir(studio.settings)
        d.mkdir(parents=True, exist_ok=True)
        for frame in frames:  # validate every frame before anything is stored
            png = box["shots"].get(frame, b"")
            if not png.startswith(b"\x89PNG\r\n\x1a\n") or len(png) > MAX_PNG_BYTES:
                raise Unavailable(f"the {frame} capture is not a valid PNG within {MAX_PNG_BYTES // 2**20} MiB")
        stored: list[tuple[str, int]] = []
        for frame in frames:
            png = box["shots"][frame]
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
            stored.append((sha, len(png)))
            out.append({"frame": frame, "sha256": sha, "width": w, "height": h, "bytes": len(png)})
        _register_and_prune(studio.settings, project_id, stored)
        return {"revision": revision, "url": url, "captures": out}
    finally:
        if not handed_off:
            _CAP_LOCK.release()
