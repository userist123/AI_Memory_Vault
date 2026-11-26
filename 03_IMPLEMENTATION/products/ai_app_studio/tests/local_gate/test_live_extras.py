"""Live extras deferred by the release gate (docs/slices/LOCAL_GATE_RELEASE.md): at most 2 emulator launches.

Launch 1 (phone): guest IPv6 is off (no global v6 address, ``ping6`` to a global address fails while ``ping6 ::1``
works) and a live ``video_keyframe`` op is followed by an IDR (NAL type 5) in the h264 stream.
Launch 2 (tablet): ``aas-tablet`` boots and one console tap lands where asked.
After each shutdown the leftover probe is empty; the owner's adb/.android/debug.keystore/android state is unchanged.
Probe marks (R-ipv6, R2 = the S8 key-frame residual, S9-tablet) put the measured details into the receipt.
"""
from __future__ import annotations

import re
import time

import pytest

from apps.device_host import launcher
from tests.local_gate.conftest import leftovers, live
from tests.local_gate.test_device_session import _snap, guest_script, last_down_xy

pytestmark = [live, pytest.mark.usefixtures("emulator_guard_module")]

STATE: dict = {}
_START = re.compile(rb"\x00\x00\x01")


def nal_types(stream: bytes) -> list[int]:
    """NAL unit types of an h264 Annex-B byte stream, in order."""
    return [stream[m.end()] & 0x1F for m in _START.finditer(stream) if m.end() < len(stream)]


def _guest(sh, cmd: str, form: str, serial: str) -> str:  # type: ignore[no-untyped-def]
    script = guest_script(cmd, serial, 5641, ns=True).replace("aas_devhost.py aas-phone", f"aas_devhost.py aas-{form}")
    out = sh(script, timeout=300).stdout
    assert "NO_ADB_SERVER" not in out and "SAME_PIDNS" not in out and "NO_DEVHOST" not in out, out[:300]
    host = out.split("HOSTNS=", 1)[1].split()[0]
    assert out.split("INNS=", 1)[1].split()[0] == host  # ran inside the session's pid namespace
    return out


def _wait_net(sh, form: str, serial: str) -> tuple[str, float]:  # type: ignore[no-untyped-def]
    t0 = time.monotonic()
    net = ""
    while time.monotonic() - t0 < 180:
        net = _guest(sh, "ip -4 addr show; ip route", form, serial)
        if "10.0.2.15" in net:
            break
        time.sleep(5)
    return net, round(time.monotonic() - t0, 1)


def _collect(s: launcher.DeviceSession, seconds: float) -> bytes:
    buf = bytearray()
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        for chunk in s.video_chunks(timeout=0.5):
            buf += chunk
            if time.monotonic() >= end:
                break
    return bytes(buf)


def test_nal_types_parser() -> None:
    assert nal_types(b"\x00\x00\x00\x01\x67aa\x00\x00\x01\x68b\x00\x00\x01\x65c") == [7, 8, 5]


