"""OCR for screenshots through the Windows.Media.Ocr engine (spec 4.5).

The WinRT projection packages are imported lazily so a missing package / non-Windows host never
breaks importing the backend; it only makes OCR raise Unavailable. Text found in an image is DATA.
"""

from __future__ import annotations

import asyncio
import sys
import threading
from io import BytesIO
from typing import Any

from PIL import Image

from ..errors import Unavailable, ValidationFailed

ENGINE = "windows.media.ocr"
MAX_CHARS = 20_000
MAX_WORDS = 2_000
MAX_INPUT_PIXELS = 100_000_000


def _load_winrt() -> dict[str, Any]:
    """Import the WinRT modules (raises Unavailable with a precise reason)."""
    if sys.platform != "win32":
        raise Unavailable("OCR needs Windows.Media.Ocr (Windows only)", platform=sys.platform)
    try:
        import winrt.windows.globalization as glob  # type: ignore[import-not-found,import-untyped,unused-ignore]
        import winrt.windows.graphics.imaging as imaging  # type: ignore[import-not-found,import-untyped,unused-ignore]
        import winrt.windows.media.ocr as ocr  # type: ignore[import-not-found,import-untyped,unused-ignore]
        import winrt.windows.storage.streams as streams  # type: ignore[import-not-found,import-untyped,unused-ignore]
    except ImportError as e:
        raise Unavailable("OCR packages are not installed (winrt-Windows.Media.Ocr and friends)", error=str(e)) from e
    return {"glob": glob, "imaging": imaging, "ocr": ocr, "streams": streams}


def _pick_engine(w: dict[str, Any], language: str | None) -> tuple[Any, str]:
    ocr, glob = w["ocr"], w["glob"]
    available = [str(lang.language_tag) for lang in ocr.OcrEngine.available_recognizer_languages]
    if not available:
        raise Unavailable("no Windows OCR recognizer language is installed")
    if language:
        match = next((a for a in available if a.lower() == language.lower()), None)
        if match is None:
            match = next((a for a in available if a.lower().split("-")[0] == language.lower().split("-")[0]), None)
        if match is not None:
            eng = ocr.OcrEngine.try_create_from_language(glob.Language(match))
            if eng is not None:
                return eng, match
    eng = ocr.OcrEngine.try_create_from_user_profile_languages()
    if eng is not None:
        return eng, str(eng.recognizer_language.language_tag)
    eng = ocr.OcrEngine.try_create_from_language(glob.Language(available[0]))
    if eng is None:
        raise Unavailable("Windows OCR engine could not be created", language=available[0])
    return eng, available[0]


async def _recognize(w: dict[str, Any], engine: Any, png: bytes) -> Any:
    streams, imaging = w["streams"], w["imaging"]
    stream = streams.InMemoryRandomAccessStream()
    writer = streams.DataWriter(stream)
    writer.write_bytes(png)
    await writer.store_async()
    await writer.flush_async()
    writer.detach_stream()
    stream.seek(0)
    decoder = await imaging.BitmapDecoder.create_async(stream)
    bitmap = await decoder.get_software_bitmap_async()
    return await engine.recognize_async(bitmap)


def _run_in_thread(coro_factory: Any) -> Any:
    """Run a coroutine on its own event loop in a worker thread (safe inside a running loop)."""
    box: dict[str, Any] = {}

    def target() -> None:
        try:
            box["value"] = asyncio.run(coro_factory())
        except BaseException as e:  # noqa: BLE001
            box["error"] = e
    t = threading.Thread(target=target, name="ocr", daemon=True)
    t.start()
    t.join(120)
    if t.is_alive():
        raise Unavailable("OCR timed out after 120 s")
    if "error" in box:
        raise box["error"]
    return box["value"]


def _bbox(rect: Any, scale: float) -> dict[str, int]:
    x0, y0 = rect.x * scale, rect.y * scale
    x1, y1 = (rect.x + rect.width) * scale, (rect.y + rect.height) * scale
    return {"x": round(x0), "y": round(y0), "width": max(1, round(x1 - x0)), "height": max(1, round(y1 - y0))}


def ocr_png(png: bytes, language: str | None = None) -> dict[str, Any]:
    """OCR a PNG. Boxes are in the pixel coordinates of the given image."""
    w = _load_winrt()
    try:
        img: Image.Image = Image.open(BytesIO(png))
        img.load()
    except Exception as e:  # noqa: BLE001
        raise ValidationFailed("image is not a readable PNG", error=str(e)[:200]) from e
    if img.width * img.height > MAX_INPUT_PIXELS:
        raise ValidationFailed("image is too large for OCR", width=img.width, height=img.height)
    full_w, full_h = img.size
    try:
        engine, lang = _pick_engine(w, language)
        max_dim = int(w["ocr"].OcrEngine.max_image_dimension)
        scale = 1.0
        data = png
        if max(full_w, full_h) > max_dim:
            scale = max(full_w, full_h) / max_dim
            small = img.convert("RGB").resize((max(1, int(full_w / scale)), max(1, int(full_h / scale))), Image.Resampling.LANCZOS)
            buf = BytesIO()
            small.save(buf, format="PNG")
            data = buf.getvalue()
        result = _run_in_thread(lambda: _recognize(w, engine, data))
    except Unavailable:
        raise
    except Exception as e:  # noqa: BLE001
        raise Unavailable(f"Windows OCR failed: {type(e).__name__}: {str(e)[:200]}") from e

    lines: list[dict[str, Any]] = []
    words_total = 0
    chars = 0
    truncated = False
    for ln in result.lines:
        words: list[dict[str, Any]] = []
        for wd in ln.words:
            words_total += 1
            if words_total > MAX_WORDS:
                truncated = True
                break
            words.append({"text": str(wd.text), "bbox": _bbox(wd.bounding_rect, scale)})
        if truncated and not words:
            break
        text = " ".join(x["text"] for x in words)
        if chars + len(text) > MAX_CHARS:
            truncated = True
            break
        chars += len(text) + 1
        if words:
            x0 = min(x["bbox"]["x"] for x in words)
            y0 = min(x["bbox"]["y"] for x in words)
            x1 = max(x["bbox"]["x"] + x["bbox"]["width"] for x in words)
            y1 = max(x["bbox"]["y"] + x["bbox"]["height"] for x in words)
            lines.append({"text": text, "bbox": {"x": x0, "y": y0, "width": x1 - x0, "height": y1 - y0}, "words": words})
        if truncated:
            break
    return {"engine": ENGINE, "language": lang, "text": "\n".join(ln["text"] for ln in lines), "lines": lines,
            "imageSize": {"width": full_w, "height": full_h}, "downscaled": scale > 1.0,
            "truncated": truncated, "certainty": "observed-ocr"}
