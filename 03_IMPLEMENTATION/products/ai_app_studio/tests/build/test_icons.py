"""D1: the desktop wrapper icons are generated from the committed SVG (no PNG/ICO in git)."""

from __future__ import annotations

import io
import subprocess
from pathlib import Path

import pytest
from PIL import Image

from aas_build import desktop, icons
from aas_build.icons import IconError

SVG = desktop.TEMPLATE / desktop.ICON_SOURCE


def test_write_icons_produces_the_files_the_wrapper_config_lists(tmp_path: Path) -> None:
    names = icons.write_icons(SVG, tmp_path / "icons")
    assert sorted(names) == ["128x128.png", "32x32.png", "icon.ico"]
    for f in desktop.ICON_FILES:
        assert (tmp_path / f).is_file()
    assert Image.open(tmp_path / "icons" / "32x32.png").size == (32, 32)
    assert Image.open(tmp_path / "icons" / "128x128.png").size == (128, 128)
    ico = Image.open(io.BytesIO((tmp_path / "icons" / "icon.ico").read_bytes()))
    assert ico.format == "ICO" and (256, 256) in ico.info["sizes"] and (32, 32) in ico.info["sizes"]


def test_render_is_deterministic_and_has_the_expected_geometry(tmp_path: Path) -> None:
    a, b = icons.render_svg(SVG, 128), icons.render_svg(SVG, 128)
    assert a.tobytes() == b.tobytes()
    assert a.getpixel((2, 2))[3] == 0  # transparent corner outside the rounded square
    assert a.getpixel((20, 64))[:3] == (0x15, 0x18, 0x1D)  # dark background
    assert a.getpixel((50, 44))[:3] == (255, 255, 255)  # first white bar
    top, bottom = a.getpixel((36, 34)), a.getpixel((36, 92))
    assert top[:3] != bottom[:3] and top[2] > 240 and bottom[0] > top[0]  # vertical gradient


def test_generated_template_files_are_not_tracked() -> None:
    root = desktop.PRODUCT
    out = subprocess.run(["git", "ls-files", "--", "images/desktop-wrapper/icons", "apps/desktop/src-tauri/icons"],
                         cwd=root, capture_output=True, text=True, check=True).stdout
    assert out.strip() == ""


@pytest.mark.parametrize("svg", [
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle r="3"/></svg>',
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 20"><rect width="1" height="1" fill="#000000"/></svg>',
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="1" height="1" fill="red"/></svg>',
])
def test_unsupported_svg_is_refused(tmp_path: Path, svg: str) -> None:
    p = tmp_path / "x.svg"
    p.write_text(svg, encoding="utf-8")
    with pytest.raises(IconError):
        icons.render_svg(p, 32)


def test_materialise_writes_generated_icons(tmp_path: Path) -> None:
    desktop._copy_template(tmp_path / "wrapper")  # noqa: SLF001
    for f in desktop.ICON_FILES:
        assert (tmp_path / "wrapper" / f).stat().st_size > 0
