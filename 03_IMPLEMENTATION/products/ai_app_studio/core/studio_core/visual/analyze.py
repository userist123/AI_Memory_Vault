"""Deterministic visual analyzer (spec 7.3).

Every finding is labelled:
  observed - measured directly by the browser or from screenshot pixels
  inferred - a likely problem derived from measurements (needs human/vision confirmation)
and properties the pipeline cannot see are listed under `unknown`.
It never claims a page "looks correct": absence of findings only means these checks passed.
"""

from __future__ import annotations

from io import BytesIO
from typing import Any

from PIL import Image, ImageStat

ANALYZER = "aws-dom-pixel/1"


def _lum(c: dict[str, float]) -> float:
    def ch(v: float) -> float:
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * ch(c["r"]) + 0.7152 * ch(c["g"]) + 0.0722 * ch(c["b"])


def contrast_ratio(fg: dict[str, float], bg: dict[str, float]) -> float:
    a, b = sorted((_lum(fg), _lum(bg)), reverse=True)
    return round((a + 0.05) / (b + 0.05), 2)


def _intersect(a: dict[str, int], b: dict[str, int]) -> int:
    w = min(a["x"] + a["width"], b["x"] + b["width"]) - max(a["x"], b["x"])
    h = min(a["y"] + a["height"], b["y"] + b["height"]) - max(a["y"], b["y"])
    return max(0, w) * max(0, h)


def _ancestors(eid: str, by_id: dict[str, dict[str, Any]]) -> set[str]:
    out: set[str] = set()
    cur = by_id.get(eid, {}).get("parentId")
    while cur and cur not in out:
        out.add(cur)
        cur = by_id.get(cur, {}).get("parentId")
    return out


