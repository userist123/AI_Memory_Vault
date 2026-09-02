"""memory_controller/task_categories.py — Controlled Task Category Vocabulary.

Enforces a strict, non-expandable controlled vocabulary for categorization.
Never allows arbitrary strings or automatic expansion from LLM prose.
"""
from __future__ import annotations

from enum import Enum
from typing import Optional, Set


class TaskCategory(str, Enum):
    FRONTEND_MOTION = "frontend_motion"
    FRONTEND_LAYOUT = "frontend_layout"
    BACKEND_API = "backend_api"
    DATABASE = "database"
    SECURITY_AUDIT = "security_audit"
    TRADING_LOGIC = "trading_logic"
    DOCUMENTATION = "documentation"
    TESTING = "testing"
    INFRA_DEVOPS = "infra_devops"
    UNKNOWN = "unknown"


VALID_TASK_CATEGORIES: Set[str] = {c.value for c in TaskCategory}


def validate_task_category(category: Optional[str]) -> str:
    """Validates and normalizes task category against controlled vocabulary.

    Raises ValueError if category is not in VALID_TASK_CATEGORIES.
    Returns 'unknown' if None or empty.
    """
    if category is None or str(category).strip() == "":
        return TaskCategory.UNKNOWN.value

    cat_str = str(category).strip().lower()
    if cat_str not in VALID_TASK_CATEGORIES:
        raise ValueError(
            f"Invalid task_category '{category}'. Must be one of {sorted(VALID_TASK_CATEGORIES)}"
        )
    return cat_str
