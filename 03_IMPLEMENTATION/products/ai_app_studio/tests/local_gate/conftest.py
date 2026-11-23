"""Local gate (WSL2 + KVM on the owner PC). Live probes run only with AAS_LOCAL_GATE=1.

Run:  AAS_LOCAL_GATE=1 py -3 -m pytest tests/local_gate --confcutdir tests/local_gate -p no:cacheprovider -q
A receipt is written to tests/local_gate/receipt-<date>.json when the live gate ran: every run is kept (run id,
start/finish time, exit status, per-test outcomes, per-probe results). A probe's aggregate status is "pass" only
when all of its known tests passed in one run and no later run failed one of them; a later partial run never
hides an earlier failure of a test it did not run ("fail"), and a pass that was never complete in one run is
"incomplete".
"""
from __future__ import annotations

import datetime as _dt
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest

HERE = Path(__file__).resolve().parent
PRODUCT = HERE.parents[1]
if str(PRODUCT) not in sys.path:
    sys.path.insert(0, str(PRODUCT))

LIVE = os.environ.get("AAS_LOCAL_GATE") == "1"
DISTRO = "Ubuntu-S9AI-D"
_WSL = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32", "wsl.exe")

live = pytest.mark.skipif(not LIVE or sys.platform != "win32", reason="local gate: set AAS_LOCAL_GATE=1 on the owner PC")

_RESULTS: dict[str, dict] = {}
_TESTS: dict[str, str] = {}  # nodeid -> worst outcome of this run
_RUN: dict[str, str] = {}
_RANK = {"pass": 0, "skipped": 1, "fail": 2, "error": 3}


def _now() -> str:
    return _dt.datetime.now(_dt.timezone.utc).isoformat(timespec="seconds")


def pytest_configure(config: pytest.Config) -> None:
    config.addinivalue_line("markers", "probe(id): threat-model probe id recorded in the local gate receipt")
    _RESULTS.clear()
    _TESTS.clear()
    _RUN.clear()
    _RUN.update(run_id=uuid.uuid4().hex[:12], started=_now())


def harness_sh(script: str, timeout: float = 120, user: str | None = None) -> subprocess.CompletedProcess[str]:
    """Test harness only (not studio code): run a bash script in the distro, script text on stdin."""
    argv = [_WSL, "-d", DISTRO]
    if user:
        argv += ["-u", user]
    argv += ["--", "/bin/bash", "-s"]
    env = {k: v for k, v in os.environ.items() if k.upper() not in ("WSLENV", "BASH_ENV")}
    # bytes: text mode would turn every \n into \r\n on Windows and bash would see stray \r
    r = subprocess.run(argv, input=script.replace("\r\n", "\n").encode("utf-8"), capture_output=True,
                       timeout=timeout, env=env)
    return subprocess.CompletedProcess(r.args, r.returncode, r.stdout.decode("utf-8", "replace"),
                                       r.stderr.decode("utf-8", "replace"))


@pytest.fixture()
def sh():
    return harness_sh


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item: pytest.Item, call: pytest.CallInfo):  # type: ignore[no-untyped-def]
    outcome = yield
    rep = outcome.get_result()
    phase = None
    if rep.when == "call":
        phase = "pass" if rep.passed else ("skipped" if rep.skipped else "fail")
    elif rep.failed:
        phase = "error" if rep.when == "setup" else "fail"
    elif rep.skipped:
        phase = "skipped"
    if phase is not None and _RANK[phase] >= _RANK.get(_TESTS.get(item.nodeid, "pass"), 0):
        _TESTS[item.nodeid] = phase
    marks = [m.args[0] for m in item.iter_markers("probe")]
    if rep.when == "teardown" and rep.failed:
        # a failing teardown (crash-dump guard) fails the probe; a module-scoped guard reports on the module's
        # last test, so every probe in that module is failed
        marks = sorted(set(marks) | {m.args[0] for it in item.session.items if it.module is item.module
                                     for m in it.iter_markers("probe")})
    if not marks:
        return
    now = _now()
    if rep.when == "setup" and rep.skipped:
        status = "skipped"
    elif rep.when == "call":
        status = "pass" if rep.passed else ("skipped" if rep.skipped else "fail")
    elif rep.when == "setup" and rep.failed:
        status = "error"
    elif rep.when == "teardown" and rep.failed:
        status = "fail"
    else:
        return
    for pid in marks:
        entry = _RESULTS.setdefault(pid, {"status": "pass", "tests": []})
        entry["tests"].append({"test": item.nodeid.split("::", 1)[-1], "status": status, "at": now,
                               "detail": getattr(item, "aas_detail", None)})
        if _RANK[status] > _RANK[entry["status"]]:
            entry["status"] = status


