"""Dispatch driven by the REAL `04_CONFIG/agent_router.json`, with fake executables on PATH.

Regression for the PR #211 review: the dispatcher used the registry's `adapter_ref`
(`claude_code`, `antigravity`) as the program name, so a route the router called ROUTED
failed at dispatch with "claude_code unavailable". The older dispatcher tests passed program
names as `adapter_ref` and mocked `shutil.which` to always succeed, which hid it.

No real agent is ever invoked: PATH is prefixed with a directory of throw-away scripts that
only record how they were called.
"""
from __future__ import annotations

import json
import os
import stat
import sys
from pathlib import Path

import pytest

from routing import route_cli
from routing.agent_router import AgentRouter
from routing.dispatcher import AgentDispatcher, CommandAdapter
from routing.models import DispatchResult, DispatchStatus, TaskRequest
from routing.registry import RegistryError, RouteRegistry

ROOT = Path(__file__).resolve().parents[1]
REAL_CONFIG = ROOT / "04_CONFIG" / "agent_router.json"

pytestmark = pytest.mark.skipif(os.name == "nt", reason="fake executables are POSIX scripts")

FAKE_SCRIPT = """#!{python}
import json, os, sys
name = os.path.basename(sys.argv[0])
stdin = sys.stdin.read()
with open(os.environ["FAKE_AGENT_LOG"], "a", encoding="utf-8") as fh:
    fh.write(json.dumps({{"prog": name, "argv": sys.argv[1:], "stdin": stdin}}) + "\\n")
if name == "agy":
    print(json.dumps({{"event": "result", "result": {{"response": "agy fake ok"}}}}))
else:
    print(name + " fake ok")
"""


def _install_fakes(tmp_path: Path, monkeypatch, names=("claude", "codex", "agy", "ollama"), isolate_path=False):
    bindir = tmp_path / "fakebin"
    bindir.mkdir()
    for name in names:
        exe = bindir / name
        exe.write_text(FAKE_SCRIPT.format(python=sys.executable), encoding="utf-8")
        exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
    log = tmp_path / "calls.jsonl"
    monkeypatch.setenv("FAKE_AGENT_LOG", str(log))
    monkeypatch.setenv("PATH", str(bindir) if isolate_path else str(bindir) + os.pathsep + os.environ.get("PATH", ""))
    return log


def _calls(log: Path) -> list[dict]:
    return [json.loads(line) for line in log.read_text(encoding="utf-8").splitlines()] if log.exists() else []


@pytest.fixture(scope="module")
def real_registry() -> RouteRegistry:
    return RouteRegistry.from_file(REAL_CONFIG)


def test_real_config_declares_the_executable_of_every_command_runtime(real_registry):
    command = {rid: rt for rid, rt in real_registry.runtimes.items() if rt.transport == "command"}
    assert {rid: rt.executable for rid, rt in command.items()} == {
        "claude_code": "claude", "codex": "codex", "antigravity": "agy", "local_llm": "ollama"}
    # The hand-built adapter fallback must not drift from the registry.
    for rid, rt in command.items():
        assert CommandAdapter(rid).binary == rt.executable
        assert CommandAdapter(rid, rt.adapter_ref, rt.model, executable=rt.executable).binary == rt.executable
    # The logical id is not a program: this is exactly the original bug.
    assert real_registry.runtimes["claude_code"].adapter_ref != real_registry.runtimes["claude_code"].executable


@pytest.mark.parametrize("runtime_id,executable", [
    ("claude_code", "claude"), ("codex", "codex"), ("antigravity", "agy"), ("local_llm", "ollama")])
def test_real_config_route_then_dispatch_reaches_the_adapter(real_registry, tmp_path, monkeypatch, runtime_id, executable):
    log = _install_fakes(tmp_path, monkeypatch)
    available = {rid: route_cli._runtime_available(rt) for rid, rt in real_registry.runtimes.items()}
    assert available[runtime_id] is True
    goal = f"REAL CONFIG GOAL {runtime_id}"
    decision = AgentRouter(real_registry).route(TaskRequest(goal=goal, requested_runtime=runtime_id), available)
    assert decision.status.value == "ROUTED" and decision.primary.runtime_id == runtime_id

    dispatcher = AgentDispatcher(real_registry, artifact_root=tmp_path / "out")  # no adapters injected: real wiring
    packet = dispatcher.make_packet(decision, goal, "test")
    result = dispatcher.dispatch(decision, packet)

    assert result.error is None, result.error
    assert result.status is DispatchStatus.COMPLETED
    (call,) = _calls(log)
    assert call["prog"] == executable
    assert goal in call["stdin"]
    assert all(goal not in arg for arg in call["argv"])
    assert "fake ok" in result.final_message


def test_route_cli_dispatch_execute_with_real_config(tmp_path, monkeypatch, capsys):
    log = _install_fakes(tmp_path, monkeypatch)
    monkeypatch.setenv("AI_MEMORY_VAULT_HOME", str(tmp_path / "home"))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(sys, "argv", ["route_cli", "dispatch", "--runtime", "claude_code", "--execute", "--goal", "cli goal"])
    route_cli.main()
    out = capsys.readouterr().out
    assert "unavailable" not in out
    assert '"status": "completed"' in out
    assert [c["prog"] for c in _calls(log)] == ["claude"]


