"""cognitive_core/retrieval_facade.py — Production Retrieval Facade.

Provides a unified, stable, production-grade retrieval facade over:
    RetrievalBoundaryAdapter -> HybridRetriever

Architecture:
┌────────────────────────────────────────────────────────────────────────┐
│                   Caller (e.g. MemoryController)                       │
│  - Caller Authorization (Principal, Operation.SEARCH, RBAC/ABAC)       │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      ProductionRetrievalFacade                         │
│  - Boundary-First Gate: Rejects unauthenticated/broadened requests     │
│  - Zero Authorization Inference: Caller must provide explicit Principal│
│  - Pagination Transport: Handles page_size, offset/page_token, next    │
│  - Secondary Defense-in-Depth Sanitization                             │
│  - Strict Deterministic Tie-Breaking (RRF score desc, note ID asc)     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      RetrievalBoundaryAdapter                          │
│  - Normalizes Principal (HUMAN, AI_AGENT, ADMIN)                       │
│  - Enforces: "Caller may narrow, never broaden trust boundary"         │
│  - Boundary: ALLOWED_SECURE_LIFECYCLES = {ACTIVE}                      │
│              ALLOWED_SECURE_VERIFICATION = {verified}                  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                          HybridRetriever                               │
│  - Multi-Signal Lexical (BM25) + Entity Retrieval                      │
│  - Pure in-memory read-only traversal                                  │
└────────────────────────────────────────────────────────────────────────┘

Status: PRODUCTION-FACADE-READY (NOT production-wired).
Zero storage mutation. Zero runtime controller modification.
"""
from __future__ import annotations

import hashlib
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set

from .hybrid_retrieval import (
    ALLOWED_SECURE_LIFECYCLES,
    ALLOWED_SECURE_VERIFICATION,
    Hit,
    HybridRetriever,
)
from .retrieval_boundary import (
    BoundaryViolationError,
    FilterValidationError,
    PrincipalValidationError,
    RetrievalBoundaryAdapter,
    RetrievalBoundaryError,
    RetrievalRequest,
    RetrievalResponse,
)
from .vault_index import Note


# ---------------------------------------------------------------------------
# Facade Request & Response Contracts
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class FacadeRetrievalRequest:
    """Production retrieval request presented to ProductionRetrievalFacade.

    Callers MUST provide an explicit principal. No default principal (such as
    AI_AGENT) is assumed; missing principal is fail-closed.
    """
    query: str
    principal: Any  # Principal enum or string ('human', 'ai_agent', 'admin')
    request_id: Optional[str] = None
    lifecycles: Optional[Iterable[str]] = None
    verification: Optional[Iterable[str]] = None
    types: Optional[Iterable[str]] = None
    page_size: int = 10
    page_token: Optional[str] = None
    metadata: Optional[Dict[str, Any]] = None


@dataclass(frozen=True)
class FacadeNoteResult:
    """Individual note hit returned by the facade."""
    id: str
    title: str
    score: float
    lifecycle: str
    verification: str
    type: str
    signals: Dict[str, int]
    body_preview: str = ""


@dataclass
class FacadeRetrievalResponse:
    """Stable response envelope emitted by ProductionRetrievalFacade."""
    results: List[FacadeNoteResult]
    total_hits: int
    page_size: int
    page_token: Optional[str]
    next_page_token: Optional[str]
    query: str
    principal: str
    request_id: Optional[str]
    effective_lifecycles: List[str]
    effective_verification: List[str]
    effective_types: Optional[List[str]]
    trace: Dict[str, Any]
    timestamp_utc: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())


# ---------------------------------------------------------------------------
# Production Retrieval Facade
# ---------------------------------------------------------------------------