@pytest.mark.probe("R-ipv6")
@pytest.mark.probe("R2")
def test_01_phone_ipv6_off_and_live_keyframe(sh, request) -> None:  # type: ignore[no-untyped-def]
    STATE["snap0"] = _snap(sh)
    s = launcher.DeviceSession("phone")
    STATE["session"] = s
    ev = s.start(boot_timeout=300)
    assert ev["kind"] == "booted"
    net, up_s = _wait_net(sh, "phone", "emulator-5600")
    assert "10.0.2.15" in net  # the interface is up: no v6 is restrict/ipv6=off, not a dead NIC
    v6 = _guest(sh, "echo V6ADDR_BEGIN; ip -6 addr show scope global; echo V6ADDR_END; "
                    "ping6 -c 2 -W 2 2001:4860:4860::8888 >/dev/null 2>&1; echo RC_GLOBAL=$?; "
                    "ping6 -c 1 -W 2 ::1 >/dev/null 2>&1; echo RC_LO=$?; ip -6 route", "phone", "emulator-5600")
    addr = v6.split("V6ADDR_BEGIN", 1)[1].split("V6ADDR_END", 1)[0]
    rc_global = int(v6.split("RC_GLOBAL=", 1)[1].split()[0])
    rc_lo = int(v6.split("RC_LO=", 1)[1].split()[0])
    detail = {"net_up_after_s": up_s, "global_v6": addr.strip()[:200], "ping6_global_rc": rc_global,
              "ping6_lo_rc": rc_lo, "v6_routes": " ".join(v6.split("RC_LO=", 1)[1].split()[1:])[:200]}
    request.node.aas_detail = detail
    assert "inet6" not in addr
    assert rc_lo == 0  # [+] ping6 works in the guest, so the global failure is the network, not the tool
    assert rc_global != 0

    s.video_start()
    first = _collect(s, 4.0)
    assert first, "no video chunk after video_start"
    time.sleep(2.2)  # past KEYFRAME_MIN_S since any earlier restart
    s.drain_video()
    t0 = time.monotonic()
    key = s.video_keyframe()
    after = _collect(s, 6.0)
    types = nal_types(after)
    detail.update({"keyframe_reply": key, "bytes_after": len(after), "nal_types_head": types[:12],
                   "idr_after": 5 in types, "sps_before_idr": 7 in types and types.index(7) < types.index(5)
                   if 5 in types else False, "reply_s": round(time.monotonic() - t0, 1)})
    s.video_stop()
    assert key.get("ok") is True and key.get("msg") == "restarted", key
    assert 5 in types and 7 in types and types.index(7) < types.index(5), types[:20]


@pytest.mark.probe("P10c")
def test_02_phone_shutdown_leaves_nothing(sh, request) -> None:  # type: ignore[no-untyped-def]
    s: launcher.DeviceSession = STATE.pop("session")
    code = s.shutdown(timeout=60)
    time.sleep(3)
    leaks, raw = leftovers(sh)
    request.node.aas_detail = {"exit_code": code, "leaks": leaks[:8]}
    assert code == 0
    assert leaks == [], raw


@pytest.mark.probe("S9-tablet")
def test_03_tablet_boot_and_tap(sh, request) -> None:  # type: ignore[no-untyped-def]
    s = launcher.DeviceSession("tablet")
    STATE["session"] = s
    t0 = time.monotonic()
    ev = s.start(boot_timeout=300)
    boot_s = round(time.monotonic() - t0, 1)
    win = sh("DISPLAY=:0 xwininfo -root -tree 2>/dev/null | grep -i 'Android Emulator - aas-tablet' | head -2\n").stdout
    request.node.aas_detail = {"boot_s": boot_s, "booted": ev, "window": win.strip()[:200]}
    assert ev["kind"] == "booted"
    assert "aas-tablet" in win
    assert s.tap(640, 400)["ok"] is True
    time.sleep(1.0)
    out = _guest(sh, "dumpsys input | grep -E 'MotionEvent|pointers=' | tail -n 6", "tablet", "emulator-5602")
    xy = last_down_xy(out)
    request.node.aas_detail.update({"tap_at": [640, 400], "last_down_xy": xy})
    assert xy is not None and round(xy[0]) == 640 and round(xy[1]) == 400, out[-400:]


@pytest.mark.probe("P10a")
@pytest.mark.probe("P10c")
def test_04_tablet_shutdown_and_owner_state(sh, request) -> None:  # type: ignore[no-untyped-def]
    s: launcher.DeviceSession = STATE.pop("session")
    code = s.shutdown(timeout=60)
    time.sleep(3)
    leaks, raw = leftovers(sh)
    snap1 = _snap(sh)
    adb0, paths0 = STATE["snap0"]
    request.node.aas_detail = {"exit_code": code, "leaks": leaks[:8], "owner_adb": [adb0, snap1[0]],
                               "tracked_paths": len(paths0), "changed": sorted(set(paths0) ^ set(snap1[1]))[:8]}
    assert code == 0
    assert leaks == [], raw
    assert snap1 == STATE["snap0"]


@pytest.fixture(scope="module", autouse=True)
def _cleanup():  # type: ignore[no-untyped-def]
    yield
    s = STATE.pop("session", None)
    if s is not None:
        s.shutdown(timeout=60)
