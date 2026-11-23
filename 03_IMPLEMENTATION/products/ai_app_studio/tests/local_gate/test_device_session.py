"""Live device session: P9a (guest cannot reach WSL listeners), N7 (bounded queue), P10a/P10c (owner state untouched).

Tests in this module run in file order and share one studio phone session.
"""
from __future__ import annotations

import queue
import random
import time

import pytest

from apps.device_host import launcher
from tests.local_gate.conftest import live

pytestmark = [live, pytest.mark.usefixtures("emulator_guard_module")]

STATE: dict = {}
SDK_ADB = "$HOME/emu/sdk/platform-tools/adb"
SNAP = r"""
pid5037=$(ss -ltnpH 'sport = :5037' 2>/dev/null | grep -o 'pid=[0-9]*' | head -1)
echo "ADB5037=${pid5037:-none}"
for p in "$HOME/.android" "$HOME/debug.keystore"; do
  [ -e "$p" ] && find "$p" -maxdepth 4 -printf 'M %p %T@ %s\n' 2>/dev/null
done
[ -d "$HOME/android" ] && find "$HOME/android" -maxdepth 1 -printf 'M %p %T@ %s\n' 2>/dev/null
true
"""


def _snap(sh) -> tuple[str, list[str]]:  # type: ignore[no-untyped-def]
    out = sh(SNAP, timeout=180).stdout.splitlines()
    adb = next(line for line in out if line.startswith("ADB5037="))
    return adb, sorted(line for line in out if line.startswith("M "))


def _listeners(sh) -> list[int]:  # type: ignore[no-untyped-def]
    out = sh("ss -ltnH | awk '{print $4}'\n").stdout.split()
    return sorted({int(a.rsplit(":", 1)[1]) for a in out if a.rsplit(":", 1)[-1].isdigit()})


GUEST_SWEEP = r"""
for p in {ports}; do
  o=$(printf 'GET / HTTP/1.0\r\n\r\n' | toybox nc -w 3 -q 2 10.0.2.2 $p 2>&1); rc=$?
  echo "P $p rc=$rc out=$(echo "$o" | head -c 120 | tr -d '\r\n')"
done
"""


def _guest(sh, cmd: str, serial: str, port: int, ns: bool) -> str:  # type: ignore[no-untyped-def]
    env = ('env -i HOME="$HOME/.aas/home" ANDROID_USER_HOME="$HOME/.aas/android-user" '
           f'{SDK_ADB} -P {port} -s {serial} shell')
    if ns:
        script = ("P=$(pgrep -f 'python3 -I -S /opt/aas/bin/aas_devhost.py aas-phone' | head -1)\n"
                  '[ -n "$P" ] || { echo NO_DEVHOST; exit 1; }\n'
                  f"sudo -n nsenter -t \"$P\" -n -- runuser -u \"$(id -un)\" -- {env} \"$1\"\n")
    else:
        script = f'{env} "$1"\n'
    wrapped = "set -- " + _sq(cmd) + "\n" + script
    return sh(wrapped, timeout=300).stdout


def _sq(s: str) -> str:
    return "'" + s.replace("'", "'\\''") + "'"


def _parse_sweep(out: str) -> dict[int, str]:
    res = {}
    for line in out.splitlines():
        if line.startswith("P "):
            port = int(line.split()[1])
            res[port] = line
    return res


def _reached(line: str) -> bool:
    low = line.lower()
    return "HTTP/" in line or (" rc=0 " in line and "refused" not in low and "unreachable" not in low)


