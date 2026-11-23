"""W5: offline brand kit (icon geometry / safe zone, monochrome layer, manifest values, versionCode counter)."""
from __future__ import annotations

import json
import math
import re
import shutil
import struct
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

import pytest
from PIL import Image

from aas_build import brand
from aas_build.brand import BrandConfig, BrandError

APP_ID = "dev.aiappstudio.p0123456789abcdef"
ANDROID = Path(brand.PRODUCT) / "images" / "android-build"


def _opaque_radius_dp(img: Image.Image, density: float) -> float:
    """Largest distance (dp) from the centre of any pixel with alpha > 8."""
    w, h = img.size
    a = img.getchannel("A").point(lambda v: 255 if v > 8 else 0)
    px = a.load()
    far = 0.0
    for y in range(h):
        for x in range(w):
            if px[x, y]:
                far = max(far, math.hypot(x + 0.5 - w / 2, y + 0.5 - h / 2))
    return far / density


# ---------------------------------------------------------------- glyph set
def test_glyph_set_matches_manifest_and_has_licence() -> None:
    assert brand.verify_glyph_set() == []
    assert len(brand.glyph_names()) >= 20
    assert "ISC License" in (brand.GLYPH_DIR / "LICENSE").read_text(encoding="utf-8")


def test_tampered_glyph_is_refused(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    d = tmp_path / "icons"
    shutil.copytree(brand.GLYPH_DIR, d)
    star = d / "star.svg"
    star.write_text(star.read_text(encoding="utf-8").replace("M", "m", 1), encoding="utf-8")
    monkeypatch.setattr(brand, "GLYPH_DIR", d)
    monkeypatch.setattr(brand, "_GLYPH_CACHE", {})
    assert brand.verify_glyph_set() == ["star.svg: sha256 mismatch"]
    with pytest.raises(BrandError):
        brand.load_glyph("star")


@pytest.mark.parametrize("bad", ["../star", "Star", "nope", "", "star.svg", "a/b"])
def test_glyph_name_is_validated(bad: str) -> None:
    with pytest.raises(BrandError):
        brand.load_glyph(bad)


def test_every_bundled_glyph_renders_inside_the_safe_zone() -> None:
    for name in brand.glyph_names():
        fg = brand.foreground_layer(BrandConfig(glyph=name), 1.0)
        assert fg.size == (108, 108)
        assert fg.getchannel("A").getbbox() is not None, name
        r = _opaque_radius_dp(fg, 1.0)
        assert r <= brand.SAFE_DP / 2, (name, r)  # inside the 66dp circle, so inside the 72dp mask too


# ---------------------------------------------------------------- adaptive / monochrome / legacy
def test_adaptive_foreground_is_108dp_at_every_density_and_centered() -> None:
    cfg = BrandConfig(glyph="house")
    for dpi, d in brand.DENSITIES.items():
        img = brand.foreground_layer(cfg, d)
        assert img.size == (round(108 * d),) * 2, dpi
        x0, _y0, x1, _y1 = img.getchannel("A").getbbox()
        assert abs((x0 + x1) / 2 - img.width / 2) <= 0.12 * img.width
        assert x0 > 0.2 * img.width and x1 < 0.8 * img.width
        assert img.getpixel((0, 0))[3] == 0


def test_monochrome_layer_is_opaque_black_with_same_geometry() -> None:
    cfg = BrandConfig(glyph="heart", background="#FFD400")
    fg, mono = brand.foreground_layer(cfg, 2.0), brand.monochrome_layer(cfg, 2.0)
    assert mono.size == fg.size
    raw = mono.tobytes()
    solid = [raw[i:i + 4] for i in range(0, len(raw), 4) if raw[i + 3] == 255]
    assert solid and all(p[:3] == bytes(3) for p in solid)
    assert mono.getchannel("A").getbbox() == fg.getchannel("A").getbbox()


def test_legacy_icons_have_plate_and_round_variant_is_a_circle() -> None:
    cfg = BrandConfig(glyph="star", background="#2563EB")
    sq, rd = brand.legacy_icon(cfg, 4.0, round_icon=False), brand.legacy_icon(cfg, 4.0, round_icon=True)
    assert sq.size == rd.size == (192, 192)
    assert sq.getpixel((96, 20))[:3] == (0x25, 0x63, 0xEB)
    assert rd.getpixel((30, 30))[3] == 0 and sq.getpixel((30, 30))[3] == 255


def test_foreground_is_picked_for_contrast_and_low_contrast_is_refused() -> None:
    assert BrandConfig(background="#FFD400").colours()[1] == brand.DARK_FOREGROUND
    assert BrandConfig(background="#1E3A8A").colours()[1] == brand.DEFAULT_FOREGROUND
    with pytest.raises(BrandError):
        brand.validate(BrandConfig(background="#FFFFFF", foreground="#F5F5F5"))
    with pytest.raises(BrandError):
        BrandConfig(background="red").colours()


def test_render_is_deterministic() -> None:
    cfg = BrandConfig(glyph="camera")
    assert brand.foreground_layer(cfg, 1.5).tobytes() == brand.foreground_layer(cfg, 1.5).tobytes()


# ---------------------------------------------------------------- generated resources
def test_write_res_manifest_values_and_monochrome(tmp_path: Path) -> None:
    cfg = BrandConfig(glyph="wallet", background="#0F766E")
    files = brand.write_res(cfg, "  Budget <Pro> & Co  ", APP_ID, tmp_path)
    assert "res/mipmap-anydpi-v26/ic_launcher.xml" in files
    assert "res/mipmap-xxxhdpi/ic_launcher_monochrome.png" in files
    for name in ("ic_launcher.xml", "ic_launcher_round.xml"):
        xml = (tmp_path / "res" / "mipmap-anydpi-v26" / name).read_text(encoding="utf-8")
        assert '<monochrome android:drawable="@mipmap/ic_launcher_monochrome"/>' in xml
        assert "@mipmap/ic_launcher_foreground" in xml and "@color/ic_launcher_background" in xml
    vals = {e.get("name"): e.text for e in ET.parse(tmp_path / "res" / "values" / "strings.xml").getroot()}
    assert vals["package_name"] == APP_ID == vals["custom_url_scheme"]
    assert vals["app_name"] == vals["title_activity_main"] == "Budget <Pro> & Co"
    assert "#0F766E" in (tmp_path / "res/values/ic_launcher_background.xml").read_text(encoding="utf-8")
    styles = (tmp_path / "res/values/styles.xml").read_text(encoding="utf-8")
    assert 'parent="Theme.SplashScreen"' in styles and "postSplashScreenTheme" in styles
    assert "windowSplashScreenAnimatedIcon" in styles
    assert Image.open(tmp_path / "res/drawable-nodpi/aas_splash_icon.png").size == (brand.SPLASH_ICON_PX,) * 2


def test_splash_glyph_stays_inside_the_visible_circle() -> None:
    img = brand.splash_icon(BrandConfig(glyph="map-pin"))
    assert _opaque_radius_dp(img, 1.0) <= brand.SPLASH_ICON_PX / 3


@pytest.mark.parametrize(("raw", "want"), [("", "App"), ("  My   App ", "My App"), ("x" * 80, "x" * 30),
                                           ("a‮b\x07c", "abc")])
def test_app_label(raw: str, want: str) -> None:
    assert brand.app_label(raw) == want


def test_write_res_refuses_foreign_application_ids(tmp_path: Path) -> None:
    for bad in ("com.evil.app", "dev.aiappstudio.pXYZ", "dev.aiappstudio.p0123456789abcdef.x"):
        with pytest.raises(BrandError):
            brand.write_res(BrandConfig(), "x", bad, tmp_path)


def test_master_svg_and_png(tmp_path: Path) -> None:
    names = brand.write_master(BrandConfig(glyph="leaf"), tmp_path)
    assert names == ["icon-master.svg", "icon-1024.png", "icon-512.png"]
    svg = (tmp_path / "icon-master.svg").read_text(encoding="utf-8")
    assert svg.startswith("<svg") and 'viewBox="0 0 1024 1024"' in svg and "<path" in svg
    assert Image.open(tmp_path / "icon-1024.png").size == (1024, 1024)


def test_palette_from_project_tokens() -> None:
    css = ":root{ --background:#ffffff; --primary:#0f766e; --primary-foreground:#fefefe; }"
    assert brand.palette_from_css(css) == {"background": "#0F766E", "foreground": "#FEFEFE"}
    assert brand.palette_from_css("body{color:red}") == {}


# ---------------------------------------------------------------- version counter
def test_version_code_increments_only_on_committed_success(tmp_path: Path) -> None:
    store = brand.BrandStore(tmp_path)
    pid = "prj_0123456789abcdef"
    assert store.next_version_code(pid) == 1
    assert store.next_version_code(pid) == 1  # asking never consumes
    store.commit_version_code(pid, 1)
    assert store.next_version_code(pid) == 2
    store.commit_version_code(pid, 2)
    store.commit_version_code(pid, 1)  # never goes backwards
    assert store.last_version_code(pid) == 2 and store.next_version_code(pid) == 3
    store.save_config(pid, BrandConfig(glyph="bell"))  # saving the config keeps the counter
    assert store.last_version_code(pid) == 2 and store.config(pid).glyph == "bell"


def test_semver_version_name_and_brand_dir(tmp_path: Path) -> None:
    cfg = BrandConfig(version_major=2, version_minor=5)
    assert brand.semver(cfg, 1) == "2.5.0" and brand.semver(cfg, 8) == "2.5.7"
    out = brand.write_brand_dir(cfg, "Demo", APP_ID, 8, tmp_path)
    assert json.loads((out / "brand.json").read_text()) == {"versionCode": 8, "versionName": "2.5.7"}
    for bad in (0, -1, 2_100_000_000):
        with pytest.raises(BrandError):
            brand.write_brand_dir(cfg, "Demo", APP_ID, bad, tmp_path / "x")


def test_store_rejects_bad_project_ids(tmp_path: Path) -> None:
    with pytest.raises(BrandError):
        brand.BrandStore(tmp_path).dir("../../etc")


# ---------------------------------------------------------------- 16 KB alignment
def _elf64(align: int) -> bytes:
    hdr = bytearray(64)
    hdr[0:4] = b"\x7fELF"
    hdr[4] = 2
    hdr[5] = 1
    struct.pack_into("<Q", hdr, 0x20, 64)
    struct.pack_into("<HH", hdr, 0x36, 56, 1)
    ph = bytearray(56)
    struct.pack_into("<I", ph, 0, 1)
    struct.pack_into("<Q", ph, 0x30, align)
    return bytes(hdr + ph)


def test_native_alignment_report(tmp_path: Path) -> None:
    def apk(name: str, libs: dict[str, bytes]) -> Path:
        p = tmp_path / name
        with zipfile.ZipFile(p, "w") as z:
            z.writestr("classes.dex", b"x")
            for k, v in libs.items():
                z.writestr(k, v)
        return p
    assert brand.check_native_alignment(apk("a.apk", {}))["status"] == "none"
    assert brand.check_native_alignment(apk("b.apk", {"lib/arm64-v8a/liba.so": _elf64(0x4000)}))["status"] == "ok"
    bad = brand.check_native_alignment(apk("c.apk", {"lib/arm64-v8a/libb.so": _elf64(0x1000)}))
    assert bad["status"] == "fail" and "libb.so" in bad["problems"][0]


# ---------------------------------------------------------------- template wiring
def test_template_pins_splashscreen_and_takes_version_from_properties() -> None:
    gradle = (ANDROID / "template" / "app" / "build.gradle").read_text(encoding="utf-8")
    assert "aasVersionCode" in gradle and "aasVersionName" in gradle and "versionCode 1\n" not in gradle
    assert re.search(r"core-splashscreen:\$coreSplashScreenVersion", gradle)
    meta = (ANDROID / "template" / "gradle" / "verification-metadata.xml").read_text(encoding="utf-8")
    assert 'name="core-splashscreen" version="1.2.0"' in meta
    script = (ANDROID / "aas-build-android").read_text(encoding="utf-8")
    assert "/brand" in script and "--dependency-verification strict" in script and "--offline" in script
    css = (ANDROID / "template" / "aas-safe-area.css").read_text(encoding="utf-8")
    assert "safe-area-inset-top" in css
