"""Visual Perception Engine (spec 7): capture, store, inspect, analyze, compare.

Captures are real files: PNG + metadata.json (+ dom.json, console.json for browser captures)
under <project>/artifacts/captures/<captureId>/. Metadata hashes are computed from the bytes
actually written; a failed capture raises CaptureFailed and stores nothing.
"""

from __future__ import annotations

import asyncio
import json
import os
import re
import sys
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path
from typing import Any

from PIL import Image

from ..config import REPO_ROOT
from ..db import Screenshot, VisualAnalysis
from ..errors import CaptureFailed, NotFound, Unavailable, ValidationFailed
from ..ids import iso, new_id, utcnow
from ..policy import Actor
from ..services import OpContext, Studio
from ..workspace import Workspace, sha256_bytes
from .analyze import ANALYZER, analyze
from .compare import pixel_diff, semantic_diff
from .dom_probe import DOM_PROBE_JS
from .ocr import ENGINE, ocr_png
from .preview_server import PreviewServer

VIEWPORTS: dict[str, dict[str, int]] = {
    "desktop": {"width": 1440, "height": 900},
    "tablet": {"width": 834, "height": 1112},
    "mobile": {"width": 390, "height": 844},
}

def local_browsers_path() -> Path:
    """AAS identity: AAS_PLAYWRIGHT_BROWSERS_PATH (the AWS_* name is ignored)."""
    return Path(os.environ.get("AAS_PLAYWRIGHT_BROWSERS_PATH", REPO_ROOT / ".tools" / "ms-playwright"))


_local_browsers = local_browsers_path()
if _local_browsers.exists():
    # Process-local override: the app uses the Chromium build pinned with its own Playwright,
    # regardless of a user-wide PLAYWRIGHT_BROWSERS_PATH pointing at other builds.
    os.environ["PLAYWRIGHT_BROWSERS_PATH"] = str(_local_browsers)


SETTLE_ANIMATIONS_JS = """
async (maxMs) => {
  const finite = document.getAnimations().filter(a => {
    const t = a.effect && a.effect.getComputedTiming(); return t && Number.isFinite(t.endTime); });
  let timedOut = false;
  await Promise.race([
    Promise.all(finite.map(a => a.finished.catch(() => null))),
    new Promise(r => setTimeout(() => { timedOut = true; r(); }, maxMs)),
  ]);
  await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  return {awaited: finite.length, timedOut};
}
"""


def same_origin(url: str, base: str) -> bool:
    """Exact scheme/host/port comparison (a string prefix would accept http://127.0.0.1:P@evil/)."""
    from urllib.parse import urlsplit
    u, b = urlsplit(url), urlsplit(base)
    return (u.scheme, u.hostname, u.port, u.username, u.password) == (b.scheme, b.hostname, b.port, None, None)


@dataclass
class Capture:
    capture_id: str
    png: bytes
    metadata: dict[str, Any]
    dom: dict[str, Any] | None = None
    console: list[dict[str, Any]] | None = None
    network: list[dict[str, Any]] | None = None


class BrowserPool:
    """One headless Chromium per process, started lazily."""

    def __init__(self) -> None:
        self._pw: Any = None
        self._browser: Any = None
        self._lock = asyncio.Lock()

    async def browser(self) -> Any:
        loop = asyncio.get_running_loop()
        if getattr(self, "_loop", None) is not loop:
            # Playwright objects are bound to the event loop that created them; a new loop needs a new driver
            self._loop, self._lock, self._browser, self._pw = loop, asyncio.Lock(), None, None
        async with self._lock:
            if self._browser is None or not self._browser.is_connected():
                from playwright.async_api import async_playwright
                self._pw = await async_playwright().start()
                self._browser = await self._pw.chromium.launch(headless=True, args=["--disable-extensions"])
            return self._browser

    async def close(self) -> None:
        if self._browser is not None:
            await self._browser.close()
        if self._pw is not None:
            await self._pw.stop()
        self._browser = self._pw = None