def test_01_baseline_and_canary(sh, request) -> None:  # type: ignore[no-untyped-def]
    STATE["snap0"] = _snap(sh)
    STATE["listeners"] = _listeners(sh)
    port = random.randint(20000, 29999)
    while port in STATE["listeners"]:
        port += 1
    r = sh(f"mkdir -p \"$HOME/.aas/run\"; cd \"$HOME/.aas/run\"; echo canary-aas > canary.txt\n"
           f"setsid nohup python3 -m http.server {port} --bind 127.0.0.1 >/dev/null 2>&1 < /dev/null &\n"
           f"echo PID=$!; sleep 1.5; curl -s -m 3 http://127.0.0.1:{port}/canary.txt\n")
    if "PID=" in r.stdout:
        STATE["canary_pid"] = int(r.stdout.split("PID=")[1].split()[0])
    assert "canary-aas" in r.stdout, r.stdout + r.stderr  # [+] canary answers from the WSL default ns
    STATE["canary"] = port
    request.node.aas_detail = {"canary_port": port, "wsl_listeners": STATE["listeners"],
                               "owner_adb": STATE["snap0"][0], "tracked_paths": len(STATE["snap0"][1])}


@pytest.mark.probe("P9a")
@pytest.mark.probe("N7")
@pytest.mark.probe("P10c")
def test_02_studio_session_boots_visible(sh, request) -> None:  # type: ignore[no-untyped-def]
    t0 = time.monotonic()
    s = launcher.DeviceSession("phone")
    STATE["session"] = s
    ev = s.start(boot_timeout=300)
    STATE["boot_s"] = round(time.monotonic() - t0, 1)
    win = sh("DISPLAY=:0 xwininfo -root -tree 2>/dev/null | grep -i 'Android Emulator - aas-phone' | head -2\n").stdout
    request.node.aas_detail = {"boot_s": STATE["boot_s"], "booted": ev, "window": win.strip()[:200]}
    assert ev["kind"] == "booted"
    assert "aas-phone" in win
    assert s.tap(540, 1200)["ok"] is True


@pytest.mark.probe("P9a")
def test_03_p9a_guest_cannot_reach_host(sh, request) -> None:  # type: ignore[no-untyped-def]
    ports = sorted(set(STATE["listeners"]) | {STATE["canary"], 5641, 5600, 5601, 5037, 8554})
    out = _guest(sh, GUEST_SWEEP.format(ports=" ".join(map(str, ports))), "emulator-5600", 5641, ns=True)
    res = _parse_sweep(out)
    reached = [p for p, line in res.items() if _reached(line)]
    request.node.aas_detail = {"ports": ports, "reached": reached,
                               "sample": {str(p): res.get(p, "missing")[:120] for p in ports}}
    net = _guest(sh, "ip -4 addr show; ip route", "emulator-5600", 5641, ns=True)
    request.node.aas_detail["guest_net"] = " ".join(net.split())[:300]
    assert sorted(res) == ports, out
    assert reached == []
    assert "10.0.2.15" in net  # the guest network is up: unreachable is restrict=on, not a dead interface


@pytest.mark.probe("N7")
def test_04_n7_tap_fast_while_video_consumer_stalled(request) -> None:  # type: ignore[no-untyped-def]
    s: launcher.DeviceSession = STATE["session"]
    assert s.video_start("540x1170")["ok"]
    lat: list[float] = []  # tap replies (the probe)
    dev_ms: list = []  # time spent inside the device host per tap (adb shell input)
    swipe_lat: list[float] = []  # swipes keep the screen changing; their reply includes the gesture time
    t_end = time.monotonic() + 10.0
    i = 0
    while time.monotonic() < t_end:  # nobody reads s.video_q for 10 s
        i += 1
        t = time.monotonic()
        is_tap = i % 2 == 0
        rep = s.tap(300 + (i % 7) * 60, 900) if is_tap else s.swipe(200 + (i % 5) * 100, 1800, 800, 600, 150)
        (lat if is_tap else swipe_lat).append(time.monotonic() - t)
        if is_tap:
            dev_ms.append(rep.get("ms"))
        assert rep["ok"], rep
        time.sleep(0.5)
    received = s.video_received
    dropped = s.video_dropped
    qlen = s.video_q.qsize()
    s.video_stop()
    s.drain_video()
    request.node.aas_detail = {"tap_replies": len(lat), "max_tap_reply_s": round(max(lat), 3),
                               "tap_reply_s": [round(x, 3) for x in lat], "tap_devhost_ms": dev_ms,
                               "max_swipe150ms_reply_s": round(max(swipe_lat), 3),
                               "video_chunks_received": received, "video_dropped_host": dropped,
                               "queue_len": qlen, "queue_max": s.video_q.maxsize}
    assert max(lat) < 1.0
    assert received > 0
    assert qlen <= s.video_q.maxsize


