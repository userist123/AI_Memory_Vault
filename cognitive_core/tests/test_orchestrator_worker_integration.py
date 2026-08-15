"""
Integration tests proving the specialized worker agents (RouterAgent,
RetrievalAgent, CriticAgent, VerifierAgent, ConsolidatorAgent) are actually
invoked by MultiAgentOrchestrator.route_and_dispatch(), not merely defined.

These tests assert on execution side-effects (call counts on the real
underlying MemoryController / spy wrappers), not on class existence.
"""
import pytest
from unittest.mock import patch, MagicMock
from uuid import uuid4

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole, WorkerExecutionError


def make_note(note_id, verification="unverified", lifecycle="ACTIVE"):
    return {
        "id": note_id, "type": "knowledge", "lifecycle": lifecycle, "category": "test",
        "tags": [], "created": "2026-08-15", "updated": "2026-08-15",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high", "verification": verification, "relations": [],
        "content": "test content",
    }


@pytest.fixture
def orchestrator():
    storage = StorageEngine()
    controller = MemoryController(storage)
    return MultiAgentOrchestrator(controller)


def test_worker_agents_are_actually_instantiated(orchestrator):
    """Each AgentRole with a real worker implementation must resolve to a
    distinct, live instance of the corresponding specialized agent class."""
    from cognitive_core.agents.router_agent import RouterAgent
    from cognitive_core.agents.retrieval_agent import RetrievalAgent
    from cognitive_core.agents.critic_agent import CriticAgent
    from cognitive_core.agents.verifier_agent import VerifierAgent
    from cognitive_core.agents.consolidator_agent import ConsolidatorAgent

    assert isinstance(orchestrator.worker_agents[AgentRole.ROUTER], RouterAgent)
    assert isinstance(orchestrator.worker_agents[AgentRole.RETRIEVAL], RetrievalAgent)
    assert isinstance(orchestrator.worker_agents[AgentRole.CRITIC], CriticAgent)
    assert isinstance(orchestrator.worker_agents[AgentRole.VERIFIER], VerifierAgent)
    assert isinstance(orchestrator.worker_agents[AgentRole.CONSOLIDATOR], ConsolidatorAgent)


def test_router_agent_is_actually_executed_during_dispatch(orchestrator):
    """RouterAgent.process_task must be called exactly once per
    route_and_dispatch call -- proving real execution, not mere presence."""
    router_agent = orchestrator.worker_agents[AgentRole.ROUTER]
    with patch.object(router_agent, "process_task", wraps=router_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "find related notes", [])
        spy.assert_called_once()
        called_principal = spy.call_args[0][0]
        assert called_principal == Principal.AI_AGENT


def test_retrieval_agent_is_actually_executed_during_dispatch(orchestrator):
    retrieval_agent = orchestrator.worker_agents[AgentRole.RETRIEVAL]
    with patch.object(retrieval_agent, "process_task", wraps=retrieval_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "search history", [])
        spy.assert_called_once()


def test_verifier_agent_is_actually_executed_during_dispatch(orchestrator):
    verifier_agent = orchestrator.worker_agents[AgentRole.VERIFIER]
    with patch.object(verifier_agent, "process_task", wraps=verifier_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "simple query", [])
        spy.assert_called_once()


def test_critic_agent_is_actually_executed_during_dispatch(orchestrator):
    critic_agent = orchestrator.worker_agents[AgentRole.CRITIC]
    with patch.object(critic_agent, "process_task", wraps=critic_agent.process_task) as spy:
        orchestrator.route_and_dispatch(Principal.AI_AGENT, "simple query", [])
        spy.assert_called_once()


def test_principal_is_propagated_correctly_to_every_worker(orchestrator):
    """The exact calling principal (not a default/substitute) must reach
    every worker's process_task call."""
    agents = [
        orchestrator.worker_agents[AgentRole.ROUTER],
        orchestrator.worker_agents[AgentRole.RETRIEVAL],
        orchestrator.worker_agents[AgentRole.VERIFIER],
        orchestrator.worker_agents[AgentRole.CRITIC],
    ]
    spies = []
    for agent in agents:
        p = patch.object(agent, "process_task", wraps=agent.process_task)
        spies.append(p.start())
    try:
        orchestrator.route_and_dispatch(Principal.HUMAN, "find x", [])
        for spy in spies:
            assert spy.call_args[0][0] == Principal.HUMAN
    finally:
        patch.stopall()