class ProductionRetrievalFacade:
    """Unified, stable retrieval facade for the AI Memory Vault.

    Guarantees:
    1. Boundary First: Boundary adapter validates all inputs before any retrieval.
    2. Narrowing Allowed, Broadening Forbidden: Boundary strictly capped at ACTIVE+verified.
    3. Principal Preserved: Principal identity flows unmodified to response and trace.
    4. Defense in Depth: Second-layer sanitizer eliminates any non-ACTIVE/non-verified leaks.
    5. Determinism: Strictly deterministic ordering via (score desc, note.id asc).
    6. Pagination Transport: Transparently manages page_size, offset, and next_page_token.
    7. No Authorization: Does not perform authorization (caller layer responsibility).
    8. Storage Immutability: Strictly read-only operation; zero filesystem writes.
    """

    def __init__(
        self,
        adapter: Optional[RetrievalBoundaryAdapter] = None,
        retriever: Optional[HybridRetriever] = None,
    ):
        if adapter is not None:
            self.adapter = adapter
        elif retriever is not None:
            self.adapter = RetrievalBoundaryAdapter(retriever)
        else:
            self.adapter = RetrievalBoundaryAdapter()

    def retrieve(self, request: FacadeRetrievalRequest) -> FacadeRetrievalResponse:
        """Executes a validated, fail-closed retrieval request."""
        # 1. Structural request validation
        if not isinstance(request, FacadeRetrievalRequest):
            raise TypeError(
                f"Expected FacadeRetrievalRequest, got {type(request).__name__}"
            )

        if not isinstance(request.query, str):
            raise FilterValidationError("Query must be a string.")

        if not isinstance(request.page_size, int) or isinstance(request.page_size, bool) or request.page_size <= 0:
            raise FilterValidationError(
                f"page_size must be a positive integer, got {request.page_size!r}."
            )

        # 2. Parse pagination token (offset parsing or default 0)
        offset = 0
        if request.page_token is not None:
            if not isinstance(request.page_token, str):
                raise FilterValidationError(
                    f"page_token must be a string, got {type(request.page_token).__name__}."
                )
            token_str = request.page_token.strip()
            if not token_str:
                offset = 0
            elif token_str.startswith("offset:"):
                val_str = token_str.split(":", 1)[1].strip()
                if not val_str.isdigit():
                    raise FilterValidationError(
                        f"Invalid page_token offset value: {val_str!r}. Must be a non-negative integer."
                    )
                offset = int(val_str)
            elif token_str.isdigit():
                offset = int(token_str)
            else:
                raise FilterValidationError(
                    f"Invalid page_token format: {token_str!r}. Expected 'offset:<non-negative int>'."
                )

        # Calculate retrieval depth needed to serve this page
        fetch_k = max(offset + request.page_size + 1, 10)

        # Query fingerprint
        query_fp = hashlib.sha256(request.query.encode("utf-8")).hexdigest()
        req_id = request.request_id or f"req-{query_fp[:8]}"

        # 3. Build inner boundary request (Boundary First!)
        inner_req = RetrievalRequest(
            query=request.query,
            principal=request.principal,
            top_k=fetch_k,
            lifecycles=request.lifecycles,
            types=request.types,
            verification=request.verification,
            audit_ref=query_fp,
            request_id=req_id,
        )

        # 4. Delegate to RetrievalBoundaryAdapter
        # This will raise PrincipalValidationError, FilterValidationError,
        # or BoundaryViolationError BEFORE any retriever call if invalid.
        boundary_resp: RetrievalResponse = self.adapter.execute(inner_req)

        # 5. Defense-in-depth secondary sanitization
        # Guarantees that even if the adapter or underlying retriever leaked notes,
        # the facade eliminates them before returning to the caller.
        sanitized_hits: List[Hit] = []
        for hit in boundary_resp.hits:
            if hit.note.lifecycle not in ALLOWED_SECURE_LIFECYCLES:
                continue
            if hit.note.verification not in ALLOWED_SECURE_VERIFICATION:
                continue
            sanitized_hits.append(hit)

        # 6. Deterministic secondary sort (RRF score desc, note ID asc)
        sanitized_hits.sort(key=lambda h: (-h.score, h.note.id))

        total_hits = len(sanitized_hits)
        page_hits = sanitized_hits[offset : offset + request.page_size]

        # Calculate next page token
        next_page_token: Optional[str] = None
        if offset + request.page_size < total_hits:
            next_page_token = f"offset:{offset + request.page_size}"

        # 7. Format note results
        results: List[FacadeNoteResult] = []
        for h in page_hits:
            preview = (h.note.body[:200] + "...") if len(h.note.body) > 200 else h.note.body
            results.append(
                FacadeNoteResult(
                    id=h.note.id,
                    title=h.note.title,
                    score=h.score,
                    lifecycle=h.note.lifecycle,
                    verification=h.note.verification,
                    type=h.note.type,
                    signals=h.signals,
                    body_preview=preview,
                )
            )

        # 8. Enrich trace
        trace = dict(boundary_resp.trace)
        trace["facade"] = {
            "page_size": request.page_size,
            "offset": offset,
            "total_hits": total_hits,
            "returned_count": len(results),
            "has_next_page": next_page_token is not None,
        }

        return FacadeRetrievalResponse(
            results=results,
            total_hits=total_hits,
            page_size=request.page_size,
            page_token=request.page_token,
            next_page_token=next_page_token,
            query=request.query,
            principal=boundary_resp.principal,
            request_id=req_id,
            effective_lifecycles=boundary_resp.effective_lifecycles,
            effective_verification=boundary_resp.effective_verification,
            effective_types=boundary_resp.effective_types,
            trace=trace,
        )
