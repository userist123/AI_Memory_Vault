import importlib.util
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "99_SYSTEM" / "Council_Orchestrator.py"
spec = importlib.util.spec_from_file_location("council_orchestrator", MODULE)
orch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(orch)


class FakeMemoryController:
    def __init__(self):
        self.calls = []

    def search(self, principal, query, **kwargs):
        self.calls.append((principal, query))
        return {"results": [{"id": f"note-{principal}", "content": query}]}


def _valid_context_manifest():
    return {
        "agents": [
            {"id": "agent_a", "role": "primary", "skills": ["a", "b"]},
            {"id": "agent_b", "role": "secondary", "skills": ["c", "d"]},
        ],
        "memory_results": [],
        "graph_hops": 0,
        "specialist_output_tokens": 100,
        "synthesis_input_tokens": 100,
    }


def test_boundary_rejection_blocks_every_controller_call():
    controller = FakeMemoryController()
    with pytest.raises(orch.BoundaryRejectedError):
        orch.run_council_retrieval(
            controller,
            principal_by_agent={"agent_a": "P_A", "agent_b": "P_B", "agent_c": "P_C"},
            agent_skills={"agent_a": ["a", "b"], "agent_b": ["c", "d"], "agent_c": ["e"]},
            agent_queries={"agent_a": "q1", "agent_b": "q2", "agent_c": "q3"},
            context_manifest=_valid_context_manifest(),
        )
    assert controller.calls == []


def test_approved_selection_calls_controller_once_per_agent():
    controller = FakeMemoryController()
    result = orch.run_council_retrieval(
        controller,
        principal_by_agent={"agent_a": "P_A", "agent_b": "P_B"},
        agent_skills={"agent_a": ["a", "b"], "agent_b": ["c", "d"]},
        agent_queries={"agent_a": "q1", "agent_b": "q2"},
        context_manifest=_valid_context_manifest(),
    )
    assert set(result.agent_packs) == {"agent_a", "agent_b"}
    assert len(controller.calls) == 2


def test_missing_principal_for_approved_agent_fails_closed():
    controller = FakeMemoryController()
    with pytest.raises(orch.BoundaryRejectedError, match="no principal mapped"):
        orch.run_council_retrieval(
            controller,
            principal_by_agent={"agent_a": "P_A"},
            agent_skills={"agent_a": ["a"], "agent_b": ["c"]},
            agent_queries={"agent_a": "q1", "agent_b": "q2"},
            context_manifest=_valid_context_manifest(),
        )
    assert controller.calls == []


def test_telemetry_reflects_selection_and_retrieved_context():
    controller = FakeMemoryController()
    result = orch.run_council_retrieval(
        controller,
        principal_by_agent={"agent_a": "P_A", "agent_b": "P_B"},
        agent_skills={"agent_a": ["a", "b"], "agent_b": ["c", "d"]},
        agent_queries={"agent_a": "q1", "agent_b": "q2"},
        context_manifest=_valid_context_manifest(),
    )
    assert result.telemetry.agents_selected == 2
    assert result.telemetry.skills_selected == 4
    assert result.telemetry.deduplicated_context_tokens > 0