class VisualService:
    def __init__(self, studio: Studio, preview: PreviewServer | None = None) -> None:
        self.studio = studio
        self.preview = preview or PreviewServer(lambda pid: studio.workspace(pid, create=False)).start()
        self.browsers = BrowserPool()
        self.last_page: dict[str, Any] = {}

    # ---------------------------------------------------------------- storage
    def _store(self, ws: Workspace, capture_id: str, png: bytes, meta: dict[str, Any],
               extra: dict[str, Any]) -> None:
        base = f"artifacts/captures/{capture_id}"
        p = ws.write_bytes(f"{base}/screenshot.png", png)
        written = p.read_bytes()
        if sha256_bytes(written) != meta["sha256"]:
            raise CaptureFailed("screenshot bytes on disk do not match the captured bytes")
        ws.write_text(f"{base}/metadata.json", json.dumps(meta, indent=2))
        for name, val in extra.items():
            if val is not None:
                ws.write_text(f"{base}/{name}.json", json.dumps(val, indent=2))

    def _row(self, meta: dict[str, Any], rel_path: str) -> None:
        with self.studio.session() as s:
            s.add(Screenshot(id=meta["captureId"], project_id=meta.get("projectId"), revision=meta.get("revision"),
                             source=meta["source"], rel_path=rel_path, sha256=meta["sha256"],
                             width=meta["image"]["width"], height=meta["image"]["height"], meta=meta))

    def _workspace_for_capture(self, project_id: str | None) -> Workspace:
        if project_id:
            return self.studio.workspace(project_id)
        return Workspace(self.studio.settings.data_dir / "desktop-captures")

    # ---------------------------------------------------------------- browser capture
    async def capture_preview(self, actor: Actor, project_id: str, *, viewport: str | dict[str, int] = "desktop",
                              full_page: bool = False, element_id: str | None = None,
                              region: dict[str, int] | None = None, source: str = "browser_preview",
                              wait_ms: int = 300, animation_time_ms: int | None = None) -> Capture:
        vp = VIEWPORTS.get(viewport) if isinstance(viewport, str) else viewport
        if not vp or not (200 <= int(vp["width"]) <= 3840 and 200 <= int(vp["height"]) <= 2160):
            raise ValidationFailed("viewport must be desktop|tablet|mobile or {width,height} within 200..3840x2160")
        if not 0 <= wait_ms <= 10000:
            raise ValidationFailed("wait_ms must be 0..10000")
        revision = self.studio.get_revision(project_id)
        site = self.studio.site_dir(project_id) / "index.html"
        if not site.exists():
            raise CaptureFailed("project has no rendered site yet")
        url = self.preview.url_for(project_id)

        async def _do() -> Capture:
            browser = await self.browsers.browser()
            ctx = await browser.new_context(viewport={"width": int(vp["width"]), "height": int(vp["height"])},
                                            device_scale_factor=1, java_script_enabled=True,
                                            service_workers="block", accept_downloads=False)
            console: list[dict[str, Any]] = []
            network: list[dict[str, Any]] = []
            try:
                page = await ctx.new_page()
                # the preview may only talk to its own origin
                await ctx.route("**/*", lambda route: route.continue_()
                                if same_origin(route.request.url, self.preview.base_url) or route.request.url.startswith(("data:", "blob:"))
                                else route.abort("blockedbyclient"))
                page.on("console", lambda m: console.append({"type": m.type, "text": m.text[:1000]}))
                page.on("pageerror", lambda e: console.append({"type": "pageerror", "text": str(e)[:1000]}))
                page.on("requestfailed", lambda r: network.append({"url": r.url, "failure": r.failure}))
                page.on("response", lambda r: network.append({"url": r.url, "status": r.status}) if r.status >= 400 else None)
                resp = await page.goto(url, wait_until="load", timeout=self.studio.settings.capture_timeout_ms)
                if resp is None or resp.status != 200:
                    raise CaptureFailed(f"preview returned HTTP {resp.status if resp else 'none'}")
                await page.wait_for_timeout(wait_ms)
                # Let finite CSS/Web animations finish so the capture shows the settled layout
                # (infinite ones, e.g. a 3D render loop, are not awaited). Capped at 5 s.
                # 3D scenes load models asynchronously: wait until none is still 'loading' (max 20 s)
                try:
                    await page.wait_for_function(
                        "() => [...document.querySelectorAll('canvas[data-scene]')]"
                        ".every(c => c.dataset.sceneStatus && c.dataset.sceneStatus !== 'loading')", timeout=20000)
                except Exception:  # noqa: BLE001,S110 - status 'loading' is then reported by the analyzer
                    pass
                if animation_time_ms is not None:
                    seeked = await page.evaluate(
                        "t => (window.__awsAnimations ? window.__awsAnimations.seek(t) : -1)", int(animation_time_ms))
                    await page.evaluate("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))")
                    settle = {"seekMs": int(animation_time_ms), "animationsSeeked": seeked}
                else:
                    settle = await page.evaluate(SETTLE_ANIMATIONS_JS, 5000)
                dom = await page.evaluate(DOM_PROBE_JS)
                if str(dom["document"]["revision"]) != str(revision):
                    raise CaptureFailed("rendered page revision does not match project revision",
                                        page=dom["document"]["revision"], project=revision)
                clip = None
                if element_id:
                    el = next((e for e in dom["elements"] if e["elementId"] == element_id), None)
                    if el is None:
                        raise NotFound("element not found in rendered page", elementId=element_id)
                    loc = page.locator(f'[data-component-id="{element_id}"]')
                    png = await loc.screenshot(timeout=self.studio.settings.capture_timeout_ms)
                    clip = el["bounds"]
                elif region:
                    clip = {k: int(region[k]) for k in ("x", "y", "width", "height")}
                    if clip["width"] <= 0 or clip["height"] <= 0:
                        raise ValidationFailed("region width/height must be positive")
                    png = await page.screenshot(clip=clip, full_page=True)
                else:
                    png = await page.screenshot(full_page=full_page)
                try:
                    aria = await page.locator("body").aria_snapshot()
                except Exception as e:  # noqa: BLE001
                    aria = f"unavailable: {type(e).__name__}"
                self.last_page[project_id] = {"url": url, "console": console, "network": network, "aria": aria,
                                              "dom": dom, "viewport": vp}
                return self._finish(project_id, revision, source, png, vp, dom, console, network,
                                    {"url": url, "fullPage": full_page, "elementId": element_id, "clip": clip,
                                     "animations": settle,
                                     "scenes": [{"componentId": e["elementId"], **e["scene"]} for e in dom["elements"]
                                                if e.get("scene")],
                                     "browser": f"chromium {browser.version}", "aria": aria[:20000]})
            finally:
                await ctx.close()

        self.studio.policy.enforce(actor, "visual.capture")  # before touching the browser
        params = {"projectId": project_id, "viewport": viewport, "fullPage": full_page,
                  "elementId": element_id, "region": region}
        err: Exception | None = None
        cap: Capture | None = None
        try:
            cap = await _do()
        except (CaptureFailed, NotFound, ValidationFailed) as e:
            err = e
        except Exception as e:  # noqa: BLE001
            err = CaptureFailed(f"browser capture failed: {type(e).__name__}: {str(e)[:300]}")

        def _record(ctx: OpContext) -> dict[str, Any]:
            if err is not None:
                raise err
            assert cap is not None
            ctx.verification = "verified"  # sha256 re-checked on disk in _store
            ctx.result_revision = revision
            return {"captureId": cap.capture_id, "sha256": cap.metadata["sha256"], "revision": revision}
        self.studio.operation(actor, "visual.capture_preview", "visual.capture", params, _record,
                              project_id=project_id, base_revision=revision)
        assert cap is not None
        self.studio.events.publish({"type": "screenshot", "captureId": cap.capture_id, "projectId": project_id,
                                    "revision": revision, "source": source})
        return cap

    def _finish(self, project_id: str | None, revision: int | None, source: str, png: bytes,
                vp: dict[str, int], dom: dict[str, Any] | None, console: list[dict[str, Any]] | None,
                network: list[dict[str, Any]] | None, extra_meta: dict[str, Any]) -> Capture:
        try:
            img = Image.open(BytesIO(png))
            img.verify()
            img = Image.open(BytesIO(png))
        except Exception as e:
            raise CaptureFailed("captured bytes are not a valid image") from e
        if img.format != "PNG":
            raise CaptureFailed(f"expected PNG, got {img.format}")
        capture_id = new_id("capture")
        meta = {
            "captureId": capture_id, "projectId": project_id, "revision": revision, "timestamp": iso(utcnow()),
            "source": source, "viewport": {"width": vp["width"], "height": vp["height"]},
            "image": {"width": img.width, "height": img.height}, "imageFormat": "png",
            "sha256": sha256_bytes(png), "bytes": len(png),
            **{k: v for k, v in extra_meta.items() if k != "aria"},
        }
        ws = self._workspace_for_capture(project_id)
        self._store(ws, capture_id, png, meta, {"dom": dom, "console": {"console": console or [], "network": network or []}
                                               if dom is not None else None})
        self._row(meta, f"artifacts/captures/{capture_id}/screenshot.png")
        return Capture(capture_id, png, meta, dom, console, network)

    # ---------------------------------------------------------------- desktop / window capture (Windows)
    async def capture_desktop(self, actor: Actor, *, window_handle: int | None = None,
                              region: dict[str, int] | None = None, project_id: str | None = None,
                              source: str | None = None) -> Capture:
        if sys.platform != "win32":
            raise CaptureFailed("desktop capture is implemented for Windows only")
        try:  # AAS: computer_use is not vendored; refuse before the operation so no `ok` row is audited
            from ..computer_use.windows import capture_bitmap
        except ImportError as e:
            raise Unavailable("desktop capture is not available in AI App Studio (computer_use not vendored)") from e
        self.studio.operation(actor, "visual.capture_screen", "visual.capture_desktop",
                              {"windowHandle": window_handle, "region": region}, lambda c: {"policy": "ok"},
                              project_id=project_id)
        png, info = await asyncio.to_thread(capture_bitmap, window_handle, region)
        vp = {"width": info["width"], "height": info["height"]}
        rev = self.studio.get_revision(project_id) if project_id else None
        src = source or ("window" if window_handle else ("region" if region else "desktop"))
        return self._finish(project_id, rev, src, png, vp, None, None, None, {"capture": info})

    # ---------------------------------------------------------------- reading back
    def load(self, capture_id: str) -> Capture:
        with self.studio.session() as s:
            row = s.get(Screenshot, capture_id)
            if row is None:
                raise NotFound("capture not found", captureId=capture_id)
            project_id, rel, meta = row.project_id, row.rel_path, dict(row.meta)
        ws = self._workspace_for_capture(project_id)
        png = ws.read_bytes(rel)
        if sha256_bytes(png) != meta["sha256"]:
            raise CaptureFailed("stored screenshot was modified after capture", captureId=capture_id)
        base = Path(rel).parent.as_posix()
        dom = cons = None
        try:
            dom = json.loads(ws.read_bytes(f"{base}/dom.json"))
            cons = json.loads(ws.read_bytes(f"{base}/console.json"))
        except (FileNotFoundError, OSError):
            pass
        return Capture(capture_id, png, meta, dom, (cons or {}).get("console"), (cons or {}).get("network"))

    def analyze(self, actor: Actor, capture_id: str, task: str = "Evaluate visual layout and identify defects",
                ocr: bool = False) -> dict[str, Any]:
        cap = self.load(capture_id)
        result = analyze(cap.dom, cap.png, console=cap.console, network=cap.network, task=task[:2000])
        if ocr:
            has_canvas = any(e.get("scene") is not None for e in (cap.dom or {}).get("elements", []))
            if cap.metadata.get("source") in ("desktop", "window", "region") or has_canvas:
                try:
                    o = ocr_png(cap.png)
                    result["ocr"] = {"text": o["text"], "lines": [{"text": ln["text"], "bbox": ln["bbox"]} for ln in o["lines"]],
                                     "language": o["language"], "certainty": "observed-ocr"}
                except Unavailable as e:
                    result["ocr"] = {"unavailable": str(e)}
                    result["unknown"].append("text inside the screenshot (OCR unavailable)")
            else:
                result["ocr"] = {"skipped": "source has a DOM; OCR is only used for desktop/window/canvas captures"}
        out = {"captureId": capture_id, "projectId": cap.metadata.get("projectId"),
               "revision": cap.metadata.get("revision"), "analysis": result, "confidence": result["confidence"]}

        def _do(ctx: OpContext) -> dict[str, Any]:
            aid = new_id("ana")
            ctx.session.add(VisualAnalysis(id=aid, capture_id=capture_id, analyzer=ANALYZER, task=task[:2000], result=out))
            ctx.verification = "verified"
            out["analysisId"] = aid
            return {"analysisId": aid, "issues": len(result["issues"])}
        self.studio.operation(actor, "visual.analyze_screen", "visual.capture", {"captureId": capture_id, "task": task},
                              _do, project_id=cap.metadata.get("projectId"))
        if cap.metadata.get("projectId"):
            ws = self.studio.workspace(cap.metadata["projectId"])
            ws.write_text(f"artifacts/captures/{capture_id}/analysis.json", json.dumps(out, indent=2))
        return out

    def ocr(self, actor: Actor, capture_id: str, language: str | None = None,
            region: dict[str, int] | None = None) -> dict[str, Any]:
        """OCR a stored capture (optionally a region of it, in image pixels). Text is DATA."""
        cap = self.load(capture_id)
        png, ox, oy = cap.png, 0, 0
        if region is not None:
            img = Image.open(BytesIO(cap.png))
            try:
                x, y, w, h = (int(region[k]) for k in ("x", "y", "width", "height"))
            except (KeyError, TypeError, ValueError) as e:
                raise ValidationFailed("region must be {x,y,width,height} integers") from e
            if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > img.width or y + h > img.height:
                raise ValidationFailed("region must lie inside the image", imageWidth=img.width, imageHeight=img.height)
            buf = BytesIO()
            img.crop((x, y, x + w, y + h)).save(buf, format="PNG")
            png, ox, oy = buf.getvalue(), x, y
        res = ocr_png(png, language)
        if region is not None:  # report boxes in full-capture pixel coordinates
            for ln in res["lines"]:
                ln["bbox"]["x"] += ox
                ln["bbox"]["y"] += oy
                for wd in ln["words"]:
                    wd["bbox"]["x"] += ox
                    wd["bbox"]["y"] += oy
            res["region"] = {"x": ox, "y": oy, "width": res["imageSize"]["width"], "height": res["imageSize"]["height"]}
        out = {"captureId": capture_id, "revision": cap.metadata.get("revision"), **res}

        def _do(ctx: OpContext) -> dict[str, Any]:
            aid = new_id("ana")
            ctx.session.add(VisualAnalysis(id=aid, capture_id=capture_id, analyzer=f"ocr:{ENGINE}:{res['language']}",
                                           task="ocr", result=out))
            ctx.verification = "verified"
            return {"analysisId": aid}
        r = self.studio.operation(actor, "visual.ocr", "visual.capture", {"captureId": capture_id, "language": language,
                                                                          "region": region}, _do,
                                  project_id=cap.metadata.get("projectId"))
        return {"analysisId": r["analysisId"], **out}

    def compare(self, actor: Actor, capture_a: str, capture_b: str) -> tuple[dict[str, Any], bytes]:
        a, b = self.load(capture_a), self.load(capture_b)
        px, diff_png = pixel_diff(a.png, b.png)
        ana_a = analyze(a.dom, a.png, console=a.console, network=a.network) if a.dom is not None else None
        ana_b = analyze(b.dom, b.png, console=b.console, network=b.network) if b.dom is not None else None
        out = {"captureA": capture_a, "captureB": capture_b,
               "revisionA": a.metadata.get("revision"), "revisionB": b.metadata.get("revision"),
               "sha256A": a.metadata["sha256"], "sha256B": b.metadata["sha256"],
               "pixel": px, "semantic": semantic_diff(a.dom, b.dom, ana_a, ana_b), "identical": px["changedPixels"] == 0}
        pid = b.metadata.get("projectId")
        if pid:
            ws = self.studio.workspace(pid)
            ws.write_bytes(f"artifacts/comparisons/{capture_a}__{capture_b}/diff.png", diff_png)
            ws.write_text(f"artifacts/comparisons/{capture_a}__{capture_b}/comparison.json", json.dumps(out, indent=2))
        self.studio.operation(actor, "visual.compare_screenshots", "visual.capture",
                              {"captureA": capture_a, "captureB": capture_b},
                              lambda c: {"changedPixels": px["changedPixels"]}, project_id=pid)
        return out, diff_png

    async def run_interaction(self, actor: Actor, project_id: str, steps: list[dict[str, Any]], *,
                              path: str = "index.html", viewport: str = "desktop") -> tuple[dict[str, Any], bytes]:
        """Drive the PREVIEW page (never the desktop) with typed steps, then screenshot.

        step = {action: click|dblclick|hover|type|press|scroll|wait|check_visible|check_text, selector?, text?,
                key?, dy?, ms?}. Selectors are Playwright CSS/text selectors evaluated inside the preview page;
        there is no 'evaluate arbitrary JS' action. Navigation off the preview origin is blocked.
        """
        self.studio.policy.enforce(actor, "browser.preview")
        if not 1 <= len(steps) <= 30:
            raise ValidationFailed("1..30 steps")
        if not re.match(r"^[A-Za-z0-9_\-./]*(#[A-Za-z0-9_\-]*)?$", path) or ".." in path:
            raise ValidationFailed("path must be a page inside the site (e.g. index.html#contact)")
        vp = VIEWPORTS.get(viewport)
        if vp is None:
            raise ValidationFailed("viewport must be desktop|tablet|mobile")
        self.studio.get_revision(project_id)
        browser = await self.browsers.browser()
        ctx = await browser.new_context(viewport=vp, service_workers="block", accept_downloads=False)
        results: list[dict[str, Any]] = []
        console: list[dict[str, Any]] = []
        try:
            await ctx.route("**/*", lambda route: route.continue_()
                            if same_origin(route.request.url, self.preview.base_url) or route.request.url.startswith(("data:", "blob:"))
                            else route.abort("blockedbyclient"))
            page = await ctx.new_page()
            page.on("console", lambda m: console.append({"type": m.type, "text": m.text[:500]}))
            page.on("pageerror", lambda e: console.append({"type": "pageerror", "text": str(e)[:500]}))
            await page.goto(self.preview.url_for(project_id, path), wait_until="load", timeout=self.studio.settings.capture_timeout_ms)
            for i, st in enumerate(steps):
                act = st.get("action")
                sel = st.get("selector")
                if act not in ("click", "dblclick", "hover", "type", "press", "scroll", "wait", "check_visible", "check_text"):
                    raise ValidationFailed(f"step {i}: unknown action {act!r}")
                if sel is not None and (not isinstance(sel, str) or len(sel) > 300):
                    raise ValidationFailed(f"step {i}: selector must be a string up to 300 chars")
                try:
                    loc = page.locator(sel).first if sel else None
                    ok, detail = True, None
                    if act in ("click", "dblclick", "hover", "type", "check_visible", "check_text") and loc is None:
                        raise ValidationFailed(f"step {i}: selector required")
                    if act == "click":
                        await loc.click(timeout=5000)  # type: ignore[union-attr]
                    elif act == "dblclick":
                        await loc.dblclick(timeout=5000)  # type: ignore[union-attr]
                    elif act == "hover":
                        await loc.hover(timeout=5000)  # type: ignore[union-attr]
                    elif act == "type":
                        await loc.fill(str(st.get("text", ""))[:2000], timeout=5000)  # type: ignore[union-attr]
                    elif act == "press":
                        await page.keyboard.press(str(st.get("key", "Enter"))[:40])
                    elif act == "scroll":
                        await page.mouse.wheel(0, int(st.get("dy", 600)))
                    elif act == "wait":
                        await page.wait_for_timeout(min(int(st.get("ms", 300)), 5000))
                    elif act == "check_visible":
                        ok = await loc.is_visible()  # type: ignore[union-attr]
                    elif act == "check_text":
                        txt = await loc.inner_text(timeout=5000)  # type: ignore[union-attr]
                        ok, detail = str(st.get("text", "")) in txt, txt[:300]
                    await page.wait_for_timeout(80)
                    results.append({"step": i, "action": act, "selector": sel, "ok": ok, "detail": detail,
                                    "url": page.url.split(self.preview.base_url, 1)[-1]})
                except ValidationFailed:
                    raise
                except Exception as e:  # noqa: BLE001
                    results.append({"step": i, "action": act, "selector": sel, "ok": False,
                                    "detail": f"{type(e).__name__}: {str(e)[:200]}"})
                    break
            png = await page.screenshot()
            final_url = page.url
        finally:
            await ctx.close()
        out = {"projectId": project_id, "viewport": viewport, "steps": results, "passed": all(r["ok"] for r in results)
               and len(results) == len(steps), "finalUrl": final_url, "console": console[:20],
               "sha256": sha256_bytes(png)}
        self.studio.operation(actor, "browser.run_interaction", "browser.preview", {"projectId": project_id, "steps": len(steps)},
                              lambda c: {"passed": out["passed"]}, project_id=project_id)
        return out, png

    def bim_viewport_component(self, project_id: str) -> str:
        """The scene3d component that shows an IFC/GLB model (first one with a model object)."""
        with self.studio.session() as s:
            from ..db import Component
            comps = s.query(Component).filter_by(project_id=project_id, type="scene3d").order_by(Component.position).all()
            for c in comps:
                scene = (c.properties or {}).get("scene") or {}
                if any(o.get("type") == "model" for o in scene.get("objects", [])):
                    return c.id
        raise NotFound("no BIM viewport in this project (add a scene3d with a model object, e.g. via bim_export_visualization)")

    def inspect_elements(self, project_id: str, element_id: str | None = None) -> dict[str, Any]:
        page = self.last_page.get(project_id)
        if not page:
            raise NotFound("no rendered page inspected yet; capture the preview first", projectId=project_id)
        els = page["dom"]["elements"]
        keep = ("elementId", "componentType", "tag", "role", "text", "bounds", "parentId", "style")
        if element_id:
            els = [e for e in els if e["elementId"] == element_id]
            if not els:
                raise NotFound("element not found", elementId=element_id)
        return {"projectId": project_id, "viewport": page["viewport"], "document": page["dom"]["document"],
                "elements": [{k: e[k] for k in keep} for e in els]}

    async def close(self) -> None:
        await self.browsers.close()
        self.preview.stop()
