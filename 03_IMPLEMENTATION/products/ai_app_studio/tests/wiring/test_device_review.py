"""S6 review item 7: the device gates are re-checked on every tap/screenshot/install (owner setting + free disk), a
failed re-check stops the device; the device is stopped when its run ends (grant gone) and at process exit."""
from __future__ import annotations

from typing import Any

import pytest
from aas_device import service as dsvc
from studio_core import activity
from studio_core.errors import Unavailable

from .conftest import OWNER
from .test_device_service import GIB, FakeSession, pid_of


@pytest.fixture(autouse=True)
def _reset() -> None:
    FakeSession.instances.clear()


def make(studio, free: list[int], alive: Any = None):  # type: ignore[no-untyped-def]
    return dsvc.DeviceService(studio, preflight=lambda: {"networkingMode": "nat"}, free_bytes=lambda: free[0],
                              session_factory=FakeSession, alive=alive, run_id="run_0123456789abcdef")


def started(studio, free: list[int], alive: Any = None):  # type: ignore[no-untyped-def]
    dsvc.set_enabled(studio, OWNER, True)
    s = make(studio, free, alive)
    s.start("phone")
    return s


@pytest.mark.parametrize("op", ["tap", "screenshot", "install"])
def test_owner_switching_off_stops_the_device_on_the_next_call(studio, op: str) -> None:  # type: ignore[no-untyped-def]
    pid = pid_of(studio)
    s = started(studio, [20 * GIB])
    dsvc.set_enabled(studio, OWNER, False)
    with pytest.raises(Unavailable, match="disabled"):
        {"tap": lambda: s.tap(1, 1), "screenshot": s.screenshot, "install": lambda: s.install(pid)}[op]()
    assert ("shutdown",) in FakeSession.instances[0].calls and s.session is None
    assert ("tap", 1, 1) not in FakeSession.instances[0].calls


@pytest.mark.parametrize("op", ["tap", "screenshot", "install"])
def test_low_disk_during_the_session_stops_the_device(studio, op: str) -> None:  # type: ignore[no-untyped-def]
    pid = pid_of(studio)
    free = [20 * GIB]
    s = started(studio, free)
    free[0] = 7 * GIB
    with pytest.raises(Unavailable, match="8 GiB"):
        {"tap": lambda: s.tap(1, 1), "screenshot": s.screenshot, "install": lambda: s.install(pid)}[op]()
    assert ("shutdown",) in FakeSession.instances[0].calls and s.session is None


def test_gates_still_open_control(studio) -> None:  # type: ignore[no-untyped-def]
    s = started(studio, [20 * GIB])
    assert s.tap(3, 4) == {"ok": True}
    assert s.screenshot()["size"] > 0
    assert ("shutdown",) not in FakeSession.instances[0].calls


def test_watch_tick_stops_the_device_when_the_run_is_over(studio) -> None:  # type: ignore[no-untyped-def]
    alive = [True]
    s = started(studio, [20 * GIB], alive=lambda: alive[0])
    assert s.watch_tick() is True and s.session is not None  # run still live: nothing happens
    alive[0] = False
    assert s.watch_tick() is False
    assert s.session is None and ("shutdown",) in FakeSession.instances[0].calls
    ev = [e for e in activity.list_events(studio, limit=100) if e["kind"] == "device.state"]
    assert ev[-1]["payload"]["state"] == "stopped" and ev[-1]["runId"] == "run_0123456789abcdef"


def test_watch_tick_stops_the_device_when_the_owner_switches_it_off(studio) -> None:  # type: ignore[no-untyped-def]
    s = started(studio, [20 * GIB], alive=lambda: True)
    dsvc.set_enabled(studio, OWNER, False)
    s.watch_tick()
    assert s.session is None


def test_close_is_idempotent_and_registered_for_process_exit(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    registered: list[Any] = []
    monkeypatch.setattr(dsvc.atexit, "register", lambda fn, *a, **k: registered.append(fn))
    s = make(studio, [20 * GIB])
    assert s.close in registered or any(getattr(f, "__self__", None) is s for f in registered)
    dsvc.set_enabled(studio, OWNER, True)
    s.start("phone")
    s.close()
    s.close()
    assert FakeSession.instances[0].calls.count(("shutdown",)) == 1
