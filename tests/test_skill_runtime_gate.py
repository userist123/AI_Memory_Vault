import importlib.util
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "99_SYSTEM" / "Skill_Runtime_Gate.py"
spec = importlib.util.spec_from_file_location("skill_runtime_gate", MODULE)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def test_rejects_more_than_two_skills_for_one_agent():
    with pytest.raises(gate.SkillBudgetError):
        gate.build_selection_manifest(["a", "b", "c"])


def test_allows_two_skills_per_agent():
    manifest = gate.build_selection_manifest(["a", "b"])
    assert manifest["count"] == 2
    assert manifest["max_per_agent"] == 2
    assert manifest["council_max_unique"] == 4


def test_allows_four_unique_skills_across_two_agents():
    selection = gate.validate_council_selection({
        "agent_a": ["a", "b"],
        "agent_b": ["c", "d"],
    })
    assert selection == {
        "agent_a": ["a", "b"],
        "agent_b": ["c", "d"],
    }


def test_shared_skill_counts_once_for_council_total():
    selection = gate.validate_council_selection({
        "agent_a": ["a", "b"],
        "agent_b": ["b", "c"],
    })
    assert selection["agent_a"] == ["a", "b"]
    assert selection["agent_b"] == ["b", "c"]


def test_rejects_more_than_four_unique_council_skills():
    with pytest.raises(gate.SkillBudgetError, match="unique Council skills"):
        gate.validate_council_selection({
            "agent_a": ["a", "b"],
            "agent_b": ["c", "d"],
            "agent_c": ["e"],
        })


def test_rejects_duplicate_skills():
    with pytest.raises(gate.SkillBudgetError):
        gate.build_selection_manifest(["a", "a"])


def test_loads_only_explicit_skills(tmp_path):
    for name in ("a", "b", "unused"):
        folder = tmp_path / name
        folder.mkdir()
        (folder / "SKILL.md").write_text(f"# {name}\n", encoding="utf-8")

    loaded = gate.load_selected_skills(tmp_path, ["a", "b"])
    assert set(loaded) == {"a", "b"}
    assert "unused" not in loaded


def test_path_traversal_fails_closed(tmp_path):
    with pytest.raises(gate.SkillBudgetError):
        gate.load_selected_skills(tmp_path, ["../outside"])
