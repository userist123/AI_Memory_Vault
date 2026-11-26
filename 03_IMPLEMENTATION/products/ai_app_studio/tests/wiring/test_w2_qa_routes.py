"""W2 backend: QA routes + Auto-polish loop (apps/backend/qa_routes.py). Gates and runner are faked; the real gates are
covered by test_w2_qa.py."""
from __future__ import annotations

import time
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from aas_build import qa, service
from starlette.testclient import TestClient
from studio_core import project_kind
from studio_core.config import Settings

from apps.backend import qa_routes

from .test_build_service import DOCKER, IMAGE, make_fake

AUTH = {"Authorization": "Bearer " + "t" * 40}


def result(passed: bool, n: int = 2, site: str = "") -> dict[str, Any]:
    issues = [] if passed else [{"check": "layout", "rule": "target-size-44", "severity": "moderate", "frames": ["phone"],
                                 "count": 1, "help": "small", "targets": [f"button#b{i}"]} for i in range(n)]
    counts = {"critical": 0, "serious": 0, "moderate": len(issues), "minor": 0, "total": len(issues)}
    return {"schema": qa.SCHEMA, "passed": passed, "counts": counts, "issues": issues, "truncated": 0, "site": site}


class FakeRunner:
    def __init__(self, status: str = "success") -> None:
        self.prompts: list[str] = []
        self.status = status
        self.live: str | None = None

    def live_project(self) -> str | None:
        return self.live

    def start(self, pid: str, prompt: str, *, chat: bool = False) -> str:
        assert chat is True  # an ordinary owner chat turn: plan gate and B6 apply unchanged
        self.prompts.append(prompt)
        return f"run_{len(self.prompts):016x}"

    def wait(self, run_id: str, timeout: float) -> str:
        return self.status


def wait_done(p: qa_routes.Polish, pid: str) -> dict[str, Any]:
    end = time.monotonic() + 10
    while time.monotonic() < end:
        st = p.status(pid)
        if st["state"] != "running":
            return st
        time.sleep(0.02)
    raise AssertionError("polish did not finish")


PID = "prj_" + "1" * 16


def test_polish_stops_when_gates_pass_after_one_round() -> None:
    seq = [result(False), result(True)]
    runner = FakeRunner()
    p = qa_routes.Polish(None, runner, gates=lambda st, pid: seq.pop(0))
    p.start(PID)
    st = wait_done(p, PID)
    assert st["state"] == "done" and st["loop"] == 1 and len(runner.prompts) == 1
    assert st["before"]["moderate"] == 2 and st["after"]["total"] == 0 and st["message"] == "all gates pass"
    assert "plan_submit" in runner.prompts[0] and "<<UNTRUSTED DATA: qa issues>>" in runner.prompts[0]


def test_polish_runs_at_most_two_rounds() -> None:
    runner = FakeRunner()
    calls: list[int] = []
    p = qa_routes.Polish(None, runner, gates=lambda st, pid: calls.append(1) or result(False))
    p.start(PID)
    st = wait_done(p, PID)
    assert st["state"] == "done" and st["loop"] == 2 and len(runner.prompts) == 2 and len(calls) == 3
    assert st["message"] == "issues remain after the last round"


def test_polish_does_nothing_when_already_clean_and_stops_on_a_failed_turn() -> None:
    runner = FakeRunner()
    p = qa_routes.Polish(None, runner, gates=lambda st, pid: result(True))
    p.start(PID)
    assert wait_done(p, PID)["state"] == "done" and runner.prompts == []
    runner = FakeRunner(status="aborted")
    p = qa_routes.Polish(None, runner, gates=lambda st, pid: result(False))
    p.start(PID)
    st = wait_done(p, PID)
    assert st["state"] == "stopped" and "aborted" in st["message"] and len(runner.prompts) == 1