def _per_test(res: dict) -> dict[str, str]:
    """{test: worst status} of one run's result for one probe (a test may report call and teardown)."""
    out: dict[str, str] = {}
    for t in res.get("tests", []):
        if _RANK[t["status"]] >= _RANK.get(out.get(t["test"], "pass"), 0):
            out[t["test"]] = t["status"]
    return out


def aggregate_probes(runs: list[dict]) -> dict[str, dict]:
    """Probe status over chronological runs. Runs without per-probe "results" (legacy format) are not counted."""
    probes: dict[str, dict] = {}
    for pid in sorted({p for r in runs for p in r.get("results", {})}):
        per_run = [(r, _per_test(r["results"][pid])) for r in runs if pid in r.get("results", {})]
        known = {t for _, st in per_run for t in st}
        latest: dict[str, dict] = {}
        full = None
        for r, st in per_run:
            for t in r["results"][pid].get("tests", []):
                if t["status"] != "skipped" or t["test"] not in latest:
                    latest[t["test"]] = dict(t, run_id=r.get("run_id"))
            ran = {t: v for t, v in st.items() if v != "skipped"}
            if any(v in ("fail", "error") for v in ran.values()):
                full = None
            elif set(ran) == known:
                full = r.get("run_id")
        if any(latest[t]["status"] in ("fail", "error") for t in latest):
            status = "fail"
        elif full is not None:
            status = "pass"
        elif all(latest[t]["status"] == "skipped" for t in latest):
            status = "skipped"
        else:
            status = "incomplete"
        probes[pid] = {"status": status, "full_pass_run": full,
                       "tests": [latest[t] for t in sorted(latest)]}
    return probes


def pytest_sessionfinish(session: pytest.Session, exitstatus: int) -> None:
    if not LIVE or not _RESULTS:
        return
    day = _dt.date.today().isoformat()
    out = HERE / f"receipt-{day}.json"
    runs: list = []
    if out.exists():
        try:
            runs = list(json.loads(out.read_text(encoding="utf-8")).get("runs", []))
        except ValueError:
            pass
    finished = _now()
    runs.append({"run_id": _RUN.get("run_id"), "started": _RUN.get("started"), "finished": finished,
                 "exitstatus": int(exitstatus), "probes": sorted(_RESULTS),
                 "tests": dict(sorted(_TESTS.items())), "results": dict(sorted(_RESULTS.items()))})
    doc = {"gate": "ai_app_studio local gate S5", "distro": DISTRO, "finished": finished,
           "runs": runs, "probes": aggregate_probes(runs)}
    out.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8", newline="\n")


# ---------- emulator launch guard (crash-dump safety) ----------
# qemu 31.3.9 segfaults on its graceful-exit path and WSL can write ~2.6 GB per crash to
# %LOCALAPPDATA%\Temp\wsl-crashes on C:. A probe that launches an emulator runs only with >= 8 GiB free on C:,
# and every new file in that folder is deleted right after the probe and fails it.
EMU_MIN_FREE = 8 * 1024 ** 3
CRASH_DIR = Path(os.environ.get("LOCALAPPDATA") or os.path.expanduser(r"~\AppData\Local")) / "Temp" / "wsl-crashes"