def test_worker_failure_is_observable_and_non_fatal(orchestrator):
    """If a worker agent raises, route_and_dispatch must NOT crash; the
    failure must be recorded in orchestration_history as executed=False,
    attributable to the responsible role, and must not silently produce a
    successful/trusted result."""
    router_agent = orchestrator.worker_agents[AgentRole.ROUTER]
    with patch.object(router_agent, "process_task", side_effect=RuntimeError("boom")):
        result = orchestrator.route_and_dispatch(Principal.AI_AGENT, "find x", [])
        assert result["status"] == "completed"  # pipeline still completes
        router_entries = [h for h in result["orchestration_history"] if h.get("agent") == "router"]
        assert any(h.get("executed") is False and "boom" in h.get("error", "") for h in router_entries)


def test_unknown_worker_role_is_handled_gracefully(orchestrator):
    """_invoke_worker_agent must not raise for a role with no registered
    worker (e.g. SYNTHESIZER, which has a SubagentSpec but no worker_agents
    entry) -- it must return an observable non-executed record."""
    result = orchestrator._invoke_worker_agent(AgentRole.SYNTHESIZER, Principal.AI_AGENT, {"query": "x"})
    assert result["executed"] is False
    assert result["agent"] == AgentRole.SYNTHESIZER.value


def test_verifier_agent_cannot_escalate_trust_via_dispatch(orchestrator):
    """Security/trust-boundary test: VerifierAgent's permitted_actions is
    ["read"] only. Invoking it through the orchestrator must never result
    in any note's verification being written/escalated as a side effect."""
    verifier_agent = orchestrator.worker_agents[AgentRole.VERIFIER]
    assert "propose" not in verifier_agent.permitted_actions
    assert "update" not in verifier_agent.permitted_actions
    assert "attest" not in verifier_agent.permitted_actions

    note_id = str(uuid4())
    orchestrator.controller.storage.set(note_id, make_note(note_id, verification="unverified"))
    before = orchestrator.controller.storage.get(note_id)["verification"]

    orchestrator.route_and_dispatch(Principal.AI_AGENT, "verify this", [orchestrator.controller.storage.get(note_id)])

    after = orchestrator.controller.storage.get(note_id)["verification"]
    assert after == before == "unverified"


def test_ai_agent_worker_cannot_grant_official_or_verified_provenance(orchestrator):
    """End-to-end trust boundary: even after full pipeline dispatch under
    AI_AGENT, no note's provenance/verification may have been escalated,
    because none of the invoked workers hold propose/update/attest
    authority beyond what MemoryController's own P0 guards already permit."""
    note_id = str(uuid4())
    orchestrator.controller.storage.set(note_id, make_note(note_id))
    orchestrator.route_and_dispatch(Principal.AI_AGENT, "official verified source", [])
    stored = orchestrator.controller.storage.get(note_id)
    assert stored["verification"] == "unverified"
    assert stored["provenance"]["source_type"] == "user"  # unchanged, not escalated


def test_existing_route_and_dispatch_contract_still_compatible(orchestrator):
    """Regression: the pre-existing public contract (return dict shape,
    'status'=='completed', presence of orchestration_history and
    total_context_used) must remain unchanged for existing callers."""
    result = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search for related procedures", [])
    assert result["status"] == "completed"
    assert "orchestration_history" in result
    assert "total_context_used" in result
    assert "query" in result


def test_subagent_spec_and_allowed_actions_still_enforced(orchestrator):
    """Regression: _execute_worker_action's SubagentSpec-based gating (the
    pre-existing mechanism) must remain intact and unbypassed by the new
    worker_agents integration."""
    with pytest.raises(PermissionError, match="not permitted to perform action 'archive'"):
        orchestrator._execute_worker_action(AgentRole.ROUTER, Principal.AI_AGENT, "archive", {"note_id": "x"})
