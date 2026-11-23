"""Store-screenshot studio (W5): framed phone and tablet screenshots with a caption, composed offline with Pillow.

The screens come from the studio's own preview capture (``capture.capture``: loopback preview of the built app, same
sandbox rules as every other capture). Each screen is placed in a device frame on the brand colour with a caption, at
a size the Play Console accepts (phone 1080x1920, tablet 1600x2560; both 9:16-ish, 320..3840 px). The images are written
under ``<data>/brand/<project id>/store-screenshots`` and copied next to the APK when the owner exports it; the owner
approval flow of the export itself is unchanged (the PNGs are extras, never executable content).
"""

from __future__ import annotations

import hashlib
import io
import os
import re
from collections.abc import Callable
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont

from studio_core.errors import ConflictError

from . import brand

SIZES = {"phone": (1080, 1920), "tablet": (1600, 2560)}
DEVICE_W_FRAC = {"phone": 0.66, "tablet": 0.78}
BEZEL = {"phone": 22, "tablet": 28}
RADIUS = {"phone": 70, "tablet": 48}
SUPERSAMPLE = 2
MAX_SHOT_PIXELS = 4000 * 4000
_PNG = b"\x89PNG\r\n\x1a\n"
_CAPTION_BAD = re.compile(r"[\x00-\x1f\x7f-\x9f‪-‮⁦-⁩]")


class ShotError(Exception):
    pass


