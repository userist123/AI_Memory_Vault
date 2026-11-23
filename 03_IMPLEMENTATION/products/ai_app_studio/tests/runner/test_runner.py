"""Built-in runner (DESIGN 6.1; P5e/N3/PM4a static part) driven by the fake CLI fixture."""
from __future__ import annotations

import json
import time
from pathlib import Path

import pytest
from aas_runner import claude as rc
from studio_core import activity, plans, run_grants

from .conftest import OWNER

SECRET = "sk-ant-api03-" + "Q1w2E3r4T5y6U7i8O9p0" * 2
RESULT = {"type": "result", "subtype": "success", "num_turns": 1, "total_cost_usd": 0.01, "duration_ms": 5, "usage": {}}


def scenario(tmp_path: Path, **kw: object) -> str:
    return json.dumps({"record": str(tmp_path / "rec.json"), **kw})


def events(studio, kind: str | None = None):  # type: ignore[no-untyped-def]
    return [e for e in activity.list_events(studio, limit=1000) if kind is None or e["kind"] == kind]


def test_argv_snapshot_has_every_design_61_flag(tmp_path: Path) -> None:
    argv = rc.build_argv(cli=["claude"], model="sonnet", mcp_config=tmp_path / "mcp.json",
                         settings=tmp_path / "settings.json", max_turns=20, max_budget_usd=1.5)
    j = " ".join(argv)
    for flag in ("-p", "--output-format stream-json", "--verbose", "--include-partial-messages", "--strict-mcp-config",
                 "--permission-mode dontAsk", "--max-turns 20", "--max-budget-usd 1.5", "--input-format text"):
        assert flag in j, flag
    assert argv[argv.index("--tools") + 1] == ""
    assert argv[argv.index("--setting-sources") + 1] == ""
    assert argv[argv.index("--mcp-config") + 1].endswith("mcp.json")
    allowed = argv[argv.index("--allowedTools") + 1].split(",")
    assert allowed == [f"mcp__ai-app-studio__{t}" for t in rc.CATALOGUE_TOOLS]
    denied = argv[argv.index("--disallowedTools") + 1].split(",")
    assert {"Bash", "Edit", "Write", "MultiEdit", "NotebookEdit", "Read", "WebFetch", "WebSearch"} <= set(denied)
    assert "--add-dir" not in argv and "--dangerously-skip-permissions" not in argv


def test_prompt_is_never_in_argv(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, events=[RESULT]))
    assert r.wait(run, 30) == "success"
    rec = json.loads((tmp_path / "rec.json").read_text())
    assert not any("record" in a or "scenario" in a for a in rec["argv"])


def test_cwd_is_a_fresh_empty_run_dir_outside_workspaces_and_env_is_minimal(
        studio, fake_cli, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("AAS_API_TOKEN_CANARY", "secret-canary")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-ant-canary")
    monkeypatch.setenv("GITHUB_TOKEN", "ghp_canary")
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path))
    r.wait(run, 30)
    rec = json.loads((tmp_path / "rec.json").read_text())
    cwd = Path(rec["cwd"]).resolve()
    assert rec["listing"] == []
    assert cwd.parent == (studio.settings.data_dir / "runner" / "runs").resolve() and cwd.name == run
    assert not str(cwd).startswith(str(studio.settings.projects_dir.resolve()))
    keys = {k.upper() for k in rec["env"]}
    assert not any(k.startswith("AAS_") or "TOKEN" in k or "API_KEY" in k for k in keys)
    assert keys <= {"SYSTEMROOT", "WINDIR", "PATH", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "HOME",
                    "SYSTEMDRIVE", "PATHEXT", "COMSPEC", "PYTHONIOENCODING", "PYTHONUTF8", "CLAUDE_CODE_ENTRYPOINT",
                    "PYTHONHASHSEED", "DISABLE_AUTOUPDATER",  # S9 B7: the runner turns the CLI auto-updater off
                    "CLAUDE_CODE_DISABLE_AUTO_MEMORY"}  # B1: and the CLI auto-memory
    assert Path(rec["env"]["TEMP"]).resolve().parent == (studio.settings.data_dir / "runner" / "tmp").resolve()


