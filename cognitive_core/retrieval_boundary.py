"""cognitive_core/retrieval_boundary.py — P1.3 Retrieval Runtime Integration Boundary.

Defines a pure, fail-closed integration adapter between MemoryController and HybridRetriever.

Contract Rule:
    "Caller may narrow, never broaden the trust boundary."

The adapter enforces:
1. Principal validation: caller principal must be present and recognized (HUMAN, AI_AGENT, ADMIN).
2. Filter validation & normalization: lifecycle and verification filters are strictly validated.
3. Fail-closed boundary:
   - ALLOWED_SECURE_LIFECYCLES = {"ACTIVE"}
   - ALLOWED_SECURE_VERIFICATION = {"verified"}
   - RAW, REVIEW, ARCHIVED, SUPERSEDED, RECONSOLIDATING, NONE are strictly excluded.
   - Any attempt to include states outside the secure boundary raises BoundaryViolationError.
4. Determinism: retrieval order is strictly deterministic (RRF score desc, note ID asc).
5. Zero storage mutation: adapter is purely read-only and executes no writes.
6. Non-authorizing: adapter validates structure and trust boundary invariants; authorization
   decisions (Operation.SEARCH) remain with MemoryController.
"""
from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set

from .hybrid_retrieval import (
    ALLOWED_SECURE_LIFECYCLES,
    ALLOWED_SECURE_VERIFICATION,
    Hit,
    HybridRetriever,
    SecureFilterViolation,
)
from .vault_index import Note


# ---------------------------------------------------------------------------
# Recognized taxonomies for boundary validation
# ---------------------------------------------------------------------------

KNOWN_PRINCIPALS: frozenset[str] = frozenset({
    "human",
    "ai_agent",
    "admin",
})

# Full lifecycle taxonomy known across the vault and MemoryController
KNOWN_LIFECYCLES: frozenset[str] = frozenset({
    "ACTIVE",
    "REVIEW",
    "ARCHIVED",
    "RAW",
    "RECONSOLIDATING",
    "SUPERSEDED",
    "CLASSIFIED",
    "NORMALIZED",
    "VERIFIED",
    "NONE",
})

KNOWN_VERIFICATIONS: frozenset[str] = frozenset({
    "verified",
    "unverified",
    "ai_generated",
    "unknown",
})


# ---------------------------------------------------------------------------
# Exceptions
# ---------------------------------------------------------------------------

class RetrievalBoundaryError(ValueError):
    """Base exception for all retrieval integration boundary errors."""
    pass


class PrincipalValidationError(RetrievalBoundaryError):
    """Raised when the requesting principal is missing, invalid, or unrecognized."""
    pass


class FilterValidationError(RetrievalBoundaryError):
    """Raised when filters contain unrecognized tokens or empty filter sets."""
    pass


class BoundaryViolationError(RetrievalBoundaryError):
    """Raised when a caller attempts to widen the secure trust boundary.

    Enforces the invariant: "caller may narrow, never broaden trust boundary".
    """
    pass


# ---------------------------------------------------------------------------
# Request & Response Protocol
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class RetrievalRequest:
    """Neutral retrieval request structure presented to the retrieval boundary."""
    query: str
    principal: Any  # Principal enum instance or str ('human', 'ai_agent', 'admin')
    top_k: int = 10
    lifecycles: Optional[Iterable[str]] = None
    types: Optional[Iterable[str]] = None
    verification: Optional[Iterable[str]] = None
    audit_ref: Optional[str] = None
    request_id: Optional[str] = None


@dataclass
class RetrievalResponse:
    """Deterministic, audit-ready retrieval response emitted across boundary."""
    hits: List[Hit]
    total_hits: int
    query: str
    principal: str
    effective_lifecycles: List[str]
    effective_verification: List[str]
    effective_types: Optional[List[str]]
    trace: Dict[str, Any]
    audit_ref: Optional[str] = None
    request_id: Optional[str] = None
    timestamp_utc: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())


# ---------------------------------------------------------------------------
# Retrieval Boundary Adapter
# ---------------------------------------------------------------------------