def _crash_files() -> set[str]:
    try:
        return {p.name for p in CRASH_DIR.iterdir()}
    except OSError:
        return set()


def _emulator_guard():  # type: ignore[no-untyped-def]
    import shutil
    free = shutil.disk_usage(os.environ.get("SystemDrive", "C:") + "\\").free
    if free < EMU_MIN_FREE:
        pytest.skip("deferred: C: has %.1f GiB free (< 8 GiB) - no emulator launch" % (free / 1024 ** 3))
    before = _crash_files()
    yield
    new = sorted(_crash_files() - before)
    for name in new:
        try:
            (CRASH_DIR / name).unlink()
        except OSError:
            pass
    assert new == [], "new WSL crash files (deleted): %s" % new


emulator_guard = pytest.fixture(name="emulator_guard")(_emulator_guard)
emulator_guard_module = pytest.fixture(scope="module", name="emulator_guard_module")(_emulator_guard)


# ---------- leftover probe (PM3d / session end) ----------
# Every line starting with "LEAK " is a leftover. Beyond the process names and ports it flags any owner-uid
# process whose net namespace is not the distro's (/proc/1/ns/net), any adb client/server for port 5641 (the
# fork-server runs as "adb -L tcp:5641 fork-server ..."), and any process whose environment has the studio fake
# HOME. /proc/1 and other users' /proc/<pid>/environ need root: read with sudo -n.
LEFTOVER_SH = r"""
U=$(id -u); AH="$HOME/.aas/home"
pgrep -af 'qemu-system|aas_devhost|emulator -avd aas-' | grep -v pgrep | sed 's/^/LEAK proc /'
lsns -t net -n -o PID,COMMAND 2>/dev/null | grep -E 'aas_devhost|aas-ns|unshare|qemu' | sed 's/^/LEAK lsns /'
ss -ltnH | awk '{print $4}' | grep -E ':(5600|5601|5641)$' | sed 's/^/LEAK port /'
pgrep -af 'adb .*5641' | grep -v pgrep | sed 's/^/LEAK adb5641 /'
sudo -n /bin/bash -c '
init=$(readlink /proc/1/ns/net) || { echo "LEAK probe-error no-init-netns"; exit 0; }
for d in /proc/[0-9]*; do
  p=${d#/proc/}
  [ "$p" = "$$" ] && continue
  [ "$(stat -c %u "$d" 2>/dev/null)" = "$1" ] || continue
  n=$(readlink "$d/ns/net" 2>/dev/null) || continue
  cmd=$(tr "\0" " " < "$d/cmdline" 2>/dev/null | head -c 120)
  [ "$n" != "$init" ] && echo "LEAK netns $p $cmd"
  tr "\0" "\n" < "$d/environ" 2>/dev/null | grep -qxF "HOME=$2" && echo "LEAK home $p $cmd"
done
echo NETNS_SCAN_DONE
' _ "$U" "$AH" || echo "LEAK probe-error sudo"
echo LEFTOVER_DONE
"""


def leftovers(sh) -> tuple[list[str], str]:  # type: ignore[no-untyped-def]
    """(LEAK lines, raw output) of the leftover probe; an incomplete probe run counts as a leak."""
    out = sh(LEFTOVER_SH, timeout=180).stdout
    leaks = [ln for ln in out.splitlines() if ln.startswith("LEAK ")]
    if "LEFTOVER_DONE" not in out or "NETNS_SCAN_DONE" not in out:
        leaks.append("LEAK probe-incomplete")
    return leaks, out


def leak_pids(leaks: list[str]) -> set[int]:
    """Pids named by LEAK lines (adb5641/proc lines start with the pid; netns/home lines carry it second)."""
    pids = set()
    for ln in leaks:
        f = ln.split()
        if len(f) >= 3 and f[1] in ("proc", "adb5641", "netns", "home", "lsns") and f[2].isdigit():
            pids.add(int(f[2]))
    return pids
