"""Receipt keeps per-run probe results; a probe passes only when all its tests passed in one run, and a later
partial run never hides an earlier failure of a test it did not run."""
from __future__ import annotations

import json

import pytest

from tests.local_gate.conftest import HERE

pytest_plugins = ["pytester"]

SRC = '''
import os
import pytest

@pytest.mark.probe("PX")
def test_a():
    pass

@pytest.mark.probe("PX")
def test_b():
    assert os.environ.get("PX_FAIL") != "1"

def test_plain():
    pass
'''


def _run(pytester: pytest.Pytester, monkeypatch: pytest.MonkeyPatch, fail: bool, *args: str) -> dict:
    monkeypatch.setenv("PX_FAIL", "1" if fail else "0")
    pytester.runpytest_inprocess("-p", "no:cacheprovider", *args)
    (receipt,) = list(pytester.path.glob("receipt-*.json"))
    return json.loads(receipt.read_text(encoding="utf-8"))


@pytest.fixture()
def gate(pytester: pytest.Pytester, monkeypatch: pytest.MonkeyPatch) -> pytest.Pytester:
    monkeypatch.setenv("AAS_LOCAL_GATE", "1")
    pytester.makeconftest((HERE / "conftest.py").read_text(encoding="utf-8"))
    pytester.makepyfile(test_px=SRC)
    return pytester


def test_partial_run_does_not_overwrite_failure(gate: pytest.Pytester, monkeypatch: pytest.MonkeyPatch) -> None:
    doc = _run(gate, monkeypatch, True)
    assert doc["probes"]["PX"]["status"] == "fail"
    doc = _run(gate, monkeypatch, False, "-k", "test_a")  # partial: test_b not run
    assert doc["probes"]["PX"]["status"] == "fail"  # test_b's failure still stands
    doc = _run(gate, monkeypatch, False)
    px = doc["probes"]["PX"]
    assert px["status"] == "pass" and px["full_pass_run"] == doc["runs"][-1]["run_id"]
    doc = _run(gate, monkeypatch, False, "-k", "test_a")
    assert doc["probes"]["PX"]["status"] == "pass"  # a later partial pass keeps the full pass


def test_pass_needs_all_tests_in_one_run(gate: pytest.Pytester, monkeypatch: pytest.MonkeyPatch) -> None:
    assert _run(gate, monkeypatch, False)["probes"]["PX"]["status"] == "pass"
    assert _run(gate, monkeypatch, True)["probes"]["PX"]["status"] == "fail"
    doc = _run(gate, monkeypatch, False, "-k", "test_b")  # b passes again, but a and b never passed together since
    assert doc["probes"]["PX"]["status"] == "incomplete"
    assert doc["probes"]["PX"]["full_pass_run"] is None


def test_run_record_has_id_time_exitstatus_and_outcomes(gate: pytest.Pytester,
                                                        monkeypatch: pytest.MonkeyPatch) -> None:
    _run(gate, monkeypatch, False)
    doc = _run(gate, monkeypatch, True)
    r0, r1 = doc["runs"]
    assert r0["run_id"] != r1["run_id"]
    assert r0["exitstatus"] == 0 and r1["exitstatus"] == 1
    assert r1["started"] <= r1["finished"]
    outcomes = {k.split("::")[-1]: v for k, v in r1["tests"].items()}
    assert outcomes == {"test_a": "pass", "test_b": "fail", "test_plain": "pass"}
    assert r1["results"]["PX"]["status"] == "fail"
    assert r0["results"]["PX"]["status"] == "pass"  # the earlier run's per-probe result is kept as it was


def test_legacy_runs_are_kept_but_not_counted(gate: pytest.Pytester, monkeypatch: pytest.MonkeyPatch) -> None:
    import datetime as dt
    legacy = {"runs": [{"finished": "2026-01-01T00:00:00+00:00", "exitstatus": 0, "probes": ["PX"]}],
              "probes": {"PX": {"status": "pass", "tests": []}}}
    (gate.path / f"receipt-{dt.date.today().isoformat()}.json").write_text(json.dumps(legacy), encoding="utf-8")
    doc = _run(gate, monkeypatch, False, "-k", "test_a")
    assert doc["runs"][0] == legacy["runs"][0]
    assert doc["probes"]["PX"]["status"] == "pass"  # only test_a is known from runs with per-test results
