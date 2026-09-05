"""Security boundary helpers for direct financial-note persistence.

This module is intentionally independent from the storage engine.  Source
frontmatter is untrusted input; lifecycle and verification are controller-owned
state and must never be accepted as caller-provided privilege.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any, Dict, FrozenSet


# States whose presence in an ingestion payload could grant or imply a trust
# level that the direct-ingestion path is not allowed to establish.
_PRIVILEGED_LIFECYCLES: FrozenSet[str] = frozenset(
    {
        "ACTIVE",
        "ARCHIVED",
        "SUPERSEDED",
        "RECONSOLIDATING",
    }
)

_PRIVILEGED_VERIFICATIONS: FrozenSet[str] = frozenset(
    {
        "verified",
    }
)


def canonicalize_financial_ingest_frontmatter(
    frontmatter: Dict[str, Any],
) -> Dict[str, Any]:
    """Return a safe persistence copy for direct financial ingestion.

    The source payload is treated as untrusted.  Ingestion can create a review
    candidate, but it cannot establish an ACTIVE/ARCHIVED/SUPERSEDED/
    RECONSOLIDATING lifecycle or a ``verified`` verification state.

    A deep copy is returned so callers cannot mutate the original payload after
    the security decision and accidentally change the persisted record.
    """

    if not isinstance(frontmatter, dict):
        raise TypeError("frontmatter must be a dictionary")

    safe = deepcopy(frontmatter)
    requested_lifecycle = str(safe.get("lifecycle", "REVIEW"))
    requested_verification = str(safe.get("verification", "unverified"))

    if requested_lifecycle in _PRIVILEGED_LIFECYCLES:
        # Fail closed rather than silently elevating or preserving a privileged
        # caller-controlled state.
        raise ValueError(
            f"direct financial ingestion cannot establish lifecycle={requested_lifecycle!r}"
        )

    if requested_verification == "verified":
        raise ValueError("direct financial ingestion cannot establish verification='verified'")

    # Controller-owned canonical creation state.  This also normalizes benign
    # caller values such as partially_verified so persistence cannot accidentally
    # become a verification source of truth.
    safe["lifecycle"] = "REVIEW"
    safe["verification"] = "unverified"
    return safe
