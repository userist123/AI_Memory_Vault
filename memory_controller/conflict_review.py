"""Auditable workflow for reviewing detected memory conflicts.

The workflow creates review cases only. It never mutates, deletes, promotes,
or resolves the conflicting memories automatically.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date
from typing import Any, Iterable, Mapping, Optional
import hashlib
import json


@dataclass(frozen=True)
class ConflictReviewCase:
    case_id: str
    conflict_type: str
    memory_ids: tuple[str, ...]
    reasons: tuple[str, ...]
    evidence_ids: tuple[str, ...] = ()
    status: str = "OPEN"
    recommendation: str = "VERIFY_WITH_EVIDENCE"
    as_of: Optional[str] = None
    known_as_of: Optional[str] = None

    def as_dict(self) -> dict[str, Any]:
        return {
            "case_id": self.case_id,
            "conflict_type": self.conflict_type,
            "memory_ids": list(self.memory_ids),
            "reasons": list(self.reasons),
            "evidence_ids": list(self.evidence_ids),
            "status": self.status,
            "recommendation": self.recommendation,
            "as_of": self.as_of,
            "known_as_of": self.known_as_of,
        }


class ConflictReviewWorkflow:
    """Create deterministic, append-only conflict review cases."""

    def __init__(self, evidence_lookup: Optional[Mapping[str, Iterable[str]]] = None):
        self.evidence_lookup = evidence_lookup or {}

    @staticmethod
    def _case_id(memory_ids: Iterable[str], conflict_type: str, as_of: Any, known_as_of: Any) -> str:
        payload = {
            "memory_ids": sorted(str(x) for x in memory_ids),
            "conflict_type": conflict_type,
            "as_of": str(as_of) if as_of is not None else None,
            "known_as_of": str(known_as_of) if known_as_of is not None else None,
        }
        digest = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        return f"CR-{digest[:16]}"

    def open_case(
        self,
        *,
        memory_ids: Iterable[str],
        reasons: Iterable[str],
        conflict_type: str = "semantic",
        evidence_ids: Iterable[str] = (),
        as_of: Any = None,
        known_as_of: Any = None,
    ) -> ConflictReviewCase:
        ids = tuple(sorted({str(x) for x in memory_ids if x}))
        if len(ids) < 2:
            raise ValueError("At least two memory IDs are required")
        reason_tuple = tuple(dict.fromkeys(str(x) for x in reasons if x))
        if not reason_tuple:
            raise ValueError("At least one conflict reason is required")
        evidence_tuple = tuple(dict.fromkeys(str(x) for x in evidence_ids if x))
        return ConflictReviewCase(
            case_id=self._case_id(ids, conflict_type, as_of, known_as_of),
            conflict_type=conflict_type,
            memory_ids=ids,
            reasons=reason_tuple,
            evidence_ids=evidence_tuple,
            as_of=str(as_of) if as_of is not None else None,
            known_as_of=str(known_as_of) if known_as_of is not None else None,
        )
