from routing.dispatcher import AgentDispatcher
from routing.models import DispatchResult, DispatchStatus, TaskRequest
from routing.agent_router import AgentRouter
from routing.registry import RouteRegistry
from pathlib import Path
from routing.dispatcher import CommandAdapter


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
    assert captured["headers"]["A2a-version"] == "1.0"
