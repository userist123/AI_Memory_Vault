"""PM3d (THREAT_MODEL M3): killing wsl.exe mid-session leaves no emulator, studio adb, devhost or namespace.

Positive control: a `setsid nohup sleep` started outside the studio namespace survives the same kill, which shows
the orphan check would see a process that escaped; the leftover probe itself (foreign net namespace, adb for
5641, studio HOME) has its own positive control in test_device_session.test_06. Launches an emulator: needs >= 8 GiB free on C: (guard).
"""
from __future__ import annotations

import time

import pytest

from apps.device_host import launcher
from tests.local_gate.conftest import leftovers, live

pytestmark = [live, pytest.mark.usefixtures("emulator_guard")]

# leftover check: conftest.LEFTOVER_SH (process names, ports, foreign net namespaces, adb for 5641, studio HOME)


@pytest.mark.probe("PM3d")
def test_kill_wsl_exe_mid_session_leaves_nothing(sh, request) -> None:  # type: ignore[no-untyped-def]
    # the 1 s wait lets setsid detach before this wsl.exe session ends (without it the sleep died with the session,
    # before any kill, in the 2026-11-23 live run); the control is checked alive before the kill below
    r = sh("setsid nohup sleep 600 >/dev/null 2>&1 < /dev/null & echo CTRL=$!; sleep 1\n")
    ctrl = int(r.stdout.split("CTRL=")[1].split()[0])
    alive_cmd = f"kill -0 {ctrl} 2>/dev/null && echo ALIVE || echo DEAD\n"
    s = launcher.DeviceSession("phone")
    try:
        s.start(boot_timeout=300)
        assert s.tap(540, 1200)["ok"] is True
        before_leaks, before = leftovers(sh)
        assert "aas_devhost" in before and any(ln.startswith("LEAK netns") for ln in before_leaks)  # sees the session
        assert "ALIVE" in sh(alive_cmd).stdout  # the control runs before the kill
        t0 = time.monotonic()
        s.proc.kill()  # TerminateProcess(wsl.exe): no shutdown request, no stdin close by us
        leaks = before_leaks
        while time.monotonic() - t0 < 30:
            time.sleep(2)
            leaks, _ = leftovers(sh)
            if not leaks:
                break
        gone_s = round(time.monotonic() - t0, 1)
        alive = "ALIVE" in sh(alive_cmd).stdout
        request.node.aas_detail = {"gone_after_s": gone_s, "leftover": leaks[:8],
                                   "control_sleep_survived": alive}
        assert leaks == [], leaks
        assert gone_s <= 30
        assert alive  # [+] an escaped process would have been seen
    finally:
        sh(f"kill {ctrl} 2>/dev/null; true\n")
        if s.proc is not None and s.proc.poll() is None:
            s._kill()