class RetrievalBoundaryAdapter:
    """Pure boundary adapter connecting MemoryController to HybridRetriever.

    Does not modify storage. Does not perform secondary authorization.
    Validates structural invariants and prevents trust boundary escape.
    """

    def __init__(self, retriever: Optional[HybridRetriever] = None):
        self.retriever = retriever

    @staticmethod
    def normalize_principal(principal: Any) -> str:
        """Validate and normalize caller principal to canonical string."""
        if principal is None:
            raise PrincipalValidationError("Principal is required for retrieval request.")

        if hasattr(principal, "value"):
            raw = principal.value
        elif isinstance(principal, str):
            raw = principal
        else:
            raise PrincipalValidationError(
                f"Invalid principal type: {type(principal).__name__}. Expected Principal enum or str."
            )

        val = str(raw).strip().lower()
        if not val:
            raise PrincipalValidationError("Principal cannot be empty string.")

        if val not in KNOWN_PRINCIPALS:
            raise PrincipalValidationError(
                f"Unknown principal {val!r}. Recognized principals: {sorted(KNOWN_PRINCIPALS)}"
            )
        return val

    @classmethod
    def validate_and_normalize_filters(
        cls,
        lifecycles: Optional[Iterable[str]],
        verification: Optional[Iterable[str]],
        types: Optional[Iterable[str]],
    ) -> tuple[frozenset[str], frozenset[str], Optional[List[str]]]:
        """Enforce: 'caller may narrow, never broaden trust boundary'."""

        # 1. Lifecycles
        if lifecycles is not None:
            raw_lc = [str(x).strip().upper() for x in lifecycles if str(x).strip()]
            if not raw_lc:
                raise FilterValidationError(
                    "lifecycles filter cannot be empty list/set (empty set would bypass boundary)."
                )
            caller_lc = set(raw_lc)

            unknown_lc = caller_lc - KNOWN_LIFECYCLES
            if unknown_lc:
                raise FilterValidationError(
                    f"Unknown lifecycle values: {sorted(unknown_lc)}. Known: {sorted(KNOWN_LIFECYCLES)}"
                )

            # Check broadening violation
            excess_lc = caller_lc - ALLOWED_SECURE_LIFECYCLES
            if excess_lc:
                raise BoundaryViolationError(
                    f"Broadening violation: requested lifecycles {sorted(excess_lc)} are outside "
                    f"secure boundary {sorted(ALLOWED_SECURE_LIFECYCLES)}. "
                    "Caller may narrow, never broaden."
                )
            eff_lc = frozenset(caller_lc)
        else:
            eff_lc = ALLOWED_SECURE_LIFECYCLES

        # 2. Verification
        if verification is not None:
            raw_v = [str(x).strip().lower() for x in verification if str(x).strip()]
            if not raw_v:
                raise FilterValidationError(
                    "verification filter cannot be empty list/set (empty set would bypass boundary)."
                )
            caller_v = set(raw_v)

            unknown_v = caller_v - KNOWN_VERIFICATIONS
            if unknown_v:
                raise FilterValidationError(
                    f"Unknown verification values: {sorted(unknown_v)}. Known: {sorted(KNOWN_VERIFICATIONS)}"
                )

            excess_v = caller_v - ALLOWED_SECURE_VERIFICATION
            if excess_v:
                raise BoundaryViolationError(
                    f"Broadening violation: requested verification {sorted(excess_v)} is outside "
                    f"secure boundary {sorted(ALLOWED_SECURE_VERIFICATION)}. "
                    "Caller may narrow, never broaden."
                )
            eff_v = frozenset(caller_v)
        else:
            eff_v = ALLOWED_SECURE_VERIFICATION

        # 3. Types (narrowing filter)
        if types is not None:
            raw_types = sorted({str(t).strip().lower() for t in types if str(t).strip()})
            eff_types = raw_types if raw_types else None
        else:
            eff_types = None

        return eff_lc, eff_v, eff_types

    def execute(
        self,
        request: RetrievalRequest,
        retriever: Optional[HybridRetriever] = None,
    ) -> RetrievalResponse:
        """Execute a validated retrieval request against HybridRetriever."""
        active_retriever = retriever or self.retriever
        if active_retriever is None:
            raise RuntimeError("No HybridRetriever provided to RetrievalBoundaryAdapter.")

        if not isinstance(request.query, str):
            raise FilterValidationError("Retrieval query must be a string.")

        if request.top_k <= 0:
            raise FilterValidationError(f"top_k must be a positive integer, got {request.top_k}.")

        # 1. Validate caller identity
        norm_principal = self.normalize_principal(request.principal)

        # 2. Validate and normalize filters
        eff_lc, eff_v, eff_types = self.validate_and_normalize_filters(
            lifecycles=request.lifecycles,
            verification=request.verification,
            types=request.types,
        )

        # 3. Delegate to HybridRetriever with full trace
        hits, trace = active_retriever.search_with_trace(
            query=request.query,
            top_k=request.top_k,
            allowed_lifecycles=eff_lc,
            allowed_types=eff_types,
            allowed_verification=eff_v,
            secure=True,
        )

        # 4. Post-retrieval invariant check (defense-in-depth)
        sanitized_hits: List[Hit] = []
        for h in hits:
            # Under no circumstances may RAW, ARCHIVED, SUPERSEDED, RECONSOLIDATING,
            # or unverified notes pass this boundary
            if h.note.lifecycle not in ALLOWED_SECURE_LIFECYCLES:
                continue
            if h.note.verification not in ALLOWED_SECURE_VERIFICATION:
                continue
            sanitized_hits.append(h)

        # 5. Enrich trace with boundary metadata
        trace["caller_principal"] = norm_principal
        trace["audit_ref"] = request.audit_ref
        trace["request_id"] = request.request_id
        trace["boundary_filters"] = {
            "lifecycles": sorted(eff_lc),
            "verification": sorted(eff_v),
            "types": eff_types,
        }

        return RetrievalResponse(
            hits=sanitized_hits,
            total_hits=len(sanitized_hits),
            query=request.query,
            principal=norm_principal,
            effective_lifecycles=sorted(eff_lc),
            effective_verification=sorted(eff_v),
            effective_types=eff_types,
            trace=trace,
            audit_ref=request.audit_ref,
            request_id=request.request_id,
        )


