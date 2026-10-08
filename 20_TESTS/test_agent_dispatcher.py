from routing.dispatcher import AgentDispatcher
from routing.models import DispatchResult, DispatchStatus, TaskRequest, RouteStatus, VerifierDescriptor
from routing.agent_router import AgentRouter
from routing.registry import RouteRegistry
from pathlib import Path
from routing.dispatcher import CommandAdapter, WorkPacket


class FakeAdapter:
    def dispatch(self, packet):
        return DispatchResult(packet.task_id, packet.route_id, DispatchStatus.COMPLETED,
                              packet.target_runtime, packet.target_agent, 0, "fake result")


def test_dispatcher_creates_targeted_work_packet():
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    decision = AgentRouter(reg).route(
        TaskRequest(goal="inspect the UI visually", capabilities=("visual",)),
        {"antigravity": True}
    )
    dispatcher = AgentDispatcher(reg, {"antigravity": FakeAdapter()})
    packet = dispatcher.make_packet(decision, "inspect the UI visually", "claude_code", ("return screenshots and findings",))
    assert packet.target_runtime == "antigravity"
    assert packet.target_agent == "visual_architect"
    assert packet.prompt_profile == "visual-architect"


def test_dispatcher_returns_remote_result_without_relabeling():
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    decision = AgentRouter(reg).route(
        TaskRequest(goal="inspect the UI visually", capabilities=("visual",)),
        {"antigravity": True}
    )
    dispatcher = AgentDispatcher(reg, {"antigravity": FakeAdapter()})
    packet = dispatcher.make_packet(decision, "inspect the UI visually", "claude_code")
    result = dispatcher.dispatch(decision, packet)
    assert result.status is DispatchStatus.COMPLETED
    assert result.final_message == "fake result"


def test_blocked_route_cannot_be_dispatched():
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    decision = AgentRouter(reg).route(
        TaskRequest(goal="use frontier visual capability", capabilities=("visual",), min_quality=40),
        {"antigravity": False}
    )
    assert decision.status.value in {"PLANNED","BLOCKED"}
    if decision.status.value == "BLOCKED":
        dispatcher = AgentDispatcher(reg, {"antigravity": FakeAdapter()})
        try:
            dispatcher.make_packet(decision, "blocked", "claude_code")
            assert False, "expected blocked route to reject packet creation"
        except Exception as exc:
            assert "route" in str(exc).lower()


def test_command_adapter_maps_logical_runtime_to_binary(tmp_path, monkeypatch):
    adapter = CommandAdapter("claude_code", artifact_root=tmp_path, working_directory=tmp_path)
    monkeypatch.setattr("routing.dispatcher.shutil.which", lambda name: "/usr/bin/claude" if name == "claude" else None)
    assert adapter.binary == "claude"
    assert adapter.working_directory == tmp_path.resolve()


def test_a2a_adapter_v1_completed_response(monkeypatch):
    import json
    from routing.dispatcher import A2AAdapter
    from routing.models import WorkPacket

    captured = {}

    class Response:
        def __enter__(self): return self
        def __exit__(self, *args): return False
        def read(self):
            return json.dumps({"result":{"task":{"id":"remote-1","status":{"state":"TASK_STATE_COMPLETED","message":{"parts":[{"text":"ok"}]}}}}}).encode()

    def fake_urlopen(request, timeout):
        captured["headers"] = dict(request.headers)
        captured["payload"] = json.loads(request.data.decode())
        return Response()

    monkeypatch.setattr("routing.dispatcher.urllib.request.urlopen", fake_urlopen)
    packet = WorkPacket("task-1","route-1","claude_code","visual_architect","antigravity",
                        "visual-architect","a2a","inspect UI")
    result = A2AAdapter("https://agent.example/rpc").dispatch(packet)
    assert result.status is DispatchStatus.COMPLETED
    assert result.final_message == "ok"
    assert captured["payload"]["method"] == "SendMessage"
    assert next(v for k,v in captured["headers"].items() if k.lower() == "a2a-version") == "1.0"


