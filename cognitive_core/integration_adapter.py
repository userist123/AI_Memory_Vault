"""cognitive_core/integration_adapter.py — P3-A Retrieval Integration Adapter.

Provides a production-ready, decoupled integration adapter that can mediate between:
    MemoryController.search() (Future Caller)
            │
            ▼
    RetrievalIntegrationAdapter
            │
            ▼
    ProductionRetrievalFacade
            │
            ▼
    RetrievalBoundaryAdapter
            │
            ▼
    HybridRetriever & Graph Search

Architecture & Security:
1. Strict Request Contract: typed, canonicalized, validated, bounded page_size (1..100).
2. Security Ceiling Enforcement: principal authorization context strictly bounded to ACTIVE + verified.
   Zero permission elevation. Rejects REVIEW, RAW, RECONSOLIDATING, SUPERSEDED, ARCHIVED, unverified fail-closed.
3. Stable Response Contract: sanitized public search hits (no internal retriever pointers leaked).
4. No-Mutation Guarantee: strictly read-only traversal. Zero filesystem or database mutations.
5. Tamper-Evident Multi-Factor Pagination: cursor bound to principal, query, filters, page_size, disclosure.
   Cross-principal cursor tampering rejected fail-closed.
6. Deterministic Tie-Breaking: guaranteed reproducible result ordering.

Status: PRODUCTION-READY (NOT production-wired).
Zero controller modification. Zero storage mutation.
"""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import re
import time
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple

from .retrieval_boundary import (
    BoundaryViolationError,
    FilterValidationError,
    PrincipalValidationError,
    RetrievalBoundaryAdapter,
)
from .retrieval_facade import (
    FacadeNoteResult,
    FacadeRetrievalRequest,
    FacadeRetrievalResponse,
    ProductionRetrievalFacade,
)
from .vault_index import Note, VaultIndex


# ---------------------------------------------------------------------------
# Exceptions
# ---------------------------------------------------------------------------

class IntegrationAdapterError(Exception):
    """Base exception for RetrievalIntegrationAdapter."""


class IntegrationSecurityError(IntegrationAdapterError):
    """Raised when security boundaries, ceilings, or unauthorized filters are violated."""


class IntegrationRequestValidationError(IntegrationAdapterError):
    """Raised when incoming search request parameters fail validation or canonicalization."""


class CursorSecurityError(IntegrationAdapterError):
    """Raised when a pagination cursor token is forged, tampered with, expired, or used by a different principal."""


import itertools

_req_counter = itertools.count(1)


# ---------------------------------------------------------------------------
# Contracts: Request & Response
# ---------------------------------------------------------------------------

class DisclosureLevel(str, Enum):
    STANDARD = "standard"
    SUMMARY = "summary"
    FULL = "full"


ALLOWED_PRINCIPALS: Set[str] = {"human", "ai_agent", "admin"}
ALLOWED_DISCLOSURE_LEVELS: Set[str] = {dl.value for dl in DisclosureLevel}
ALLOWED_SECURE_LIFECYCLES: Set[str] = {"ACTIVE"}
ALLOWED_SECURE_VERIFICATION: Set[str] = {"verified"}


@dataclass(frozen=True)
class IntegrationSearchRequest:
    """Explicit, strongly-typed request presented to RetrievalIntegrationAdapter.

    Ensures parameter non-ambiguity and strict canonicalization.
    """
    query: str
    principal: Any  # Principal enum or string ("human", "ai_agent", "admin")
    page_size: int = 10
    page_token: Optional[str] = None
    lifecycles: Optional[Sequence[str]] = None
    types: Optional[Sequence[str]] = None
    disclosure_level: str = "standard"
    request_id: Optional[str] = None
    metadata: Optional[Dict[str, Any]] = None


@dataclass(frozen=True)
class IntegrationSearchHit:
    """Stable, sanitized search result item exposed to caller.

    Completely abstracts and hides retriever internals and graph representations.
    """
    id: str
    title: str
    score: float
    lifecycle: str
    verification: str
    type: str
    summary: str
    citation: str
    signals: Dict[str, int] = field(default_factory=dict)


