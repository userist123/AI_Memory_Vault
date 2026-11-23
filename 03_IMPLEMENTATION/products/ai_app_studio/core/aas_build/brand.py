"""Offline brand kit for the Android build (W5): launcher icon set, adaptive icon, splash, app label, version.

Everything is deterministic and offline. The glyph comes from a small bundled Lucide subset
(``images/android-build/brand-icons``: ISC licence + MANIFEST.json with the sha256 of every file; a glyph whose bytes
do not match the manifest is refused). Only the SVG subset those icons use is rendered (path/circle/rect/line, stroked,
round caps and joins) with Pillow, 4x supersampled, so the same input always yields the same bytes.

Android geometry (Material adaptive icons): the layers are 108dp, the launcher masks to at most a 72dp circle and the
guaranteed "safe zone" is the 66dp circle in the middle. The glyph box is 44dp, whose corners (22*sqrt(2) = 31.1dp from
the centre) stay inside the 33dp radius of the 66dp safe circle.

The studio writes the generated ``res/`` tree into a directory that the build container mounts read-only
(``/brand``); ``aas-build-android`` copies the whitelisted files over the Capacitor template.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import struct
import uuid
import xml.etree.ElementTree as ET  # the glyphs are committed files whose sha256 is verified first
import zipfile
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw

PRODUCT = Path(__file__).resolve().parents[2]
GLYPH_DIR = PRODUCT / "images" / "android-build" / "brand-icons"
SVG_NS = "{http://www.w3.org/2000/svg}"
SUPERSAMPLE = 4
DENSITIES = {"mdpi": 1.0, "hdpi": 1.5, "xhdpi": 2.0, "xxhdpi": 3.0, "xxxhdpi": 4.0}
ADAPTIVE_DP = 108.0
SAFE_DP = 66.0
MASK_DP = 72.0
GLYPH_BOX_DP = 44.0  # corners at 31.1dp from the centre < 33dp (the 66dp safe circle)
LEGACY_DP = 48.0
SPLASH_ICON_PX = 576  # 288dp at xxhdpi/2: Android shows the icon inside a circle of 2/3 of this size
SPLASH_GLYPH_FRAC = 0.30  # glyph box = 30% of the canvas (corners stay inside the visible 2/3 circle)
DEFAULT_BACKGROUND = "#2563EB"
DEFAULT_FOREGROUND = "#FFFFFF"
DARK_FOREGROUND = "#111827"
_HEX = re.compile(r"^#[0-9a-fA-F]{6}$")
_GLYPH = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
_NUM = re.compile(r"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")


class BrandError(Exception):
    pass


# ---- colours ----------------------------------------------------------------------------------------------------------
def check_hex(value: str) -> str:
    if not isinstance(value, str) or not _HEX.match(value):
        raise BrandError(f"colour must be #RRGGBB: {value!r}")
    return value.upper()


def rgb(value: str) -> tuple[int, int, int]:
    v = check_hex(value)
    return int(v[1:3], 16), int(v[3:5], 16), int(v[5:7], 16)


def luminance(value: str) -> float:
    def lin(c: int) -> float:
        s = c / 255
        return s / 12.92 if s <= 0.03928 else ((s + 0.055) / 1.055) ** 2.4
    r, g, b = rgb(value)
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)


def contrast(a: str, b: str) -> float:
    la, lb = sorted((luminance(a), luminance(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


@dataclass(frozen=True)
class BrandConfig:
    glyph: str = "star"
    background: str = DEFAULT_BACKGROUND
    foreground: str | None = None  # None = white or near-black, whichever contrasts with the background
    caption: str = ""
    version_major: int = 1
    version_minor: int = 0

    def colours(self) -> tuple[str, str]:
        bg = check_hex(self.background)
        if self.foreground:
            return bg, check_hex(self.foreground)
        return bg, DEFAULT_FOREGROUND if contrast(DEFAULT_FOREGROUND, bg) >= contrast(DARK_FOREGROUND, bg) else DARK_FOREGROUND

    def to_json(self) -> dict[str, object]:
        return {"glyph": self.glyph, "background": self.background, "foreground": self.foreground,
                "caption": self.caption, "versionMajor": self.version_major, "versionMinor": self.version_minor}

    @classmethod
    def from_json(cls, d: dict[str, object]) -> BrandConfig:
        return cls(glyph=str(d.get("glyph", "star")), background=str(d.get("background", DEFAULT_BACKGROUND)),
                   foreground=(str(d["foreground"]) if d.get("foreground") else None), caption=str(d.get("caption", "")),
                   version_major=int(d.get("versionMajor", 1)),  # type: ignore[call-overload]
                   version_minor=int(d.get("versionMinor", 0)))  # type: ignore[call-overload]


def palette_from_css(css: str) -> dict[str, str]:
    """Pick a brand palette from the project's design tokens (CSS custom properties). Looks for the first of
    --primary/--color-primary/--brand/--accent for the background and --on-primary/--primary-foreground for the glyph."""
    found: dict[str, str] = {}
    for m in re.finditer(r"--([a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{6})\b", css or ""):
        found.setdefault(m.group(1), m.group(2).upper())
    out: dict[str, str] = {}
    for key in ("primary", "color-primary", "brand", "accent", "color-accent"):
        if key in found:
            out["background"] = found[key]
            break
    for key in ("on-primary", "primary-foreground", "color-on-primary"):
        if key in found:
            out["foreground"] = found[key]
            break
    return out


# ---- glyphs -----------------------------------------------------------------------------------------------------------
def glyph_names() -> list[str]:
    return sorted(p.stem for p in GLYPH_DIR.glob("*.svg"))


def _manifest() -> dict[str, str]:
    return json.loads((GLYPH_DIR / "MANIFEST.json").read_text(encoding="utf-8"))["files"]


def verify_glyph_set() -> list[str]:
    """Problems (empty = every bundled glyph matches MANIFEST.json and the licence file is present)."""
    problems = [] if (GLYPH_DIR / "LICENSE").is_file() else ["LICENSE missing"]
    man = _manifest()
    for name, sha in man.items():
        f = GLYPH_DIR / name
        if not f.is_file() or hashlib.sha256(f.read_bytes()).hexdigest() != sha:
            problems.append(f"{name}: sha256 mismatch")
    for p in GLYPH_DIR.glob("*.svg"):
        if p.name not in man:
            problems.append(f"{p.name}: not in the manifest")
    return problems


Polyline = list[tuple[float, float]]


def _bezier(p0, p1, p2, p3, n=18) -> Polyline:  # type: ignore[no-untyped-def]
    out = []
    for i in range(1, n + 1):
        t = i / n
        u = 1 - t
        out.append((u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                    u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))
    return out


def _arc(x1: float, y1: float, rx: float, ry: float, phi: float, fa: int, fs: int, x2: float, y2: float,
         n: int = 24) -> Polyline:
    if rx == 0 or ry == 0 or (x1 == x2 and y1 == y2):
        return [(x2, y2)]
    rx, ry = abs(rx), abs(ry)
    cp, sp = math.cos(math.radians(phi)), math.sin(math.radians(phi))
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    x1p, y1p = cp * dx + sp * dy, -sp * dx + cp * dy
    lam = x1p**2 / rx**2 + y1p**2 / ry**2
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx**2 * ry**2 - rx**2 * y1p**2 - ry**2 * x1p**2
    den = rx**2 * y1p**2 + ry**2 * x1p**2
    co = math.sqrt(max(0.0, num / den)) * (-1 if fa == fs else 1)
    cxp, cyp = co * rx * y1p / ry, -co * ry * x1p / rx
    cx, cy = cp * cxp - sp * cyp + (x1 + x2) / 2, sp * cxp + cp * cyp + (y1 + y2) / 2

    def ang(ux: float, uy: float, vx: float, vy: float) -> float:
        a = math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
        return a
    th1 = ang(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry)
    dth = ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry)
    if not fs and dth > 0:
        dth -= 2 * math.pi
    elif fs and dth < 0:
        dth += 2 * math.pi
    pts = []
    for i in range(1, n + 1):
        t = th1 + dth * i / n
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cp * x - sp * y + cx, sp * x + cp * y + cy))
    return pts


def parse_path(d: str) -> list[Polyline]:
    """SVG path data -> polylines (M L H V C S Q T A Z, absolute and relative)."""
    i, n = 0, len(d)
    subs: list[Polyline] = []
    cur: Polyline = []
    x = y = sx = sy = 0.0
    cmd = ""
    last_c: tuple[float, float] | None = None  # last cubic/quad control for S/T
    last_kind = ""

    def num() -> float:
        nonlocal i
        while i < n and d[i] in " ,\t\r\n":
            i += 1
        m = _NUM.match(d, i)
        if not m:
            raise BrandError("bad path data")
        i = m.end()
        return float(m.group())

    def flag() -> int:
        nonlocal i
        while i < n and d[i] in " ,\t\r\n":
            i += 1
        if i >= n or d[i] not in "01":
            raise BrandError("bad arc flag")
        i += 1
        return int(d[i - 1])
    while True:
        while i < n and d[i] in " ,\t\r\n":
            i += 1
        if i >= n:
            break
        if d[i].isalpha():
            cmd = d[i]
            i += 1
            if cmd in "Zz":
                if cur:
                    cur.append((sx, sy))
                    subs.append(cur)
                    cur = []
                x, y = sx, sy
                last_kind = ""
                continue
        elif not cmd:
            raise BrandError("bad path data")
        rel = cmd.islower()
        c = cmd.upper()
        ox, oy = (x, y) if rel else (0.0, 0.0)
        if c == "M":
            if cur:
                subs.append(cur)
            x, y = num() + ox, num() + oy
            sx, sy = x, y
            cur = [(x, y)]
            cmd = "l" if rel else "L"
            last_kind = ""
        elif c == "L":
            x, y = num() + ox, num() + oy
            cur.append((x, y))
            last_kind = ""
        elif c == "H":
            x = num() + ox
            cur.append((x, y))
            last_kind = ""
        elif c == "V":
            y = num() + oy
            cur.append((x, y))
            last_kind = ""
        elif c == "C":
            p1 = (num() + ox, num() + oy)
            p2 = (num() + ox, num() + oy)
            p3 = (num() + ox, num() + oy)
            cur += _bezier((x, y), p1, p2, p3)
            x, y = p3
            last_c, last_kind = p2, "C"
        elif c == "S":
            p1 = (2 * x - last_c[0], 2 * y - last_c[1]) if last_kind == "C" and last_c else (x, y)
            p2 = (num() + ox, num() + oy)
            p3 = (num() + ox, num() + oy)
            cur += _bezier((x, y), p1, p2, p3)
            x, y = p3
            last_c, last_kind = p2, "C"
        elif c in "QT":
            if c == "Q":
                q = (num() + ox, num() + oy)
            else:
                q = (2 * x - last_c[0], 2 * y - last_c[1]) if last_kind == "Q" and last_c else (x, y)
            p3 = (num() + ox, num() + oy)
            c1 = (x + 2 / 3 * (q[0] - x), y + 2 / 3 * (q[1] - y))
            c2 = (p3[0] + 2 / 3 * (q[0] - p3[0]), p3[1] + 2 / 3 * (q[1] - p3[1]))
            cur += _bezier((x, y), c1, c2, p3)
            x, y = p3
            last_c, last_kind = q, "Q"
        elif c == "A":
            rx, ry, rot = num(), num(), num()
            fa, fs = flag(), flag()
            ex, ey = num() + ox, num() + oy
            cur += _arc(x, y, rx, ry, rot, fa, fs, ex, ey)
            x, y = ex, ey
            last_kind = ""
        else:
            raise BrandError(f"unsupported path command {cmd!r}")
    if cur:
        subs.append(cur)
    return subs


def _ellipse(cx: float, cy: float, rx: float, ry: float, n: int = 64) -> Polyline:
    return [(cx + rx * math.cos(2 * math.pi * k / n), cy + ry * math.sin(2 * math.pi * k / n)) for k in range(n + 1)]


def _rect(x: float, y: float, w: float, h: float, rx: float) -> Polyline:
    rx = min(rx, w / 2, h / 2)
    if rx <= 0:
        return [(x, y), (x + w, y), (x + w, y + h), (x, y + h), (x, y)]
    pts: Polyline = []
    for cx, cy, a0 in ((x + w - rx, y + rx, -90), (x + w - rx, y + h - rx, 0), (x + rx, y + h - rx, 90),
                       (x + rx, y + rx, 180)):
        pts += [(cx + rx * math.cos(math.radians(a0 + 90 * k / 8)), cy + rx * math.sin(math.radians(a0 + 90 * k / 8)))
                for k in range(9)]
    pts.append(pts[0])
    return pts


_GLYPH_CACHE: dict[str, list[Polyline]] = {}


def load_glyph(name: str) -> list[Polyline]:
    """Polylines (24-unit box, stroke 2) of a bundled glyph; the file must match MANIFEST.json."""
    if not isinstance(name, str) or not _GLYPH.match(name):
        raise BrandError("invalid glyph name")
    if name in _GLYPH_CACHE:
        return _GLYPH_CACHE[name]
    f = GLYPH_DIR / f"{name}.svg"
    want = _manifest().get(f"{name}.svg")
    if want is None or not f.is_file():
        raise BrandError(f"unknown glyph {name!r}")
    data = f.read_bytes()
    if hashlib.sha256(data).hexdigest() != want:
        raise BrandError(f"glyph {name!r} does not match the manifest")
    out: list[Polyline] = []
    for el in ET.fromstring(data):  # noqa: S314
        tag = el.tag.removeprefix(SVG_NS)
        g = el.get
        if tag == "path":
            out += parse_path(g("d", ""))
        elif tag == "circle":
            out.append(_ellipse(float(g("cx", "0")), float(g("cy", "0")), float(g("r", "0")), float(g("r", "0"))))
        elif tag == "rect":
            out.append(_rect(float(g("x", "0")), float(g("y", "0")), float(g("width", "0")), float(g("height", "0")),
                             float(g("rx", "0"))))
        elif tag == "line":
            out.append([(float(g("x1", "0")), float(g("y1", "0"))), (float(g("x2", "0")), float(g("y2", "0")))])
        else:
            raise BrandError(f"unsupported glyph element {tag!r}")
    _GLYPH_CACHE[name] = out
    return out


def render_glyph(name: str, canvas_px: int, box_px: float, color: str) -> Image.Image:
    """RGBA canvas of ``canvas_px`` with the glyph centred in a ``box_px`` square (stroke scales with the box)."""
    polys = load_glyph(name)
    big = canvas_px * SUPERSAMPLE
    k = box_px * SUPERSAMPLE / 24.0
    off = (big - 24.0 * k) / 2
    layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    col = rgb(color) + (255,)
    w = max(1, round(2.0 * k))
    r = w / 2
    for pl in polys:
        pts = [(off + px * k, off + py * k) for px, py in pl]
        if len(pts) > 1:
            d.line(pts, fill=col, width=w, joint="curve")
        for px, py in (pts[0], pts[-1]) if len(pts) > 1 else pts:
            d.ellipse((px - r, py - r, px + r, py + r), fill=col)
    return layer.resize((canvas_px, canvas_px), Image.Resampling.LANCZOS)


# ---- layers -----------------------------------------------------------------------------------------------------------
def _px(dp: float, density: float) -> int:
    return round(dp * density)


def foreground_layer(cfg: BrandConfig, density: float) -> Image.Image:
    """Adaptive-icon foreground: 108dp transparent canvas, glyph in a 44dp box (inside the 66dp safe zone)."""
    return render_glyph(cfg.glyph, _px(ADAPTIVE_DP, density), GLYPH_BOX_DP * density, cfg.colours()[1])


def monochrome_layer(cfg: BrandConfig, density: float) -> Image.Image:
    """Themed-icon layer: the same geometry in opaque black (the launcher tints it)."""
    return render_glyph(cfg.glyph, _px(ADAPTIVE_DP, density), GLYPH_BOX_DP * density, "#000000")


def legacy_icon(cfg: BrandConfig, density: float, *, round_icon: bool) -> Image.Image:
    """Pre-API-26 launcher icon (48dp): background plate + glyph; circle for the round variant."""
    px = _px(LEGACY_DP, density)
    big = px * SUPERSAMPLE
    bg, fg = cfg.colours()
    plate = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    m = 2.0 * density * SUPERSAMPLE
    box = (m, m, big - 1 - m, big - 1 - m)
    dr = ImageDraw.Draw(plate)
    if round_icon:
        dr.ellipse(box, fill=rgb(bg) + (255,))
    else:
        dr.rounded_rectangle(box, radius=10.0 * density * SUPERSAMPLE, fill=rgb(bg) + (255,))
    plate = plate.resize((px, px), Image.Resampling.LANCZOS)
    plate.alpha_composite(render_glyph(cfg.glyph, px, 26.0 * density, fg))
    return plate


def splash_icon(cfg: BrandConfig) -> Image.Image:
    """Splash animated icon on a transparent canvas (the splash window background is the brand colour)."""
    return render_glyph(cfg.glyph, SPLASH_ICON_PX, SPLASH_ICON_PX * SPLASH_GLYPH_FRAC, cfg.colours()[1])


def master_png(cfg: BrandConfig, px: int = 1024) -> Image.Image:
    bg, fg = cfg.colours()
    img = Image.new("RGBA", (px, px), rgb(bg) + (255,))
    img.alpha_composite(render_glyph(cfg.glyph, px, px * 0.46, fg))
    return img


def master_svg(cfg: BrandConfig) -> str:
    """Resolution-independent master (1024 box): background plate + the glyph as strokes."""
    bg, fg = cfg.colours()
    src = (GLYPH_DIR / f"{cfg.glyph}.svg").read_text(encoding="utf-8")
    load_glyph(cfg.glyph)  # verifies the manifest hash
    inner = "\n".join(line for line in src.splitlines()[1:-1])
    s = 1024 * 0.46 / 24
    t = (1024 - 24 * s) / 2
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">\n'
            f'  <rect width="1024" height="1024" fill="{bg}"/>\n'
            f'  <g transform="translate({t:.2f} {t:.2f}) scale({s:.4f})" fill="none" stroke="{fg}" stroke-width="2" '
            'stroke-linecap="round" stroke-linejoin="round">\n' + inner + "\n  </g>\n</svg>\n")


# ---- Android resources ------------------------------------------------------------------------------------------------
_XML_BAD = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f-\x9f‪-‮⁦-⁩]")


def app_label(name: str) -> str:
    """Launcher label from the project name: printable text, no markup, at most 30 characters."""
    s = _XML_BAD.sub("", str(name or "")).strip()
    s = re.sub(r"\s+", " ", s)[:30].strip()
    return s or "App"


def _xml_text(s: str) -> str:
    # Android string resources: escape XML and the characters aapt2 treats specially
    s = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    s = s.replace("\\", "\\\\").replace("'", "\\'").replace('"', '\\"').replace("@", "\\@").replace("?", "\\?")
    return s


def strings_xml(label: str, app_id: str) -> str:
    return ("<?xml version='1.0' encoding='utf-8'?>\n<resources>\n"
            f'    <string name="app_name">{_xml_text(label)}</string>\n'
            f'    <string name="title_activity_main">{_xml_text(label)}</string>\n'
            f'    <string name="package_name">{app_id}</string>\n'
            f'    <string name="custom_url_scheme">{app_id}</string>\n</resources>\n')


def styles_xml() -> str:
    return """<?xml version="1.0" encoding="utf-8"?>
