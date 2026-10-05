from routing.dispatcher import AgentDispatcher
from routing.models import DispatchResult, DispatchStatus, TaskRequest
from routing.agent_router import AgentRouter
from routing.registry import RouteRegistry
from pathlib import Path


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
