"""S6 review fixes for the runner: fail-closed init (H2), bounded line reader (8), stale-typing token split (3),
plan-gate pause with timeout (P5f) and stop_all (backend shutdown)."""
from __future__ import annotations

import json
import time
from pathlib import Path

import pytest
from aas_runner import claude as rc
from aas_runner import streamjson as sj
from studio_core import activity, plans

from .conftest import OWNER

SECRET = "sk-ant-api03-" + "Q1w2E3r4T5y6U7i8O9p0" * 2
RESULT = {"type": "result", "subtype": "success", "num_turns": 1, "total_cost_usd": 0.01, "duration_ms": 5, "usage": {}}
BASH = {"type": "assistant", "message": {"content": [
    {"type": "tool_use", "id": "toolu_x", "name": "Bash", "input": {"command": "calc.exe"}}]}}


def scenario(tmp_path: Path, **kw: object) -> str:
    return json.dumps({"record": str(tmp_path / "rec.json"), **kw})


def events(studio, kind: str | None = None):  # type: ignore[no-untyped-def]
    return [e for e in activity.list_events(studio, limit=1000) if kind is None or e["kind"] == kind]


# ---------------------------------------------------------------- H2: nothing is accepted before a validated init
def test_event_before_init_aborts_the_run_and_is_never_recorded(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, no_init=True, events=[BASH, RESULT], sleep=30))
    assert r.wait(run, 20) == "aborted"
    fin = events(studio, "run.finished")[-1]["payload"]
    assert fin["status"] == "aborted" and fin["reason"] == "init_missing"
    assert not [e for e in events(studio, "tool.call") if e["source"] == "runner"]  # project ops are server-sourced
    assert not events(studio, "step.started")
    assert not events(studio, "cost.update")  # the RESULT after the bad line was never read


def test_stream_that_ends_without_init_is_aborted(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, no_init=True, events=[]))
    assert r.wait(run, 20) == "aborted"
    assert events(studio, "run.finished")[-1]["payload"]["reason"] == "init_missing"


