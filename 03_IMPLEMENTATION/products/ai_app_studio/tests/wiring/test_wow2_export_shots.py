"""wow2 #8: the OWNER Export APK path generates the store screenshots (when the brand kit enables them) from the preview
before approval, and they are part of the approval hash; plus #9 the APK label rule."""
from __future__ import annotations

import io
from pathlib import Path
from typing import Any

import pytest
from aas_build import brand, export, store_shots
from PIL import Image
from studio_core.errors import ApprovalRequired

from .conftest import OWNER
from .test_s9b_export import apk_bytes, approve, built, record_apk


def _png(tag: int) -> bytes:
    b = io.BytesIO()
    Image.new("RGB", (390, 844), (200 + tag, 220, 250)).save(b, format="PNG")
    return b.getvalue()


class Shooter:
    def __init__(self) -> None:
        self.calls = 0

    def __call__(self, url: str, frames: list[str]) -> dict[str, bytes]:
        self.calls += 1
        sizes = {"phone": (390, 844), "tablet": (834, 1112)}
        out = {}
        for f in frames:
            b = io.BytesIO()
            Image.new("RGB", sizes[f], (200 + self.calls, 220, 250)).save(b, format="PNG")
            out[f] = b.getvalue()
        return out


def _enable(studio: Any, pid: str, on: bool = True) -> None:
    store = brand.BrandStore(studio.settings.data_dir)
    store.save_config(pid, brand.BrandConfig(glyph="leaf", background="#166534", caption="Plan fast", store_screenshots=on))


def test_brand_kit_has_the_store_screenshots_switch() -> None:
    assert brand.BrandConfig().store_screenshots is False
    cfg = brand.BrandConfig(store_screenshots=True)
    assert cfg.to_json()["storeScreenshots"] is True
    assert brand.BrandConfig.from_json(cfg.to_json()).store_screenshots is True
    assert brand.BrandConfig.from_json({"glyph": "star"}).store_screenshots is False  # older saved kits


def test_owner_export_generates_store_screenshots_before_approval_and_binds_them(studio, tmp_path: Path, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    record_apk(studio, pid, apk_bytes())
    _enable(studio, pid)
    shooter = Shooter()
    monkeypatch.setattr(export, "STORE_SHOOT", shooter)
    with pytest.raises(ApprovalRequired) as ei:  # step 1: the request carries the generated screenshots
        export.export(studio, OWNER, pid, "android", dest_root=tmp_path)
    assert shooter.calls == 1
    d = brand.BrandStore(studio.settings.data_dir).shots_dir(pid)
    assert sorted(p.name for p in d.glob("store-*.png")) == ["store-phone.png", "store-tablet.png"]
    art = export.prepare(studio, pid, "android")
    assert set(art.params["storeShots"]) == {"store-phone.png", "store-tablet.png", "caption"}  # in the approval hash
    assert shooter.calls == 1  # same revision and caption: not regenerated, so the approved hash stays valid
    approve(studio, ei.value.details["approvalId"])
    res = export.export(studio, OWNER, pid, "android", approval_id=ei.value.details["approvalId"], dest_root=tmp_path)
    assert sorted(res["storeScreenshots"]) == ["store-phone.png", "store-tablet.png"]
    assert sorted(p.name for p in tmp_path.rglob("store-*.png") if "data" not in p.relative_to(tmp_path).parts) == ["store-phone.png", "store-tablet.png"]


def test_disabled_kit_generates_nothing(studio, tmp_path: Path, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    record_apk(studio, pid, apk_bytes())
    _enable(studio, pid, on=False)
    shooter = Shooter()
    monkeypatch.setattr(export, "STORE_SHOOT", shooter)
    art = export.prepare(studio, pid, "android")
    assert shooter.calls == 0 and not art.params.get("storeShots")


def test_changed_caption_regenerates(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    record_apk(studio, pid, apk_bytes())
    _enable(studio, pid)
    shooter = Shooter()
    monkeypatch.setattr(export, "STORE_SHOOT", shooter)
    a = export.prepare(studio, pid, "android").params["storeShots"]
    store = brand.BrandStore(studio.settings.data_dir)
    store.save_config(pid, brand.BrandConfig(glyph="leaf", background="#166534", caption="Other words", store_screenshots=True))
    b = export.prepare(studio, pid, "android").params["storeShots"]
    assert shooter.calls == 2 and a != b


def test_capture_failure_does_not_block_the_export(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    pid = built(studio)
    record_apk(studio, pid, apk_bytes())
    _enable(studio, pid)

    def boom(url: str, frames: list[str]) -> dict[str, bytes]:
        raise RuntimeError("no chromium")

    monkeypatch.setattr(export, "STORE_SHOOT", boom)
    art = export.prepare(studio, pid, "android")
    assert not art.params.get("storeShots")
