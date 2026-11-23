"""S7 wiring: app_build {target: android} -> runner -> APK artifact + events -> install/launch on the studio emulator;
preview_capture -> Playwright PNGs under the data root -> preview.captured events -> authenticated REST by sha256."""
from __future__ import annotations

import hashlib
import http.server
import os
import threading
import zipfile
from pathlib import Path
from typing import Any

import pytest
from aas_build import capture, runner, service
from aas_build.preview_server import PreviewServer
from aas_device import service as dsvc
from studio_core import activity
from studio_core.errors import Unavailable

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, make_fake

SIGNER = "5" * 64


def new_project(studio) -> str:  # type: ignore[no-untyped-def]
    return studio.create_project(OWNER, "p1", "blank", "")["id"]


def fake_keys(**kw: Any) -> tuple[Path, str]:
    d: Path = kw["keys_dir"]
    d.mkdir(parents=True, exist_ok=True)
    (d / "aas-debug.keystore").write_bytes(b"ks")
    return d / "aas-debug.keystore", SIGNER


SEEN: dict[str, Any] = {}


def fake_android(fail: str | None = None):  # type: ignore[no-untyped-def]
    def run(**kw: Any) -> runner.AndroidResult:
        SEEN.update(kw)
        SEEN["dist_files"] = sorted(p.name for p in Path(kw["dist"]).iterdir())
        if fail:
            raise runner.BuildError(fail)
        with zipfile.ZipFile(kw["dest"], "w") as z:
            z.writestr("assets/public/index.html", "x")
        data = Path(kw["dest"]).read_bytes()
        return runner.AndroidResult(apk=kw["dest"], sha256=hashlib.sha256(data).hexdigest(), size=len(data),
                                    signer_sha256=kw["expected_signer"], log_tail="BUILD SUCCESSFUL in 30s\nAAS-SIGNER-SHA256=x")
    return run


def events(studio, kind: str) -> list[dict[str, Any]]:  # type: ignore[no-untyped-def]
    return [e for e in activity.list_events(studio, limit=500) if e["kind"] == kind]