def test_dispatcher_persists_route_receipt(tmp_path):
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    decision = AgentRouter(reg).route(
        TaskRequest(goal="inspect the UI visually", capabilities=("visual",)),
        {"antigravity": True}
    )
    dispatcher = AgentDispatcher(reg, {"antigravity": FakeAdapter()}, artifact_root=tmp_path)
    packet = dispatcher.make_packet(decision, "inspect the UI visually", "claude_code")
    dispatcher.dispatch(decision, packet)
    receipt = tmp_path / "ai-memory-vault-dispatch" / packet.task_id / "route.json"
    assert receipt.exists()
    payload = receipt.read_text(encoding="utf-8")
    assert "agent-route.receipt.v1" in payload
    assert packet.route_id in payload


def _routed_dispatch():
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    decision = AgentRouter(reg).route(TaskRequest(goal="inspect the UI visually", capabilities=("visual",)), {"antigravity": True})
    dispatcher = AgentDispatcher(reg, {"antigravity": FakeAdapter()})
    packet = dispatcher.make_packet(decision, "inspect the UI visually", "claude_code")
    return decision, packet, dispatcher


def test_planned_route_is_blocked_at_dispatch():
    decision, packet, dispatcher = _routed_dispatch()
    result = dispatcher.dispatch(__import__("dataclasses").replace(decision, status=RouteStatus.PLANNED), packet)
    assert result.status is DispatchStatus.BLOCKED


def test_forged_packet_route_id_is_blocked():
    decision, packet, dispatcher = _routed_dispatch()
    forged = __import__("dataclasses").replace(packet, route_id="forged-route")
    result = dispatcher.dispatch(decision, forged)
    assert result.status is DispatchStatus.BLOCKED


def test_verifier_gate_returns_pending_verification():
    decision, packet, dispatcher = _routed_dispatch()
    verifier = VerifierDescriptor("engineering_reviewer", "antigravity", decision.primary.model_tier, "engineering-review", "independent verification required")
    gated = __import__("dataclasses").replace(decision, verifier=verifier)
    result = dispatcher.dispatch(gated, packet)
    assert result.status is DispatchStatus.PENDING_VERIFICATION
    assert result.metadata["verifier_agent"] == "engineering_reviewer"


def test_local_llm_uses_registry_binary_and_model(monkeypatch, tmp_path):
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    runtime = reg.runtimes["local_llm"]
    assert runtime.adapter_ref == "ollama"
    assert runtime.model == "llama3.2"
    captured = {}
    class Proc:
        returncode = 0
        stdout = "local result"
        stderr = ""
    monkeypatch.setattr("routing.dispatcher.shutil.which", lambda name: "/mock/" + name)
    def fake_run(cmd, **kwargs):
        captured["cmd"] = cmd; captured["input"] = kwargs["input"]; return Proc()
    monkeypatch.setattr("routing.dispatcher.subprocess.run", fake_run)
    adapter = CommandAdapter("local_llm", runtime.adapter_ref, runtime.model, working_directory=tmp_path)
    packet = WorkPacket("local-task","local-route","router","local_ai_engineer","local_llm","local-ai","command","LOCAL GOAL")
    result = adapter.dispatch(packet)
    assert result.status is DispatchStatus.COMPLETED
    assert captured["cmd"] == ["ollama", "run", "llama3.2"]


def test_command_adapters_never_put_goal_in_argv(monkeypatch, tmp_path):
    captured = {}
    class Proc:
        returncode = 0
        stdout = '{"event":"result","result":{"response":"agy ok"}}\n'
        stderr = ""
    monkeypatch.setattr("routing.dispatcher.shutil.which", lambda name: "/mock/" + name)
    def fake_run(cmd, **kwargs):
        captured["cmd"] = cmd; captured["input"] = kwargs["input"]; return Proc()
    monkeypatch.setattr("routing.dispatcher.subprocess.run", fake_run)
    for runtime_id, adapter_ref, model in (("claude_code","claude",None),("codex","codex",None),("antigravity","agy",None),("local_llm","ollama","registry-model")):
        goal=f"UNIQUE SECRET GOAL {runtime_id}"
        packet=WorkPacket("task-"+runtime_id,"route-"+runtime_id,"router","agent",runtime_id,"profile","command",goal)
        result=CommandAdapter(runtime_id,adapter_ref,model,working_directory=tmp_path).dispatch(packet)
        assert result.status is not DispatchStatus.FAILED
        assert all(goal not in str(arg) for arg in captured["cmd"])
        assert goal in captured["input"]


