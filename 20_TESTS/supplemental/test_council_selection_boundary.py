import importlib.util
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "99_SYSTEM" / "Council_Selection_Boundary.py"
spec = importlib.util.spec_from_file_location("council_selection_boundary", MODULE)
boundary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(boundary)


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


def test_passes_with_valid_selection_and_context():
    result = boundary.enforce_council_boundary(
        {"agent_a": ["a", "b"], "agent_b": ["c", "d"]},
        _valid_context_manifest(),
    )
    assert result.agent_skills == {"agent_a": ["a", "b"], "agent_b": ["c", "d"]}
    assert result.context_manifest["graph_hops"] == 0


def test_rejects_when_too_many_agents():
    agent_skills = {f"agent_{i}": ["a"] for i in range(boundary.MAX_AGENTS_PER_COUNCIL + 1)}
    with pytest.raises(boundary.BoundaryRejectedError, match="too many agents"):
        boundary.enforce_council_boundary(agent_skills, _valid_context_manifest())


def test_rejects_when_skill_budget_exceeded():
    with pytest.raises(boundary.BoundaryRejectedError, match="skill budget rejected"):
        boundary.enforce_council_boundary(
            {"agent_a": ["a", "b"], "agent_b": ["c", "d"], "agent_c": ["e"]},
            _valid_context_manifest(),
        )


def test_rejects_when_context_manifest_invalid():
    manifest = _valid_context_manifest()
    manifest["whole_vault"] = True
    with pytest.raises(boundary.BoundaryRejectedError, match="context manifest rejected"):
        boundary.enforce_council_boundary({"agent_a": ["a"]}, manifest)


def test_rejects_when_context_manifest_not_a_dict():
    with pytest.raises(boundary.BoundaryRejectedError, match="must be a dict"):
        boundary.enforce_council_boundary({"agent_a": ["a"]}, [])


def test_rejects_when_agent_skills_not_a_mapping():
    with pytest.raises(boundary.BoundaryRejectedError, match="must be a mapping"):
        boundary.enforce_council_boundary(["agent_a"], _valid_context_manifest())


def test_failure_never_returns_a_usable_result():
    # Fail-closed contract: any exception path must not leave a caller with a
    # BoundaryResult it could mistakenly treat as validated.
    with pytest.raises(boundary.BoundaryRejectedError):
        boundary.enforce_council_boundary({"agent_a": ["a", "b", "c"]}, _valid_context_manifest())
