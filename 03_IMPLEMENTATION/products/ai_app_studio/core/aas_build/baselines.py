"""Visual regression protection (WOW W6): owner-approved baseline screenshots + perceptual diff after each build.

- The owner approves the current screens ("Aprobă ecranul"): every (route, frame) the capture knows is shot through
  ``capture.capture`` (same Chromium args, same-origin route filter, capture lock, timeout kill, 5 GiB disk gate,
  PNG validation) and its PNG copied to ``<data>/baselines/<project>/<sha256>.png``; the record lives in
  ``<data>/baselines/<project>/state.json``. Default route ``/``; the owner may add up to MAX_ROUTES same-origin paths
  (validated, joined to the fixed preview origin: a route can never name another host).
- Volatile regions: elements marked ``data-aas-volatile`` (clocks, timestamps, random avatars) are measured in the page
  (bounding boxes, clamped to the viewport, at most MAX_MASKS) and excluded from the diff, in the baseline and in the
  new capture alike.
- ``check`` (MCP ``baseline_status``, owner route ``POST .../baselines/check``) shoots the same keys again and compares
  each with its baseline: both images get a 1 px Gaussian blur (anti-aliasing / sub-pixel noise), the per-pixel max
  channel difference above PIXEL_TOLERANCE counts as changed; changed % = changed / unmasked pixels. A screen is
  ``changed`` above the project's threshold (default 0.1 %), or when more than ABS_FLOOR_PIXELS pixels changed, or when one
  changed region is >= 24x10 px (wow2 #7: a small title change on a big desktop frame stays under the percentage). A diff PNG (faded baseline, changed pixels red, masks
  outlined blue, change box magenta) is stored next to the baselines. A changed route the caller did not declare as
  intended is a warning: the agent should revert or fix it.
- Acceptance checklist (owner-editable, data under the studio data root, never in the app sources):
  ``<data>/baselines/<project>/checklist.json`` = {"flows": [{"id", "title", "statements": [str]}]}. Pass/fail comes
  from the flow-test results contract (W3, loose coupling): ``<data>/flow_results/<project>.json`` =
  {"results": [{"flow": "<id>", "status": "pass"|"fail"|"skip", "at": "<iso>", "message": "<short>"}]}; a missing or
  malformed file means "unknown", never an error.
- Quotas: at most MAX_ROUTES routes x 3 frames; the directory keeps only the PNGs the state references (baseline,
  last current, last diff) and refuses to grow beyond MAX_BYTES; writes are refused below capture.MIN_FREE_BYTES free.
"""
from __future__ import annotations

import datetime as _dt
import hashlib
import io
import json
import os
import re
import shutil
import threading
from collections.abc import Callable
from pathlib import Path
from typing import Any

from studio_core.errors import PolicyDenied, Unavailable, ValidationFailed

from . import capture

SCHEMA = 1
MAX_ROUTES = 8
MAX_MASKS = 50
MAX_MASK_PCT = 15.0           # masked share of the frame, owner approval refuses more (a mask hides regressions)
MAX_BYTES = 200 * 1024 * 1024
MAX_PIXELS = 4096 * 4096
PIXEL_TOLERANCE = 24          # 0..255, per channel after the blur
BLUR_RADIUS = 1
DEFAULT_THRESHOLD_PCT = 0.1   # % of the unmasked pixels
# wow2 #7: the percentage alone missed a visible title change on desktop (1280x800: 0.1 % is ~1000 px), so a screen is
# also changed when the changed-pixel COUNT passes an absolute floor or one visible change region is big enough.
ABS_FLOOR_PIXELS = 150
REGION_MIN_W, REGION_MIN_H = 24, 10
REGION_GAP = 6                # changed pixels closer than this belong to one region (glyph strokes of one word)
REGION_SCAN_MAX = 5000        # above this many changed pixels the floor already decides; no region search needed
MIN_THRESHOLD_PCT, MAX_THRESHOLD_PCT = 0.01, 20.0
MAX_FLOWS, MAX_STATEMENTS, MAX_STATEMENT_CHARS, MAX_TITLE_CHARS = 20, 30, 300, 120
FLOW_STATUSES = ("pass", "fail", "skip")
ROUTE_RE = re.compile(r"^/[A-Za-z0-9._~/\-]{0,100}(\?[A-Za-z0-9._~=&\-]{0,60})?(#[A-Za-z0-9._~/=&\-]{0,60})?$")
FLOW_ID_RE = re.compile(r"^[a-z0-9][a-z0-9_-]{0,39}$")
_SHA = re.compile(r"^[0-9a-f]{64}$")
_LOCK = threading.RLock()
VOLATILE_JS = ("() => Array.from(document.querySelectorAll('[data-aas-volatile]')).slice(0, %d).map(e => {"
               "const r = e.getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; })" % MAX_MASKS)