<resources>

    <!-- Base application theme. -->
    <style name="AppTheme" parent="Theme.AppCompat.Light.DarkActionBar">
        <item name="colorPrimary">@color/colorPrimary</item>
        <item name="colorPrimaryDark">@color/colorPrimaryDark</item>
        <item name="colorAccent">@color/colorAccent</item>
    </style>

    <!-- Edge-to-edge: transparent system bars, content drawn under them (insets reach the page as CSS safe-area). -->
    <style name="AppTheme.NoActionBar" parent="Theme.AppCompat.DayNight.NoActionBar">
        <item name="windowActionBar">false</item>
        <item name="windowNoTitle">true</item>
        <item name="android:background">@null</item>
        <item name="android:statusBarColor">@android:color/transparent</item>
        <item name="android:navigationBarColor">@android:color/transparent</item>
    </style>

    <!-- androidx.core:core-splashscreen (>= 1.2.0): brand-coloured window with the glyph, then AppTheme.NoActionBar. -->
    <style name="AppTheme.NoActionBarLaunch" parent="Theme.SplashScreen">
        <item name="windowSplashScreenBackground">@color/aas_splash_background</item>
        <item name="windowSplashScreenAnimatedIcon">@drawable/aas_splash_icon</item>
        <item name="postSplashScreenTheme">@style/AppTheme.NoActionBar</item>
    </style>
