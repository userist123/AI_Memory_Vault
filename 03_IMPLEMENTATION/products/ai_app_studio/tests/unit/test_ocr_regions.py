"""L4 (S1 review backlog): OCR bad-region ValidationFailed checks on a desktop-like capture (no Chromium needed)."""

from __future__ import annotations

import os
import sys
from io import BytesIO
from pathlib import Path

import pytest
from PIL import Image, ImageDraw, ImageFont
from studio_core.errors import Unavailable, ValidationFailed
from studio_core.services import Studio
from studio_core.visual.ocr import ocr_png
from studio_core.visual.service import VisualService

from tests.conftest import AGENT

ARIAL = Path(os.environ.get("WINDIR", "")) / "Fonts" / "arial.ttf"


def _squash(s: str) -> str:
    return "".join(s.lower().split())


def test_ocr_regions_on_desktop_like_capture(studio: Studio) -> None:
    if sys.platform != "win32" or not ARIAL.exists():
        pytest.skip("Windows.Media.Ocr and arial.ttf are required")
    img = Image.new("RGB", (900, 400), "white")
    d = ImageDraw.Draw(img)
    d.text((30, 40), "Visual OCR Check", fill="black", font=ImageFont.truetype(str(ARIAL), 56))
    buf = BytesIO()
    img.save(buf, format="PNG")
    try:
        ocr_png(buf.getvalue())
    except Unavailable as e:
        pytest.skip(f"OCR unavailable: {e}")
    vs = VisualService(studio)
    cap = vs._finish(None, None, "desktop", buf.getvalue(), {"width": 900, "height": 400}, None, None, None, {})
    res = vs.ocr(AGENT, cap.capture_id)
    assert "visualocrcheck" in _squash(res["text"]) and res["certainty"] == "observed-ocr"
    hit = next(ln for ln in res["lines"] if "ocr" in ln["text"].lower())["bbox"]
    y0 = hit["y"] + hit["height"] + 40  # a strip below the title must not contain it
    sub = vs.ocr(AGENT, cap.capture_id, region={"x": 0, "y": y0, "width": 900, "height": 400 - y0})
    assert "visualocrcheck" not in _squash(sub["text"]) and sub["region"]["y"] == y0
    top = vs.ocr(AGENT, cap.capture_id, region={"x": 0, "y": max(0, hit["y"] - 10), "width": 900, "height": hit["height"] + 20})
    assert "ocr" in top["text"].lower()
    assert all(ln["bbox"]["y"] >= hit["y"] - 15 for ln in top["lines"])  # boxes are in full-capture pixels
    for bad in ({"x": 0, "y": 0, "width": 99999, "height": 10}, {"x": -1, "y": 0, "width": 10, "height": 10},
                {"x": 0, "y": 0, "width": 0, "height": 10}, {"x": 0}):
        with pytest.raises(ValidationFailed):
            vs.ocr(AGENT, cap.capture_id, region=bad)
