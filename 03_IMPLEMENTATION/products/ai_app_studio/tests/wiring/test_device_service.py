"""MCP device tools -> S5 apps/device_host launcher API, behind an owner setting, disabled on low disk / failed preflight."""
from __future__ import annotations

import hashlib
from pathlib import Path
from typing import Any

import pytest
from aas_device import service as dsvc
from apps.device_host.launcher import LauncherRefused
from studio_core import activity
from studio_core.errors import PolicyDenied, Unavailable
from studio_core.policy import Actor

from .conftest import OWNER

GIB = 1024 ** 3
PNG = b"\x89PNG\r\n\x1a\n" + b"fake"


class FakeSession:
    instances: list[FakeSession] = []

    def __init__(self, form: str) -> None:
        self.form, self.calls = form, []
        FakeSession.instances.append(self)

    def start(self) -> dict[str, Any]:
        self.calls.append(("start",))
        return {"kind": "booted"}

    def tap(self, x: int, y: int) -> dict[str, Any]:
        self.calls.append(("tap", x, y))
        return {"ok": True}

    def screenshot(self) -> bytes:
        return PNG

    def install(self, apk: Path) -> dict[str, Any]:
        self.calls.append(("install", Path(apk).name))
        return {"ok": True}

    def shutdown(self) -> int:
        self.calls.append(("shutdown",))
        return 0


@pytest.fixture(autouse=True)
def _reset() -> None:
    FakeSession.instances.clear()


def svc(studio, *, free: int = 20 * GIB, preflight=None):  # type: ignore[no-untyped-def]
    return dsvc.DeviceService(studio, preflight=preflight or (lambda: {"networkingMode": "nat"}),
                              free_bytes=lambda: free, session_factory=FakeSession)


def pid_of(studio) -> str:  # type: ignore[no-untyped-def]
    return studio.create_project(OWNER, "p1", "blank", "")["id"]


def test_disabled_by_default_and_no_session_is_created(studio) -> None:  # type: ignore[no-untyped-def]
    with pytest.raises(Unavailable, match="disabled"):
        svc(studio).start("phone")
    assert FakeSession.instances == [] and dsvc.is_enabled(studio) is False


def test_setting_is_owner_only(studio) -> None:  # type: ignore[no-untyped-def]
    for a in (Actor("agent", "x", "mcp"), Actor("human", "owner", "mcp")):
        with pytest.raises(PolicyDenied):
            dsvc.set_enabled(studio, a, True)
    assert dsvc.set_enabled(studio, OWNER, True) == {"enabled": True} and dsvc.is_enabled(studio) is True