</resources>
"""


def colours_xml(bg: str) -> str:
    return ('<?xml version="1.0" encoding="utf-8"?>\n<resources>\n'
            f'    <color name="ic_launcher_background">{bg}</color>\n'
            f'    <color name="aas_splash_background">{bg}</color>\n</resources>\n')


def adaptive_xml(*, with_monochrome: bool = True) -> str:
    mono = '    <monochrome android:drawable="@mipmap/ic_launcher_monochrome"/>\n' if with_monochrome else ""
    return ('<?xml version="1.0" encoding="utf-8"?>\n'
            '<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n'
            '    <background android:drawable="@color/ic_launcher_background"/>\n'
            '    <foreground android:drawable="@mipmap/ic_launcher_foreground"/>\n' + mono + "</adaptive-icon>\n")


def _save(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, format="PNG", optimize=False)


def write_res(cfg: BrandConfig, project_name: str, app_id: str, out: Path) -> list[str]:
    """Write the brand ``res/`` tree (relative paths returned) into ``out``/res. Never writes outside ``out``."""
    if not re.fullmatch(r"dev\.aiappstudio\.p[0-9a-f]{16}", app_id):
        raise BrandError("application id is not a studio package id")
    bg, _ = cfg.colours()
    res = Path(out) / "res"
    for dpi, dens in DENSITIES.items():
        _save(foreground_layer(cfg, dens), res / f"mipmap-{dpi}" / "ic_launcher_foreground.png")
        _save(monochrome_layer(cfg, dens), res / f"mipmap-{dpi}" / "ic_launcher_monochrome.png")
        _save(legacy_icon(cfg, dens, round_icon=False), res / f"mipmap-{dpi}" / "ic_launcher.png")
        _save(legacy_icon(cfg, dens, round_icon=True), res / f"mipmap-{dpi}" / "ic_launcher_round.png")
    _save(splash_icon(cfg), res / "drawable-nodpi" / "aas_splash_icon.png")
    for name in ("ic_launcher.xml", "ic_launcher_round.xml"):
        p = res / "mipmap-anydpi-v26" / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(adaptive_xml(), encoding="utf-8")
    (res / "values").mkdir(parents=True, exist_ok=True)
    (res / "values" / "strings.xml").write_text(strings_xml(app_label(project_name), app_id), encoding="utf-8")
    (res / "values" / "styles.xml").write_text(styles_xml(), encoding="utf-8")
    (res / "values" / "ic_launcher_background.xml").write_text(colours_xml(bg), encoding="utf-8")
    return sorted(p.relative_to(out).as_posix() for p in res.rglob("*") if p.is_file())


def write_master(cfg: BrandConfig, out: Path) -> list[str]:
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "icon-master.svg").write_text(master_svg(cfg), encoding="utf-8")
    _save(master_png(cfg, 1024), out / "icon-1024.png")
    _save(master_png(cfg, 512), out / "icon-512.png")  # Play Store listing icon size
    return ["icon-master.svg", "icon-1024.png", "icon-512.png"]


# ---- version (auto-increment per successful Android build) --------------------------------------------------------------
VERSION_MAX = 2_100_000_000  # Android versionCode upper bound


def semver(cfg: BrandConfig, version_code: int) -> str:
    """SemVer 2.0 versionName: <major>.<minor>.<patch>, patch = versionCode - 1 (the first build is x.y.0)."""
    return f"{cfg.version_major}.{cfg.version_minor}.{max(0, version_code - 1)}"


@dataclass
class BrandStore:
    """Per-project brand config + versionCode counter under <data>/brand/<project id>/ (atomic JSON writes)."""
    root: Path

    def dir(self, project_id: str) -> Path:
        if not re.fullmatch(r"prj_[0-9a-f]{16}", project_id or ""):
            raise BrandError("invalid project id")
        return self.root / "brand" / project_id

    def _read(self, project_id: str) -> dict[str, object]:
        try:
            d = json.loads((self.dir(project_id) / "brand.json").read_text(encoding="utf-8"))
            return d if isinstance(d, dict) else {}
        except (OSError, ValueError):
            return {}

    def _write(self, project_id: str, d: dict[str, object]) -> None:
        folder = self.dir(project_id)
        folder.mkdir(parents=True, exist_ok=True)
        tmp = folder / f".brand.{uuid.uuid4().hex[:8]}.partial"
        tmp.write_text(json.dumps(d, indent=1, sort_keys=True), encoding="utf-8")
        os.replace(tmp, folder / "brand.json")

    def config(self, project_id: str) -> BrandConfig:
        d = self._read(project_id)
        try:
            return BrandConfig.from_json(d)
        except (TypeError, ValueError):
            return BrandConfig()

    def save_config(self, project_id: str, cfg: BrandConfig) -> None:
        validate(cfg)
        d = self._read(project_id)
        d.update(cfg.to_json())
        self._write(project_id, d)

    def last_version_code(self, project_id: str) -> int:
        v = self._read(project_id).get("versionCode", 0)
        return v if isinstance(v, int) and 0 <= v < VERSION_MAX else 0

    def next_version_code(self, project_id: str) -> int:
        """The versionCode the next build will carry (nothing is consumed until commit_version_code)."""
        return self.last_version_code(project_id) + 1

    def commit_version_code(self, project_id: str, code: int) -> None:
        """Record a SUCCESSFUL build; the counter only moves forward."""
        d = self._read(project_id)
        if code > self.last_version_code(project_id) and code < VERSION_MAX:
            d["versionCode"] = code
            self._write(project_id, d)

    def shots_dir(self, project_id: str) -> Path:
        return self.dir(project_id) / "store-screenshots"


def validate(cfg: BrandConfig) -> BrandConfig:
    if cfg.glyph not in glyph_names():
        raise BrandError(f"unknown glyph {cfg.glyph!r}; choose one of {', '.join(glyph_names())}")
    cfg.colours()
    if cfg.foreground and contrast(cfg.foreground, cfg.background) < 3.0:
        raise BrandError("foreground and background need a contrast ratio of at least 3:1")
    if len(cfg.caption) > 60 or _XML_BAD.search(cfg.caption):
        raise BrandError("caption must be printable text of at most 60 characters")
    if not (0 <= cfg.version_major <= 999 and 0 <= cfg.version_minor <= 999):
        raise BrandError("version parts must be 0..999")
    return cfg


def write_brand_dir(cfg: BrandConfig, project_name: str, app_id: str, version_code: int, out: Path) -> Path:
    """Everything the build container reads from /brand: res/ and brand.json {versionCode, versionName}."""
    validate(cfg)
    if not (1 <= version_code < VERSION_MAX):
        raise BrandError("invalid versionCode")
    write_res(cfg, project_name, app_id, out)
    (Path(out) / "brand.json").write_text(
        json.dumps({"versionCode": version_code, "versionName": semver(cfg, version_code)}), encoding="utf-8")
    return Path(out)


# ---- 16 KB page-size alignment of native libraries --------------------------------------------------------------------
PAGE_16K = 0x4000


def _elf_max_align_ok(data: bytes) -> tuple[bool, str]:
    if data[:4] != b"\x7fELF":
        return False, "not an ELF file"
    is64 = data[4] == 2
    little = data[5] == 1
    e = "<" if little else ">"
    try:
        if is64:
            phoff, = struct.unpack_from(e + "Q", data, 0x20)
            phentsize, phnum = struct.unpack_from(e + "HH", data, 0x36)
        else:
            phoff, = struct.unpack_from(e + "I", data, 0x1C)
            phentsize, phnum = struct.unpack_from(e + "HH", data, 0x2A)
        worst = None
        for k in range(phnum):
            o = phoff + k * phentsize
            ptype, = struct.unpack_from(e + "I", data, o)
            if ptype != 1:  # PT_LOAD
                continue
            align, = struct.unpack_from(e + ("Q" if is64 else "I"), data, o + (0x30 if is64 else 0x1C))
            worst = align if worst is None else min(worst, align)
    except struct.error:
        return False, "truncated ELF header"
    if worst is None:
        return True, "no PT_LOAD"
    return worst >= PAGE_16K, f"min LOAD align 0x{worst:x}"


def check_native_alignment(apk: Path) -> dict[str, object]:
    """16 KB check of an APK: every 64-bit native lib (arm64-v8a, x86_64) needs ELF LOAD alignment >= 0x4000 and be
    stored page-aligned when uncompressed. Report: {status: ok|none|fail, libs: [...], problems: [...]}."""
    libs: list[dict[str, object]] = []
    problems: list[str] = []
    with zipfile.ZipFile(apk) as z:
        for info in z.infolist():
            m = re.fullmatch(r"lib/([^/]+)/[^/]+\.so", info.filename)
            if not m:
                continue
            abi = m.group(1)
            ok, why = _elf_max_align_ok(z.read(info))
            libs.append({"name": info.filename, "abi": abi, "elfAlignOk": ok, "detail": why})
            if abi in ("arm64-v8a", "x86_64") and not ok:
                problems.append(f"{info.filename}: {why}")
    status = "none" if not libs else ("fail" if problems else "ok")
    return {"status": status, "libs": libs, "problems": problems}


# ---- preview sheet (what the owner/agent looks at) ---------------------------------------------------------------------
def _adaptive_composite(cfg: BrandConfig, shape: str, px: int, mono: bool = False) -> Image.Image:
    """The adaptive icon as a launcher shows it: background + foreground masked to a circle/squircle (72dp window)."""
    d = px / ADAPTIVE_DP
    bg, _ = cfg.colours()
    canvas = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    if mono:  # themed icon: the launcher tints the monochrome layer onto a neutral plate
        base = Image.new("RGBA", (px, px), rgb("#E5E7EB") + (255,))
        base.alpha_composite(_tint(monochrome_layer(cfg, d), bg))
    else:
        base = Image.new("RGBA", (px, px), rgb(bg) + (255,))
        base.alpha_composite(foreground_layer(cfg, d))
    big = px * SUPERSAMPLE
    mask = Image.new("L", (big, big), 0)
    win = MASK_DP * d * SUPERSAMPLE
    o = (big - win) / 2
    dr = ImageDraw.Draw(mask)
    if shape == "circle":
        dr.ellipse((o, o, o + win, o + win), fill=255)
    else:
        dr.rounded_rectangle((o, o, o + win, o + win), radius=win * 0.30, fill=255)
    mask = mask.resize((px, px), Image.Resampling.LANCZOS)
    canvas.paste(base, (0, 0), mask)
    return canvas


def _tint(layer: Image.Image, colour: str) -> Image.Image:
    solid = Image.new("RGBA", layer.size, rgb(colour) + (255,))
    solid.putalpha(layer.getchannel("A"))
    return solid


def preview_sheet(cfg: BrandConfig, project_name: str = "App") -> bytes:
    """One PNG: circle + squircle launcher masks, the themed (monochrome) icon, the legacy square and round icons, the
    safe-zone guide, and the splash. Used by the brand_kit tool and the UI panel."""
    validate(cfg)
    cell = 216
    pad = 24
    w = pad + 6 * (cell + pad)
    h = pad * 3 + cell * 2 + 40
    sheet = Image.new("RGBA", (w, h), (245, 246, 248, 255))
    dr = ImageDraw.Draw(sheet)
    items = [
        _adaptive_composite(cfg, "circle", cell),
        _adaptive_composite(cfg, "squircle", cell),
        _adaptive_composite(cfg, "circle", cell, mono=True),
        legacy_icon(cfg, cell / LEGACY_DP, round_icon=False),
        legacy_icon(cfg, cell / LEGACY_DP, round_icon=True),
    ]
    for i, im in enumerate(items):
        sheet.alpha_composite(im, (pad + i * (cell + pad), pad))
    # safe-zone guide: foreground on a checker with the 66dp and 72dp circles
    guide = Image.new("RGBA", (cell, cell), (255, 255, 255, 255))
    d2 = cell / ADAPTIVE_DP
    guide.alpha_composite(Image.new("RGBA", (cell, cell), rgb(cfg.colours()[0]) + (60,)))
    guide.alpha_composite(foreground_layer(cfg, d2))
    gd = ImageDraw.Draw(guide)
    for dp, col in ((SAFE_DP, (220, 38, 38, 255)), (MASK_DP, (22, 163, 74, 255))):
        r = dp * d2 / 2
        gd.ellipse((cell / 2 - r, cell / 2 - r, cell / 2 + r, cell / 2 + r), outline=col, width=2)
    sheet.alpha_composite(guide, (pad + 5 * (cell + pad), pad))
    # splash (portrait phone window, brand background + glyph)
    splash = Image.new("RGBA", (cell, cell), rgb(cfg.colours()[0]) + (255,))
    ic = splash_icon(cfg).resize((cell, cell), Image.Resampling.LANCZOS)
    splash.alpha_composite(ic)
    y2 = pad * 2 + cell
    sheet.alpha_composite(splash, (pad, y2))
    dr.text((pad + cell + pad, y2 + 8), f"{app_label(project_name)}  -  {cfg.glyph}  -  {cfg.colours()[0]} / {cfg.colours()[1]}", fill=(30, 30, 30, 255))
    dr.text((pad + cell + pad, y2 + 36), "circle / squircle / themed (monochrome) / legacy square / legacy round / safe zone (red 66dp, green 72dp)", fill=(90, 90, 90, 255))
    dr.text((pad + cell + pad, y2 + 64), "splash (core-splashscreen, left)", fill=(90, 90, 90, 255))
    import io
    buf = io.BytesIO()
    sheet.convert("RGB").save(buf, format="PNG")
    return buf.getvalue()