def test_init_then_tool_use_is_accepted_control(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    ok = {"type": "assistant", "message": {"content": [
        {"type": "tool_use", "id": "toolu_1", "name": "mcp__ai-app-studio__project_list", "input": {}}]}}
    run = r.start(pid, scenario(tmp_path, events=[ok, RESULT]))
    assert r.wait(run, 20) == "success"
    assert events(studio, "tool.call")


# ---------------------------------------------------------------- 8: bounded line reader
def test_overlong_line_aborts_the_run(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    huge = json.dumps({"type": "assistant", "message": {"content": [{"type": "text", "text": "x" * (rc.MAX_LINE_BYTES + 10)}]}})
    run = r.start(pid, scenario(tmp_path, events=[huge, RESULT], sleep=30))
    assert r.wait(run, 30) == "aborted"
    assert events(studio, "run.finished")[-1]["payload"]["reason"] == "line_too_long"
    assert not events(studio, "agent.text")


def test_line_just_under_the_limit_is_read_control(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    big = json.dumps({"type": "assistant", "message": {"content": [{"type": "text", "text": "y" * (rc.MAX_LINE_BYTES - 200)}]}})
    run = r.start(pid, scenario(tmp_path, events=[big, RESULT]))
    assert r.wait(run, 30) == "success"
    assert events(studio, "agent.text")


def test_line_cap_is_bounded_and_fits_a_max_size_agent_image() -> None:
    from aas_build import sight
    assert rc.MAX_LINE_BYTES == 2 * 1024 * 1024
    assert sight.MAX_AGENT_BYTES * 2 < rc.MAX_LINE_BYTES  # an image line (plus JSON overhead) never hits the cap


def test_max_size_image_tool_result_line_is_read(studio, fake_cli, tmp_path: Path) -> None:
    from aas_build import sight
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    use = {"type": "assistant", "message": {"content": [
        {"type": "tool_use", "id": "toolu_img", "name": "mcp__ai-app-studio__preview_capture", "input": {}}]}}
    img = {"type": "user", "message": {"role": "user", "content": [{"type": "tool_result", "tool_use_id": "toolu_img",
           "content": [{"type": "text", "text": "capture"},
                       {"type": "image", "source": {"type": "base64", "media_type": "image/jpeg",
                                                    "data": "A" * sight.MAX_AGENT_BYTES}}]}]}}
    line = json.dumps(img)
    assert len(line.encode()) > 1024 * 1024 // 2
    run = r.start(pid, scenario(tmp_path, events=[use, img, RESULT]))
    assert r.wait(run, 30) == "success"
    assert events(studio, "run.finished")[-1]["payload"]["status"] == "success"


# ---------------------------------------------------------------- 3: stale flush never splits a token
def _delta(t: str) -> dict:
    return {"type": "stream_event", "event": {"type": "content_block_delta", "index": 0,
                                              "delta": {"type": "text_delta", "text": t}}}


def test_stale_flush_keeps_a_split_key_buffered_across_a_gap(monkeypatch: pytest.MonkeyPatch) -> None:
    clock = [1000.0]
    monkeypatch.setattr(sj.time, "monotonic", lambda: clock[0])
    m = sj.StreamMapper()
    out: list[tuple[str, dict]] = []
    parts = ["the key is " + SECRET[:9], SECRET[9:24], SECRET[24:] + " and more"]
    out += m.feed(json.dumps(_delta(parts[0])))
    clock[0] += 2.5
    out += m.flush_stale()
    out += m.feed(json.dumps(_delta(parts[1])))
    clock[0] += 2.5
    out += m.flush_stale()
    out += m.feed(json.dumps(_delta(parts[2])))
    clock[0] += 2.5
    out += m.flush_stale()
    out += m.flush()
    texts = [p["text"] for k, p in out if k == "agent.typing"]
    joined = "".join(texts)
    assert texts and texts[0].startswith("the key is")  # the owner still sees text during the gap
    body = SECRET[len("sk-ant-"):]
    for i in range(0, len(body) - 8):
        assert body[i:i + 8] not in joined, (body[i:i + 8], texts)
    assert "sk-ant-a" not in joined


def test_stale_flush_without_whitespace_is_bounded(monkeypatch: pytest.MonkeyPatch) -> None:
    clock = [1000.0]
    monkeypatch.setattr(sj.time, "monotonic", lambda: clock[0])
    m = sj.StreamMapper()
    m.feed(json.dumps(_delta("a" * 100)))
    clock[0] += 2.5
    assert m.flush_stale() == []  # one unfinished token: kept
    m.feed(json.dumps(_delta("b" * sj.TYPING_TOKEN_CAP)))
    clock[0] += 2.5
    out = m.flush_stale()
    assert [k for k, _ in out] == ["agent.typing"]  # over the cap: flushed (redacted) instead of growing
    assert m._typing == ""  # noqa: SLF001


# ---------------------------------------------------------------- P5f: the runner pauses on the plan, timeout aborts
def test_pending_plan_past_the_timeout_aborts_the_run(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli, plan_timeout=2)
    calls = [{"tool": "plan_submit", "args": {"projectId": "$project", "title": "Build it", "steps": ["a"]}}]
    t0 = time.time()
    run = r.start(pid, scenario(tmp_path, mcp_calls=calls, project_id=pid))
    assert r.wait(run, 60) == "aborted"
    assert time.time() - t0 < 45
    fin = events(studio, "run.finished")[-1]["payload"]
    assert fin["reason"] == "plan_timeout"
    assert plans.list_records(studio) == []  # the stale plan card is withdrawn


def test_approved_plan_lets_the_run_write(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    rev = studio.get_revision(pid)
    r = rc.Runner(studio, cli=fake_cli, plan_timeout=60)
    calls = [{"tool": "plan_submit", "args": {"projectId": "$project", "title": "Build it", "steps": ["a"]}},
             {"tool": "code_write_file", "args": {"projectId": "$project", "baseRevision": rev, "path": "src/App.tsx",
                                                  "content": "export default function App(){return <p>gated</p>}\n"}}]
    run = r.start(pid, scenario(tmp_path, mcp_calls=calls, project_id=pid))
    deadline = time.time() + 30
    while time.time() < deadline and not plans.list_records(studio):
        time.sleep(0.1)
    rec = plans.list_records(studio)
    assert rec and rec[0]["runId"] == run
    plans.decide(studio, OWNER, rec[0]["id"], "approve", content_hash=rec[0]["contentSha256"])
    r.wait(run, 60)
    assert "gated" in studio.read_file(OWNER, pid, "src/App.tsx")["content"]


def test_stop_all_stops_every_run(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, sleep=60))
    deadline = time.time() + 20
    while time.time() < deadline and not events(studio, "run.started"):
        time.sleep(0.05)
    r.stop_all(timeout=20)
    assert not r.is_running(run)
    assert events(studio, "run.finished")[-1]["payload"]["status"] == "cancelled"