Rect = tuple[int, int, int, int]
Snap = Callable[[str, list[str]], tuple[dict[str, bytes], dict[str, list[Any]]]]


# ---------------------------------------------------------------- perceptual diff (pure)
def _open(png: bytes) -> Any:
    from PIL import Image
    if not isinstance(png, bytes | bytearray) or not bytes(png[:8]) == b"\x89PNG\r\n\x1a\n":
        raise ValidationFailed("not a PNG")
    im = Image.open(io.BytesIO(png))
    if im.width * im.height > MAX_PIXELS:
        raise ValidationFailed("image too large to compare")
    return im.convert("RGB")


def clean_masks(raw: Any, width: int, height: int) -> list[Rect]:
    """Untrusted page measurements -> at most MAX_MASKS integer rectangles clamped to the image."""
    out: list[Rect] = []
    for r in (raw if isinstance(raw, list) else [])[:MAX_MASKS]:
        if not isinstance(r, list | tuple) or len(r) != 4:
            continue
        try:
            x, y, w, h = (float(v) for v in r)
        except (TypeError, ValueError):
            continue
        if not all(v == v and abs(v) < 1e6 for v in (x, y, w, h)) or w <= 0 or h <= 0:  # NaN / absurd
            continue
        x0, y0 = max(0, int(x)), max(0, int(y))
        x1, y1 = min(width, int(x + w + 0.999)), min(height, int(y + h + 0.999))
        if x1 > x0 and y1 > y0:
            out.append((x0, y0, x1 - x0, y1 - y0))
    return out


def mask_pct(masks: Any, width: int, height: int) -> float:
    """Share (0..100) of the frame covered by the union of the (cleaned) masks."""
    from PIL import Image, ImageDraw
    rects = clean_masks([list(m) for m in masks] if isinstance(masks, list | tuple) else [], width, height)
    if not rects or width * height <= 0:
        return 0.0
    layer = Image.new("L", (width, height), 0)
    d = ImageDraw.Draw(layer)
    for x, y, mw, mh in rects:
        d.rectangle((x, y, x + mw - 1, y + mh - 1), fill=255)
    return round(100.0 * layer.histogram()[255] / (width * height), 2)