def test_low_disk_disables_the_tools(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    with pytest.raises(Unavailable, match="8 GiB"):
        svc(studio, free=7 * GIB).start("phone")
    assert FakeSession.instances == []
    svc(studio, free=8 * GIB).start("phone")  # control: exactly the floor is accepted
    assert len(FakeSession.instances) == 1


def test_failed_wsl_preflight_disables_the_tools(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)

    def bad() -> dict[str, Any]:
        raise LauncherRefused("WSL networking mode must be nat")

    with pytest.raises(Unavailable, match="preflight"):
        svc(studio, preflight=bad).start("phone")
    assert FakeSession.instances == []


def test_start_tap_screenshot_stop_record_device_events(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    s = svc(studio)
    assert s.start("phone")["state"] == "running"
    assert s.tap(100, 200) == {"ok": True}
    shot = s.screenshot()
    assert shot["sha256"] == hashlib.sha256(PNG).hexdigest() and shot["size"] == len(PNG)
    saved = studio.settings.data_dir / "device" / "shots" / f"{shot['sha256']}.png"
    assert saved.read_bytes() == PNG
    s.stop()
    ev = activity.list_events(studio, limit=100)
    kinds = [e["kind"] for e in ev]
    # device.state: starting, running, stopped
    assert kinds.count("device.state") == 3 and "device.input" in kinds and "device.log" in kinds
    tap = next(e for e in ev if e["kind"] == "device.input")["payload"]
    assert (tap["x"], tap["y"], tap["action"]) == (100, 200, "tap")
    assert FakeSession.instances[0].calls == [("start",), ("tap", 100, 200), ("shutdown",)]


@pytest.mark.parametrize("x,y", [(-1, 5), (5, -1), (10_001, 5), (1.5, 2), ("1", 2), (True, 2)])
def test_tap_coordinates_are_validated(studio, x: Any, y: Any) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    s = svc(studio)
    s.start("phone")
    with pytest.raises(Exception, match="coordinate"):
        s.tap(x, y)


def test_tap_without_a_started_session_is_refused(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    with pytest.raises(Unavailable, match="not started"):
        svc(studio).tap(1, 1)


def test_install_needs_a_built_apk_of_that_project(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    pid = pid_of(studio)
    s = svc(studio)
    s.start("phone")
    with pytest.raises(Unavailable, match="no APK"):
        s.install(pid)
    apk = studio.settings.data_dir / "preview" / pid / "app-debug.apk"
    apk.parent.mkdir(parents=True)
    apk.write_bytes(b"PK\x03\x04apk")
    _record_build(studio, pid, b"PK\x03\x04apk")
    assert s.install(pid) == {"ok": True}
    assert FakeSession.instances[0].calls[-1] == ("install", "app-debug.apk")
    with pytest.raises(Exception):  # noqa: B017 - invalid project id never reaches a path
        s.install("../../evil")


def _record_build(studio, pid: str, data: bytes) -> None:  # type: ignore[no-untyped-def]
    sha = hashlib.sha256(data).hexdigest()
    activity.record(studio, "build.finished", project_id=pid, run_id="run_x", source="build", artifact_sha256=sha,
                    payload={"target": "android", "status": "ok", "artifactSha256": sha})


def test_install_refuses_an_apk_replaced_after_the_build(studio) -> None:  # type: ignore[no-untyped-def]
    """S7 review M1: the bytes installed must be the bytes the build recorded (sha256 in build.finished)."""
    dsvc.set_enabled(studio, OWNER, True)
    pid = pid_of(studio)
    s = svc(studio)
    s.start("phone")
    apk = studio.settings.data_dir / "preview" / pid / "app-debug.apk"
    apk.parent.mkdir(parents=True)
    apk.write_bytes(b"PK\x03\x04good")
    _record_build(studio, pid, b"PK\x03\x04good")
    assert s.install(pid) == {"ok": True}
    apk.write_bytes(b"PK\x03\x04EVIL")  # replaced after the build
    n = len([c for c in FakeSession.instances[0].calls if c[0] == "install"])
    with pytest.raises(Unavailable, match="sha256"):
        s.install(pid)
    assert len([c for c in FakeSession.instances[0].calls if c[0] == "install"]) == n
    evs = activity.list_events(studio)
    assert any(e["kind"] == "device.log" and "refused" in e["payload"].get("text", "") for e in evs)


def test_install_refuses_an_apk_without_a_recorded_build(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    pid = pid_of(studio)
    s = svc(studio)
    s.start("phone")
    apk = studio.settings.data_dir / "preview" / pid / "app-debug.apk"
    apk.parent.mkdir(parents=True)
    apk.write_bytes(b"PK\x03\x04good")
    with pytest.raises(Unavailable, match="recorded build"):
        s.install(pid)


def test_install_uses_a_private_copy_not_the_original_path(studio) -> None:  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    pid = pid_of(studio)
    s = svc(studio)
    s.start("phone")
    apk = studio.settings.data_dir / "preview" / pid / "app-debug.apk"
    apk.parent.mkdir(parents=True)
    apk.write_bytes(b"PK\x03\x04good")
    _record_build(studio, pid, b"PK\x03\x04good")
    seen: list[Path] = []
    orig = FakeSession.install
    FakeSession.install = lambda self, p: (seen.append(Path(p)), orig(self, p))[1]  # type: ignore[method-assign]
    try:
        s.install(pid)
    finally:
        FakeSession.install = orig  # type: ignore[method-assign]
    assert seen and seen[0] != apk and not seen[0].exists()  # staged copy, removed afterwards
