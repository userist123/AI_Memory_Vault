"""Owner chat per project (aas_runner/chat.py, apps/backend/chat_routes.py) driven by the fake CLI fixture.

Continuity: full profile -> the session id from system/init is stored per project and the next turn runs
``--resume <id>`` from the project's fixed chat cwd; isolated profile, no session, or a refused resume -> the new
prompt gets a capped summary of the earlier turns + the current file list. Every security flag stays.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest
from aas_runner import chat
from aas_runner import claude as rc
from aas_runner import profile as prof
from studio_core import activity

from .conftest import OWNER

SID1 = "11111111-2222-4333-8444-555555555555"
SID2 = "66666666-7777-4888-9999-aaaaaaaaaaaa"
SECRET = "sk-ant-api03-" + "Q1w2E3r4T5y6U7i8O9p0" * 2


def result(cost: float = 0.01) -> dict:
    return {"type": "result", "subtype": "success", "num_turns": 1, "total_cost_usd": cost, "duration_ms": 5,
            "usage": {}}


def text(t: str) -> dict:
    return {"type": "assistant", "message": {"content": [{"type": "text", "text": t}]}}


def scenario(tmp_path: Path, **kw: object) -> str:
    return json.dumps({"record": str(tmp_path / "rec.json"), **kw})


def records(tmp_path: Path) -> list[dict]:
    log = tmp_path / "rec.json.log"
    return [json.loads(x) for x in log.read_text(encoding="utf-8").splitlines()] if log.exists() else []


def kinds(studio, run_id: str) -> list[str]:  # type: ignore[no-untyped-def]
    return [e["kind"] for e in activity.list_events(studio, limit=1000) if e["runId"] == run_id]


def turn(studio, r: rc.Runner, pid: str, prompt: str) -> str:  # type: ignore[no-untyped-def]
    run = r.start(pid, prompt, chat=True)
    r.wait(run, 60)
    return run


# ---------------------------------------------------------------- argv
def test_argv_chat_session_only_in_full_profile_and_resume_needs_a_uuid(tmp_path: Path) -> None:
    base = {"cli": ["claude"], "model": "sonnet", "mcp_config": tmp_path / "m.json", "settings": tmp_path / "s.json",
            "max_turns": 2, "max_budget_usd": 0.1}
    deny = prof.load_denylist(None)
    assert "--no-session-persistence" in rc.build_argv(**base, profile="full", deny=deny)
    assert "--no-session-persistence" in rc.build_argv(**base)
    a = rc.build_argv(**base, profile="full", deny=deny, chat_session=True, resume=SID1)
    assert "--no-session-persistence" not in a and a[a.index("--resume") + 1] == SID1
    full = rc.build_argv(**base, profile="full", deny=deny)
    assert [x for x in a if x not in ("--resume", SID1)] == [x for x in full if x != "--no-session-persistence"]
    with pytest.raises(ValueError):
        rc.build_argv(**base, chat_session=True)  # B7: isolated never persists
    for bad in ("../x", "--dangerously-skip-permissions", SID2.upper(), ""):
        with pytest.raises(ValueError):
            rc.build_argv(**base, profile="full", deny=deny, chat_session=True, resume=bad)
    with pytest.raises(ValueError):
        rc.build_argv(**base, profile="full", deny=deny, resume=SID1)  # resume without a chat session


# ---------------------------------------------------------------- continuity (full profile, default)
def test_two_turns_resume_the_stored_session_from_the_same_chat_cwd(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run1 = turn(studio, r, pid, scenario(tmp_path, init={"session_id": SID1}, events=[text("Am făcut notițele."), result()]))
    assert chat.get_session(studio, pid) == SID1
    run2 = turn(studio, r, pid, scenario(tmp_path, events=[text("Butonul + e verde."), result(0.02)]))
    recs = records(tmp_path)
    assert len(recs) == 2
    first, second = recs
    assert "--resume" not in first["argv"] and "--no-session-persistence" not in first["argv"]
    assert second["argv"][second["argv"].index("--resume") + 1] == SID1
    assert first["cwd"] == second["cwd"] and Path(second["cwd"]).name == pid and second["listing"] == []
    assert json.loads(second["stdin"])  # resumed: the CLI already has the memory, stdin = the new message only
    # every security flag is still there on the resumed turn
    j = " ".join(second["argv"])
    for flag in ("--strict-mcp-config", "--permission-mode dontAsk", "--disallowedTools", "--max-budget-usd"):
        assert flag in j
    assert "--dangerously-skip-permissions" not in second["argv"]
    turns = chat.history(studio, pid)
    assert [t["runId"] for t in turns] == [run1, run2]
    assert turns[0]["mode"] == "new" and turns[1]["mode"] == "resume"
    assert turns[1]["reply"] == "Butonul + e verde." and turns[1]["status"] == "success"
    assert turns[1]["costUsd"] == pytest.approx(0.02) and turns[1]["chat"] is True
    assert not (studio.settings.data_dir / "runner" / "chat" / pid).exists()  # removed when the run ends


def test_refused_resume_falls_back_to_the_summary_in_the_same_run(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    turn(studio, r, pid, scenario(tmp_path, init={"session_id": SID1}, events=[text("Notes app gata."), result()]))
    run2 = turn(studio, r, pid, scenario(tmp_path, resume_fail=True, init={"session_id": SID2},
                                         events=[text("Am schimbat culoarea."), result()]))
    recs = records(tmp_path)
    assert len(recs) == 3  # turn 1, refused resume, fallback
    assert "--resume" in recs[1]["argv"] and "--resume" not in recs[2]["argv"]
    stdin = recs[2]["stdin"]
    assert "[Studio context" in stdin and "Notes app gata." in stdin and "Current files:" in stdin
    assert "index.html" in stdin or "(none)" not in stdin
    ks = kinds(studio, run2)
    assert ks.count("run.started") == 1 and ks.count("run.finished") == 1 and "chat.fallback" in ks
    fin = [e for e in activity.list_events(studio, limit=1000) if e["runId"] == run2 and e["kind"] == "run.finished"]
    assert fin[0]["payload"]["status"] == "success"
    assert chat.get_session(studio, pid) == SID2  # the new session replaces the refused one
    assert chat.history(studio, pid)[-1]["mode"] == "summary"


def test_isolated_profile_never_persists_and_uses_the_summary(studio, fake_cli, tmp_path: Path) -> None:
    prof.set_profile(studio, OWNER, "isolated")
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    turn(studio, r, pid, scenario(tmp_path, init={"session_id": SID1}, events=[text("Prima versiune."), result()]))
    assert chat.get_session(studio, pid) is None
    turn(studio, r, pid, scenario(tmp_path, events=[result()]))
    recs = records(tmp_path)
    assert all("--no-session-persistence" in x["argv"] and "--resume" not in x["argv"] for x in recs)
    assert "Prima versiune." in recs[1]["stdin"] and "continue the same project" in recs[1]["stdin"]


def test_plain_runs_are_unchanged(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    turn(studio, r, pid, scenario(tmp_path, init={"session_id": SID1}, events=[result()]))
    run = r.start(pid, scenario(tmp_path, events=[result()]))
    r.wait(run, 60)
    rec = records(tmp_path)[-1]
    assert "--no-session-persistence" in rec["argv"] and "--resume" not in rec["argv"]
    assert Path(rec["cwd"]).name == run and json.loads(rec["stdin"])
    assert "chat.prompt" not in kinds(studio, run)


def test_a_forged_session_id_in_init_is_never_stored(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    turn(studio, r, pid, scenario(tmp_path, init={"session_id": "x --dangerously-skip-permissions"}, events=[result()]))
    assert chat.get_session(studio, pid) is None
    assert not chat.set_session(studio, pid, "../../etc")


def test_history_reply_is_sanitised_and_redacted(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    turn(studio, r, pid, scenario(tmp_path, events=[text("cheia " + SECRET + " \x1b[31mroșu‮"), result()]))
    reply = chat.history(studio, pid)[-1]["reply"]
    assert SECRET not in reply and "\x1b" not in reply and "‮" not in reply


def test_summary_is_capped(studio) -> None:  # type: ignore[no-untyped-def]
    pid = studio.create_project(OWNER, "notes", "blank", "")["id"]
    for i in range(12):
        rid = f"run_{i:016x}"
        activity.record(studio, "run.started", project_id=pid, run_id=rid, source="runner", payload={})
        activity.record(studio, "chat.prompt", project_id=pid, run_id=rid, source="runner",
                        payload={"text": "p" * 4000, "mode": "new"})
        activity.record(studio, "agent.text", project_id=pid, run_id=rid, source="runner", payload={"text": "r" * 4000})
    ctx = chat.context(studio, pid)
    assert len(ctx) < chat.CONTEXT_CAP + 2000 and ctx.endswith("[End of studio context]")


# ---------------------------------------------------------------- REST
def test_rest_chat_send_history_and_one_run_at_a_time(tmp_path: Path, fake_cli) -> None:  # type: ignore[no-untyped-def]
    from fastapi.testclient import TestClient

    from apps.backend.main import create_app
    from studio_core.config import Settings
    tok = "k" * 40
    app = create_app(Settings(data_dir=tmp_path / "d"), tok, runner_cli=fake_cli)
    h = {"Authorization": f"Bearer {tok}", "Host": "127.0.0.1:17875"}
    with TestClient(app, base_url="http://127.0.0.1:17875") as c:
        pid = c.post("/api/v1/projects", json={"name": "p"}, headers=h).json()["id"]
        assert c.get(f"/api/v1/projects/{pid}/chat").status_code == 401  # no token, no chat
        r1 = c.post(f"/api/v1/projects/{pid}/chat",
                    json={"message": scenario(tmp_path, sleep=20, events=[text("hi"), result()])}, headers=h)
        assert r1.status_code == 202
        busy = c.get(f"/api/v1/projects/{pid}/chat", headers=h).json()
        assert busy["running"] is True and busy["busy"] is True
        r2 = c.post(f"/api/v1/projects/{pid}/chat", json={"message": "fă butonul + verde"}, headers=h)
        assert r2.status_code == 409
        c.post(f"/api/v1/runs/{r1.json()['runId']}/stop", headers=h)
        app.state.runner.wait(r1.json()["runId"], 60)
        body = c.get(f"/api/v1/projects/{pid}/chat", headers=h).json()
        assert body["running"] is False and len(body["turns"]) == 1 and body["turns"][0]["chat"] is True
        assert c.post(f"/api/v1/projects/{pid}/chat", json={"message": ""}, headers=h).status_code == 422
        assert c.post(f"/api/v1/projects/{pid}/chat", json={"message": "x" * 4001}, headers=h).status_code == 422
        assert c.post(f"/api/v1/projects/{pid}/chat", json={"message": "x", "resume": SID1},
                      headers=h).status_code == 422  # extra fields refused: the session id never comes from a client
        assert c.get("/api/v1/projects/prj_zz/chat", headers=h).status_code in (400, 422)
        assert c.get("/api/v1/projects/prj_0000000000000000/chat", headers=h).status_code == 404
