"""Real Windows.Media.Ocr on rendered images (no mocks of the engine)."""

from __future__ import annotations

import sys
from io import BytesIO
from pathlib import Path

import pytest
from PIL import Image, ImageDraw, ImageFont
from studio_core.errors import Unavailable
from studio_core.visual import ocr as ocr_mod
from studio_core.visual.ocr import ocr_png

ARIAL = Path("C:/Windows/Fonts/arial.ttf")

needs_win = pytest.mark.skipif(sys.platform != "win32" or not ARIAL.exists(),
                               reason="Windows.Media.Ocr and C:/Windows/Fonts/arial.ttf are required (win32 only)")


def _png(img: Image.Image) -> bytes:
    buf = BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def _require_recognizer() -> None:
    try:
        ocr_png(_png(Image.new("RGB", (40, 40), "white")))
    except Unavailable as e:
        pytest.skip(f"no Windows OCR recognizer / winrt packages available: {e}")


def _squash(s: str) -> str:
    return "".join(s.lower().split())


@needs_win
def test_ocr_finds_known_text_with_boxes() -> None:
    _require_recognizer()
    img = Image.new("RGB", (1200, 400), "white")
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(str(ARIAL), 64)
    d.text((50, 60), "Modern Architecture 2026", fill="black", font=font)
    d.text((50, 220), "Contact us", fill="black", font=font)
    r = ocr_png(_png(img))
    assert r["engine"] == "windows.media.ocr" and r["certainty"] == "observed-ocr"
    assert r["imageSize"] == {"width": 1200, "height": 400} and r["downscaled"] is False
    assert "modernarchitecture2026" in _squash(r["text"])
    assert "contactus" in _squash(r["text"])
    line = next(ln for ln in r["lines"] if "contact" in ln["text"].lower())
    bb = line["bbox"]  # OCR line boxes include ascender space, so compare with the drawing origin (50, 220)
    assert abs(bb["x"] - 50) <= 10 and abs(bb["y"] - 220) <= 10
    assert 250 <= bb["width"] <= 360 and 40 <= bb["height"] <= 80
    assert line["words"] and all({"x", "y", "width", "height"} <= set(w["bbox"]) for w in line["words"])


@needs_win
def test_ocr_downscales_huge_images_and_maps_boxes_back() -> None:
    _require_recognizer()
    img = Image.new("RGB", (12000, 300), "white")
    ImageDraw.Draw(img).text((100, 50), "Modern Architecture 2026", fill="black", font=ImageFont.truetype(str(ARIAL), 160))
    r = ocr_png(_png(img))
    assert r["downscaled"] is True and r["imageSize"] == {"width": 12000, "height": 300}
    assert "architecture" in r["text"].lower()
    bb = r["lines"][0]["bbox"]
    assert 60 <= bb["x"] <= 160 and 20 <= bb["y"] <= 100 and bb["x"] + bb["width"] > 1500  # image pixels, not scaled pixels


@needs_win
def test_unavailable_when_winrt_import_fails(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setitem(sys.modules, "winrt.windows.media.ocr", None)  # makes `import` raise ImportError
    with pytest.raises(Unavailable, match="not installed"):
        ocr_png(_png(Image.new("RGB", (10, 10), "white")))


def test_unavailable_on_other_platforms(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(ocr_mod.sys, "platform", "linux")
    with pytest.raises(Unavailable, match="Windows only"):
        ocr_png(b"")
