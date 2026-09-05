"""cognitive_core/p4_runtime_wiring_harness.py — P4 Runtime Integration Wiring Harness.

Prototype wiring harness and conversion shims bridging:
Caller / MemoryController.search()
        │
        ▼
request_from_controller(...)
        │
        ▼
RetrievalIntegrationAdapter.search(...)
        │
        ▼
response_to_controller(...)
        │
        ▼
Standard Pack Dictionary Output

Status: PROTOTYPE / INTEGRATION HARNESS (controller itself remains unmodified in P4).
Ensures zero permission elevation, full lifecycle ceiling enforcement, and seamless
format translation between controller callers and the underlying retrieval stack.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Sequence

from .hybrid_retrieval import HybridRetriever
from .integration_adapter import (
    CursorSecurityError,
    IntegrationRequestValidationError,
    IntegrationSearchHit,
    IntegrationSearchRequest,
    IntegrationSearchResponse,
    IntegrationSecurityError,
    RetrievalIntegrationAdapter,
)
from .retrieval_boundary import RetrievalBoundaryAdapter
from .retrieval_facade import ProductionRetrievalFacade
from .vault_index import VaultIndex


# ---------------------------------------------------------------------------
# Bidirectional Shims
# ---------------------------------------------------------------------------

def request_from_controller(
    principal: Any,
    query: str,
    page_size: int = 10,
    page_token: Optional[str] = None,
    lifecycles: Optional[Sequence[Any]] = None,
    types: Optional[Sequence[str]] = None,
    disclosure_level: Optional[str] = None,
    request_id: Optional[str] = None,
    metadata: Optional[Dict[str, Any]] = None,
) -> IntegrationSearchRequest:
    """Translates incoming parameters from MemoryController.search() into an IntegrationSearchRequest."""
    # Normalize lifecycles
    raw_lcs: Optional[List[str]] = None
    if lifecycles is not None:
        raw_lcs = []
        for lc in lifecycles:
            if hasattr(lc, "value"):
                raw_lcs.append(str(lc.value))
            else:
                raw_lcs.append(str(lc))

    # Map controller disclosure levels ('metadata', 'snippet', 'sections', 'full')
    # to adapter levels ('summary', 'standard', 'full')
    level = (disclosure_level or "metadata").strip().lower()
    if level in ("metadata", "snippet", "summary"):
        adapter_disclosure = "summary"
    elif level in ("sections", "standard"):
        adapter_disclosure = "standard"
    elif level in ("full", "full_document"):
        adapter_disclosure = "full"
    else:
        adapter_disclosure = "standard"

    return IntegrationSearchRequest(
        query=query,
        principal=principal,
        page_size=page_size,
        page_token=page_token,
        lifecycles=raw_lcs,
        types=types,
        disclosure_level=adapter_disclosure,
        request_id=request_id or "search",
        metadata=metadata,
    )


def response_to_controller(
    response: IntegrationSearchResponse,
    budget: Optional[Any] = None,
    original_disclosure_level: str = "metadata",
) -> Dict[str, Any]:
    """Translates IntegrationSearchResponse back into the standard pack dictionary expected by controller callers."""
    soft_budget = getattr(budget, "soft_context_budget", 4000)
    hard_budget = getattr(budget, "hard_context_budget", 8000)

    results_list = []
    for hit in response.results:
        results_list.append({
            "id": hit.id,
            "title": hit.title,
            "score": hit.score,
            "lifecycle": hit.lifecycle,
            "verification": hit.verification,
            "type": hit.type,
            "summary": hit.summary,
            "citation": hit.citation,
            "signals": hit.signals,
        })

    return {
        "requestId": response.request_id,
        "agentId": response.principal,
        "budget": {
            "soft": soft_budget,
            "hard": hard_budget,
        },
        "disclosureLevel": original_disclosure_level,
        "results": results_list,
        "next_page_token": response.next_page_token,
        "total_hits": response.total_hits,
        "trace": response.trace,
    }


# ---------------------------------------------------------------------------
# Runtime Wiring Harness Class
# ---------------------------------------------------------------------------

class P4RuntimeWiringHarness:
    """Simulates the prospective runtime behavior of MemoryController wired to RetrievalIntegrationAdapter.

    Can be instantiated directly with a VaultIndex or an existing RetrievalIntegrationAdapter.
    """

    def __init__(self, adapter: RetrievalIntegrationAdapter):
        if not isinstance(adapter, RetrievalIntegrationAdapter):
            raise TypeError("adapter must be an instance of RetrievalIntegrationAdapter")
        self.adapter = adapter

    @classmethod
    def from_vault(cls, vault: VaultIndex) -> "P4RuntimeWiringHarness":
        retriever = HybridRetriever(vault)
        boundary = RetrievalBoundaryAdapter(retriever)
        facade = ProductionRetrievalFacade(adapter=boundary)
        adapter = RetrievalIntegrationAdapter(facade=facade)
        return cls(adapter=adapter)

    def execute_controller_search(
        self,
        principal: Any,
        query: str,
        page_size: int = 10,
        page_token: Optional[str] = None,
        lifecycles: Optional[Sequence[Any]] = None,
        types: Optional[Sequence[str]] = None,
        disclosure_level: Optional[str] = None,
        request_id: Optional[str] = None,
        budget: Optional[Any] = None,
    ) -> Dict[str, Any]:
        """Executes search using exact MemoryController signature and error handling semantics."""
        effective_disclosure = disclosure_level or "metadata"

        # 1. Translate request
        try:
            req = request_from_controller(
                principal=principal,
                query=query,
                page_size=page_size,
                page_token=page_token,
                lifecycles=lifecycles,
                types=types,
                disclosure_level=effective_disclosure,
                request_id=request_id,
            )
        except Exception as err:
            raise ValueError(f"Invalid search request parameters: {err}") from err

        # 2. Execute through adapter
        try:
            adapter_resp = self.adapter.search(req)
        except IntegrationSecurityError as err:
            # Map security violations to PermissionError for controller callers
            raise PermissionError(f"Security Boundary Violation: {err}") from err
        except CursorSecurityError as err:
            # Map cursor violations to ValueError / InvalidPaginationTokenError
            raise ValueError(f"Invalid pagination cursor: {err}") from err
        except IntegrationRequestValidationError as err:
            raise ValueError(f"Search parameter validation failed: {err}") from err

        # 3. Translate response to pack dictionary
        return response_to_controller(
            adapter_resp,
            budget=budget,
            original_disclosure_level=effective_disclosure,
        )
