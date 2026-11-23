"""Local gate (WSL2 + KVM on the owner PC). Live probes run only with AAS_LOCAL_GATE=1.

Run:  AAS_LOCAL_GATE=1 py -3 -m pytest tests/local_gate --confcutdir tests/local_gate -p no:cacheprovider -q
A receipt (pass/fail per probe id, timestamps) is written to tests/local_gate/receipt-<date>.json when the
live gate ran.
"""
from __future__ import annotations

import datetime as _dt
import json
import os
import subprocess
import sys
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


def pytest_configure(config: pytest.Config) -> None:
    config.addinivalue_line("markers", "probe(id): threat-model probe id recorded in the local gate receipt")


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
    marks = [m.args[0] for m in item.iter_markers("probe")]
    if rep.when == "teardown" and rep.failed:
        # a failing teardown (crash-dump guard) fails the probe; a module-scoped guard reports on the module's
        # last test, so every probe in that module is failed
        marks = sorted(set(marks) | {m.args[0] for it in item.session.items if it.module is item.module
                                     for m in it.iter_markers("probe")})
    if not marks:
        return
    now = _dt.datetime.now(_dt.timezone.utc).isoformat(timespec="seconds")
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
        rank = {"pass": 0, "skipped": 1, "fail": 2, "error": 3}
        if rank[status] > rank[entry["status"]]:
            entry["status"] = status


def pytest_sessionfinish(session: pytest.Session, exitstatus: int) -> None:
    if not LIVE or not _RESULTS:
        return
    day = _dt.date.today().isoformat()
    out = HERE / f"receipt-{day}.json"
    # Same-day runs merge per probe id (a later run of a probe replaces that probe's earlier entry).
    probes: dict = {}
    runs: list = []
    if out.exists():
        try:
            old = json.loads(out.read_text(encoding="utf-8"))
            probes, runs = dict(old.get("probes", {})), list(old.get("runs", []))
        except ValueError:
            pass
    probes.update(_RESULTS)
    finished = _dt.datetime.now(_dt.timezone.utc).isoformat(timespec="seconds")
    runs.append({"finished": finished, "exitstatus": int(exitstatus), "probes": sorted(_RESULTS)})
    doc = {"gate": "ai_app_studio local gate S5", "distro": DISTRO, "finished": finished,
           "runs": runs, "probes": dict(sorted(probes.items()))}
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
