import json
import math
import zlib
from typing import Dict, Any, List


class ContextBudgetError(RuntimeError):
    """Base error for context budget failures."""


class BudgetExceededError(ContextBudgetError):
    """Raised when the final context cannot fit within the hard budget."""


class ContextBudget:
    """Per-request context budget with deterministic byte and token limits."""

    def __init__(self, config: Dict[str, Any]):
        self.max_notes = max(1, int(config.get("max_notes", 50)))
        self.max_full_documents = max(0, int(config.get("max_full_documents", 3)))
        soft = config.get("soft_limit_bytes", config.get("soft_context_budget", config.get("soft", 16 * 1024)))
        hard = config.get("hard_limit_bytes", config.get("hard_context_budget", config.get("hard", 32 * 1024)))
        self.soft_limit_bytes = max(1, int(soft))
        self.hard_limit_bytes = int(hard)
        self.soft_limit_tokens = max(1, int(config.get("soft_limit_tokens", config.get("soft_tokens", 1800))))
        self.hard_limit_tokens = max(1, int(config.get("hard_limit_tokens", config.get("hard_tokens", 3000))))
        self.chars_per_token = max(1.0, float(config.get("chars_per_token", 3.0)))

    @property
    def soft_context_budget(self) -> int:
        return self.soft_limit_bytes

    @property
    def hard_context_budget(self) -> int:
        return self.hard_limit_bytes

    @property
    def soft_token_budget(self) -> int:
        return self.soft_limit_tokens

    @property
    def hard_token_budget(self) -> int:
        return self.hard_limit_tokens

    @staticmethod
    def _size_of(note: Dict[str, Any]) -> int:
        content = note.get("content", "")
        if isinstance(content, bytes):
            return len(content)
        return len(str(content).encode("utf-8"))

    @staticmethod
    def serialized_size(value: Any) -> int:
        if isinstance(value, bytes):
            return len(value)
        return len(json.dumps(value, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8"))

    def estimate_tokens(self, value: Any) -> int:
        text = json.dumps(value, ensure_ascii=False, default=str, separators=(",", ":"))
        return max(1, math.ceil(len(text) / self.chars_per_token))

    def usage(self, notes: List[Dict[str, Any]]) -> int:
        return sum(self._size_of(n) for n in notes)

    def check_hard_limit(self, usage: int) -> None:
        if usage > self.hard_limit_bytes:
            raise BudgetExceededError(f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes")

    def check_hard_token_limit(self, tokens: int) -> None:
        if tokens > self.hard_limit_tokens:
            raise BudgetExceededError(f"Context token estimate {tokens} exceeds hard limit {self.hard_limit_tokens} tokens")

    def check_budget(self, usage: int) -> None:
        self.check_hard_limit(usage)

    def apply_degradation(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        ordered = [dict(n) for n in sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)[:self.max_notes]]
        for index, note in enumerate(ordered):
            if index >= self.max_full_documents:
                note["content"] = ""

        while len(ordered) > 1 and (self.usage(ordered) > self.soft_limit_bytes or self.serialized_size(ordered) > self.hard_limit_bytes):
            ordered.pop()

        if ordered and self.usage(ordered) > self.soft_limit_bytes:
            for note in ordered:
                content = note.get("content", "")
                if isinstance(content, str) and len(content) > 50:
                    note["content"] = content[:50] + "...[PARTIAL]"
                    if self.usage(ordered) <= self.soft_limit_bytes:
                        break

        if ordered and self.usage(ordered) > self.soft_limit_bytes:
            for note in reversed(ordered):
                note["content"] = ""
                if self.usage(ordered) <= self.soft_limit_bytes:
                    break

        for note in ordered:
            content = note.get("content", "")
            if isinstance(content, str) and len(content.encode("utf-8")) > 1024:
                note["content"] = zlib.compress(content.encode("utf-8"))

        while len(ordered) > 1 and self.serialized_size(ordered) > self.hard_limit_bytes:
            ordered.pop()

        if ordered and self.serialized_size(ordered) > self.hard_limit_bytes:
            raise BudgetExceededError(f"Context usage exceeds hard limit {self.hard_limit_bytes} bytes")

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