@dataclass
class IntegrationSearchResponse:
    """Stable public response envelope emitted by RetrievalIntegrationAdapter."""
    results: List[IntegrationSearchHit]
    total_hits: int
    page_size: int
    request_id: str
    principal: str
    next_page_token: Optional[str]
    trace: Dict[str, Any]
    retrieval_mode: str
    deterministic: bool
    timestamp_utc: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())


# ---------------------------------------------------------------------------
# Retrieval Integration Adapter
# ---------------------------------------------------------------------------

class RetrievalIntegrationAdapter:
    """Production retrieval integration adapter mediating between controller and facade.

    Guarantees:
    - Zero permission elevation: caller cannot broaden beyond ACTIVE + verified.
    - Principal bound cursor verification: prevents cross-principal pagination attacks.
    - Zero storage mutation.
    """

    CURSOR_SECRET: bytes = b"p3-retrieval-cursor-binding-key-2026"
    CURSOR_MAX_AGE_SECONDS: int = 3600  # 1 hour expiration window

    def __init__(self, facade: ProductionRetrievalFacade):
        if not isinstance(facade, ProductionRetrievalFacade):
            raise TypeError("facade must be an instance of ProductionRetrievalFacade")
        self._facade = facade

    # -----------------------------------------------------------------------
    # Canonicalization & Input Validation
    # -----------------------------------------------------------------------

    def _normalize_principal(self, principal: Any) -> str:
        if principal is None:
            raise IntegrationSecurityError("Principal cannot be None; explicit principal identity is required.")
        if hasattr(principal, "value"):
            raw = str(principal.value).strip().lower()
        else:
            raw = str(principal).strip().lower()

        if raw not in ALLOWED_PRINCIPALS:
            raise IntegrationSecurityError(
                f"Invalid or untrusted principal '{raw}'. Must be one of {sorted(ALLOWED_PRINCIPALS)}."
            )
        return raw

    def _canonicalize_query(self, query: str) -> str:
        if not isinstance(query, str):
            raise IntegrationRequestValidationError("Query must be a string.")
        # Collapse excessive whitespace
        clean = " ".join(query.strip().split())
        if not clean:
            raise IntegrationRequestValidationError("Query cannot be empty or pure whitespace.")
        return clean

    def _validate_page_size(self, page_size: Any) -> int:
        if isinstance(page_size, bool) or not isinstance(page_size, int):
            raise IntegrationRequestValidationError("page_size must be a non-boolean integer.")
        if page_size < 1:
            raise IntegrationRequestValidationError("page_size must be >= 1.")
        if page_size > 100:
            raise IntegrationRequestValidationError("page_size cannot exceed maximum limit of 100.")
        return page_size

    def _validate_and_narrow_lifecycles(self, lifecycles: Optional[Iterable[str]]) -> List[str]:
        if lifecycles is None:
            return sorted(ALLOWED_SECURE_LIFECYCLES)

        norm_lcs = []
        for lc in lifecycles:
            if not isinstance(lc, str):
                raise IntegrationRequestValidationError("Lifecycle values must be strings.")
            val = lc.strip().upper()
            if not val:
                raise IntegrationRequestValidationError("Empty lifecycle value is not permitted.")
            norm_lcs.append(val)

        if not norm_lcs:
            raise IntegrationSecurityError("Empty lifecycles filter supplied; fail-closed.")

        # Check for broadening attempts
        for val in norm_lcs:
            if val not in ALLOWED_SECURE_LIFECYCLES:
                raise IntegrationSecurityError(
                    f"Security Ceiling Violation: Requested lifecycle '{val}' exceeds boundary "
                    f"ceiling {ALLOWED_SECURE_LIFECYCLES}. Non-active notes cannot be retrieved."
                )

        return sorted(set(norm_lcs))

    def _validate_types(self, types: Optional[Iterable[str]]) -> Optional[List[str]]:
        if types is None:
            return None

        clean_types = []
        for t in types:
            if not isinstance(t, str):
                raise IntegrationRequestValidationError("Type filter values must be strings.")
            ct = t.strip().lower()
            if not ct:
                raise IntegrationRequestValidationError("Empty type value is not permitted.")
            clean_types.append(ct)

        if not clean_types:
            raise IntegrationRequestValidationError("Empty types collection supplied.")

        return sorted(set(clean_types))

    def _validate_disclosure_level(self, level: Any) -> str:
        if not isinstance(level, str):
            raise IntegrationRequestValidationError("disclosure_level must be a string.")
        dl = level.strip().lower()
        if dl not in ALLOWED_DISCLOSURE_LEVELS:
            raise IntegrationRequestValidationError(
                f"Unknown disclosure_level '{dl}'. Supported: {sorted(ALLOWED_DISCLOSURE_LEVELS)}."
            )
        return dl

    # -----------------------------------------------------------------------
    # Multi-Factor Cursor Generation & Verification
    # -----------------------------------------------------------------------

    def _compute_filter_signature(
        self,
        query: str,
        lifecycles: List[str],
        types: Optional[List[str]],
        page_size: int,
        disclosure_level: str,
    ) -> str:
        payload = f"{query}|{','.join(lifecycles)}|{','.join(types) if types else '*'}|{page_size}|{disclosure_level}"
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:16]

    def _create_secure_cursor(
        self,
        facade_page_token: str,
        principal: str,
        filter_sig: str,
    ) -> str:
        """Encodes facade token into a principal-bound, tamper-evident HMAC cursor."""
        ts = int(time.time())
        token_body = {
            "pt": facade_page_token,
            "p": principal,
            "fs": filter_sig,
            "ts": ts,
        }
        body_bytes = json.dumps(token_body, separators=(",", ":")).encode("utf-8")
        sig = hmac.new(self.CURSOR_SECRET, body_bytes, hashlib.sha256).hexdigest()[:16]
        bundle = {"b": base64.urlsafe_b64encode(body_bytes).decode("ascii"), "s": sig}
        return base64.urlsafe_b64encode(json.dumps(bundle, separators=(",", ":")).encode("ascii")).decode("ascii")

    def _unpack_secure_cursor(
        self,
        cursor: str,
        expected_principal: str,
        expected_filter_sig: str,
    ) -> str:
        """Unpacks and verifies cursor authenticity, principal binding, and expiration."""
        try:
            raw_bundle = base64.urlsafe_b64decode(cursor.encode("ascii")).decode("ascii")
            bundle = json.loads(raw_bundle)
            body_b64 = bundle["b"]
            sig = bundle["s"]
            body_bytes = base64.urlsafe_b64decode(body_b64.encode("ascii"))
        except Exception as err:
            raise CursorSecurityError(f"Malformed or non-decodable pagination token: {err}") from err

        expected_sig = hmac.new(self.CURSOR_SECRET, body_bytes, hashlib.sha256).hexdigest()[:16]
        if not hmac.compare_digest(sig, expected_sig):
            raise CursorSecurityError("Cursor token signature verification failed (tampered cursor).")

        try:
            token_body = json.loads(body_bytes.decode("utf-8"))
            facade_pt = token_body["pt"]
            token_principal = token_body["p"]
            token_filter_sig = token_body["fs"]
            token_ts = token_body["ts"]
        except Exception as err:
            raise CursorSecurityError(f"Cursor token payload corrupt: {err}") from err

        # Cross-principal check
        if token_principal != expected_principal:
            raise CursorSecurityError(
                f"Cross-principal cursor violation: Cursor issued for '{token_principal}', "
                f"but presented by '{expected_principal}'."
            )

        # Filter and query signature check
        if token_filter_sig != expected_filter_sig:
            raise CursorSecurityError(
                "Cursor query/filter mismatch: Cursor was created for different query or filter parameters."
            )

        # Expiration check
        now = int(time.time())
        if (now - token_ts) > self.CURSOR_MAX_AGE_SECONDS or token_ts > (now + 60):
            raise CursorSecurityError("Cursor token has expired.")

        return str(facade_pt)

    # -----------------------------------------------------------------------
    # Formatting Search Hits
    # -----------------------------------------------------------------------

    def _format_hit(self, result: FacadeNoteResult, disclosure_level: str) -> IntegrationSearchHit:
        preview = result.body_preview or ""
        if disclosure_level == DisclosureLevel.SUMMARY.value:
            summary = preview[:120] + ("..." if len(preview) > 120 else "")
        elif disclosure_level == DisclosureLevel.FULL.value:
            summary = preview
        else:  # standard
            summary = preview[:240] + ("..." if len(preview) > 240 else "")

        citation = f"[[{result.id}]]"
        return IntegrationSearchHit(
            id=result.id,
            title=result.title,
            score=round(result.score, 6),
            lifecycle=result.lifecycle,
            verification=result.verification,
            type=result.type,
            summary=summary,
            citation=citation,
            signals=result.signals,
        )

    # -----------------------------------------------------------------------
    # Primary Search Execution
    # -----------------------------------------------------------------------

    def search(self, request: IntegrationSearchRequest) -> IntegrationSearchResponse:
        """Executes a fully validated, secure search through the facade.

        1. Canonicalizes inputs.
        2. Enforces security boundaries fail-closed.
        3. Validates principal binding on cursors.
        4. Invokes ProductionRetrievalFacade.
        5. Returns normalized, sanitized response envelope.
        """
        t0 = time.perf_counter()

        # Step 1: Input canonicalization and validation
        principal = self._normalize_principal(request.principal)
        query = self._canonicalize_query(request.query)
        page_size = self._validate_page_size(request.page_size)
        lifecycles = self._validate_and_narrow_lifecycles(request.lifecycles)
        types = self._validate_types(request.types)
        disclosure_level = self._validate_disclosure_level(request.disclosure_level)
        req_id = request.request_id or f"req-{next(_req_counter)}"

        filter_sig: Optional[str] = None

        # Step 2: Unpack page token if provided
        facade_page_token: Optional[str] = None
        if request.page_token:
            filter_sig = self._compute_filter_signature(query, lifecycles, types, page_size, disclosure_level)
            facade_page_token = self._unpack_secure_cursor(request.page_token, principal, filter_sig)

        # Step 3: Dispatch to ProductionRetrievalFacade
        facade_req = FacadeRetrievalRequest(
            query=query,
            principal=principal,
            request_id=req_id,
            lifecycles=lifecycles,
            verification=["verified"],
            types=types,
            page_size=page_size,
            page_token=facade_page_token,
            metadata=request.metadata,
        )

        try:
            facade_resp: FacadeRetrievalResponse = self._facade.retrieve(facade_req)
        except (BoundaryViolationError, FilterValidationError, PrincipalValidationError) as err:
            raise IntegrationSecurityError(f"Facade security violation: {err}") from err
        except Exception as err:
            raise IntegrationAdapterError(f"Facade retrieval failure: {err}") from err

        # Step 4: Format hits
        hits = [self._format_hit(r, disclosure_level) for r in facade_resp.results]

        # Step 5: Create next cursor if facade returned next_page_token
        next_cursor: Optional[str] = None
        if facade_resp.next_page_token:
            if filter_sig is None:
                filter_sig = self._compute_filter_signature(query, lifecycles, types, page_size, disclosure_level)
            next_cursor = self._create_secure_cursor(
                facade_resp.next_page_token,
                principal,
                filter_sig,
            )

        duration_ms = round((time.perf_counter() - t0) * 1000.0, 3)

        trace: Dict[str, Any] = {
            "adapter_duration_ms": duration_ms,
            "facade_duration_ms": facade_resp.trace.get("duration_ms", 0.0),
            "effective_lifecycles": facade_resp.effective_lifecycles,
            "effective_verification": facade_resp.effective_verification,
            "effective_types": facade_resp.effective_types,
            "filter_signature": filter_sig,
            "disclosure_level": disclosure_level,
        }

        return IntegrationSearchResponse(
            results=hits,
            total_hits=facade_resp.total_hits,
            page_size=facade_resp.page_size,
            request_id=req_id,
            principal=principal,
            next_page_token=next_cursor,
            trace=trace,
            retrieval_mode="hybrid_boundary_secure",
            deterministic=True,
        )
