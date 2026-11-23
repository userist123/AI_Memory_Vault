"""W2 quality gates: deterministic checks of the built web preview on phone / tablet / desktop.

What runs (one Playwright pass through ``capture.capture(..., probe=...)``: same Chromium flags, same same-origin
request filter, same lock, timeout and kill as preview_capture; the screenshots are stored like any capture):

- axe-core (``images/qa/axe.min.js``, pinned by sha256 in ``images/qa/MANIFEST.json``; refused on mismatch) with the
  tags wcag2a, wcag2aa, wcag22aa;
- horizontal overflow (document wider than the viewport, with the widest offending elements);
- overlapping interactive boxes (neither contains the other; fixed/sticky vs. scrolling content is not a finding);
- broken images (loaded with naturalWidth 0);
- interactive targets smaller than 44 x 44 CSS px (inline text links excepted, WCAG 2.5.8 inline exception);
- console errors and uncaught page errors (from capture's listeners).

Injection boundary: axe and the probe are evaluated over CDP in an ISOLATED WORLD of the preview page created by the
studio's Playwright. They share the DOM but not the page's JavaScript globals or prototypes (the page cannot fake
``axe`` or patch ``getBoundingClientRect`` for us), nothing is added to a served file or to the generated app, and the
page's CSP stays exactly as served (no ``bypass_csp``).

Everything taken from the page (selectors, HTML snippets, console text) is sanitised + value-redacted + capped here;
the MCP tool still puts the issue lines only inside an UNTRUSTED DATA block. The latest result per project is kept at
``<data>/qa/<project>.json`` for the owner's QA badge.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import time
from pathlib import Path
from typing import Any

from studio_core import activity
from studio_core.errors import Unavailable, ValidationFailed

from . import capture, service

SCHEMA = "aas.qa/1"
QA_DIR = Path(__file__).resolve().parents[2] / "images" / "qa"
AXE_TAGS = ("wcag2a", "wcag2aa", "wcag22aa")
SEVERITIES = ("critical", "serious", "moderate", "minor")
GATING = ("critical", "serious", "moderate")  # the gates pass when none of these is present
MIN_TARGET = 44
MAX_ISSUES = 40  # issues kept in a result (most severe first)
MAX_TARGETS = 3  # example elements kept per issue
MAX_TEXT = 160
MAX_RESULT_JSON = 64 * 1024
_PID = re.compile(r"^prj_[0-9a-f]{16}$")
_AXE: dict[str, str] = {}

VISION_RUBRIC = (
    "Visual hierarchy: one clear primary action per screen; headings, body and captions differ by size/weight.",
    "Spacing: consistent 4/8 px rhythm; nothing touches the screen edges or the safe areas.",
    "Typography: at most two font families, readable body size (>= 16 px on phone), no clipped or wrapped labels.",
    "Colour: semantic, consistent palette; sufficient contrast; dark/light surfaces look intentional.",
    "Touch: targets look tappable and comfortably sized; states (empty/loading/error) look designed.",
    "Polish: aligned edges, no placeholder text, no lorem ipsum, no broken or stretched images.",
)


# ------------------------------------------------------------------ axe-core (pinned)
def manifest() -> dict[str, Any]:
    data = json.loads((QA_DIR / "MANIFEST.json").read_text(encoding="utf-8"))
    entry = data.get("axe-core") if isinstance(data, dict) else None
    if not isinstance(entry, dict) or not re.fullmatch(r"[0-9a-f]{64}", str(entry.get("sha256", ""))):
        raise Unavailable("images/qa/MANIFEST.json has no valid axe-core entry")
    return entry


def load_axe(qa_dir: Path | None = None) -> str:
    """The pinned axe.min.js source; ``Unavailable`` when the file is missing or its sha256 differs (fail closed)."""
    base = qa_dir or QA_DIR
    key = str(base)
    if key in _AXE:
        return _AXE[key]
    entry = json.loads((base / "MANIFEST.json").read_text(encoding="utf-8")).get("axe-core") or {}
    name = str(entry.get("file", ""))
    if name != "axe.min.js":
        raise Unavailable("the axe-core manifest names an unexpected file")
    try:
        raw = (base / name).read_bytes()
    except OSError as e:
        raise Unavailable("the pinned axe-core file is missing (images/qa/axe.min.js)") from e
    if hashlib.sha256(raw).hexdigest() != entry.get("sha256"):
        raise Unavailable("the axe-core file does not match its pinned sha256 (images/qa/MANIFEST.json)")
    _AXE[key] = raw.decode("utf-8")
    return _AXE[key]


AXE_RUN_JS = """(async () => {
  const r = await axe.run(document, {runOnly: {type: 'tag', values: %s}, resultTypes: ['violations'],
                                     iframes: false, elementRef: false, ancestry: false, xpath: false});
  return JSON.stringify({version: axe.version, violations: r.violations.map(v => ({
    id: String(v.id), impact: String(v.impact || 'minor'), help: String(v.help || '').slice(0, 200),
    count: v.nodes.length,
    targets: v.nodes.slice(0, %d).map(n => ({target: [].concat(n.target || []).join(' ').slice(0, 200),
                                            html: String(n.html || '').slice(0, 200)}))}))});
})()"""

# Layout probe (isolated world). Returns a JSON string; every list is capped in the page already.
PROBE_JS = r"""(() => {
  const MIN = %d, CAP = 20;
  const de = document.documentElement, vw = de.clientWidth, vh = de.clientHeight;
  const desc = (el) => {
    let s = el.tagName.toLowerCase();
    if (el.id) s += '#' + el.id;
    const cls = (typeof el.className === 'string' ? el.className : '').trim().split(/\s+/).filter(Boolean).slice(0, 2);
    if (cls.length) s += '.' + cls.join('.');
    const t = (el.getAttribute('aria-label') || el.innerText || el.getAttribute('alt') || el.getAttribute('placeholder') || '').toString();
    return {target: s.slice(0, 120), text: t.replace(/\s+/g, ' ').trim().slice(0, 60)};
  };
  const visible = (el) => {
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden' || cs.visibility === 'collapse' || Number(cs.opacity) === 0) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };
  const pinned = (el) => {
    for (let e = el; e && e !== de; e = e.parentElement) {
      const p = getComputedStyle(e).position;
      if (p === 'fixed' || p === 'sticky') return true;
    }
    return false;
  };
  const out = {viewport: [vw, vh], overflow: null, overlaps: [], smallTargets: [], brokenImages: []};
  const sw = Math.max(de.scrollWidth, document.body ? document.body.scrollWidth : 0);
  if (sw > vw + 1) {
    const wide = [];
    for (const el of document.body ? document.body.querySelectorAll('*') : []) {
      if (wide.length >= 5) break;
      const r = el.getBoundingClientRect();
      if ((r.right > vw + 1 || r.left < -1) && r.width > 0 && visible(el)) {
        const p = el.parentElement;
        const pr = p ? p.getBoundingClientRect() : null;
        if (!pr || pr.right <= vw + 1 || p === document.body) wide.push({...desc(el), right: Math.round(r.right)});
      }
    }
    out.overflow = {scrollWidth: sw, viewportWidth: vw, elements: wide};
  }
  const SEL = 'a[href],button,input:not([type=hidden]),select,textarea,summary,[role=button],[role=link],' +
              '[role=checkbox],[role=radio],[role=switch],[role=tab],[role=menuitem],[role=option],[tabindex]:not([tabindex="-1"])';
  const items = [];
  for (const el of document.querySelectorAll(SEL)) {
    if (items.length >= 300) break;
    if (el.disabled || !visible(el)) continue;
    let box = el;
    const label = el.closest('label');
    if (label && (el.type === 'checkbox' || el.type === 'radio')) box = label;
    items.push({el, box, r: box.getBoundingClientRect(), fixed: pinned(el)});
  }
  for (const it of items) {
    if (out.smallTargets.length >= CAP) break;
    const cs = getComputedStyle(it.el);
    if (it.el.tagName === 'A' && cs.display === 'inline' && it.el.parentElement &&
        (it.el.parentElement.innerText || '').trim().length > (it.el.innerText || '').trim().length + 1) continue;
    if (it.r.width < MIN || it.r.height < MIN)
      out.smallTargets.push({...desc(it.el), width: Math.round(it.r.width), height: Math.round(it.r.height)});
  }
  for (let i = 0; i < items.length && out.overlaps.length < CAP; i++) {
    for (let j = i + 1; j < items.length && out.overlaps.length < CAP; j++) {
      const a = items[i], b = items[j];
      if (a.fixed !== b.fixed) continue;
      if (a.box.contains(b.box) || b.box.contains(a.box)) continue;
      const w = Math.min(a.r.right, b.r.right) - Math.max(a.r.left, b.r.left);
      const h = Math.min(a.r.bottom, b.r.bottom) - Math.max(a.r.top, b.r.top);
      if (w > 2 && h > 2) out.overlaps.push({a: desc(a.el), b: desc(b.el), area: Math.round(w * h)});
    }
  }
  for (const img of document.images) {
    if (out.brokenImages.length >= CAP) break;
    if (img.complete && img.naturalWidth === 0 && (img.currentSrc || img.getAttribute('src'))) {
      const src = (img.currentSrc || img.getAttribute('src') || '');
      out.brokenImages.push({...desc(img), src: src.startsWith('data:') ? 'data:…' : src.slice(0, 160)});
    }
  }
  return JSON.stringify(out);
})()"""


def _clean(value: Any, cap: int = MAX_TEXT) -> str:
    text, _ = activity.clean_text(str(value if value is not None else ""), cap)
    return " ".join(text.split())


def _eval_isolated(ctx: Any, page: Any, expressions: list[str]) -> list[Any]:
    """Evaluate ``expressions`` in a fresh isolated world of the page's main frame (CDP); returns their values."""
    cdp = ctx.new_cdp_session(page)
    try:
        frame_id = cdp.send("Page.getFrameTree")["frameTree"]["frame"]["id"]
        world = cdp.send("Page.createIsolatedWorld", {"frameId": frame_id, "worldName": "aas-qa",
                                                      "grantUniveralAccess": False})
        out: list[Any] = []
        for expr in expressions:
            res = cdp.send("Runtime.evaluate", {"expression": expr, "contextId": world["executionContextId"],
                                                "returnByValue": True, "awaitPromise": True})
            if res.get("exceptionDetails"):
                desc = res["exceptionDetails"].get("exception", {}).get("description") or res["exceptionDetails"].get("text")
                raise RuntimeError(f"qa probe failed: {str(desc)[:200]}")
            out.append(res.get("result", {}).get("value"))
        return out
    finally:
        try:
            cdp.detach()
        except Exception:  # noqa: BLE001 - the context closes right after
            pass


def make_probe(axe_src: str) -> capture.Probe:
    """The capture hook: per frame {axe: {...}, layout: {...}} (raw page data; sanitised by ``evaluate``)."""
    axe_run = AXE_RUN_JS % (json.dumps(list(AXE_TAGS)), MAX_TARGETS)
    layout = PROBE_JS % MIN_TARGET

    def probe(ctx: Any, page: Any, frame: str) -> dict[str, Any]:
        try:
            _, axe_json, layout_json = _eval_isolated(ctx, page, [axe_src + "\n;void 0", axe_run, layout])
            return {"axe": json.loads(axe_json or "{}"), "layout": json.loads(layout_json or "{}")}
        except Exception as e:  # noqa: BLE001 - reported as a gate error for this frame, never as "passed"
            return {"error": f"{type(e).__name__}: {str(e)[:200]}"}
    return probe


# ------------------------------------------------------------------ result building (pure, tested on fixtures)
def _issue(check: str, rule: str, severity: str, frame: str, help_: str, targets: list[str], count: int = 1) -> dict[str, Any]:
    return {"check": check, "rule": rule, "severity": severity if severity in SEVERITIES else "minor",
            "frames": [frame], "count": max(1, int(count)), "help": help_, "targets": targets}


def _frame_issues(frame: str, data: Any, console: list[str]) -> list[dict[str, Any]]:
    out: list[dict[str, Any]] = []
    data = data if isinstance(data, dict) else {"error": "no probe result"}
    if data.get("error"):
        out.append(_issue("gate", "probe-error", "critical", frame, _clean(data["error"], 240), []))
    axe = data.get("axe") if isinstance(data.get("axe"), dict) else {}
    for v in (axe.get("violations") or [])[:100]:
        if not isinstance(v, dict):
            continue
        tg = [_clean(t.get("target"), MAX_TEXT) for t in (v.get("targets") or [])[:MAX_TARGETS] if isinstance(t, dict)]
        out.append(_issue("axe", _clean(v.get("id"), 60), str(v.get("impact") or "minor"), frame,
                          _clean(v.get("help"), 200), tg, int(v.get("count") or 1)))
    lay = data.get("layout") if isinstance(data.get("layout"), dict) else {}

    def t(e: Any) -> str:
        e = e if isinstance(e, dict) else {}
        txt = _clean(e.get("text"), 60)
        return _clean(e.get("target"), 120) + (f' "{txt}"' if txt else "")
    ov = lay.get("overflow")
    if isinstance(ov, dict):
        out.append(_issue("layout", "horizontal-overflow", "serious", frame,
                          f"page is {int(ov.get('scrollWidth') or 0)} px wide in a {int(ov.get('viewportWidth') or 0)} px "
                          "viewport (horizontal scroll)", [t(e) for e in (ov.get("elements") or [])[:MAX_TARGETS]]))
    ovl = [o for o in (lay.get("overlaps") or []) if isinstance(o, dict)]
    if ovl:
        out.append(_issue("layout", "overlapping-targets", "serious", frame, "interactive elements overlap each other",
                          [f"{t(o.get('a'))} x {t(o.get('b'))}" for o in ovl[:MAX_TARGETS]], len(ovl)))
    small = [o for o in (lay.get("smallTargets") or []) if isinstance(o, dict)]
    if small:
        out.append(_issue("layout", "target-size-44", "moderate", frame,
                          f"interactive targets smaller than {MIN_TARGET}x{MIN_TARGET} px",
                          [f"{t(o)} {int(o.get('width') or 0)}x{int(o.get('height') or 0)}" for o in small[:MAX_TARGETS]],
                          len(small)))
    broken = [o for o in (lay.get("brokenImages") or []) if isinstance(o, dict)]
    if broken:
        out.append(_issue("layout", "broken-image", "serious", frame, "images that failed to load",
                          [f"{t(o)} src={_clean(o.get('src'), 120)}" for o in broken[:MAX_TARGETS]], len(broken)))
    tag = f"[{frame}] "
    errs = [c[len(tag):] for c in console if isinstance(c, str) and c.startswith(tag)]
    uncaught = [c for c in errs if c.startswith("uncaught")]
    logged = [c for c in errs if not c.startswith("uncaught")]
    if uncaught:
        out.append(_issue("console", "uncaught-error", "critical", frame, "uncaught exceptions in the page",
                          [_clean(c, MAX_TEXT) for c in uncaught[:MAX_TARGETS]], len(uncaught)))
    if logged:
        out.append(_issue("console", "console-error", "serious", frame, "console errors",
                          [_clean(c, MAX_TEXT) for c in logged[:MAX_TARGETS]], len(logged)))
    return out


def evaluate(probe: dict[str, Any], console_raw: list[str] | None, frames: list[str]) -> dict[str, Any]:
    """Per-frame raw probe data -> {passed, counts, issues (merged across frames, most severe first, capped)}."""
    merged: dict[tuple[str, str, str], dict[str, Any]] = {}
    for frame in frames:
        for iss in _frame_issues(frame, (probe or {}).get(frame), list(console_raw or [])):
            key = (iss["check"], iss["rule"], "|".join(iss["targets"][:1]))
            if key in merged:
                m = merged[key]
                m["frames"] = sorted(set(m["frames"]) | set(iss["frames"]), key=frames.index)
                m["count"] = max(m["count"], iss["count"])
                if SEVERITIES.index(iss["severity"]) < SEVERITIES.index(m["severity"]):
                    m["severity"] = iss["severity"]
            else:
                merged[key] = iss
    issues = sorted(merged.values(), key=lambda i: (SEVERITIES.index(i["severity"]), -len(i["frames"]), i["rule"]))
    counts = {s: sum(1 for i in issues if i["severity"] == s) for s in SEVERITIES}
    counts["total"] = len(issues)
    versions = {str(((probe or {}).get(f) or {}).get("axe", {}).get("version", "")) for f in frames} - {""}
    return {"passed": not any(counts[s] for s in GATING), "counts": counts, "issues": issues[:MAX_ISSUES],
            "truncated": max(0, len(issues) - MAX_ISSUES), "axeVersion": _clean(",".join(sorted(versions)), 40)}


def issue_lines(result: dict[str, Any]) -> list[str]:
    """One compact line per issue (page-derived parts sanitised already) for an UNTRUSTED DATA block."""
    lines = []
    for i in result.get("issues") or []:
        tg = "; ".join(i.get("targets") or [])
        lines.append(f"{i['severity']} {i['check']}:{i['rule']} on {','.join(i['frames'])} (x{i['count']}): "
                     f"{i['help']}" + (f" -- e.g. {tg}" if tg else ""))
    if result.get("truncated"):
        lines.append(f"[{result['truncated']} more issues not listed]")
    return lines


# ------------------------------------------------------------------ run + store
def qa_dir(settings: Any) -> Path:
    return Path(settings.data_dir) / "qa"


def current_site(settings: Any, project_id: str) -> str:
    """Name of the web build the preview serves now (``site-<ms>``), '' when none."""
    try:
        name = (service.preview_base(settings, project_id) / "current").read_text(encoding="utf-8").strip()
    except OSError:
        return ""
    return name if re.fullmatch(r"site-\d{13}", name) else ""


def latest(settings: Any, project_id: str) -> dict[str, Any] | None:
    if not isinstance(project_id, str) or not _PID.match(project_id):
        return None
    p = capture._contained(qa_dir(settings), f"{project_id}.json")  # noqa: SLF001
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return data if isinstance(data, dict) and data.get("schema") == SCHEMA else None


def _store(settings: Any, project_id: str, result: dict[str, Any]) -> None:
    d = qa_dir(settings)
    d.mkdir(parents=True, exist_ok=True)
    blob = json.dumps(result, ensure_ascii=False)
    if len(blob) > MAX_RESULT_JSON:  # defence in depth: the caps above keep it far below
        result = {**result, "issues": result["issues"][:10], "truncated": result.get("truncated", 0) + len(result["issues"]) - 10}
        blob = json.dumps(result, ensure_ascii=False)
    tmp = d / f".{project_id}.{os.getpid()}.partial"
    tmp.write_text(blob, encoding="utf-8")
    os.replace(tmp, capture._contained(d, f"{project_id}.json"))  # noqa: SLF001


def run(studio: Any, project_id: str, *, run_id: str | None = None, frames: list[str] | None = None,
        capture_fn: Any = None, axe_src: str | None = None) -> dict[str, Any]:
    """Run the gates on the current web build; stores and returns the result
    {schema, projectId, revision, site, ranAt, passed, counts, issues, truncated, axeVersion, frames: {frame: sha256}}."""
    if not isinstance(project_id, str) or not _PID.match(project_id):
        raise ValidationFailed("invalid project id")
    frames = list(frames or capture.FRAMES)
    site = current_site(studio.settings, project_id)
    probe = make_probe(axe_src if axe_src is not None else load_axe())
    res = (capture_fn or capture.capture)(studio, project_id, frames, run_id=run_id, probe=probe)
    ev = evaluate(res.get("probe") or {}, res.get("consoleRaw") or [], frames)
    out = {"schema": SCHEMA, "projectId": project_id, "revision": res.get("revision"), "site": site,
           "ranAt": int(time.time() * 1000), **ev,
           "frames": {c["frame"]: c["sha256"] for c in res.get("captures", []) if isinstance(c, dict)}}
    _store(studio.settings, project_id, out)
    return out


def summary(result: dict[str, Any] | None) -> dict[str, Any] | None:
    """The studio-controlled part of a result (no page-derived text): what the agent's JSON body and badges show."""
    if not result:
        return None
    return {k: result.get(k) for k in ("revision", "site", "ranAt", "passed", "counts", "truncated", "axeVersion", "frames")}