def analyze(dom: dict[str, Any] | None, png: bytes | None, *, console: list[dict[str, Any]] | None = None,
            network: list[dict[str, Any]] | None = None, task: str = "") -> dict[str, Any]:
    issues: list[dict[str, Any]] = []
    warnings: list[dict[str, Any]] = []
    unknown = ["hover/focus states", "animation timing", "rendering on other GPUs/browsers",
               "aesthetic quality and brand fit (requires a vision model or human)"]

    def add(kind: str, severity: str, certainty: str, message: str, *, el: dict[str, Any] | None = None,
            evidence: dict[str, Any] | None = None, fix: str | None = None) -> None:
        target = issues if certainty == "observed" else warnings
        target.append({"id": f"{kind}:{el['elementId'] if el else 'page'}", "kind": kind, "severity": severity,
                       "certainty": certainty, "componentId": el["elementId"] if el else None,
                       "bounds": el["bounds"] if el else None, "message": message,
                       "evidence": evidence or {}, "suggestedFix": fix})

    if dom:
        vw = dom["viewport"]["width"]
        els = dom["elements"]
        by_id = {e["elementId"]: e for e in els}
        sw = dom["document"]["scrollWidth"]

        def right_edge(e: dict[str, Any]) -> int:
            # content that spills out of its box (overflow: visible) counts too
            return e["bounds"]["x"] + max(e["bounds"]["width"], e.get("scrollWidth") or 0)

        for e in els:
            if (e["hasOwnText"] and e["style"]["overflow"] == "visible"
                    and (e.get("scrollWidth") or 0) > (e.get("clientWidth") or 0) + 1 and e["clientWidth"]):
                add("text_overflows_box", "high" if right_edge(e) > vw + 1 else "medium", "observed",
                    f"{e['elementId']} text is {e['scrollWidth']}px wide but its box is {e['clientWidth']}px"
                    + (" and it runs past the viewport edge (text is cut off)" if right_edge(e) > vw + 1 else ""),
                    el=e, evidence={"scrollWidth": e["scrollWidth"], "clientWidth": e["clientWidth"],
                                    "fontSize": e["style"]["fontSize"]},
                    fix="allow wrapping (remove whiteSpace: nowrap) and/or reduce fontSize")
        if sw > vw + 1:
            culprits = [e for e in els if right_edge(e) > vw + 1]
            leaf = [e for e in culprits if not any(o["parentId"] == e["elementId"] for o in culprits)] or culprits
            add("horizontal_overflow", "high", "observed",
                f"page is {sw}px wide in a {vw}px viewport (horizontal scroll)",
                evidence={"scrollWidth": sw, "viewportWidth": vw, "culprits": [e["elementId"] for e in leaf]})
            for e in leaf:
                add("element_exceeds_viewport", "high", "observed",
                    f"{e['elementId']} extends to x={right_edge(e)}px, beyond the {vw}px viewport",
                    el=e, evidence={"right": right_edge(e), "viewportWidth": vw},
                    fix="remove fixed width / use maxWidth: 100%")
        for e in els:
            b, st = e["bounds"], e["style"]
            visible = st["display"] != "none" and st["visibility"] != "hidden" and st["opacity"] > 0.05
            if visible and b["width"] > 0 and b["x"] + b["width"] <= 0:
                add("offscreen", "medium", "observed", f"{e['elementId']} is entirely left of the viewport", el=e)
            if e["hasOwnText"] and e["text"] and visible and b["width"] > 0:
                if e["bg"].get("image"):
                    warnings.append({"id": f"contrast_unknown:{e['elementId']}", "kind": "contrast_unknown",
                                     "certainty": "unknown", "componentId": e["elementId"],
                                     "message": "text over background image: contrast not measurable from DOM"})
                elif e["fg"]:
                    ratio = contrast_ratio(e["fg"], e["bg"])
                    try:
                        weight = int(float(st["fontWeight"]))
                    except (TypeError, ValueError):
                        weight = 400
                    large = st["fontSize"] >= 24 or (st["fontSize"] >= 18.66 and weight >= 700)
                    need = 3.0 if large else 4.5
                    if ratio < need:
                        sev = "high" if ratio < 1.5 else "medium"
                        add("low_contrast", sev, "observed",
                            f"{e['elementId']} text contrast {ratio}:1 is below WCAG AA {need}:1"
                            + (" (text is practically invisible)" if ratio < 1.5 else ""),
                            el=e, evidence={"ratio": ratio, "required": need, "color": st["color"],
                                            "background": e["bg"], "backgroundAssumedWhite": bool(e["bg"].get("assumed"))},
                            fix="change color or backgroundColor")
                if st["fontSize"] < 12:
                    add("tiny_text", "low", "inferred", f"{e['elementId']} font-size {st['fontSize']}px may be unreadable", el=e)
                if st["overflow"] in ("hidden", "clip") and e["scrollWidth"] > e["clientWidth"] + 1:
                    add("text_clipped", "medium", "observed", f"{e['elementId']} content is clipped horizontally", el=e,
                        evidence={"scrollWidth": e["scrollWidth"], "clientWidth": e["clientWidth"]})
            if e["img"] is not None and (not e["img"]["complete"] or e["img"]["naturalWidth"] == 0):
                add("broken_image", "high", "observed", f"image {e['img']['src']!r} did not load", el=e)
            if e["scene"] is not None and e["scene"]["status"] != "rendering":
                add("scene3d_not_rendering", "high", "observed",
                    f"3D scene in {e['elementId']} status={e['scene']['status']!r}", el=e)
            elif e["scene"] is not None:
                sc = e["scene"]
                if sc.get("fps") is not None and sc["fps"] < 30:
                    add("scene3d_low_fps", "low", "inferred",
                        f"3D scene {e['elementId']} renders at {sc['fps']} fps in the headless capture browser "
                        "(software GL; real GPUs usually differ)", el=e, evidence=sc)
                if (sc.get("triangles") or 0) > 2_000_000 or (sc.get("drawCalls") or 0) > 2000:
                    add("scene3d_heavy", "medium", "inferred", f"3D scene {e['elementId']} is heavy: "
                        f"{sc.get('triangles')} triangles, {sc.get('drawCalls')} draw calls", el=e, evidence=sc)
        texts = [e for e in els if e["hasOwnText"] and e["text"] and e["bounds"]["width"] * e["bounds"]["height"] > 0]
        for i, a in enumerate(texts):
            for b2 in texts[i + 1:]:
                if a["elementId"] in _ancestors(b2["elementId"], by_id) or b2["elementId"] in _ancestors(a["elementId"], by_id):
                    continue
                area = _intersect(a["bounds"], b2["bounds"])
                smaller = min(a["bounds"]["width"] * a["bounds"]["height"], b2["bounds"]["width"] * b2["bounds"]["height"])
                if smaller and area / smaller > 0.2:
                    add("overlap", "medium", "observed", f"{a['elementId']} overlaps {b2['elementId']}",
                        el=a, evidence={"other": b2["elementId"], "overlapRatio": round(area / smaller, 2)})
        if not els:
            add("no_components", "medium", "observed", "page contains no studio components")
    else:
        unknown.append("layout semantics (no DOM available for this capture source)")

    for c in console or []:
        if c.get("type") in ("error", "pageerror"):
            add("console_error", "medium", "observed", f"console: {str(c.get('text'))[:300]}")
    for n in network or []:
        add("network_error", "medium", "observed", f"request failed: {n.get('url')} ({n.get('failure') or n.get('status')})")

    pixel: dict[str, Any] = {}
    if png:
        img = Image.open(BytesIO(png)).convert("RGB")
        stat = ImageStat.Stat(img.resize((64, 64)))
        stddev = sum(stat.stddev) / 3
        pixel = {"width": img.width, "height": img.height, "meanRGB": [round(x, 1) for x in stat.mean],
                 "stddev": round(stddev, 2)}
        if stddev < 2.0:
            add("blank_render", "high", "observed", "screenshot is nearly a single flat colour", evidence=pixel)

    high = sum(1 for i in issues if i["severity"] == "high")
    layout = "no_dom" if not dom else ("broken" if high else ("degraded" if issues else "structured"))
    return {
        "analyzer": ANALYZER, "task": task, "layout": layout,
        "issues": issues, "warnings": warnings, "unknown": unknown, "pixel": pixel,
        "recommendations": sorted({i["suggestedFix"] for i in issues if i.get("suggestedFix")}),
        # confidence applies to the measured checks only, not to overall visual quality
        "confidence": 0.9 if dom else 0.3,
        "visionModel": "not_configured",
    }
