"""S7 review fixes M3 (capture quota, free-disk gate, per-image cap), L3 (timeout kills the browser, joins the worker)."""
from __future__ import annotations

import threading
import time
from typing import Any

import pytest
from aas_build import capture, service
from studio_core.errors import Unavailable

from .conftest import OWNER
from .test_build_service import DOCKER, IMAGE, make_fake

GIB = 1024 ** 3


def _png(tag: str, pad: int = 0) -> bytes:
    return b"\x89PNG\r\n\x1a\n" + tag.encode() + b"." * pad


def _built(studio, name: str = "p1") -> str:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, name, "blank", "")["id"]
    service.build_web(studio, pid, run_build=make_fake("w1"), docker=DOCKER, image=IMAGE)
    return pid


def _shoot_one(tag: str, pad: int = 0):  # type: ignore[no-untyped-def]
    return lambda u, f: {"phone": _png(tag, pad)}


def _shot(studio, pid: str, tag: str, pad: int = 0, **kw: Any) -> str:  # type: ignore[no-untyped-def]
    kw.setdefault("free_bytes", lambda: 50 * GIB)
    return capture.capture(studio, pid, ["phone"], shoot=_shoot_one(tag, pad), **kw)["captures"][0]["sha256"]


def _files(studio) -> set[str]:  # type: ignore[no-untyped-def]
    d = capture.captures_dir(studio.settings)
    return {p.stem for p in d.glob("*.png")} if d.is_dir() else set()


def test_per_project_file_quota_prunes_the_oldest(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    monkeypatch.setattr(capture, "MAX_FILES", 3)
    pid = _built(studio)
    shas = [_shot(studio, pid, f"n{i}") for i in range(5)]
    assert _files(studio) == set(shas[2:])
    assert capture.capture_file(studio.settings, shas[0]) is None and capture.capture_file(studio.settings, shas[4])


def test_per_project_byte_quota_prunes_the_oldest(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    monkeypatch.setattr(capture, "MAX_BYTES", 2500)
    pid = _built(studio)
    shas = [_shot(studio, pid, f"b{i}", pad=1000) for i in range(4)]
    assert _files(studio) == set(shas[-2:])  # 2 x ~1000 B fit under 2500, 3 do not


def test_pruning_keeps_a_file_another_project_still_references(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    monkeypatch.setattr(capture, "MAX_FILES", 1)
    a, b = _built(studio), _built(studio, "p2")
    shared = _shot(studio, a, "same")
    _shot(studio, b, "same")  # identical bytes: the same content-addressed file
    _shot(studio, a, "other")  # project a prunes "same", but b still holds it
    assert shared in _files(studio)


def test_low_free_disk_refuses_before_any_browser_work(studio) -> None:  # type: ignore[no-untyped-def]
    pid = _built(studio)
    called: list[int] = []
    with pytest.raises(Unavailable, match="free"):
        capture.capture(studio, pid, ["phone"], free_bytes=lambda: 4 * GIB,
                        shoot=lambda u, f: called.append(1) or {"phone": _png("x")})
    assert not called and not _files(studio)


def test_oversized_png_is_refused_and_not_stored(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    monkeypatch.setattr(capture, "MAX_PNG_BYTES", 100)
    pid = _built(studio)
    with pytest.raises(Unavailable):
        _shot(studio, pid, "big", pad=500)
    assert not _files(studio)


def test_default_limits_are_the_documented_ones() -> None:
    assert (capture.MAX_FILES, capture.MAX_BYTES, capture.MIN_FREE_BYTES) == (200, 200 * 1024 ** 2, 5 * GIB)
    assert capture.MAX_PNG_BYTES <= 8 * 1024 ** 2


def test_timeout_kills_the_browser_and_joins_the_worker_before_the_lock_is_released(studio) -> None:  # type: ignore[no-untyped-def]
    pid = _built(studio)
    stop, order = threading.Event(), []

    def shoot(url: str, frames: list[str]) -> dict[str, bytes]:
        stop.wait(30)  # a hung browser
        order.append("worker-exit")
        raise RuntimeError("target closed")

    def kill() -> None:
        order.append("kill")
        stop.set()

    t0 = time.monotonic()
    with pytest.raises(Unavailable, match="timed out"):
        capture.capture(studio, pid, ["phone"], shoot=shoot, kill=kill, timeout=0.3, free_bytes=lambda: 50 * GIB)
    assert time.monotonic() - t0 < 10
    assert order == ["kill", "worker-exit"]  # killed, then the worker is gone, only then the call returned
    assert not [t for t in threading.enumerate() if t.name == "aas-capture"]
    assert _shot(studio, pid, "after")  # the lock was released: the next capture runs


def test_a_worker_that_survives_the_kill_keeps_the_lock(studio, monkeypatch) -> None:  # type: ignore[no-untyped-def]
    pid = _built(studio)
    stop = threading.Event()

    def shoot(url: str, frames: list[str]) -> dict[str, bytes]:
        stop.wait(30)
        return {}

    with pytest.raises(Unavailable, match="timed out"):
        capture.capture(studio, pid, ["phone"], shoot=shoot, kill=lambda: None, timeout=0.2, join_timeout=0.2,
                        free_bytes=lambda: 50 * GIB)
    monkeypatch.setattr(capture, "LOCK_WAIT_S", 0.1)  # wow2 #3: a second capture waits for the lock, then answers 409
    with pytest.raises(capture.CaptureBusy, match="ocupată"):  # no second browser while the first is still alive
        _shot(studio, pid, "second")
    stop.set()
    for _ in range(100):  # the worker releases the lock itself when it finally ends
        try:
            _shot(studio, pid, "third")
            break
        except (Unavailable, capture.CaptureBusy):
            time.sleep(0.05)
    else:
        pytest.fail("the capture lock was never released")
