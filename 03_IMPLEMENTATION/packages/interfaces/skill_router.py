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

_PROMPT_COMPILER_ACTION_TOKENS = {
    # English action & compilation triggers
    "build", "implement", "fix", "repair", "debug", "optimize", "improve",
    "performant", "audit", "review", "investigate", "research", "refactor",
    "migrate", "analyze", "verify", "prompt", "compiler", "executor", "compile",
    # Romanian action & compilation triggers (ASCII + normalized diacritics)
    "vreau", "imbunatatesc", "imbunatateste", "construieste", "aplicatie",
    "analiza", "investigheaza", "consuma", "tokenuri", "repara", "problema",
    "verifica", "remedierea", "functioneaza", "implementeaza", "optimizeaza",
    "auditeaza", "refactorizeaza", "cerceteaza",
}

_TRIVIAL_PREFIX_RE = re.compile(
    r"^\s*(?:what\s+is|what\s+does|who\s+is|where\s+is|which\s+branch|what\s+branch|"
    r"ce\s+este|ce\s+face|care\s+este|unde\s+este|pe\s+ce\s+branch|salut|hello|hi|ping)\b",
    re.I,
)

_DIACRITIC_MAP = str.maketrans("ăâîșțĂÂÎȘȚ", "aaistAAIST")


def _tokenize(text: str) -> set:
    normalized = text.translate(_DIACRITIC_MAP).lower()
    tokens = re.findall(r"[a-zA-Z0-9]+", normalized)
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
        is_trivial_query = bool(_TRIVIAL_PREFIX_RE.search(task))
        matches: List[SkillMatch] = []
        for skill in self.list_skills():
            skill_tokens = _tokenize(skill.replace("-", " ").replace("_", " "))
            if not skill_tokens:
                continue
            overlap = task_tokens & skill_tokens
            score = (
                len(overlap) / len(skill_tokens | task_tokens)
                if overlap
                else 0.0
            )
            if skill == "prompt-compiler-executor" and not is_trivial_query:
                action_hits = task_tokens & _PROMPT_COMPILER_ACTION_TOKENS
                if action_hits:
                    trigger_score = min(0.95, 0.55 + 0.1 * len(action_hits))
                    score = max(score, trigger_score)
            if score <= 0.0:
                continue
            matches.append(SkillMatch(skill=skill, score=round(score, 4)))
        matches.sort(key=lambda m: m.score, reverse=True)
        return matches[:top_k]

