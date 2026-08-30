import pytest
from cognitive_core.global_workspace import GlobalWorkspace, WorkspaceProposal
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from cognitive_core.orchestrator import MultiAgentOrchestrator


def test_global_workspace_single_proposal():
    gw = GlobalWorkspace()
    prop = WorkspaceProposal(
        agent_id="retrieval_agent",
        content={"id": "note_1", "text": "single result"},
        coherence_score=0.9,
        action_type="search"
    )
    gw.submit_proposal(prop)
    broadcast = gw.compete_and_broadcast()

    assert broadcast is not None
    assert broadcast["winner_agent"] == "retrieval_agent"
    assert broadcast["score"] == 0.9
    assert broadcast["coalition"] == ["retrieval_agent"]


def test_global_workspace_competing_proposals():
    gw = GlobalWorkspace(max_slots=2, score_tolerance=0.1)

    prop1 = WorkspaceProposal(
        agent_id="retrieval_agent",
        content={"id": "note_retrieved", "text": "high quality recall"},
        coherence_score=0.9,
        action_type="search"
    )
    prop2 = WorkspaceProposal(
        agent_id="critic_agent",
        content={"id": "note_critic", "text": "weak criticism"},
        coherence_score=0.2,
        action_type="critic"
    )

    gw.submit_proposal(prop1)
    gw.submit_proposal(prop2)

    broadcast = gw.compete_and_broadcast()

    assert broadcast is not None
    assert broadcast["winner_agent"] == "retrieval_agent", "Highest composite score proposal must win competition"
    assert "retrieval_agent" in broadcast["coalition"]


def test_orchestrator_keeps_current_dispatch_contract_independent_of_global_workspace():
    """GlobalWorkspace is a standalone cognitive primitive in the current
    orchestrator contract; route_and_dispatch remains provider/worker neutral.
    """
    storage = StorageEngine()
    mc = MemoryController(storage=storage)
    gw = GlobalWorkspace()
    orchestrator = MultiAgentOrchestrator(mc)

    gw.submit_proposal(
        WorkspaceProposal(
            agent_id="retrieval_agent",
            content={"id": "note_1", "text": "search result"},
            coherence_score=0.8,
            action_type="search",
        )
    )
    broadcast = gw.compete_and_broadcast()
    res = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search technical docs", context=[])

    assert broadcast["winner_agent"] == "retrieval_agent"
    assert res["status"] == "completed"
    assert "orchestration_history" in res
    assert "global_broadcast" not in res
