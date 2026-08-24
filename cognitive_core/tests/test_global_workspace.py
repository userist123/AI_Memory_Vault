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
    
    # Proposal 1: High coherence from RetrievalAgent
    prop1 = WorkspaceProposal(
        agent_id="retrieval_agent",
        content={"id": "note_retrieved", "text": "high quality recall"},
        coherence_score=0.9,
        action_type="search"
    )
    # Proposal 2: Low coherence from CriticAgent
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

def test_orchestrator_integration_with_global_workspace():
    storage = StorageEngine()
    mc = MemoryController(storage=storage)
    gw = GlobalWorkspace()
    orchestrator = MultiAgentOrchestrator(controller=mc, global_workspace=gw)

    res = orchestrator.route_and_dispatch(Principal.AI_AGENT, "search technical docs", context=[])

    assert "global_broadcast" in res
    assert res["status"] == "completed"
