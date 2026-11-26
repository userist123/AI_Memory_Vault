"""W5 wiring: every successful android build carries the brand kit (res/, label, versionCode+1, SemVer name); a failed
build does not consume a versionCode; the export copies the framed store screenshots next to the APK."""
from __future__ import annotations

import io
import json
from pathlib import Path
from typing import Any

import pytest
from aas_build import brand, runner, service, store_shots
from PIL import Image

from .test_build_service import DOCKER, IMAGE, make_fake
from .test_s7_android_capture import fake_android, fake_keys, new_project

GOT: list[dict[str, Any]] = []


def spying(fail: str | None = None):  # type: ignore[no-untyped-def]
    inner = fake_android(fail)

    def run(**kw: Any) -> runner.AndroidResult:
        b = Path(kw["brand"])
        GOT.append({"brand": json.loads((b / "brand.json").read_text()),
                    "files": sorted(p.relative_to(b).as_posix() for p in b.rglob("*") if p.is_file()),
                    "strings": (b / "res/values/strings.xml").read_text(encoding="utf-8")})
        return inner(**kw)
    return run


def _build(studio, pid, fail=None):  # type: ignore[no-untyped-def]
    return service.build_android(studio, pid, run_build=spying(fail), ensure_keys=fake_keys, docker=DOCKER, image=IMAGE)


def test_version_code_goes_up_per_successful_build_and_not_on_failure(studio) -> None:  # type: ignore[no-untyped-def]
    GOT.clear()
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    r1 = _build(studio, pid)
    assert (r1["versionCode"], r1["versionName"]) == (1, "1.0.0")
    with pytest.raises(Exception, match="boom"):
        _build(studio, pid, fail="boom")
    r2 = _build(studio, pid)  # the failed build did not consume code 2 ... it was offered again
    r3 = _build(studio, pid)
    assert [GOT[i]["brand"]["versionCode"] for i in (0, 1, 2, 3)] == [1, 2, 2, 3]
    assert (r2["versionCode"], r3["versionCode"], r3["versionName"]) == (2, 3, "1.0.2")
    assert r3["alignment16k"]["status"] == "none"  # the fake APK has no native libs
    assert "res/mipmap-anydpi-v26/ic_launcher.xml" in GOT[0]["files"] and "res/drawable-nodpi/aas_splash_icon.png" in GOT[0]["files"]
    assert "res/mipmap-xxxhdpi/ic_launcher_monochrome.png" in GOT[0]["files"]
    assert 'name="app_name">p1<' in GOT[0]["strings"]  # label comes from the project name
    assert 'name="package_name">dev.aiappstudio.p' + pid[4:] in GOT[0]["strings"]  # applicationId unchanged


def test_saved_brand_is_used_and_counter_survives_config_changes(studio) -> None:  # type: ignore[no-untyped-def]
    GOT.clear()
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    _build(studio, pid)
    store = brand.BrandStore(studio.settings.data_dir)
    store.save_config(pid, brand.BrandConfig(glyph="leaf", background="#166534", version_major=3, version_minor=1))
    r = _build(studio, pid)
    assert (r["versionCode"], r["versionName"]) == (2, "3.1.1")
    assert GOT[1]["brand"] == {"versionCode": 2, "versionName": "3.1.1"}


def _png(w: int, h: int) -> bytes:
    b = io.BytesIO()
    Image.new("RGB", (w, h), (240, 240, 250)).save(b, format="PNG")
    return b.getvalue()


def test_store_shots_compose_and_export_next_to_the_apk(studio, tmp_path: Path) -> None:  # type: ignore[no-untyped-def]
    cfg = brand.BrandConfig(glyph="star", background="#1E3A8A", caption="Plan your day in seconds")
    phone = Image.open(io.BytesIO(store_shots.compose(_png(390, 844), "phone", cfg)))
    tablet = Image.open(io.BytesIO(store_shots.compose(_png(834, 1112), "tablet", cfg)))
    assert phone.size == (1080, 1920) and tablet.size == (1600, 2560)
    assert phone.getpixel((5, 5)) == (0x1E, 0x3A, 0x8A)  # brand colour behind the device
    assert phone.getpixel((540, 900)) == (240, 240, 250)  # the screenshot is inside the frame
    assert len(phone.crop((100, 100, 980, 330)).getcolors(100_000)) > 1  # the caption is drawn in the top band
    for bad in (b"GIF89a", b"", b"\x89PNG\r\n\x1a\nxxxx"):
        with pytest.raises(store_shots.ShotError):
            store_shots.compose(bad, "phone", cfg)
    with pytest.raises(store_shots.ShotError):
        store_shots.compose(_png(10, 10), "desktop", cfg)

    pid = new_project(studio)
    store = brand.BrandStore(studio.settings.data_dir)
    d = store.shots_dir(pid)
    d.mkdir(parents=True)
    (d / "store-phone.png").write_bytes(store_shots.compose(_png(390, 844), "phone", cfg))
    (d / "store-evil.png").write_bytes(b"not a png")
    out = tmp_path / "exp"
    out.mkdir()
    assert store_shots.copy_for_export(studio.settings, pid, out) == ["store-phone.png"]
    assert sorted(p.name for p in (out / "store-screenshots").iterdir()) == ["store-phone.png"]


# ---------------------------------------------------------------- REST (owner)
def test_rest_brand_get_put_roundtrip_and_validation(tmp_path: Path) -> None:
    from apps.backend.main import create_app
    from starlette.testclient import TestClient
    from studio_core import project_kind
    from studio_core.config import Settings
    auth = {"Authorization": "Bearer " + "t" * 40}
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    try:
        with TestClient(app, base_url="http://127.0.0.1:17875") as c:
            pid = c.post("/api/v1/projects", json={"name": "Notes"}, headers=auth).json()["id"]
            assert c.get(f"/api/v1/projects/{pid}/brand").status_code in (401, 403)  # owner token required
            g = c.get(f"/api/v1/projects/{pid}/brand", headers=auth)
            assert g.status_code == 200
            body = g.json()
            assert body["appLabel"] == "Notes" and body["nextVersionCode"] == 1 and len(body["glyphs"]) >= 20
            assert body["previewPng"]  # base64 preview sheet
            ok = c.put(f"/api/v1/projects/{pid}/brand", headers=auth,
                       json={"glyph": "leaf", "background": "#166534", "caption": "Green", "versionMajor": 2, "versionMinor": 1})
            assert ok.status_code == 200 and ok.json()["kit"]["glyph"] == "leaf" and ok.json()["nextVersionName"] == "2.1.0"
            for bad in ({"glyph": "../x", "background": "#166534"}, {"glyph": "leaf", "background": "green"},
                        {"glyph": "nope", "background": "#166534"}, {"glyph": "leaf", "background": "#FFFFFF", "foreground": "#F5F5F5"},
                        {"glyph": "leaf", "background": "#166534", "extra": 1}):
                assert c.put(f"/api/v1/projects/{pid}/brand", headers=auth, json=bad).status_code in (400, 422), bad
            assert c.get(f"/api/v1/projects/{pid}/brand", headers=auth).json()["kit"]["glyph"] == "leaf"  # bad PUTs changed nothing
            assert c.get("/api/v1/projects/prj_zzzzzzzzzzzzzzzz/brand", headers=auth).status_code in (400, 404, 422)
    finally:
        project_kind.unregister("app")
