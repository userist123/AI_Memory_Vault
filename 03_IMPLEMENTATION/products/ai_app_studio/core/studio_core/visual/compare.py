"""Screenshot comparison: pixel diff + semantic (component bounds and issue) diff."""

from __future__ import annotations

from io import BytesIO
from typing import Any

from PIL import Image, ImageChops, ImageDraw


def pixel_diff(a_png: bytes, b_png: bytes, threshold: int = 16) -> tuple[dict[str, Any], bytes]:
    a = Image.open(BytesIO(a_png)).convert("RGB")
    b = Image.open(BytesIO(b_png)).convert("RGB")
    w, h = max(a.width, b.width), max(a.height, b.height)
    ca, cb = Image.new("RGB", (w, h), (255, 0, 255)), Image.new("RGB", (w, h), (255, 0, 255))
    ca.paste(a, (0, 0))
    cb.paste(b, (0, 0))
    diff = ImageChops.difference(ca, cb).convert("L").point(lambda v: 255 if v > threshold else 0)
    hist = diff.histogram()
    changed = hist[255]
    bbox = diff.getbbox()
    overlay = cb.copy()
    red = Image.new("RGB", (w, h), (255, 0, 0))
    overlay = Image.composite(Image.blend(overlay, red, 0.6), overlay, diff)
    if bbox:
        ImageDraw.Draw(overlay).rectangle(bbox, outline=(255, 0, 0), width=3)
    buf = BytesIO()
    overlay.save(buf, "PNG")
    return ({"sizeA": [a.width, a.height], "sizeB": [b.width, b.height], "changedPixels": changed,
             "changedRatio": round(changed / (w * h), 5), "changedBBox": list(bbox) if bbox else None,
             "threshold": threshold}, buf.getvalue())


def semantic_diff(dom_a: dict[str, Any] | None, dom_b: dict[str, Any] | None,
                  ana_a: dict[str, Any] | None, ana_b: dict[str, Any] | None) -> dict[str, Any]:
    out: dict[str, Any] = {}
    if dom_a and dom_b:
        ea = {e["elementId"]: e for e in dom_a["elements"]}
        eb = {e["elementId"]: e for e in dom_b["elements"]}
        out["added"] = sorted(set(eb) - set(ea))
        out["removed"] = sorted(set(ea) - set(eb))
        out["moved_or_resized"] = {k: {"before": ea[k]["bounds"], "after": eb[k]["bounds"]}
                                   for k in sorted(set(ea) & set(eb)) if ea[k]["bounds"] != eb[k]["bounds"]}
        out["styleChanged"] = sorted(k for k in set(ea) & set(eb) if ea[k]["style"] != eb[k]["style"])
        out["documentWidth"] = {"before": dom_a["document"]["scrollWidth"], "after": dom_b["document"]["scrollWidth"]}
    if ana_a is not None and ana_b is not None:
        ia = {i["id"] for i in ana_a["issues"]}
        ib = {i["id"] for i in ana_b["issues"]}
        out["issuesResolved"] = sorted(ia - ib)
        out["issuesIntroduced"] = sorted(ib - ia)
        out["issuesRemaining"] = sorted(ia & ib)
    return out