def _font(px: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    try:
        return ImageFont.load_default(size=px)
    except TypeError:  # very old Pillow: bitmap font
        return ImageFont.load_default()


def _wrap(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.ImageFont, width: int, max_lines: int = 3) -> list[str]:
    lines: list[str] = []
    cur = ""
    for word in text.split():
        trial = f"{cur} {word}".strip()
        if draw.textlength(trial, font=font) <= width or not cur:
            cur = trial
        else:
            lines.append(cur)
            cur = word
    if cur:
        lines.append(cur)
    if len(lines) > max_lines:
        lines = [*lines[: max_lines - 1], " ".join(lines[max_lines - 1:])]
    return lines


def compose(shot_png: bytes, form: str, cfg: brand.BrandConfig, caption: str = "") -> bytes:
    """One framed store image (PNG bytes). ``shot_png`` must be a PNG of at most 16 megapixels."""
    if form not in SIZES:
        raise ShotError("form must be phone or tablet")
    if not isinstance(shot_png, bytes) or not shot_png.startswith(_PNG):
        raise ShotError("the screenshot is not a PNG")
    caption = _CAPTION_BAD.sub("", caption or cfg.caption or "").strip()[:60]
    Image.MAX_IMAGE_PIXELS = MAX_SHOT_PIXELS
    try:
        shot = Image.open(io.BytesIO(shot_png))
        shot.load()
    except (OSError, Image.DecompressionBombError) as e:
        raise ShotError("the screenshot cannot be decoded") from e
    shot = shot.convert("RGB")
    bg, fg = cfg.colours()
    w, h = SIZES[form]
    k = SUPERSAMPLE
    img = Image.new("RGB", (w * k, h * k), brand.rgb(bg))
    d = ImageDraw.Draw(img)
    # caption block (top 22%)
    cap_font = _font(int(h * 0.045 * k))
    lines = _wrap(d, caption, cap_font, int(w * 0.82 * k)) if caption else []
    line_h = int(h * 0.058 * k)
    top = int(h * 0.075 * k)
    for i, line in enumerate(lines):
        tw = d.textlength(line, font=cap_font)
        d.text(((w * k - tw) / 2, top + i * line_h), line, font=cap_font, fill=brand.rgb(fg))
    # device
    dev_w = int(w * DEVICE_W_FRAC[form] * k)
    bz = BEZEL[form] * k
    scr_w = dev_w - 2 * bz
    scr_h = round(scr_w * shot.height / shot.width)
    dev_h = scr_h + 2 * bz
    x0 = (w * k - dev_w) // 2
    y0 = int(h * 0.22 * k)
    avail = h * k - y0 - int(h * 0.03 * k)
    if dev_h > avail:  # the phone is cropped at the bottom edge on purpose (taller than the canvas), like store mockups
        dev_h = h * k - y0 + bz * 2
    r = RADIUS[form] * k
    d.rounded_rectangle((x0, y0, x0 + dev_w, y0 + dev_h), radius=r, fill=(16, 16, 18))
    screen = shot.resize((scr_w, scr_h), Image.Resampling.LANCZOS)
    mask = Image.new("L", (scr_w, scr_h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, scr_w - 1, scr_h - 1), radius=max(4, r - bz), fill=255)
    img.paste(screen, (x0 + bz, y0 + bz), mask)
    out = img.resize((w, h), Image.Resampling.LANCZOS)
    buf = io.BytesIO()
    out.save(buf, format="PNG")
    return buf.getvalue()


def generate(studio: Any, project_id: str, *, caption: str | None = None,
             shoot: Callable[..., dict[str, Any]] | None = None) -> dict[str, Any]:
    """Capture the built app on a phone and a tablet frame, compose both and store them (replacing earlier ones)."""
    from . import capture
    store = brand.BrandStore(studio.settings.data_dir)
    cfg = store.config(project_id)
    cap = capture.capture(studio, project_id, ["phone", "tablet"], **({"shoot": shoot} if shoot else {}))
    folder = store.shots_dir(project_id)
    folder.mkdir(parents=True, exist_ok=True)
    out: list[dict[str, Any]] = []
    for c in cap["captures"]:
        path = capture.capture_file(studio.settings, c["sha256"])
        if path is None:
            raise ShotError("a captured screen is no longer available")
        png = compose(path.read_bytes(), c["frame"], cfg, caption if caption is not None else cfg.caption)
        target = folder / f"store-{c['frame']}.png"
        tmp = folder / f".store-{c['frame']}.{os.getpid()}.partial"
        tmp.write_bytes(png)
        os.replace(tmp, target)
        out.append({"frame": c["frame"], "file": target.name, "width": SIZES[c["frame"]][0],
                    "height": SIZES[c["frame"]][1], "bytes": len(png)})
    return {"revision": cap["revision"], "screenshots": out}


def _store_files(src: Path) -> list[Path]:
    if not src.is_dir() or src.is_symlink():
        return []
    return [f for f in sorted(src.glob("store-*.png"))
            if not f.is_symlink() and f.is_file() and f.stat().st_size <= 8 * 1024 * 1024]


def fingerprint(settings: Any, project_id: str) -> dict[str, Any]:
    """sha256 of every exportable store screenshot plus of the caption text: bound into the export approval."""
    store = brand.BrandStore(settings.data_dir)
    out: dict[str, Any] = {}
    for f in _store_files(store.shots_dir(project_id)):
        data = f.read_bytes()
        if data.startswith(_PNG):
            out[f.name] = hashlib.sha256(data).hexdigest()
    if not out:
        return {}
    try:
        caption = store.config(project_id).caption
    except Exception:  # noqa: BLE001 - an unreadable brand config binds as "no caption"
        caption = ""
    out["caption"] = hashlib.sha256(caption.encode("utf-8")).hexdigest()
    return out


def copy_for_export(settings: Any, project_id: str, dest: Path, expected: dict[str, Any] | None = None) -> list[str]:
    """Copy the stored store images (plain PNG files only) into ``dest``/store-screenshots; names returned.
    With ``expected`` (the approved fingerprint) any difference raises ConflictError: re-approval needed."""
    if expected is not None and fingerprint(settings, project_id) != expected:
        raise ConflictError("the store screenshots changed after the owner approved the export: approve again")
    src = brand.BrandStore(settings.data_dir).shots_dir(project_id)
    names: list[str] = []
    if not src.is_dir() or src.is_symlink():
        return names
    target = Path(dest) / "store-screenshots"
    for f in _store_files(src):
        data = f.read_bytes()
        if not data.startswith(_PNG):
            continue
        if expected is not None and hashlib.sha256(data).hexdigest() != expected.get(f.name):
            raise ConflictError("the store screenshots changed after the owner approved the export: approve again")
        target.mkdir(parents=True, exist_ok=True)
        tmp = target / f".{f.name}.{os.getpid()}.partial"
        tmp.write_bytes(data)
        os.replace(tmp, target / f.name)
        names.append(f.name)
    return names
