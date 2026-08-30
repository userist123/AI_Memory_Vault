import importlib.util
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "99_SYSTEM" / "Council_Context_Validator.py"

spec = importlib.util.spec_from_file_location("council_validator", MODULE_PATH)
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


def test_valid_sparse_context_passes():
    data = {
        "agents": [
            {
                "id": "backend_systems_engineer",
                "role": "primary",
                "skills": [{"id": "backend.skill"}],
                "memory_results": [{"id": "m1", "content": "relevant"}],
            },
            {
                "id": "secops_auditor",
                "role": "support",
                "skills": [{"id": "security.skill"}],
            },
        ],
        "graph_hops": 1,
        "specialist_output_tokens": 100,
        "synthesis_input_tokens": 500,
    }
    assert validator.validate(data) == []


def test_duplicate_skill_is_rejected():
    data = {
        "agents": [
            {"id": "a", "skills": [{"id": "same"}]},
            {"id": "b", "skills": [{"id": "same"}]},
        ]
    }
    errors = validator.validate(data)
    assert any("duplicate selected skills" in error for error in errors)


def test_broad_context_flags_are_rejected():
    data = {"agents": [], "whole_vault": True, "load_council_map": True}
    errors = validator.validate(data)
    assert any("whole-vault" in error for error in errors)
    assert any("Council map" in error for error in errors)


def test_serialized_hard_budget_is_enforced():
    data = {"agents": [], "memory_results": [{"content": "x" * 2000}]}
    errors = validator.validate(data, hard_context_bytes=500)
    assert any("serialized context too large" in error for error in errors)
