"""F3: a teardown failure (crash-dump guard) must be recorded against the probe in the receipt as fail."""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from tests.local_gate.conftest import HERE

pytest_plugins = ["pytester"]

TEST_SRC = '''
import pytest

@pytest.fixture()
def guard_fn():
    yield
    assert False, "new WSL crash files"

@pytest.fixture(scope="module")
def guard_mod():
    yield
    assert False, "new WSL crash files (module)"

@pytest.mark.probe("PX1")
def test_fn_guard(guard_fn):
    pass

@pytest.mark.probe("PX2")
def test_mod_guard(guard_mod):
    pass
'''

CLEAN_SRC = '''
import pytest

@pytest.mark.probe("PX3")
def test_clean():
    pass
'''


def test_teardown_failure_marks_probe_fail(pytester: pytest.Pytester, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("AAS_LOCAL_GATE", "1")
    pytester.makeconftest((HERE / "conftest.py").read_text(encoding="utf-8"))
    pytester.makepyfile(test_probes=TEST_SRC, test_clean=CLEAN_SRC)
    pytester.runpytest_inprocess("-p", "no:cacheprovider")
    candidates = list(pytester.path.glob("receipt-*.json"))
    assert candidates, "no receipt written"
    probes = json.loads(candidates[0].read_text(encoding="utf-8"))["probes"]
    assert probes["PX1"]["status"] == "fail"
    assert probes["PX2"]["status"] == "fail"
    assert probes["PX3"]["status"] == "pass"
    assert any(t["status"] == "fail" for t in probes["PX1"]["tests"])