def test_polish_refuses_while_a_run_is_live_and_reports_gate_failure() -> None:
    from studio_core.errors import ConflictError, Unavailable
    runner = FakeRunner()
    runner.live = PID
    p = qa_routes.Polish(None, runner, gates=lambda st, pid: result(False))
    with pytest.raises(ConflictError):
        p.start(PID)
    runner.live = None

    def boom(st: Any, pid: str) -> dict[str, Any]:
        raise Unavailable("nothing to capture: build the web target first")
    p = qa_routes.Polish(None, runner, gates=boom)
    p.start(PID)
    st = wait_done(p, PID)
    assert st["state"] == "failed" and "build the web target" in st["message"]


def test_polish_prompt_is_capped_and_wrapped() -> None:
    big = result(False, n=200)
    for i in big["issues"]:
        i["help"] = "x" * 150
    text = qa_routes.polish_prompt(big)
    assert len(text) <= qa_routes.PROMPT_CAP
    assert text.rstrip().endswith("<<END UNTRUSTED DATA: qa issues>>") and "more issues" in text


# ---------------------------------------------------------------- REST (owner)
@pytest.fixture()
def client(tmp_path: Path) -> Iterator[TestClient]:
    from apps.backend.main import create_app
    app = create_app(Settings(data_dir=tmp_path / "data", preview_port=0), "t" * 40)
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        yield c
    project_kind.unregister("app")


def test_rest_qa_auto_runs_once_per_build_and_needs_the_token(client: TestClient) -> None:
    st = client.app.state.studio
    polish = client.app.state.qa_polish
    runs: list[str] = []

    def gates(s: Any, pid: str) -> dict[str, Any]:
        r = result(False, site=qa.current_site(s.settings, pid))
        qa._store(s.settings, pid, r)  # noqa: SLF001
        runs.append(pid)
        return r
    polish.gates = gates
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    assert client.get(f"/api/v1/projects/{pid}/qa").status_code == 401
    assert client.post(f"/api/v1/projects/{pid}/qa", json={"auto": True, "x": 1}, headers=AUTH).status_code == 422
    assert client.get("/api/v1/projects/prj_bad/qa", headers=AUTH).status_code in (404, 422)
    r = client.post(f"/api/v1/projects/{pid}/qa", json={"auto": True}, headers=AUTH).json()
    assert r["ran"] is False and runs == []  # no web build yet
    service.build_web(st, pid, run_build=make_fake("r1"), docker=DOCKER, image=IMAGE)
    assert client.get(f"/api/v1/projects/{pid}/qa", headers=AUTH).json()["stale"] is True
    assert client.post(f"/api/v1/projects/{pid}/qa", json={"auto": True}, headers=AUTH).json()["ran"] is True
    assert client.post(f"/api/v1/projects/{pid}/qa", json={"auto": True}, headers=AUTH).json()["ran"] is False
    assert runs == [pid]  # the same build is not gated twice by the auto trigger (no capture -> nonce -> QA loop)
    g = client.get(f"/api/v1/projects/{pid}/qa", headers=AUTH).json()
    assert g["stale"] is False and g["result"]["counts"]["moderate"] == 2 and g["polish"]["state"] == "idle"
    assert client.post(f"/api/v1/projects/{pid}/qa", json={}, headers=AUTH).json()["ran"] is True  # manual: always


def test_rest_polish_start_and_status(client: TestClient) -> None:
    polish = client.app.state.qa_polish
    seq = [result(False), result(True)]
    polish.gates = lambda s, pid: seq.pop(0)
    fake = FakeRunner()
    polish.runner = fake
    pid = client.post("/api/v1/projects", json={"name": "Notes"}, headers=AUTH).json()["id"]
    assert client.post(f"/api/v1/projects/{pid}/qa/polish").status_code == 401
    r = client.post(f"/api/v1/projects/{pid}/qa/polish", headers=AUTH)
    assert r.status_code == 202, r.text
    wait_done(polish, pid)
    st = client.get(f"/api/v1/projects/{pid}/qa/polish", headers=AUTH).json()
    assert st["state"] == "done" and st["before"]["total"] == 2 and st["after"]["total"] == 0