@pytest.mark.probe("N7")
def test_05_n7_positive_control_blocking_queue_stalls(request) -> None:  # type: ignore[no-untyped-def]
    s: launcher.DeviceSession = STATE["session"]
    real_q = s.video_q
    s.video_q = queue.Queue(maxsize=8)  # small so the stall shows within seconds
    s._on_video = lambda obj: s.video_q.put(obj)  # unbounded-wait policy: reader blocks on a full queue
    try:
        assert s.video_start("540x1170")["ok"]
        t_end = time.monotonic() + 15
        while time.monotonic() < t_end and not s.video_q.full():
            s.swipe(200, 1800, 800, 600, 150)
            time.sleep(0.3)
        assert s.video_q.full()
        for k in range(6):  # keep the screen changing so screenrecord keeps writing
            try:
                s.swipe(200, 1800, 800, 600, 150, timeout=1.0)
            except launcher.DeviceTimeout:
                pass
        t = time.monotonic()
        stalled = False
        try:
            s.tap(540, 1200, timeout=3.0)
        except launcher.DeviceTimeout:
            stalled = True
        lat = time.monotonic() - t
    finally:
        s._on_video = s._default_on_video
        s.drain_video()
        s.video_q = real_q
    s.video_stop()
    s.drain_video()
    request.node.aas_detail = {"tap_reply_s": round(lat, 3), "stalled": stalled}
    assert stalled or lat > 1.0
    assert s.tap(540, 1200)["ok"]  # recovers once drained


@pytest.mark.probe("P10c")
@pytest.mark.probe("PM3d")
def test_06_shutdown_leaves_nothing(sh, request) -> None:  # type: ignore[no-untyped-def]
    s: launcher.DeviceSession = STATE.pop("session")
    code = s.shutdown(timeout=60)
    time.sleep(3)
    r = sh("pgrep -af 'qemu-system|aas_devhost|emulator -avd aas-|adb -P 5641' | grep -v pgrep || echo NONE\n"
           "ss -ltnH | awk '{print $4}' | grep -E ':(5600|5601|5641)$' || echo NOPORTS\n")
    request.node.aas_detail = {"exit_code": code, "leftover": r.stdout.strip()[:300]}
    assert code == 0
    assert "NONE" in r.stdout and "NOPORTS" in r.stdout, r.stdout


@pytest.mark.probe("P10a")
@pytest.mark.probe("P10c")
def test_07_owner_state_unchanged(sh, request) -> None:  # type: ignore[no-untyped-def]
    adb1, files1 = _snap(sh)
    adb0, files0 = STATE["snap0"]
    request.node.aas_detail = {"owner_adb_before": adb0, "owner_adb_after": adb1, "tracked": len(files0),
                               "changed": sorted(set(files0) ^ set(files1))[:10]}
    assert adb0 == adb1
    assert files0 == files1


@pytest.mark.probe("P10c")
def test_08_checker_positive_control_detects_touch(sh, request) -> None:  # type: ignore[no-untyped-def]
    probe = "$HOME/.aas/run/p10-control"
    script = (f'mkdir -p "{probe}"; touch -d "2026-01-01" "{probe}/f"\n'
              f'find "{probe}" -printf "M %p %T@ %s\\n"\n')
    a = sorted(line for line in sh(script).stdout.splitlines() if line.startswith("M "))
    time.sleep(1.1)
    b_out = sh(f'touch "{probe}/f"; find "{probe}" -printf "M %p %T@ %s\\n"; rm -rf "{probe}"\n').stdout
    b = sorted(line for line in b_out.splitlines() if line.startswith("M "))
    request.node.aas_detail = {"detected": a != b}
    assert a and a != b


