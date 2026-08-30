import json
from typing import Dict, Any, List


class BudgetExceededError(RuntimeError):
    """Raised when the final context cannot fit within the hard byte limit."""


class ContextBudgetError(BudgetExceededError):
    """Backward-compatible alias for budget failures."""


class ContextBudget:
    """Per-request context budget with deterministic degradation."""

    def __init__(self, config: Dict[str, Any]):
        self.max_notes = max(1, int(config.get("max_notes", 5)))
        self.max_full_documents = max(0, int(config.get("max_full_documents", 2)))
        self.soft_limit_bytes = max(1, int(config.get("soft_limit_bytes", 12 * 1024)))
        self.hard_limit_bytes = max(self.soft_limit_bytes, int(config.get("hard_limit_bytes", 24 * 1024)))

    @property
    def soft_context_budget(self) -> int:
        return self.soft_limit_bytes

    @property
    def hard_context_budget(self) -> int:
        return self.hard_limit_bytes

    @staticmethod
    def _size_of(note: Dict[str, Any]) -> int:
        return len(json.dumps(note, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8"))

    def usage(self, notes: List[Dict[str, Any]]) -> int:
        return sum(self._size_of(n) for n in notes)

    def check_hard_limit(self, usage: int) -> None:
        if usage > self.hard_limit_bytes:
            raise BudgetExceededError(f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes")

    def check_budget(self, usage: int) -> None:
        self.check_hard_limit(usage)

    def apply_degradation(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        ordered = [dict(n) for n in sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)[:self.max_notes]]
        for index, note in enumerate(ordered):
            if index >= self.max_full_documents:
                note["content"] = ""

        while len(ordered) > 1 and self.usage(ordered) > self.soft_limit_bytes:
            ordered.pop()

        if ordered and self.usage(ordered) > self.soft_limit_bytes:
            for note in ordered:
                content = note.get("content", "")
                if isinstance(content, str) and len(content) > 256:
                    note["content"] = content[:256] + "...[PARTIAL]"
                    if self.usage(ordered) <= self.soft_limit_bytes:
                        break

        if ordered and self.usage(ordered) > self.soft_limit_bytes:
            for note in reversed(ordered):
                if self.usage(ordered) <= self.soft_limit_bytes:
                    break
                note["content"] = ""

        self.check_hard_limit(self.usage(ordered))
        return ordered

    def enforce_max_full(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)[:self.max_notes]
        for index, note in enumerate(ordered):
            if index >= self.max_full_documents:
                note["content"] = ""
        return ordered


def load_agent_budget(agent_id: str, config_path: str = "config/agent_budgets.json") -> ContextBudget:
    try:
        with open(config_path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
        return ContextBudget(data.get(agent_id, data.get("default", {})))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return ContextBudget({})
