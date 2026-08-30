"""Authorized, auditable verdicts for conflict review.

Verdicts never mutate memory. They authorize a later, explicit mutation step
and are bound to the verified evidence bundle and temporal snapshot.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Iterable, Optional
import hashlib
import json

from .authorizer import Authorizer, DefaultAuthorizer, Operation, Principal


class Verdict(str, Enum):
    ACCEPT_A = "ACCEPT_A"
    ACCEPT_B = "ACCEPT_B"
    REJECT_BOTH = "REJECT_BOTH"
    DEFER = "DEFER"


@dataclass(frozen=True)
class AuthorizedVerdict:
    verdict_id: str
    verdict: Verdict
    reviewer: str
    reviewer_principal: str
    memory_ids: tuple[str, ...]
    evidence_bundle_hash: str
    as_of: Optional[str]
    known_as_of: Optional[str]
    evidence_valid: bool
    reason: str
    issued_at: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "verdict_id": self.verdict_id,
            "verdict": self.verdict.value,
            "reviewer": self.reviewer,
            "reviewer_principal": self.reviewer_principal,
            "memory_ids": list(self.memory_ids),
            "evidence_bundle_hash": self.evidence_bundle_hash,
            "as_of": self.as_of,
            "known_as_of": self.known_as_of,
            "evidence_valid": self.evidence_valid,
            "reason": self.reason,
            "issued_at": self.issued_at,
        }


class AuthorizedVerdictEngine:
    """Issue verdict records only; does not call promote/attest/supersede."""

    def __init__(self, authorizer: Optional[Authorizer] = None):
        self.authorizer = authorizer or DefaultAuthorizer()

    @staticmethod
    def _verdict_id(memory_ids: Iterable[str], evidence_bundle_hash: str, verdict: Verdict, reviewer: str) -> str:
        payload = {
            "memory_ids": sorted(str(x) for x in memory_ids),
            "evidence_bundle_hash": evidence_bundle_hash,
            "verdict": verdict.value,
            "reviewer": reviewer,
        }
        digest = hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        return f"VR-{digest[:16]}"

    def issue(
        self,
        *,
        principal: Principal,
        reviewer: str,
        verdict: Verdict | str,
        memory_ids: Iterable[str],
        evidence_bundle_hash: str,
        evidence_valid: bool,
        reason: str,
        as_of: Any = None,
        known_as_of: Any = None,
    ) -> AuthorizedVerdict:
        self.authorizer_check(principal)
        normalized = Verdict(verdict)
        ids = tuple(sorted({str(x) for x in memory_ids if x}))
        if len(ids) < 2:
            raise ValueError("At least two memory IDs are required")
        if not reviewer.strip():
            raise ValueError("Reviewer identity is required")
        if not evidence_bundle_hash.strip():
            raise ValueError("Evidence bundle hash is required")
        if not evidence_valid:
            raise ValueError("Cannot issue an authorized verdict against invalid evidence")
        if not reason.strip():
            raise ValueError("Verdict reason is required")
        return AuthorizedVerdict(
            verdict_id=self._verdict_id(ids, evidence_bundle_hash, normalized, reviewer),
            verdict=normalized,
            reviewer=reviewer.strip(),
            reviewer_principal=principal.value,
            memory_ids=ids,
            evidence_bundle_hash=evidence_bundle_hash,
            as_of=str(as_of) if as_of is not None else None,
            known_as_of=str(known_as_of) if known_as_of is not None else None,
            evidence_valid=True,
            reason=reason.strip(),
            issued_at=datetime.now(timezone.utc).isoformat(),
        )

    def authorizer_check(self, principal: Principal) -> None:
        if not self.authorizer.is_allowed(principal, Operation.REVIEW):
            raise PermissionError(f"{principal.value} is not authorized to issue conflict verdicts")