def test_route_cli_probe_uses_the_registry_executable(tmp_path, monkeypatch, capsys):
    _install_fakes(tmp_path, monkeypatch, names=("claude", "agy"), isolate_path=True)
    monkeypatch.setattr(sys, "argv", ["route_cli", "probe"])
    route_cli.main()
    probe = json.loads(capsys.readouterr().out)
    assert probe == {"claude_code": True, "codex": False, "antigravity": True, "perplexity": False, "local_llm": False}


def _mutated_config(tmp_path: Path, mutate) -> Path:
    raw = json.loads(REAL_CONFIG.read_text(encoding="utf-8"))
    mutate({r["id"]: r for r in raw["runtimes"]})
    path = tmp_path / "router.json"
    path.write_text(json.dumps(raw), encoding="utf-8")
    return path


@pytest.mark.parametrize("bad", [None, "", "/usr/bin/claude", "..\\claude", "claude --yolo", "a/b", 7])
def test_registry_rejects_missing_or_unsafe_executable_for_command_runtimes(tmp_path, bad):
    def mutate(rts):
        if bad is None:
            del rts["claude_code"]["executable"]
        else:
            rts["claude_code"]["executable"] = bad
    with pytest.raises(RegistryError, match="executable"):
        RouteRegistry.from_file(_mutated_config(tmp_path, mutate))


def test_registry_rejects_executable_on_non_command_runtime(tmp_path):
    with pytest.raises(RegistryError, match="executable"):
        RouteRegistry.from_file(_mutated_config(tmp_path, lambda rts: rts["perplexity"].__setitem__("executable", "perplexity")))


# ── one output directory for route.json and result.json ──────────────────────────────────
def test_route_and_result_receipts_share_one_directory(real_registry, tmp_path, monkeypatch):
    _install_fakes(tmp_path, monkeypatch)
    available = {rid: route_cli._runtime_available(rt) for rid, rt in real_registry.runtimes.items()}
    decision = AgentRouter(real_registry).route(TaskRequest(goal="receipts", requested_runtime="claude_code"), available)
    out = tmp_path / "out"
    dispatcher = AgentDispatcher(real_registry, artifact_root=out)
    packet = dispatcher.make_packet(decision, "receipts", "test")
    result = dispatcher.dispatch(decision, packet)

    run = out / "ai-memory-vault-dispatch" / packet.task_id
    assert Path(result.result_path) == run / "result.json"
    assert (run / "route.json").is_file() and (run / "result.json").is_file()
    # the suffix is applied exactly once and nothing else was created next to the run
    assert [p.name for p in (out / "ai-memory-vault-dispatch").iterdir()] == [packet.task_id]
    assert "ai-memory-vault-dispatch/ai-memory-vault-dispatch" not in str(run)


def test_route_and_result_share_the_default_root_too(real_registry, tmp_path, monkeypatch):
    log = _install_fakes(tmp_path, monkeypatch)
    monkeypatch.setenv("AI_MEMORY_VAULT_HOME", str(tmp_path / "home"))
    available = {rid: route_cli._runtime_available(rt) for rid, rt in real_registry.runtimes.items()}
    decision = AgentRouter(real_registry).route(TaskRequest(goal="receipts", requested_runtime="codex"), available)
    dispatcher = AgentDispatcher(real_registry)
    packet = dispatcher.make_packet(decision, "receipts", "test")
    result = dispatcher.dispatch(decision, packet)
    run = tmp_path / "home" / "dispatch" / packet.task_id
    assert Path(result.result_path) == run / "result.json"
    assert (run / "route.json").is_file()
    # codex's -o final.txt goes to the same run directory as well
    argv = _calls(log)[0]["argv"]
    assert Path(argv[argv.index("-o") + 1]) == run / "final.txt"


def test_dispatcher_hands_its_run_directory_to_adapters_that_accept_it(real_registry, tmp_path):
    seen = {}

    class RunDirAdapter:
        def dispatch(self, packet, run_dir=None):
            seen["run_dir"] = run_dir
            return DispatchResult(packet.task_id, packet.route_id, DispatchStatus.COMPLETED,
                                  packet.target_runtime, packet.target_agent, 0, "ok")

    decision = AgentRouter(real_registry).route(TaskRequest(goal="x", requested_runtime="claude_code"), {"claude_code": True})
    dispatcher = AgentDispatcher(real_registry, {"claude_code": RunDirAdapter()}, artifact_root=tmp_path)
    packet = dispatcher.make_packet(decision, "x", "test")
    dispatcher.dispatch(decision, packet)
    assert seen["run_dir"] == (tmp_path / "ai-memory-vault-dispatch" / packet.task_id).resolve()
    assert (seen["run_dir"] / "route.json").is_file()


def test_standalone_command_adapter_still_writes_its_own_receipt(tmp_path, monkeypatch):
    from routing.models import WorkPacket
    _install_fakes(tmp_path, monkeypatch)
    adapter = CommandAdapter("claude_code", "claude_code", executable="claude", artifact_root=tmp_path / "a", working_directory=tmp_path)
    packet = WorkPacket("task-standalone", "route-x", "router", "code_engineer", "claude_code", "profile", "command", "G")
    result = adapter.dispatch(packet)
    assert result.status is DispatchStatus.COMPLETED
    assert Path(result.result_path) == (tmp_path / "a" / "ai-memory-vault-dispatch" / "task-standalone" / "result.json").resolve()
