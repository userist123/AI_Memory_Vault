"""Security boundary helpers for direct financial-note persistence.

This module is intentionally independent from the storage engine. Source
frontmatter is untrusted input; lifecycle and verification are controller-owned
state and must never be accepted as caller-provided privilege.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any, Dict, FrozenSet


_PRIVILEGED_LIFECYCLES: FrozenSet[str] = frozenset(
    {
        "VERIFIED",
        "ACTIVE",
        "ARCHIVED",
        "SUPERSEDED",
        "RECONSOLIDATING",
    }
)

_PRIVILEGED_VERIFICATIONS: FrozenSet[str] = frozenset({"VERIFIED"})


def canonicalize_financial_ingest_frontmatter(
    frontmatter: Dict[str, Any],
) -> Dict[str, Any]:
    """Return a safe persistence copy for direct financial ingestion.

    Direct ingestion can create a review candidate, but it cannot establish
    any privileged lifecycle or a verified verification state. The input is
    deep-copied before normalization so the caller retains its original data.
    """

    if not isinstance(frontmatter, dict):
        raise TypeError("frontmatter must be a dictionary")

    safe = deepcopy(frontmatter)
    requested_lifecycle = str(safe.get("lifecycle", "REVIEW")).strip().upper()
    requested_verification = str(safe.get("verification", "unverified")).strip().upper()

    if requested_lifecycle in _PRIVILEGED_LIFECYCLES:
        raise ValueError(
            f"direct financial ingestion cannot establish lifecycle={requested_lifecycle!r}"
        )

    if requested_verification in _PRIVILEGED_VERIFICATIONS:
        raise ValueError("direct financial ingestion cannot establish verification='verified'")

    safe["lifecycle"] = "REVIEW"
    safe["verification"] = "unverified"
    return safe