@pytest.mark.probe("P9a")
def test_09_p9a_positive_control_default_slirp_reaches_canary(sh, request) -> None:  # type: ignore[no-untyped-def]
    """Harness-only emulator: not namespaced, no restrict=on, headless; must reach the canary."""
    port = STATE["canary"]
    launcher.check_disk_space()  # never launch with < 5 GiB free (crash-dump safety)
    start = r"""
set -u
ulimit -c 1  # qemu segfaults on exit; RLIMIT_CORE=1 stops WSL piping a 2.6 GB core to Windows TEMP
mkdir -p "$HOME/.aas/run/ctl-user" "$HOME/.aas/home"
env -i HOME="$HOME/.aas/home" ANDROID_USER_HOME="$HOME/.aas/run/ctl-user" ANDROID_ADB_SERVER_PORT=5651 \
  "$HOME/emu/sdk/platform-tools/adb" -P 5651 start-server >/dev/null 2>&1
sudo -n runuser -u "$(id -un)" -g kvm -- env -i HOME="$HOME/.aas/home" ANDROID_SDK_ROOT="$HOME/emu/sdk" \
  ANDROID_AVD_HOME="$HOME/.aas/avd" ANDROID_USER_HOME="$HOME/.aas/run/ctl-user" ANDROID_ADB_SERVER_PORT=5651 \
  setsid "$HOME/emu/sdk/emulator/emulator" -avd aas-phone -port 5610 -no-window -no-audio -no-boot-anim \
  -gpu swiftshader_indirect -accel on -no-snapshot-save -no-snapshot-load -read-only \
  > "$HOME/.aas/run/ctl-emu.log" 2>&1 < /dev/null &
A="env -i HOME=$HOME/.aas/home ANDROID_USER_HOME=$HOME/.aas/run/ctl-user $HOME/emu/sdk/platform-tools/adb -P 5651 -s emulator-5610"
for i in $(seq 1 90); do [ "$($A shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && { echo BOOTED; break; }; sleep 2; done
"""
    stop = r"""
A="env -i HOME=$HOME/.aas/home ANDROID_USER_HOME=$HOME/.aas/run/ctl-user $HOME/emu/sdk/platform-tools/adb -P 5651"
# SIGKILL only (no `emu kill`: the graceful path segfaults and dumps core)
sudo -n pkill -KILL -f -- '-avd aas-phone -port 561[0]' 2>/dev/null
for i in $(seq 1 20); do pgrep -f -- '-port 561[0]' >/dev/null || break; sleep 1; done
$A kill-server >/dev/null 2>&1
rm -rf "$HOME/.aas/run/ctl-user"
pgrep -af 'qemu-system|adb -P 5651' | grep -v pgrep || echo CLEAN
"""
    try:
        r = sh(start, timeout=300)
        assert "BOOTED" in r.stdout, r.stdout + r.stderr
        time.sleep(5)  # user-mode network settles after boot
        out = ""
        for _ in range(3):
            out = _guest(sh, GUEST_SWEEP.format(ports=str(port)), "emulator-5610", 5651, ns=False)
            if _reached(_parse_sweep(out).get(port, "")):
                break
            time.sleep(5)
        line = _parse_sweep(out).get(port, "missing")
        request.node.aas_detail = {"canary_port": port, "line": line[:160]}
        assert _reached(line), out
    finally:
        r2 = sh(stop, timeout=120)
        STATE["ctl_clean"] = "CLEAN" in r2.stdout
        sh(f"kill {STATE['canary_pid']} 2>/dev/null; rm -f \"$HOME/.aas/run/canary.txt\"; echo ok\n")
    assert STATE["ctl_clean"], r2.stdout


@pytest.fixture(scope="module", autouse=True)
def _cleanup():  # type: ignore[no-untyped-def]
    yield
    s = STATE.pop("session", None)
    if s is not None:
        s.shutdown(timeout=60)
    if "canary_pid" in STATE:
        from tests.local_gate.conftest import harness_sh
        harness_sh(f"kill {STATE['canary_pid']} 2>/dev/null; true\n")
