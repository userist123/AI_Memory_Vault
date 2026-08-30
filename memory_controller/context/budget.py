import json
import zlib
from typing import Dict, Any, List


class BudgetExceededError(RuntimeError):
    """Raised when the context cannot be fitted within the hard byte limit."""


class ContextBudgetError(BudgetExceededError):
    """Backward-compatible alias for budget failures."""


class ContextBudget:
    """Per-request context budget with hard limits and deterministic degradation."""

    def __init__(self, config: Dict[str, Any]):
        self.max_notes = int(config.get("max_notes", 5))
        self.max_full_documents = int(config.get("max_full_documents", 3))
        self.soft_limit_bytes = int(config.get("soft_limit_bytes", config.get("soft_context_budget", 16 * 1024)))
        self.hard_limit_bytes = int(config.get("hard_limit_bytes", config.get("hard_context_budget", 32 * 1024)))
        if self.max_notes < 1 or self.max_full_documents < 0:
            raise ValueError("Invalid context note limits")
        if self.hard_limit_bytes <= 0 or self.soft_limit_bytes <= 0 or self.soft_limit_bytes > self.hard_limit_bytes:
            raise ValueError("Invalid context byte limits")

    @property
    def soft_context_budget(self) -> int:
        return self.soft_limit_bytes

    @property
    def hard_context_budget(self) -> int:
        return self.hard_limit_bytes

    def _size_of(self, note: Dict[str, Any]) -> int:
        content = note.get("content", "")
        if isinstance(content, bytes):
            return len(content)
        return len(str(content).encode("utf-8"))

    def usage(self, notes: List[Dict[str, Any]]) -> int:
        return sum(self._size_of(n) for n in notes)

    def check_hard_limit(self, usage: int) -> None:
        if usage > self.hard_limit_bytes:
            raise BudgetExceededError(f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes")

    def check_budget(self, usage: int) -> None:
        self.check_hard_limit(usage)

    def apply_degradation(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Keep the highest-value notes and deterministically fit the soft/hard budget."""
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)[:self.max_notes]

        for index, note in enumerate(ordered):
            if index >= self.max_full_documents:
                note["content"] = ""

        while self.usage(ordered) > self.soft_limit_bytes and len(ordered) > 1:
            ordered.pop()

        # Degrade remaining content without destroying metadata/provenance.
        if self.usage(ordered) > self.soft_limit_bytes:
            for note in ordered:
                content = note.get("content", "")
                if isinstance(content, str) and len(content) > 256:
                    note["content"] = content[:256] + "...[PARTIAL]"
                    if self.usage(ordered) <= self.soft_limit_bytes:
                        break

        if self.usage(ordered) > self.soft_limit_bytes:
            for note in reversed(ordered):
                if self.usage(ordered) <= self.soft_limit_bytes:
                    break
                note["content"] = ""

        self.check_hard_limit(self.usage(ordered))
        return ordered

    def enforce_max_full(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)
        for index, note in enumerate(ordered):
            if index >= self.max_full_documents:
                note["content"] = ""
        return ordered[:self.max_notes]


def load_agent_budget(agent_id: str, config_path: str = "config/agent_budgets.json") -> ContextBudget:
    """Load an agent-specific budget; absent config safely uses sparse defaults."""
    try:
        with open(config_path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
        agent_cfg = data.get(agent_id, {})
    except (FileNotFoundError, json.JSONDecodeError):
        agent_cfg = {}
    return ContextBudget(agent_cfg)