def largest_region(changed: Any, box: tuple[int, int, int, int] | None, n: int) -> list[int]:
    """[w, h] of the biggest group of changed pixels (pixels within REGION_GAP of each other join). Only searched for a
    small count of changed pixels (REGION_SCAN_MAX); more than that is already decided by the pixel floor."""
    if not box or n <= 0 or n > REGION_SCAN_MAX:
        return [0, 0]
    x0, y0, x1, y1 = box
    data = changed.crop(box).tobytes()
    bw = x1 - x0
    pts = [(x0 + i % bw, y0 + i // bw) for i, v in enumerate(data) if v]
    parent = list(range(len(pts)))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    cells: dict[tuple[int, int], list[int]] = {}
    for i, (x, y) in enumerate(pts):
        cx, cy = x // REGION_GAP, y // REGION_GAP
        for gx in (cx - 1, cx, cx + 1):
            for gy in (cy - 1, cy, cy + 1):
                for j in cells.get((gx, gy), ()):
                    if abs(pts[j][0] - x) <= REGION_GAP and abs(pts[j][1] - y) <= REGION_GAP:
                        parent[find(i)] = find(j)
        cells.setdefault((cx, cy), []).append(i)
    ext: dict[int, list[int]] = {}
    for i, (x, y) in enumerate(pts):
        e = ext.setdefault(find(i), [x, y, x, y])
        e[0], e[1], e[2], e[3] = min(e[0], x), min(e[1], y), max(e[2], x), max(e[3], y)
    best = [0, 0]
    for e in ext.values():
        w, h = e[2] - e[0] + 1, e[3] - e[1] + 1
        if w * h > best[0] * best[1]:
            best = [w, h]
    return best


def is_changed(d: dict[str, Any], threshold_pct: float) -> bool:
    """A screen differs when the share of changed pixels passes the owner's threshold, OR (while the threshold is the
    default or stricter) the count passes the absolute floor, OR one changed region is at least REGION_MIN_W x
    REGION_MIN_H. An owner who raised the threshold on purpose keeps the percentage-only rule he asked for."""
    if d["changedPct"] > threshold_pct or d["sizeChanged"]:
        return True
    if threshold_pct > DEFAULT_THRESHOLD_PCT:
        return False
    rw, rh = d.get("region") or [0, 0]
    return bool(d["changedPixels"] > ABS_FLOOR_PIXELS or (rw >= REGION_MIN_W and rh >= REGION_MIN_H))


def diff_images(base_png: bytes, cur_png: bytes, masks: list[Rect] | None = None,
                *, tolerance: int = PIXEL_TOLERANCE) -> dict[str, Any]:
    """{changedPct, changedPixels, comparedPixels, bbox [x,y,w,h]|None, region [w,h], sizeChanged, diffPng}."""
    from PIL import Image, ImageChops, ImageDraw, ImageFilter
    a, b = _open(base_png), _open(cur_png)
    size_changed = a.size != b.size
    w, h = max(a.width, b.width), max(a.height, b.height)
    if size_changed:  # the uncovered area of either image always counts as changed (black vs white padding)
        ca, cb = Image.new("RGB", (w, h), (0, 0, 0)), Image.new("RGB", (w, h), (255, 255, 255))
        ca.paste(a, (0, 0))
        cb.paste(b, (0, 0))
        a, b = ca, cb
    fa, fb = a.filter(ImageFilter.GaussianBlur(BLUR_RADIUS)), b.filter(ImageFilter.GaussianBlur(BLUR_RADIUS))
    r, g, bl = ImageChops.difference(fa, fb).split()
    delta = ImageChops.lighter(ImageChops.lighter(r, g), bl)
    changed = delta.point(lambda v: 255 if v > tolerance else 0)
    mask_layer = Image.new("L", (w, h), 0)
    md = ImageDraw.Draw(mask_layer)
    rects = clean_masks([list(m) for m in (masks or [])], w, h)
    for x, y, mw, mh in rects:
        md.rectangle((x, y, x + mw - 1, y + mh - 1), fill=255)
    changed = ImageChops.subtract(changed, mask_layer)
    masked = mask_layer.histogram()[255]
    compared = max(1, w * h - masked)
    n = changed.histogram()[255]
    box = changed.getbbox()
    # diff image: faded baseline, changed pixels red, masks outlined blue, change box magenta
    faded = Image.blend(a.convert("L").convert("RGB"), Image.new("RGB", (w, h), (255, 255, 255)), 0.6)
    out = Image.composite(Image.new("RGB", (w, h), (230, 20, 40)), faded, changed)
    d = ImageDraw.Draw(out)
    for x, y, mw, mh in rects:
        d.rectangle((x, y, x + mw - 1, y + mh - 1), outline=(40, 90, 220), width=2)
    if box:
        d.rectangle((box[0], box[1], box[2] - 1, box[3] - 1), outline=(200, 0, 200), width=2)
    buf = io.BytesIO()
    out.save(buf, "PNG", optimize=True)
    return {"changedPct": round(100.0 * n / compared, 4), "changedPixels": n, "comparedPixels": compared,
            "bbox": [box[0], box[1], box[2] - box[0], box[3] - box[1]] if box else None,
            "region": largest_region(changed, box, n), "sizeChanged": size_changed, "diffPng": buf.getvalue()}


# ---------------------------------------------------------------- storage
def _now() -> str:
    return _dt.datetime.now(_dt.UTC).isoformat(timespec="seconds")


def _check_pid(project_id: Any) -> str:
    if not isinstance(project_id, str) or not capture._PID.fullmatch(project_id):  # noqa: SLF001
        raise ValidationFailed("invalid project id")
    return project_id


def project_dir(settings: Any, project_id: str) -> Path:
    return capture._contained(Path(settings.data_dir) / "baselines", _check_pid(project_id))  # noqa: SLF001


def _state_path(settings: Any, pid: str) -> Path:
    return project_dir(settings, pid) / "state.json"


def _read_json(path: Path) -> Any:
    try:
        if path.is_symlink():
            return None
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def _write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.parent / f".{path.name}.{os.getpid()}.partial"
    tmp.write_text(json.dumps(data, indent=1), encoding="utf-8")
    os.replace(tmp, path)


def _empty_state() -> dict[str, Any]:
    return {"schema": SCHEMA, "routes": ["/"], "thresholdPct": DEFAULT_THRESHOLD_PCT, "baselines": {}, "lastCheck": None}


def load_state(settings: Any, pid: str) -> dict[str, Any]:
    st = _read_json(_state_path(settings, pid))
    if not isinstance(st, dict) or st.get("schema") != SCHEMA:
        return _empty_state()
    base = _empty_state()
    routes = [r for r in st.get("routes", []) if isinstance(r, str) and ROUTE_RE.fullmatch(r)][:MAX_ROUTES]
    base["routes"] = routes or ["/"]
    t = st.get("thresholdPct")
    if isinstance(t, int | float) and not isinstance(t, bool) and MIN_THRESHOLD_PCT <= t <= MAX_THRESHOLD_PCT:
        base["thresholdPct"] = float(t)
    bl = st.get("baselines")
    if isinstance(bl, dict):
        base["baselines"] = {k: v for k, v in bl.items() if isinstance(v, dict) and _SHA.fullmatch(str(v.get("sha256", "")))
                             and v.get("frame") in capture.FRAMES and isinstance(v.get("route"), str)}
    if isinstance(st.get("lastCheck"), dict):
        base["lastCheck"] = st["lastCheck"]
    return base


def key_of(route: str, frame: str) -> str:
    return f"{route}@{frame}"


def _referenced(st: dict[str, Any]) -> set[str]:
    shas = {v["sha256"] for v in st["baselines"].values()}
    for r in (st.get("lastCheck") or {}).get("results", []):
        for k in ("currentSha256", "diffSha256"):
            if isinstance(r, dict) and isinstance(r.get(k), str) and _SHA.fullmatch(r[k]):
                shas.add(r[k])
    return shas


def _gc(settings: Any, pid: str, st: dict[str, Any]) -> None:
    d = project_dir(settings, pid)
    keep = _referenced(st)
    for p in d.glob("*.png"):
        if p.stem not in keep and not p.is_symlink():
            p.unlink(missing_ok=True)


def _dir_bytes(d: Path) -> int:
    return sum(p.stat().st_size for p in d.glob("*.png") if p.is_file() and not p.is_symlink()) if d.is_dir() else 0


def _store_png(settings: Any, pid: str, png: bytes, free_bytes: Callable[[], int] | None = None) -> str:
    d = project_dir(settings, pid)
    d.mkdir(parents=True, exist_ok=True)
    if len(png) > capture.MAX_PNG_BYTES:
        raise Unavailable("baseline image too large")
    free = free_bytes() if free_bytes else shutil.disk_usage(d).free
    if free < capture.MIN_FREE_BYTES:
        raise Unavailable(f"baselines are disabled: less than 5 GiB free on the data drive ({free // 2**20} MiB)")
    sha = hashlib.sha256(png).hexdigest()
    target = capture._contained(d, f"{sha}.png")  # noqa: SLF001
    if not target.is_file():
        if _dir_bytes(d) + len(png) > MAX_BYTES:
            raise Unavailable("the project's baseline storage quota is full")
        tmp = d / f".{sha}.{os.getpid()}.partial"
        tmp.write_bytes(png)
        os.replace(tmp, target)
    return sha


def baseline_file(settings: Any, project_id: str, sha256: str) -> Path | None:
    """A stored baseline / current / diff PNG of the project, or None (bad id, missing, link)."""
    if not isinstance(sha256, str) or not _SHA.fullmatch(sha256):
        return None
    try:
        p = capture._contained(project_dir(settings, project_id), f"{sha256}.png")  # noqa: SLF001
    except ValidationFailed:
        return None
    return p if p.is_file() and not p.is_symlink() else None


def forget_project(settings: Any, project_id: str) -> None:
    """Project delete: drop the project's baselines, checklist, diffs and flow results."""
    try:
        d = project_dir(settings, project_id)
    except ValidationFailed:
        return
    with _LOCK:
        if d.is_dir() and not d.is_symlink():
            shutil.rmtree(d, ignore_errors=True)
        try:  # the W3 flow results of the project (checklist pass/fail)
            r = capture._contained(Path(settings.data_dir) / "flow_results", f"{_check_pid(project_id)}.json")  # noqa: SLF001
            if r.is_file() or r.is_symlink():
                r.unlink()
        except (OSError, ValidationFailed):
            pass


# ---------------------------------------------------------------- capture (Playwright, masks measured in the page)
def _snap(url: str, frames: list[str]) -> tuple[dict[str, bytes], dict[str, list[Any]]]:
    """capture._shoot plus the volatile rectangles per frame (same browser args, same-origin filter, kill hook)."""
    from playwright.sync_api import sync_playwright

    origin = url
    shots: dict[str, bytes] = {}
    masks: dict[str, list[Any]] = {}
    with sync_playwright() as pw:
        capture._ACTIVE["proc"] = getattr(getattr(getattr(getattr(pw, "_impl_obj", None), "_connection", None),  # noqa: SLF001
                                                  "_transport", None), "_proc", None)
        browser = pw.chromium.launch(headless=True, args=list(capture.BROWSER_ARGS))
        try:
            for frame in frames:
                w, h = capture.FRAMES[frame]
                ctx = browser.new_context(viewport={"width": w, "height": h}, device_scale_factor=1,
                                          service_workers="block", accept_downloads=False,
                                          is_mobile=frame == "phone", has_touch=frame != "desktop")
                try:
                    ctx.route("**/*", lambda route: route.continue_()
                              if capture._same_origin(route.request.url, origin)  # noqa: SLF001
                              or route.request.url.startswith(("data:", "blob:"))
                              else route.abort("blockedbyclient"))
                    page = ctx.new_page()
                    page.goto(url, wait_until="load", timeout=capture.NAV_TIMEOUT_MS)
                    page.wait_for_timeout(capture.SETTLE_MS)
                    try:  # an isolated world: the page's own scripts cannot patch what we measure with
                        from .qa import _eval_isolated
                        masks[frame] = _eval_isolated(ctx, page, [f"({VOLATILE_JS})()"])[0]
                    except Exception:  # noqa: BLE001 - no masks is a valid (stricter) comparison
                        masks[frame] = []
                    shots[frame] = page.screenshot(full_page=False, type="png", animations="disabled", caret="hide")
                finally:
                    ctx.close()
        finally:
            browser.close()
            capture._ACTIVE.pop("proc", None)  # noqa: SLF001
    return shots, masks


def _shoot_route(studio: Any, pid: str, route: str, frames: list[str], snap: Snap, run_id: str | None,
                 free_bytes: Callable[[], int] | None) -> dict[str, tuple[bytes, list[Rect]]]:
    if not ROUTE_RE.fullmatch(route) or "//" in route or ".." in route:
        raise ValidationFailed("invalid route")
    url = f"http://p-{pid[4:]}.localhost:{studio.settings.preview_port}{route}"
    box: dict[str, Any] = {}

    def shoot(u: str, fr: list[str]) -> dict[str, bytes]:
        shots, masks = snap(u, fr)
        box["masks"] = masks if isinstance(masks, dict) else {}
        return shots
    res = capture.capture(studio, pid, frames, run_id=run_id, url=url, shoot=shoot, free_bytes=free_bytes)
    out: dict[str, tuple[bytes, list[Rect]]] = {}
    for c in res["captures"]:
        p = capture.capture_file(studio.settings, c["sha256"])
        if p is None:
            raise Unavailable("the capture disappeared before it could be compared")
        w, h = capture.FRAMES[c["frame"]]
        out[c["frame"]] = (p.read_bytes(), clean_masks(box.get("masks", {}).get(c["frame"]), w, h))
    return out


def _routes_arg(routes: Any) -> list[str] | None:
    if routes is None:
        return None
    if not isinstance(routes, list) or not 1 <= len(routes) <= MAX_ROUTES or len(set(routes)) != len(routes):
        raise ValidationFailed(f"routes: 1..{MAX_ROUTES} distinct paths")
    for r in routes:
        if not isinstance(r, str) or not ROUTE_RE.fullmatch(r) or "//" in r or ".." in r:
            raise ValidationFailed("invalid route")
    return list(routes)


# ---------------------------------------------------------------- owner: approve / reset / settings
def _owner(actor: Any) -> None:
    if not getattr(actor, "is_owner", False):
        raise PolicyDenied("only the authenticated human owner can approve or change baselines")


def _audit(studio: Any, actor: Any, action: str, pid: str, data: dict[str, Any]) -> None:
    from studio_core import audit
    with studio.audit_session() as s:
        audit.record(s, actor, action, "ok", project_id=pid, data=data)


def approve(studio: Any, actor: Any, project_id: str, *, frames: list[str] | None = None, routes: list[str] | None = None,
            snap: Snap = _snap, free_bytes: Callable[[], int] | None = None) -> dict[str, Any]:
    """Owner: the current screens become the baselines (all frames of every known route unless narrowed)."""
    _owner(actor)
    pid = _check_pid(project_id)
    frames = list(frames or capture.FRAMES)
    if any(f not in capture.FRAMES for f in frames) or len(set(frames)) != len(frames):
        raise ValidationFailed("frames must be a subset of phone, tablet, desktop")
    with _LOCK:
        st = load_state(studio.settings, pid)
        if (rs := _routes_arg(routes)) is not None:
            st["routes"] = rs
        revision = studio.get_revision(pid)
        approved: list[str] = []
        for route in st["routes"]:
            shots = _shoot_route(studio, pid, route, frames, snap, None, free_bytes)
            for frame, (png, masks) in shots.items():
                fw, fh = capture.FRAMES[frame]
                pct = mask_pct(masks, fw, fh)
                if pct > MAX_MASK_PCT:
                    raise ValidationFailed(f"{route} ({frame}): the volatile regions cover {pct}% of the screen; "
                                           f"at most {MAX_MASK_PCT:g}% may be masked, so the screen cannot be approved "
                                           "as it is (remove data-aas-volatile from large areas)")
            for frame, (png, masks) in shots.items():
                sha = _store_png(studio.settings, pid, png, free_bytes)
                st["baselines"][key_of(route, frame)] = {"route": route, "frame": frame, "sha256": sha,
                                                          "masks": [list(m) for m in clean_masks(masks, *capture.FRAMES[frame])],
                                                          "maskedPct": mask_pct(masks, *capture.FRAMES[frame]),
                                                          "revision": revision,
                                                          "approvedAt": _now()}
                approved.append(key_of(route, frame))
        st["baselines"] = {k: v for k, v in st["baselines"].items() if v["route"] in st["routes"]}
        st["lastCheck"] = None
        _write_json(_state_path(studio.settings, pid), st)
        _gc(studio.settings, pid, st)
    _audit(studio, actor, "baseline.approve", pid, {"revision": revision, "keys": approved})
    return status(studio, pid)


def reset(studio: Any, actor: Any, project_id: str) -> dict[str, Any]:
    _owner(actor)
    pid = _check_pid(project_id)
    with _LOCK:
        st = load_state(studio.settings, pid)
        st["baselines"], st["lastCheck"] = {}, None
        _write_json(_state_path(studio.settings, pid), st)
        _gc(studio.settings, pid, st)
    _audit(studio, actor, "baseline.reset", pid, {})
    return status(studio, pid)


def set_threshold(studio: Any, actor: Any, project_id: str, pct: float) -> dict[str, Any]:
    _owner(actor)
    pid = _check_pid(project_id)
    if isinstance(pct, bool) or not isinstance(pct, int | float) or not MIN_THRESHOLD_PCT <= pct <= MAX_THRESHOLD_PCT:
        raise ValidationFailed(f"threshold must be {MIN_THRESHOLD_PCT}..{MAX_THRESHOLD_PCT} %")
    with _LOCK:
        st = load_state(studio.settings, pid)
        st["thresholdPct"] = float(pct)
        _write_json(_state_path(studio.settings, pid), st)
    _audit(studio, actor, "baseline.threshold", pid, {"thresholdPct": float(pct)})
    return status(studio, pid)


# ---------------------------------------------------------------- check (agent tool + owner)
def check(studio: Any, project_id: str, *, intended_routes: list[str] | None = None, run_id: str | None = None,
          snap: Snap = _snap, free_bytes: Callable[[], int] | None = None) -> dict[str, Any]:
    """Capture every baselined (route, frame) again and compare. Never changes the baselines."""
    pid = _check_pid(project_id)
    intended = set(_routes_arg(intended_routes) or []) if intended_routes else set()
    with _LOCK:
        st = load_state(studio.settings, pid)
        if not st["baselines"]:
            return {**status(studio, pid), "status": "no_baseline",
                    "next": "no approved screens yet: the owner approves them in the studio (Aprobă ecranul)"}
        revision = studio.get_revision(pid)
        by_route: dict[str, list[str]] = {}
        for v in st["baselines"].values():
            by_route.setdefault(v["route"], []).append(v["frame"])
        results: list[dict[str, Any]] = []
        for route, frames in by_route.items():
            frames = [f for f in capture.FRAMES if f in frames]
            shots = _shoot_route(studio, pid, route, frames, snap, run_id, free_bytes)
            for frame in frames:
                b = st["baselines"][key_of(route, frame)]
                bp = baseline_file(studio.settings, pid, b["sha256"])
                png, masks = shots[frame]
                cur_sha = _store_png(studio.settings, pid, png, free_bytes)
                row: dict[str, Any] = {"key": key_of(route, frame), "route": route, "frame": frame,
                                       "baselineSha256": b["sha256"], "currentSha256": cur_sha,
                                       "baselineRevision": b.get("revision")}
                if bp is None:
                    row.update(status="baseline_missing", changedPct=None)
                else:
                    # only the masks the owner approved; whatever the page claims now is ignored
                    allm = clean_masks(b.get("masks"), *capture.FRAMES[frame])
                    mpct = mask_pct(allm, *capture.FRAMES[frame])
                    d = diff_images(bp.read_bytes(), png, allm)
                    row.update(changedPct=d["changedPct"], changedPixels=d["changedPixels"], bbox=d["bbox"],
                               region=d["region"], sizeChanged=d["sizeChanged"],
                               maskedRegions=len(allm), maskedPct=mpct,
                               status="changed" if is_changed(d, st["thresholdPct"]) or mpct > MAX_MASK_PCT else "same")
                    if d["changedPixels"]:
                        row["diffSha256"] = _store_png(studio.settings, pid, d["diffPng"], free_bytes)
                results.append(row)
        warnings = [f"{r['route']} ({r['frame']}) changed {r['changedPct']}% although it was not declared as intended: "
                    "revert or fix it unless the owner asked for it"
                    for r in results if r["status"] == "changed" and r["route"] not in intended]
        st["lastCheck"] = {"at": _now(), "revision": revision, "thresholdPct": st["thresholdPct"],
                           "intendedRoutes": sorted(intended), "results": results, "warnings": warnings}
        _write_json(_state_path(studio.settings, pid), st)
        _gc(studio.settings, pid, st)
    changed = [r for r in results if r["status"] == "changed"]
    return {**status(studio, pid), "status": "changed" if changed else "same"}


def status(studio: Any, project_id: str) -> dict[str, Any]:
    pid = _check_pid(project_id)
    st = load_state(studio.settings, pid)
    for v in st["baselines"].values():
        v["maskedPct"] = mask_pct(v.get("masks"), *capture.FRAMES[v["frame"]])
    return {"projectId": pid, "routes": st["routes"], "thresholdPct": st["thresholdPct"],
            "baselines": sorted(st["baselines"].values(), key=lambda v: (v["route"], list(capture.FRAMES).index(v["frame"]))),
            "lastCheck": st["lastCheck"]}


# ---------------------------------------------------------------- acceptance checklist
def _checklist_path(settings: Any, pid: str) -> Path:
    return project_dir(settings, pid) / "checklist.json"


def validate_checklist(data: Any) -> dict[str, Any]:
    flows = data.get("flows") if isinstance(data, dict) else None
    if not isinstance(flows, list) or len(flows) > MAX_FLOWS:
        raise ValidationFailed(f"flows: a list of at most {MAX_FLOWS}")
    out, seen = [], set()
    for f in flows:
        if not isinstance(f, dict) or set(f) - {"id", "title", "statements"}:
            raise ValidationFailed("a flow is {id, title, statements}")
        fid, title, stm = f.get("id"), f.get("title"), f.get("statements")
        if not isinstance(fid, str) or not FLOW_ID_RE.fullmatch(fid) or fid in seen:
            raise ValidationFailed("flow id: lowercase letters, digits, - or _, unique, at most 40")
        if not isinstance(title, str) or not 1 <= len(title.strip()) <= MAX_TITLE_CHARS:
            raise ValidationFailed(f"flow title: 1..{MAX_TITLE_CHARS} characters")
        if not isinstance(stm, list) or len(stm) > MAX_STATEMENTS or not all(
                isinstance(s, str) and 1 <= len(s.strip()) <= MAX_STATEMENT_CHARS for s in stm):
            raise ValidationFailed(f"statements: at most {MAX_STATEMENTS} texts of 1..{MAX_STATEMENT_CHARS} characters")
        seen.add(fid)
        out.append({"id": fid, "title": title.strip(), "statements": [s.strip() for s in stm]})
    return {"flows": out}


def _flow_results(settings: Any, pid: str) -> dict[str, dict[str, Any]]:
    """W3 contract reader; anything malformed is ignored (status unknown)."""
    raw = _read_json(capture._contained(Path(settings.data_dir) / "flow_results", f"{pid}.json"))  # noqa: SLF001
    res: dict[str, dict[str, Any]] = {}
    for r in (raw.get("results") if isinstance(raw, dict) and isinstance(raw.get("results"), list) else [])[:200]:
        if isinstance(r, dict) and isinstance(r.get("flow"), str) and FLOW_ID_RE.fullmatch(r["flow"]) \
                and r.get("status") in FLOW_STATUSES:
            res[r["flow"]] = {"status": r["status"], "at": str(r.get("at", ""))[:40],
                              "message": str(r.get("message", ""))[:300]}
    return res


def get_checklist(settings: Any, project_id: str) -> dict[str, Any]:
    pid = _check_pid(project_id)
    raw = _read_json(_checklist_path(settings, pid))
    try:
        cl = validate_checklist(raw) if raw is not None else {"flows": []}
    except ValidationFailed:
        cl = {"flows": []}
    results = _flow_results(settings, pid)
    for f in cl["flows"]:
        r = results.get(f["id"])
        f["result"] = r or {"status": "unknown", "at": "", "message": ""}
    return {"projectId": pid, **cl}


def set_checklist(studio: Any, actor: Any, project_id: str, data: Any) -> dict[str, Any]:
    _owner(actor)
    pid = _check_pid(project_id)
    cl = validate_checklist(data)
    with _LOCK:
        _write_json(_checklist_path(studio.settings, pid), cl)
    _audit(studio, actor, "checklist.update", pid, {"flows": len(cl["flows"])})
    return get_checklist(studio.settings, pid)


def require_project(studio: Any, actor: Any, project_id: str) -> None:
    """NotFound unless the project exists (and the actor may read it)."""
    studio.peek_project(actor, _check_pid(project_id))