# ---------------------------------------------------------------------------
# Simulated Runtime Caller (TEST MODE ONLY)
# ---------------------------------------------------------------------------

@dataclass
class SimulatedAuditEvent:
    """Simulates an audit event record emitted to the tamper-evident audit log."""
    operation: str
    principal: str
    target_id: str
    success: bool
    details: Dict[str, Any]
    timestamp_utc: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())


class StubMemoryControllerConsumer:
    """Simulates the future runtime caller (MemoryController) interfacing with RetrievalBoundaryAdapter.

    TEST MODE ONLY.
    Demonstrates end-to-end compatibility without modifying production controller code:
    1. Accepts standard MemoryController search arguments.
    2. Builds a structured, immutable RetrievalRequest.
    3. Traverses the boundary validation, normalization, and sanitization pipeline.
    4. Intercepts security violations and captures them in the simulated audit log.
    5. Formats the output payload into the MemoryController pack/result wire format.
    """

    def __init__(self, adapter: RetrievalBoundaryAdapter):
        self.adapter = adapter
        self.audit_log: List[SimulatedAuditEvent] = []

    def search(
        self,
        principal: Any,
        query: str,
        page_size: int = 10,
        lifecycles: Optional[Iterable[str]] = None,
        types: Optional[Iterable[str]] = None,
        verification: Optional[Iterable[str]] = None,
        request_id: Optional[str] = None,
    ) -> Dict[str, Any]:
        """Simulates MemoryController.search() delegating to RetrievalBoundaryAdapter."""
        query_fp = hashlib.sha256(str(query).encode("utf-8")).hexdigest()
        req_id = request_id or f"search-{query_fp[:8]}"
        princ_str = str(getattr(principal, "value", principal) or "unknown")

        req = RetrievalRequest(
            query=query,
            principal=principal,
            top_k=page_size,
            lifecycles=lifecycles,
            types=types,
            verification=verification,
            audit_ref=query_fp,
            request_id=req_id,
        )

        try:
            resp = self.adapter.execute(req)
            self.audit_log.append(SimulatedAuditEvent(
                operation="search",
                principal=resp.principal,
                target_id=query_fp,
                success=True,
                details={
                    "page_size": page_size,
                    "total_hits": resp.total_hits,
                    "effective_lifecycles": resp.effective_lifecycles,
                    "effective_verification": resp.effective_verification,
                    "effective_types": resp.effective_types,
                    "trace": resp.trace,
                }
            ))
            return {
                "requestId": resp.request_id,
                "agentId": resp.principal,
                "totalHits": resp.total_hits,
                "effectiveFilters": {
                    "lifecycles": resp.effective_lifecycles,
                    "verification": resp.effective_verification,
                    "types": resp.effective_types,
                },
                "results": [
                    {
                        "id": h.note.id,
                        "title": h.note.title,
                        "score": h.score,
                        "lifecycle": h.note.lifecycle,
                        "verification": h.note.verification,
                        "type": h.note.type,
                        "signals": h.signals,
                    }
                    for h in resp.hits
                ],
                "trace": resp.trace,
                "auditRef": resp.audit_ref,
                "timestamp": resp.timestamp_utc,
            }
        except RetrievalBoundaryError as e:
            self.audit_log.append(SimulatedAuditEvent(
                operation="search",
                principal=princ_str,
                target_id=query_fp,
                success=False,
                details={"error": str(e), "error_type": type(e).__name__}
            ))
            raise
