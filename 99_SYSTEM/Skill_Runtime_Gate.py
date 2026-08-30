#!/usr/bin/env python3
"""Hard runtime gate for sparse skill loading.

The per-agent loader enforces MAX_SKILLS_PER_AGENT. Council-wide selection is
validated separately with validate_council_selection(), which enforces
MAX_TOTAL_SKILLS across all agents without making the per-agent limit appear to
be a Council-wide limit.
"""

from __future__ import annotations

from collections.abc import Iterable, Mapping
from pathlib import Path

MAX_SKILLS_PER_AGENT = 2
MAX_TOTAL_SKILLS = 4


class SkillBudgetError(ValueError):
    pass


def _normalise_ids(skill_ids: Iterable[str]) -> list[str]:
    ids = [str(value).strip() for value in skill_ids if str(value).strip()]
    if len(ids) != len(set(ids)):
        duplicates = sorted({item for item in ids if ids.count(item) > 1})
        raise SkillBudgetError("duplicate selected skills: " + ", ".join(duplicates))
    if len(ids) > MAX_SKILLS_PER_AGENT:
        raise SkillBudgetError(
            f"too many skills for agent: {len(ids)} > {MAX_SKILLS_PER_AGENT}"
        )
    return ids


def validate_council_selection(agent_skills: Mapping[str, Iterable[str]]) -> dict[str, list[str]]:
    """Validate skill selection per agent and across the whole Council.

    Each agent may select at most MAX_SKILLS_PER_AGENT skills. The Council may
    select at most MAX_TOTAL_SKILLS unique skills in total. A skill selected by
    two agents counts once toward the Council-wide unique-skill limit, while
    each agent is still subject to its own per-agent limit.
    """
    normalised: dict[str, list[str]] = {}
    all_skills: set[str] = set()

    for agent, skills in agent_skills.items():
        agent_id = str(agent).strip()
        if not agent_id:
            raise SkillBudgetError("agent id must not be empty")
        selected = _normalise_ids(skills)
        normalised[agent_id] = selected
        all_skills.update(selected)

    if len(all_skills) > MAX_TOTAL_SKILLS:
        raise SkillBudgetError(
            f"too many unique Council skills: {len(all_skills)} > {MAX_TOTAL_SKILLS}"
        )

    return normalised


def load_selected_skills(skill_root: str | Path, skill_ids: Iterable[str]) -> dict[str, str]:
    """Load only explicitly selected SKILL.md files for one agent."""
    ids = _normalise_ids(skill_ids)
    root = Path(skill_root).resolve()
    loaded: dict[str, str] = {}

    for skill_id in ids:
        candidate = (root / skill_id / "SKILL.md").resolve()
        try:
            candidate.relative_to(root)
        except ValueError as exc:
            raise SkillBudgetError(f"skill path escapes registry: {skill_id}") from exc
        if not candidate.is_file():
            raise SkillBudgetError(f"selected skill not found: {skill_id}")
        loaded[skill_id] = candidate.read_text(encoding="utf-8")

    return loaded


def build_selection_manifest(skill_ids: Iterable[str]) -> dict:
    """Return a compact per-agent selection manifest."""
    ids = _normalise_ids(skill_ids)
    return {
        "selected_skills": ids,
        "count": len(ids),
        "max_per_agent": MAX_SKILLS_PER_AGENT,
        "council_max_unique": MAX_TOTAL_SKILLS,
    }
