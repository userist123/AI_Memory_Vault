"""S9b item 3 - device host ``video_keyframe`` op: restarts screenrecord (a new screenrecord stream starts with
SPS/PPS + IDR), throttled, no-op when the video is off; protocol + launcher expose it, so the hub's key request on a
discontinuity reaches the device host (fakes only, no emulator)."""
from __future__ import annotations

import json
import threading
from pathlib import Path
from typing import Any

import pytest
from apps.device_host import launcher, protocol

from tests.local_gate.test_devhost_unit import _host, devhost


class FakeRec:
    def __init__(self) -> None:
        self.terminated = 0

    def poll(self) -> None:
        return None

    def terminate(self) -> None:
        self.terminated += 1

    def wait(self, timeout: float | None = None) -> int:
        return 0

    def kill(self) -> None:
        pass


def test_fields_and_protocol_know_the_op() -> None:
    assert devhost.FIELDS["video_keyframe"] == set()
    line = protocol.encode_request(7, "video_keyframe")
    assert json.loads(line) == {"id": 7, "op": "video_keyframe"}
    with pytest.raises(protocol.ProtocolError):
        protocol.encode_request(7, "video_keyframe", size="540x1170")


def test_keyframe_restarts_screenrecord_and_is_throttled(tmp_path: Path) -> None:
    h = _host(tmp_path)  # built without __init__: give it the video fields __init__ sets
    h.rec, h.rec_lock, h.video_on, h.key_t = None, threading.Lock(), False, 0.0
    assert h.handle({"id": 1, "op": "video_keyframe"}) == {"ok": True, "msg": "video-off"}
    rec = FakeRec()
    h.video_on, h.rec = True, rec
    assert h.handle({"id": 2, "op": "video_keyframe"}) == {"ok": True, "msg": "restarted"}
    assert rec.terminated == 1  # the video loop starts a fresh screenrecord: SPS/PPS + IDR first
    assert h.video_on is True
    assert h.handle({"id": 3, "op": "video_keyframe"}) == {"ok": True, "msg": "throttled"}
    assert rec.terminated == 1
    h.key_t -= devhost.KEYFRAME_MIN_S + 0.1
    assert h.handle({"id": 4, "op": "video_keyframe"})["msg"] == "restarted"
    assert rec.terminated == 2


def test_launcher_session_sends_the_op(monkeypatch: pytest.MonkeyPatch) -> None:
    sent: list[tuple[str, dict[str, Any]]] = []
    s = launcher.DeviceSession.__new__(launcher.DeviceSession)
    monkeypatch.setattr(s, "request", lambda op, timeout=None, **kw: sent.append((op, kw)) or {"ok": True},
                        raising=False)
    assert s.video_keyframe() == {"ok": True}
    assert sent == [("video_keyframe", {})]
