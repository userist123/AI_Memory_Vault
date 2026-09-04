"""Routes a natural-language task description to matching operational skills.

Read-only over the .agents/skills/ directory listing. Uses simple token
overlap scoring against skill directory names (which are highly descriptive
by convention in this vault) so it works with zero extra dependencies and
without loading every SKILL.md into memory.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import List
import re

_STOPWORDS = {"the", "a", "an", "for", "to", "of", "and", "with", "in", "on",
              "un", "o", "de", "la", "cu", "si", "pentru", "in"}


def _tokenize(text: str) -> set:
    tokens = re.findall(r"[a-zA-Z0-9]+", text.lower())
    return {t for t in tokens if t not in _STOPWORDS and len(t) > 1}


@dataclass
class SkillMatch:
    skill: str
    score: float


class SkillRouter:
    def __init__(self, skills_root: str | Path):
        self.root = Path(skills_root)

    def list_skills(self) -> List[str]:
        if not self.root.exists():
            return []
        return sorted(p.name for p in self.root.iterdir() if p.is_dir())

    def route(self, task: str, top_k: int = 5) -> List[SkillMatch]:
        task_tokens = _tokenize(task)
        if not task_tokens:
            return []
        matches: List[SkillMatch] = []
        for skill in self.list_skills():
            skill_tokens = _tokenize(skill.replace("-", " ").replace("_", " "))
            if not skill_tokens:
                continue
            overlap = task_tokens & skill_tokens
            if not overlap:
                continue
            score = len(overlap) / len(skill_tokens | task_tokens)
            matches.append(SkillMatch(skill=skill, score=round(score, 4)))
        matches.sort(key=lambda m: m.score, reverse=True)
        return matches[:top_k]