# ── regressions for the PR #211 review ──────────────────────────────────────────────────
def test_a2a_sends_constraints_acceptance_and_memory_refs_not_only_the_goal(monkeypatch):
    import json
    from routing.dispatcher import A2AAdapter

    captured = {}

    class Response:
        def __enter__(self): return self
        def __exit__(self, *args): return False
        def read(self):
            return json.dumps({"result": {"message": {"parts": [{"text": "ok"}]}}}).encode()

    def fake_urlopen(request, timeout):
        captured["payload"] = json.loads(request.data.decode())
        return Response()

    monkeypatch.setattr("routing.dispatcher.urllib.request.urlopen", fake_urlopen)
    packet = WorkPacket("task-a2a", "route-a2a", "claude_code", "visual_architect", "antigravity",
                        "visual-architect", "a2a", "inspect UI",
                        ("screenshot attached",), ("do not modify files", "read-only"), ("vault://governance/vault_state",))
    A2AAdapter("https://agent.example/rpc").dispatch(packet)
    params = captured["payload"]["params"]
    text = params["message"]["parts"][0]["text"]
    for needle in ("inspect UI", "screenshot attached", "do not modify files", "read-only", "vault://governance/vault_state"):
        assert needle in text
    meta = params["metadata"]
    assert meta["constraints"] == ["do not modify files", "read-only"]
    assert meta["acceptance_criteria"] == ["screenshot attached"]
    assert meta["memory_refs"] == ["vault://governance/vault_state"]


def test_receipts_default_to_a_private_per_user_directory_not_temp(monkeypatch, tmp_path):
    import tempfile
    from routing.dispatcher import default_artifact_root
    monkeypatch.setenv("AI_MEMORY_VAULT_HOME", str(tmp_path / "home"))
    root = default_artifact_root()
    assert root == tmp_path / "home" / "dispatch"
    assert not root.resolve().is_relative_to(Path(tempfile.gettempdir()).resolve() / "ai-memory-vault-dispatch")
    import os
    if os.name != "nt":
        assert (root.stat().st_mode & 0o777) == 0o700


def test_run_dir_refuses_traversal(tmp_path):
    import pytest
    from routing.dispatcher import DispatchError, _run_dir
    for bad in ("../x", "a/b", "..", "", "x" * 65):
        with pytest.raises(DispatchError):
            _run_dir(tmp_path, bad)
    assert _run_dir(tmp_path, "task_abc").parent == tmp_path.resolve()


def test_route_receipt_keeps_the_goal_only_as_a_digest(tmp_path):
    import json
    root = Path(__file__).resolve().parents[1]
    reg = RouteRegistry.from_file(root / "04_CONFIG" / "agent_router.json")
    decision = AgentRouter(reg).route(TaskRequest(goal="inspect the UI visually", capabilities=("visual",)),
                                      {"antigravity": True})
    dispatcher = AgentDispatcher(reg, {"antigravity": FakeAdapter()}, artifact_root=tmp_path)
    secret_goal = "rotate the MApN key ring at 03:00 SECRET-MARKER"
    packet = dispatcher.make_packet(decision, secret_goal, "claude_code")
    dispatcher.dispatch(decision, packet)
    receipt = (tmp_path / "ai-memory-vault-dispatch" / packet.task_id / "route.json").read_text(encoding="utf-8")
    assert "SECRET-MARKER" not in receipt
    assert json.loads(receipt)["packet"]["goal_chars"] == len(secret_goal)
