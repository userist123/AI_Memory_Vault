"""Non-live checks of the live-gate harness: guest commands enter net+pid namespaces and never auto-start an adb
server; the leftover probe covers foreign net namespaces, adb for 5641 and the studio HOME."""
from __future__ import annotations

from tests.local_gate import conftest
from tests.local_gate import test_device_session as ds


def test_guest_enters_net_and_pid_namespaces() -> None:
    sc = ds.guest_script("id", "emulator-5600", 5641, ns=True)
    assert 'nsenter -t "$P" -n -p --' in sc
    assert "SAME_PIDNS" in sc and "/proc/self/ns/pid" in sc  # caller and session pid ns must differ
    inner = ds.GUEST_INNER
    assert inner.index("NO_ADB_SERVER") < inner.index("exec ")  # server must already listen, checked first
    assert "start-server" not in sc


def test_guest_without_ns_also_checks_server() -> None:
    sc = ds.guest_script("id", "emulator-5610", 5651, ns=False)
    assert "nsenter" not in sc and "NO_ADB_SERVER" in sc and " 5651 " in sc


def test_leftover_probe_covers_all_checks() -> None:
    sh = conftest.LEFTOVER_SH
    assert "/proc/1/ns/net" in sh and "ns/net" in sh
    assert "pgrep -af 'adb .*5641'" in sh
    assert 'HOME=$2' in sh and '"$HOME/.aas/home"' in sh


def test_leak_pids_and_incomplete_probe() -> None:
    leaks = ["LEAK adb5641 812 adb -L tcp:5641 fork-server server", "LEAK netns 812 adb -L tcp:5641",
             "LEAK home 813 sleep 1", "LEAK port 127.0.0.1:5641", "LEAK probe-error sudo"]
    assert conftest.leak_pids(leaks) == {812, 813}

    def fake_sh(script: str, timeout: float = 0) -> object:
        class R:
            stdout = "LEFTOVER_DONE\n"  # the root scan did not finish
        return R()

    leaks2, _ = conftest.leftovers(fake_sh)
    assert leaks2 == ["LEAK probe-incomplete"]


def test_last_down_xy_parses_both_formats() -> None:
    native = ("MotionEvent(deviceId=4, action=DOWN, xPrecision=1.0, pointers=[0: (531.0, 1207.0)]), policyFlags=0\n"
              "MotionEvent(deviceId=4, action=UP, pointers=[0: (9.0, 9.0)])")
    assert ds.last_down_xy(native) == [531.0, 1207.0]
    assert ds.last_down_xy("MotionEvent { action=ACTION_DOWN, id[0]=0, x[0]=12.5, y[0]=7.0 }") == [12.5, 7.0]
    assert ds.last_down_xy("action=UP pointers=[0: (1.0, 2.0)]") is None
