"""OCR of a real preview capture (Chromium + Windows.Media.Ocr)."""

from __future__ import annotations

import asyncio
import sys
from io import BytesIO
from pathlib import Path

import pytest
from PIL import Image, ImageDraw, ImageFont
from sqlalchemy import select
from studio_core.db import VisualAnalysis
from studio_core.errors import Unavailable, ValidationFailed
from studio_core.services import Studio
from studio_core.visual.ocr import ocr_png
from studio_core.visual.service import VisualService

from tests.conftest import AGENT, HUMAN

pytestmark = pytest.mark.browser

ARIAL = Path("C:/Windows/Fonts/arial.ttf")


def _squash(s: str) -> str:
    return "".join(s.lower().split())


def test_analyze_ocr_attaches_text_for_desktop_like_capture(studio: Studio) -> None:
    """A capture whose source is 'desktop' (no DOM) gets OCR text when ocr=True."""
    if sys.platform != "win32" or not ARIAL.exists():
        pytest.skip("Windows.Media.Ocr and arial.ttf are required")
    img = Image.new("RGB", (900, 200), "white")
    ImageDraw.Draw(img).text((30, 50), "Desktop Window Title", fill="black", font=ImageFont.truetype(str(ARIAL), 56))
    buf = BytesIO()
    img.save(buf, format="PNG")
    try:
        ocr_png(buf.getvalue())
    except Unavailable as e:
        pytest.skip(f"OCR unavailable: {e}")
    vs = VisualService(studio)
    cap = vs._finish(None, None, "desktop", buf.getvalue(), {"width": 900, "height": 200}, None, None, None, {})
    res = vs.analyze(AGENT, cap.capture_id, ocr=True)["analysis"]
    assert "windowtitle" in _squash(res["ocr"]["text"]) and res["ocr"]["certainty"] == "observed-ocr"
    assert "ocr" not in vs.analyze(AGENT, cap.capture_id)["analysis"]
