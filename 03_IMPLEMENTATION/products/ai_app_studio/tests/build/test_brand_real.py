"""W5 real probe: one offline, strict Gradle build of the brand kit, then install-free inspection with the image's own
aapt/zipalign (badging: label, versionCode/versionName, applicationId; adaptive icon with monochrome; splash theme
attributes; edge-to-edge patch in the packaged index.html; 16 KB zip alignment). Needs the rebuilt image:
  docker build -f images/android-build/Dockerfile --build-context aas=images/android-build -t aas-android-build:dev kit
"""
from __future__ import annotations

import shutil
import subprocess
import uuid
import zipfile
from pathlib import Path

import pytest
from aas_build import brand, runner

DOCKER = shutil.which("docker") or ""
APP_ID = "dev.aiappstudio.p0123456789abcdef"


def _image() -> str | None:
    if not DOCKER:
        return None
    r = subprocess.run([DOCKER, "image", "inspect", runner.ANDROID_TAG, "--format", "{{.Id}}"],  # noqa: S603
                       capture_output=True, text=True, timeout=60)
    if r.returncode != 0 or not r.stdout.strip().startswith("sha256:"):
        return None
    probe = subprocess.run([DOCKER, "run", "--rm", "--network", "none", "--entrypoint", "test", runner.ANDROID_TAG,  # noqa: S603
                            "-f", "/opt/aas/shell_patch.py"], capture_output=True, timeout=120)
    return r.stdout.strip() if probe.returncode == 0 else None


IMAGE = _image()
pytestmark = pytest.mark.skipif(IMAGE is None, reason="aas-android-build image with the W5 brand support is not built")


def _in_image(apk: Path, script: str) -> str:
    r = subprocess.run([DOCKER, "run", "--rm", "--network", "none", "--entrypoint", "sh",  # noqa: S603
                        "-v", f"{apk}:/a.apk:ro", runner.ANDROID_TAG, "-c", script],
                       capture_output=True, text=True, timeout=300)
    assert r.returncode == 0, r.stderr[-400:]
    return r.stdout


def test_real_brand_build_badging_icon_splash_insets_and_alignment(tmp_path: Path) -> None:
    keys = tmp_path / "keys"
    ks, signer = runner.ensure_keystore(docker=DOCKER, image=IMAGE, keys_dir=keys)
    dist = tmp_path / "dist"
    dist.mkdir()
    (dist / "index.html").write_text('<!doctype html><html><head><meta charset="utf-8">'
                                     '<meta name="viewport" content="width=device-width, initial-scale=1">'
                                     "<title>Budget Pro</title></head><body>x</body></html>", encoding="utf-8")
    work = tmp_path / ("w" + uuid.uuid4().hex[:8])
    work.mkdir()
    cfg = brand.BrandConfig(glyph="wallet", background="#0F766E")
    bd = brand.write_brand_dir(cfg, "Budget Pro", APP_ID, 7, work / "brand")
    out = work / "out"
    out.mkdir()
    dest = tmp_path / "app-debug.apk"
    res = runner.run_android_build(docker=DOCKER, build_id=work.name, dist=dist, keystore=ks, expected_signer=signer,
                                   out=out, image=IMAGE, app_id=APP_ID, dest=dest, brand=bd)
    assert res.signer_sha256 == signer
    badging = _in_image(dest, "aapt dump badging /a.apk")
    assert f"package: name='{APP_ID}' versionCode='7' versionName='1.0.6'" in badging
    assert "application-label:'Budget Pro'" in badging and "sdkVersion:'24'" in badging
    assert "application-icon-640:'res/mipmap-anydpi-v26/ic_launcher.xml'" in badging
    tree = _in_image(dest, "aapt dump xmltree /a.apk res/mipmap-anydpi-v26/ic_launcher.xml")
    assert "E: monochrome" in tree and "E: foreground" in tree and "E: background" in tree
    assert "attr/windowSplashScreenAnimatedIcon" in _in_image(dest, "aapt dump resources /a.apk | grep -i splash")
    with zipfile.ZipFile(dest) as z:
        names = z.namelist()
        html = z.read("assets/public/index.html").decode("utf-8")
    assert "viewport-fit=cover" in html and 'id="aas-safe-area"' in html and "safe-area-inset-top" in html
    assert not any(n.endswith("/splash.png") for n in names)  # the Capacitor placeholder splash is gone
    assert any(n.endswith("ic_launcher_monochrome.png") for n in names)
    _in_image(dest, "zipalign -c -P 16 4 /a.apk")  # exit 0 = page-aligned for 16 KB devices
    assert brand.check_native_alignment(dest)["status"] in ("none", "ok")
