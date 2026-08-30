#!/usr/bin/env python3
"""Hard runtime gate for sparse skill loading.

This module deliberately does not load or index the whole skill registry.
The caller supplies already-selected skill IDs; this gate enforces the runtime
limit and loads only those SKILL.md files.
"""

from __future__ import annotations

from pathlib import Path
from typing import Iterable

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
    if len(ids) > MAX_TOTAL_SKILLS:
        raise SkillBudgetError(
            f"too many selected skills: {len(ids)} > {MAX_TOTAL_SKILLS}"
        )
    return ids


def load_selected_skills(skill_root: str | Path, skill_ids: Iterable[str]) -> dict[str, str]:
    """Load only explicitly selected SKILL.md files.

    No registry-wide scan is performed. Path traversal and missing skills fail
    closed rather than falling back to loading additional skills.
    """
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
    """Return a compact machine-readable selection manifest."""
    ids = _normalise_ids(skill_ids)
    return {
        "selected_skills": ids,
        "count": len(ids),
        "max_per_agent": MAX_SKILLS_PER_AGENT,
        "max_total": MAX_TOTAL_SKILLS,
    }
