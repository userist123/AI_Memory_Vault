"""Build-time icon generation for the desktop wrapper (D1, PNG/ICO are no longer committed).

The committed source is ``images/desktop-wrapper/icon.svg``. Only the tiny SVG subset the icon uses is rendered
(``rect`` with x/y/width/height/rx, a solid hex fill or a vertical two-stop ``linearGradient``); anything else raises
``IconError`` so the source cannot silently drift from what the renderer draws. Output is deterministic (4x
supersampling, fixed resampling), so the same SVG always yields the same bytes.

    python core/aas_build/icons.py <icon.svg> <out_dir>
"""

from __future__ import annotations

import io
import sys
import xml.etree.ElementTree as ET  # the SVG is a committed, trusted source file (never agent or user input)
from pathlib import Path

from PIL import Image, ImageDraw

SVG_NS = "{http://www.w3.org/2000/svg}"
SUPERSAMPLE = 4
PNG_SIZES = {"32x32.png": 32, "128x128.png": 128}
ICO_SIZES = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


class IconError(Exception):
    pass


def _hex(value: str) -> tuple[int, int, int]:
    v = value.strip()
    if len(v) != 7 or not v.startswith("#"):
        raise IconError(f"unsupported colour: {value!r}")
    try:
        return int(v[1:3], 16), int(v[3:5], 16), int(v[5:7], 16)
    except ValueError as e:
        raise IconError(f"unsupported colour: {value!r}") from e


def _gradients(root: ET.Element) -> dict[str, tuple[tuple[int, int, int], tuple[int, int, int]]]:
    out = {}
    for g in root.iter(SVG_NS + "linearGradient"):
        if g.get("x1") != g.get("x2") or g.get("y1") != "0" or g.get("y2") != "1":
            raise IconError("only top-to-bottom gradients are supported")
        stops = g.findall(SVG_NS + "stop")
        if len(stops) != 2:
            raise IconError("gradients need exactly two stops")
        out[g.get("id", "")] = (_hex(stops[0].get("stop-color", "")), _hex(stops[1].get("stop-color", "")))
    return out


def render_svg(svg: Path, px: int) -> Image.Image:
    """Render the icon SVG at ``px`` x ``px`` (RGBA)."""
    root = ET.fromstring(Path(svg).read_text(encoding="utf-8"))  # noqa: S314 - trusted committed file
    vb = [float(x) for x in root.get("viewBox", "").split()]
    if len(vb) != 4 or vb[0] != 0 or vb[1] != 0 or vb[2] != vb[3]:
        raise IconError("viewBox must be a square starting at 0 0")
    big = px * SUPERSAMPLE
    k = big / vb[2]
    grads = _gradients(root)
    canvas = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    for el in root:
        tag = el.tag.removeprefix(SVG_NS)
        if tag == "defs":
            continue
        if tag != "rect":
            raise IconError(f"unsupported element: {tag}")
        x, y, w, h = (float(el.get(a, "0")) * k for a in ("x", "y", "width", "height"))
        r = float(el.get("rx", "0")) * k
        mask = Image.new("L", (big, big), 0)
        ImageDraw.Draw(mask).rounded_rectangle((x, y, x + w - 1, y + h - 1), radius=r, fill=255)
        fill = el.get("fill", "")
        if fill.startswith("url(#") and fill.endswith(")"):
            top, bottom = grads[fill[5:-1]]
            layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
            d = ImageDraw.Draw(layer)
            y0, y1 = int(y), int(y + h)
            for row in range(y0, y1):
                t = (row - y0) / max(1, y1 - y0 - 1)
                d.line([(0, row), (big, row)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom, strict=True)) + (255,))
        else:
            layer = Image.new("RGBA", (big, big), _hex(fill) + (255,))
        canvas.paste(layer, (0, 0), mask)
    return canvas.resize((px, px), Image.Resampling.LANCZOS)


def write_icons(svg: Path, out: Path) -> list[str]:
    """Write 32x32.png, 128x128.png and icon.ico (the files the wrapper's tauri.conf lists) into ``out``."""
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    names = []
    for name, px in PNG_SIZES.items():
        render_svg(svg, px).save(out / name, format="PNG")
        names.append(name)
    buf = io.BytesIO()
    render_svg(svg, 256).save(buf, format="ICO", sizes=ICO_SIZES)
    (out / "icon.ico").write_bytes(buf.getvalue())
    names.append("icon.ico")
    return names


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__)
        return 2
    write_icons(Path(argv[0]), Path(argv[1]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