def test_run_events_in_order_with_allow_listed_fields_and_no_thinking(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    evs = [
        {"type": "assistant", "message": {"content": [{"type": "thinking", "thinking": "think " + SECRET},
                                                      {"type": "text", "text": "Starting " + SECRET}]}},
        {"type": "user", "message": {"content": [{"type": "tool_result", "tool_use_id": "t1", "content": "x" * 900}]}},
        {"type": "result", "subtype": "success", "num_turns": 2, "total_cost_usd": 0.5, "duration_ms": 10,
         "usage": {"input_tokens": 7, "output_tokens": 9}},
    ]
    r = rc.Runner(studio, cli=fake_cli, max_budget_usd=2.0)
    run = r.start(pid, scenario(tmp_path, events=evs))
    assert r.wait(run, 30) == "success"
    got = [e for e in events(studio) if e["runId"] == run]  # the project.create row before the run has no run id
    kinds = [e["kind"] for e in got]
    assert kinds[0] == "run.started" and kinds[-1] == "run.finished"
    assert {"agent.text", "tool.result", "cost.update"} <= set(kinds)
    started = got[0]["payload"]
    assert started["budgetUsd"] == 2.0 and started["title"]
    blob = json.dumps(got)
    assert SECRET not in blob and "think " not in blob
    tr = next(e for e in got if e["kind"] == "tool.result")["payload"]
    assert tr["size"] == 900 and len(tr["excerpt"]) <= 300 and len(tr["sha256"]) == 64
    fin = got[-1]["payload"]
    assert fin["status"] == "success" and fin["turns"] == 2 and fin["costUsd"] == 0.5


def test_stop_cancels_the_process_and_finishes_the_run(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, sleep=60))
    deadline = time.time() + 20
    while time.time() < deadline and not events(studio, "run.started"):
        time.sleep(0.05)
    time.sleep(0.5)
    t0 = time.time()
    r.stop(run)
    assert r.wait(run, 20) == "cancelled"
    assert time.time() - t0 < 15
    assert events(studio, "run.finished")[-1]["payload"]["status"] == "cancelled"
    assert not r.is_running(run)


def test_init_violation_aborts_the_run_and_kills_the_process(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, sleep=60, init={"mcp_servers": [
        {"name": "ai-app-studio", "status": "connected"}, {"name": "canary", "status": "connected"}]}))
    assert r.wait(run, 20) == "aborted"
    assert events(studio, "run.finished")[-1]["payload"]["status"] == "aborted"


def test_nonzero_exit_without_result_is_failed(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, exit=3))
    assert r.wait(run, 20) == "failed"


def test_agent_writes_go_through_mcp_with_runner_grant_while_external_mcp_is_off(studio, fake_cli,
                                                                                tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    assert studio.get_external_mcp() is False
    rev = studio.get_revision(pid)
    calls = [{"tool": "plan_submit", "args": {"projectId": "$project", "title": "write", "steps": ["App.tsx"]}},
             {"tool": "code_write_file", "args": {
                 "projectId": "$project", "baseRevision": rev, "path": "src/App.tsx",
                 "content": "export default function App(){return <p>from runner</p>}\n"}}]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path, mcp_calls=calls, project_id=pid))
    deadline = time.time() + 30  # P5f: the run waits on plan_submit until the owner approves
    while time.time() < deadline and not plans.list_records(studio):
        time.sleep(0.1)
    rec = plans.list_records(studio)[0]
    plans.decide(studio, OWNER, rec["id"], "approve", content_hash=rec["contentSha256"])
    assert r.wait(run, 60) in ("success", "failed")  # the scenario has no result line
    assert "from runner" in studio.read_file(OWNER, pid, "src/App.tsx")["content"]
    ev = events(studio)
    call = next(e for e in ev if e["kind"] == "tool.call" and e["payload"].get("callId") == "toolu_1")
    assert call["payload"]["target"] == "src/App.tsx"
    ops = [e for e in ev if e["kind"] == "tool.call" and e["payload"].get("operationId")
           and e["payload"].get("tool") == "code.write_file"]
    assert ops and ops[-1]["payload"]["actor"] == f"agent:runner:{run}@mcp"
    diff = next(e for e in ev if e["kind"] == "file.diff")
    assert diff["runId"] == run and diff["payload"]["path"] == "src/App.tsx" and diff["payload"]["added"] >= 1
    assert "from runner" in diff["payload"]["diff"]
    assert studio.get_external_mcp() is False  # the grant never flips the owner's setting


def test_grant_is_revoked_and_config_removed_when_the_run_ends(studio, fake_cli, tmp_path: Path) -> None:
    pid = studio.create_project(OWNER, "p1", "blank", "")["id"]
    r = rc.Runner(studio, cli=fake_cli)
    run = r.start(pid, scenario(tmp_path))
    r.wait(run, 30)
    assert run_grants.is_valid(studio, run, "anything") is False
    assert run_grants.count(studio) == 0
    assert not (studio.settings.data_dir / "runner" / f"{run}.mcp.json").exists()


def test_start_refuses_unknown_project_and_creates_no_run_dir(studio, fake_cli) -> None:
    r = rc.Runner(studio, cli=fake_cli)
    with pytest.raises(Exception):  # noqa: B017 - NotFound from the studio
        r.start("prj_ffffffffffffffff", "{}")
    runs = studio.settings.data_dir / "runner" / "runs"
    assert not runs.exists() or not list(runs.iterdir())
