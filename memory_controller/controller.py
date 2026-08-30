from typing import Any, Dict, Optional, List

# Context-budget helpers used by the search/read pipeline.
# Existing controller implementation should import these names from this module.

# NOTE: This file is intentionally a focused runtime guard module. The canonical
# MemoryController implementation remains the source of domain behavior; callers
# should invoke `enforce_context_budget` immediately before building a ContextPack.


def _context_bytes(items: List[Dict[str, Any]]) -> int:
    import json
    return len(json.dumps(items, ensure_ascii=False, default=str, separators=(",", ":")).encode("utf-8"))


def enforce_context_budget(results: List[Dict[str, Any]], budget) -> List[Dict[str, Any]]:
    """Apply the final hard context gate before a ContextPack is emitted.

    The function never mutates the caller's result objects and guarantees that
    the returned list is within the configured hard byte budget or raises the
    canonical BudgetExceededError.
    """
    candidates = [dict(item) for item in results[:budget.max_notes]]
    candidates = budget.apply_degradation(candidates)
    budget.check_hard_limit(_context_bytes(candidates))
    return candidates