def test_android_build_needs_a_web_build_first(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    with pytest.raises(Unavailable, match="web target first"):
        service.build_android(studio, pid, run_build=fake_android(), ensure_keys=fake_keys, docker=DOCKER, image=IMAGE)
    assert not events(studio, "build.started")


def test_android_build_records_artifact_and_uses_the_studio_package(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    res = service.build_android(studio, pid, run_id="run_0000000000000001", run_build=fake_android(),
                                ensure_keys=fake_keys, docker=DOCKER, image=IMAGE)
    apk = service.apk_path(studio.settings, pid)
    assert apk.is_file() and res["apkSha256"] == hashlib.sha256(apk.read_bytes()).hexdigest()
    assert res["packageId"] == "dev.aiappstudio.p" + pid[4:] == SEEN["app_id"]
    assert SEEN["expected_signer"] == SIGNER and SEEN["keystore"].parent == studio.settings.data_dir / "keys"
    assert SEEN["dist_files"] == ["index.html"]  # the current web build is the APK's input
    assert SEEN["image"] == IMAGE and SEEN["docker"] == DOCKER
    started = [e for e in events(studio, "build.started") if e["payload"].get("target") == "android"]
    fin = [e for e in events(studio, "build.finished") if e["payload"].get("target") == "android"]
    assert len(started) == 1 and len(fin) == 1
    assert fin[0]["payload"]["artifactSha256"] == res["apkSha256"] == fin[0]["artifactSha256"]
    assert fin[0]["payload"]["sizeBytes"] == res["sizeBytes"] and fin[0]["payload"]["signerSha256"] == SIGNER
    assert any("BUILD SUCCESSFUL" in e["payload"].get("line", "") for e in events(studio, "build.log"))


def test_failed_android_build_keeps_the_previous_apk(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    service.build_android(studio, pid, run_build=fake_android(), ensure_keys=fake_keys, docker=DOCKER, image=IMAGE)
    before = service.apk_path(studio.settings, pid).read_bytes()
    with pytest.raises(service.BuildFailed):
        service.build_android(studio, pid, run_build=fake_android("Dependency verification failed"),
                              ensure_keys=fake_keys, docker=DOCKER, image=IMAGE)
    assert service.apk_path(studio.settings, pid).read_bytes() == before
    fin = [e for e in events(studio, "build.finished") if e["payload"].get("target") == "android"]
    assert fin[-1]["payload"]["status"] == "failed"
    assert any("verification" in e["payload"].get("message", "") for e in events(studio, "build.error"))


# ---------------------------------------------------------------- install + launch on the studio emulator
class FakeSession:
    def __init__(self, form: str, pkg: str | None = None) -> None:
        self.calls: list[tuple[Any, ...]] = []
        self.pkg = pkg

    def start(self) -> dict[str, Any]:
        return {"kind": "booted"}

    def install(self, apk: Path) -> dict[str, Any]:
        self.calls.append(("install", Path(apk).name))
        return {"ok": True, "phase": "done", "pkg": self.pkg}

    def launch(self, pkg: str) -> dict[str, Any]:
        self.calls.append(("launch", pkg))
        return {"ok": True}

    def shutdown(self) -> int:
        return 0


def _device(studio, pkg: str | None):  # type: ignore[no-untyped-def]
    holder: dict[str, FakeSession] = {}

    def factory(form: str) -> FakeSession:
        holder["s"] = FakeSession(form, pkg)
        return holder["s"]
    d = dsvc.DeviceService(studio, preflight=lambda: {}, free_bytes=lambda: 20 * 1024 ** 3, session_factory=factory)
    return d, holder


def test_install_and_launch_only_the_projects_package(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    pid = new_project(studio)
    apk = service.apk_path(studio.settings, pid)
    apk.parent.mkdir(parents=True)
    apk.write_bytes(b"PK\x03\x04apk")
    sha = hashlib.sha256(b"PK\x03\x04apk").hexdigest()
    activity.record(studio, "build.finished", project_id=pid, run_id="run_x", source="build", artifact_sha256=sha,
                    payload={"target": "android", "status": "ok", "artifactSha256": sha})
    d, h = _device(studio, "dev.aiappstudio.p" + pid[4:])
    d.start("phone")
    res = d.install(pid, launch=True)
    assert res["packageId"] == "dev.aiappstudio.p" + pid[4:] and res["launch"] == {"ok": True}
    assert h["s"].calls == [("install", "app-debug.apk"), ("launch", "dev.aiappstudio.p" + pid[4:])]
    texts = [e["payload"]["text"] for e in events(studio, "device.log")]
    assert any(t.startswith("launch dev.aiappstudio.p") for t in texts)
    d.stop()
    # a different package reported by the device host is never launched
    d2, h2 = _device(studio, "dev.aiappstudio.p" + "f" * 16)
    d2.start("phone")
    res2 = d2.install(pid, launch=True)
    assert res2["launch"] is None and ("launch", "dev.aiappstudio.p" + "f" * 16) not in h2["s"].calls
    d2.stop()


def test_install_is_refused_when_the_owner_switched_devices_off(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    pid = new_project(studio)
    d, _ = _device(studio, "dev.aiappstudio.p" + pid[4:])
    d.start("phone")
    dsvc.set_enabled(studio, OWNER, False)
    with pytest.raises(Unavailable):
        d.install(pid, launch=True)
    assert d.session is None  # switched off -> the device is stopped


# ---------------------------------------------------------------- preview_capture
def test_capture_needs_a_built_site_and_validates_frames(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    with pytest.raises(Unavailable):
        capture.capture(studio, pid, ["phone"], shoot=lambda u, f: {})
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    for bad in (["watch"], ["phone", "phone"]):
        with pytest.raises(Exception):  # noqa: B017
            capture.capture(studio, pid, bad, shoot=lambda u, f: {})


def test_capture_stores_pngs_and_emits_events_with_sha(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    png = {f: b"\x89PNG\r\n\x1a\n" + f.encode() for f in capture.FRAMES}
    seen: dict[str, Any] = {}

    def shoot(url: str, frames: list[str]) -> dict[str, bytes]:
        seen["url"], seen["frames"] = url, frames
        return {f: png[f] for f in frames}
    res = capture.capture(studio, pid, None, run_id="run_0000000000000002", shoot=shoot)
    assert seen["url"] == f"http://p-{pid[4:]}.localhost:{studio.settings.preview_port}/"
    assert [c["frame"] for c in res["captures"]] == ["phone", "tablet", "desktop"]
    ev = [e for e in events(studio, "preview.captured") if e["source"] == "build" and e["runId"] == "run_0000000000000002"]
    assert len(ev) == 3
    for c, e in zip(res["captures"], ev, strict=True):
        assert c["sha256"] == hashlib.sha256(png[c["frame"]]).hexdigest() == e["payload"]["sha256"] == e["artifactSha256"]
        assert capture.capture_file(studio.settings, c["sha256"]).read_bytes() == png[c["frame"]]  # type: ignore[union-attr]
    assert capture.capture_file(studio.settings, "../x") is None


def test_capture_rejects_non_png(studio) -> None:  # type: ignore[no-untyped-def]
    pid = new_project(studio)
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    with pytest.raises(Unavailable):
        capture.capture(studio, pid, ["phone"], shoot=lambda u, f: {"phone": b"<html>"})


def _chromium_available() -> bool:
    """Installed Playwright Chromium (checked on disk: sync Playwright cannot run while pytest-asyncio owns a loop)."""
    root = os.environ.get("PLAYWRIGHT_BROWSERS_PATH") or os.path.join(os.environ.get("LOCALAPPDATA", ""), "ms-playwright")
    return any(Path(root).glob("chromium*"))


@pytest.mark.browser
@pytest.mark.skipif(not _chromium_available(), reason="Playwright Chromium not installed")
def test_real_capture_of_the_preview_origin_blocks_other_origins(studio) -> None:  # type: ignore[no-untyped-def]
    hits: list[str] = []

    class Canary(http.server.BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802
            hits.append(self.path)
            self.send_response(200)
            self.end_headers()

        def log_message(self, *a: Any) -> None:
            pass

    canary = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Canary)
    threading.Thread(target=canary.serve_forever, daemon=True).start()
    cport = canary.server_address[1]
    pid = new_project(studio)
    page = (f"<!doctype html><title>cap</title><body style='background:#123456'><h1>Hello capture</h1>"
            f"<img src='http://127.0.0.1:{cport}/img'><img src='http://example.invalid/x.png'></body>")

    def fake(*, dest: Path, **kw: Any):  # type: ignore[no-untyped-def]
        dest.mkdir(parents=True)
        (dest / "index.html").write_text(page, encoding="utf-8")
        from aas_build.runner import BuildResult
        return BuildResult(dest=dest, files=["index.html"], total_bytes=len(page), dist_sha256="0" * 64)
    service.build_web(studio, pid, run_build=fake, docker=DOCKER, image=IMAGE)
    srv = PreviewServer(service.site_root_for(studio.settings), port=0).start()
    try:
        url = f"http://p-{pid[4:]}.localhost:{srv.port}/"
        res = capture.capture(studio, pid, ["phone", "desktop"], url=url)
    finally:
        srv.stop()
    from PIL import Image
    sizes = {}
    for c in res["captures"]:
        p = capture.capture_file(studio.settings, c["sha256"])
        assert p is not None
        with Image.open(p) as im:
            sizes[c["frame"]] = im.size
            px = im.getpixel((5, im.size[1] - 5))
        assert px[:3] == (0x12, 0x34, 0x56)  # the page really rendered
    assert sizes == {"phone": (390, 844), "desktop": (1440, 900)}
    assert hits == []  # the other-origin request never left the browser
    import urllib.request
    urllib.request.urlopen(f"http://127.0.0.1:{cport}/direct", timeout=5).read()  # noqa: S310 - [+] canary works
    canary.shutdown()
    assert hits == ["/direct"]
