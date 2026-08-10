import json
import zlib
from typing import Dict, Any, List


class BudgetExceededError(RuntimeError):
    """Raised when the context cannot be fitted within the hard byte limit."""

    pass

class ContextBudgetError(RuntimeError):
    """Raised when the context exceeds the hard limit (alias for BudgetExceededError)."""
    pass


class ContextBudget:
    """Manage context budgets per request using UTF-8 byte measurements.

    Parameters
    ----------
    config: Dict[str, Any]
        Expected keys:
        - soft_limit_bytes (int): advisory soft limit.
        - hard_limit_bytes (int): hard limit that must never be exceeded.
        - max_full_documents (int): maximum number of notes allowed full disclosure.
    """

    def __init__(self, config: Dict[str, Any]):
        # Default values align with previous character‑based defaults but expressed in bytes.
        # Initialize budget limits and note limits
        self.max_notes = config.get("max_notes", 50)  # default max notes for a query
        # Alias for backward compatibility
        self.max_full_documents = config.get("max_full_documents", 3)
        self.soft_limit_bytes = config.get("soft_limit_bytes", config.get("soft_context_budget", 16 * 1024))
        self.hard_limit_bytes = config.get("hard_limit_bytes", config.get("hard_context_budget", 32 * 1024))

    # ---------------------------------------------------------------------
    # Compatibility properties (used by existing controller code)
    # ---------------------------------------------------------------------
    @property
    def soft_context_budget(self) -> int:
        """Alias for backward compatibility with existing controller expectations."""
        return self.soft_limit_bytes

    @property
    def hard_context_budget(self) -> int:
        """Alias for backward compatibility with existing controller expectations."""
        return self.hard_limit_bytes

    # ---------------------------------------------------------------------
    # Budget enforcement helpers
    # ---------------------------------------------------------------------
    def _size_of(self, note: Dict[str, Any]) -> int:
        """Return the UTF‑8 byte size of a note's content.

        The note is expected to contain a ``content`` field (string). If the field is missing,
        size is considered 0. Provenance fields are *not* counted toward the budget because they
        are stored separately in the final pack.
        """
        content = note.get("content", "")
        if isinstance(content, bytes):
            # Already compressed – use its length directly.
            return len(content)
        return len(str(content).encode("utf-8"))

    def check_hard_limit(self, usage: int) -> None:
        """Raise :class:`BudgetExceededError` if ``usage`` exceeds the hard byte limit.
        """
        if usage > self.hard_limit_bytes:
            raise BudgetExceededError(
                f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes"
            )

    def check_budget(self, usage: int) -> None:
        """Alias for backward compatibility: raise :class:`ContextBudgetError` if usage exceeds hard limit.
        """
        if usage > self.hard_limit_bytes:
            raise ContextBudgetError(
                f"Context usage {usage} exceeds hard limit {self.hard_limit_bytes} bytes"
            )

    # ---------------------------------------------------------------------
    # Degradation algorithm
    def apply_degradation(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Apply deterministic degradation to fit within soft and hard limits.

        Steps:
        1. Sort notes by relevance descending.
        2. Enforce max_full_documents: allow at most N notes to stay FULL.
        3. If soft limit exceeded, downgrade lower‑relevance notes first.
        4. Degrade FULL → PARTIAL (truncated with marker) → METADATA_ONLY as needed.
        5. Compress large contents (>1 KiB) internally with zlib.
        6. Enforce hard limit (raise BudgetExceededError).
        """
        # Step 1: sort notes by relevance descending
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)

        # Step 2: enforce max_full_documents (initially keep full for top N)
        for i, note in enumerate(ordered):
            if i >= self.max_full_documents:
                note["content"] = ""

        def total_usage(ns: List[Dict[str, Any]]) -> int:
            return sum(self._size_of(n) for n in ns)

        # Step 3: drop notes entirely if still over soft and we have more than max_full_documents notes
        while total_usage(ordered) > self.soft_limit_bytes and len(ordered) > self.max_full_documents:
            ordered.pop()  # remove lowest‑relevance note

        # Step 4: degrade remaining top notes if still over soft limit
        for note in ordered[:self.max_full_documents]:
            if total_usage(ordered) <= self.soft_limit_bytes:
                break
            content = note.get("content", "")
            if isinstance(content, str) and len(content) > 0:
                # Create a PARTIAL version: truncate to 50 chars and add marker
                truncated = content[:50] + "...[PARTIAL]"
                note["content"] = truncated
                # If still exceeds soft after truncation, fall back to METADATA_ONLY
                if total_usage(ordered) > self.soft_limit_bytes:
                    note["content"] = ""
            else:
                # Already empty, nothing to do
                continue

        # Step 5: compress large contents internally (after possible truncation)
        for note in ordered:
            content = note.get("content", "")
            if isinstance(content, str) and len(content.encode("utf-8")) > 1024:
                note["content"] = zlib.compress(content.encode("utf-8"))

        # Step 6: enforce hard limit
        self.check_hard_limit(total_usage(ordered))
        return ordered
        """Apply deterministic degradation to fit within soft and hard limits.

        Steps:
        1. Sort notes by relevance descending.
        2. Enforce max_full_documents: keep full content for top N, clear others.
        3. If still over soft limit, drop lowest‑relevant notes until within soft or only max_full_documents remain.
        4. If still over soft, clear content of remaining notes (starting from lowest relevance) until within soft.
        5. Compress large contents (>1 KiB) internally with zlib.
        6. Enforce hard limit; raise BudgetExceededError if exceeded.
        """
        # Step 1: sort notes by relevance descending
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)

        # Step 2: enforce max_full_documents
        for i, note in enumerate(ordered):
            if i >= self.max_full_documents:
                note["content"] = ""

        def total_usage(ns: List[Dict[str, Any]]) -> int:
            return sum(self._size_of(n) for n in ns)

        # Step 3: drop notes if over soft limit
        while total_usage(ordered) > self.soft_limit_bytes and len(ordered) > self.max_full_documents:
            ordered.pop()  # remove least relevant note

        # Step 4: clear content of remaining notes if still over soft limit
        for note in ordered[:self.max_full_documents]:
            if total_usage(ordered) <= self.soft_limit_bytes:
                break
            note["content"] = ""

        # Step 5: compress large contents internally
        for note in ordered:
            content = note.get("content", "")
            if isinstance(content, str) and len(content.encode("utf-8")) > 1024:
                note["content"] = zlib.compress(content.encode("utf-8"))

        # Step 6: enforce hard limit
        self.check_hard_limit(total_usage(ordered))
        return ordered
    

    # ---------------------------------------------------------------------
    # Utility for max_full_documents enforcement (called by callers as needed).
    # ---------------------------------------------------------------------
    def enforce_max_full(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Ensure no more than ``max_full_documents`` notes retain full content.

        Higher‑relevance notes keep full content; lower‑relevance notes have their ``content``
        replaced with an empty string (metadata only). The function returns the mutated list.
        """
        ordered = sorted(notes, key=lambda n: n.get("relevance", 0), reverse=True)
        for i, note in enumerate(ordered):
            if i >= self.max_full_documents:
                note["content"] = ""
        return ordered


def load_agent_budget(agent_id: str, config_path: str = "config/agent_budgets.json") -> ContextBudget:
    """Load a JSON config for the given agent and return a :class:`ContextBudget`.

    Missing files or entries fall back to defaults defined in :class:`ContextBudget`.
    """
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        agent_cfg = data.get(agent_id, {})
    except FileNotFoundError:
        agent_cfg = {}
    return ContextBudget(agent_cfg)

    # Removed duplicated legacy definitions that conflicted with the primary ContextBudget implementation.
