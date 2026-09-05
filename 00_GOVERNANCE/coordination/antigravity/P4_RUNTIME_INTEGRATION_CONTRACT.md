# P4 Runtime Integration Contract

**Agent**: Antigravity  
**Branch**: `antigravity/p1-retrieval-foundation`  
**P3 Baseline Commit**: `0e9390c0c624b57cfdedaa4247ab7316b5be7dc7`  
**Date**: 2026-09-05  
**Document Purpose**: Definitive technical specification and bidirectional interface contract mediating between `MemoryController.search()` and `RetrievalIntegrationAdapter`.

---

## 1. Architectural Call Hierarchy

The target runtime retrieval stack forms a unidirectional, strictly gated call chain:

```
┌────────────────────────────────────────────────────────────────────────┐
│                      Caller / Consumer Layer                           │
│  - REST API / External Agent / Offline CLI (recall_cli.py)             │
│  - Presents: principal, query, page_size, page_token, lifecycles, types │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                       MemoryController.search()                        │
│  - Performs query size check & sanitize_query(query)                   │
│  - Resolves agent token budget via load_agent_budget(principal.value)  │
│  - Maps controller parameters to IntegrationSearchRequest              │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                     RetrievalIntegrationAdapter                        │
│  - Principal identity canonicalization (HUMAN, AI_AGENT, ADMIN)       │
│  - Security Ceiling Gate: ALLOWED_LIFECYCLES = {ACTIVE}                │
│                           ALLOWED_VERIFICATION = {verified}            │
│  - Multi-Factor HMAC Cursor Signing & Cross-Principal Verification    │
│  - Early fail-closed rejection prior to retriever invocation           │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      ProductionRetrievalFacade                         │
│  - Boundary-first gate & defense-in-depth sanitization                 │
│  - Deterministic tie-breaking (Score DESC, Note ID ASC)                │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      RetrievalBoundaryAdapter                          │
│  - Invariant ceiling enforcement: {ACTIVE} + {verified}                │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   HybridRetriever & CorpusGraph                        │
│  - BM25 lexical search + Entity extraction + Multi-Hop Graph Traversal │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Current Runtime Contract (`MemoryController.search()`)

Inspected directly from `memory_controller/controller.py` (lines 209–291):

### Method Signature
```python
def search(
    self,
    principal: Principal,
    query: str,
    page_size: int = 10,
    page_token: Optional[str] = None,
    lifecycles: Optional[List[Lifecycle]] = None,
    types: Optional[List[str]] = None
) -> Dict[str, Any]:
```

### Parameter Semantics
- `principal`: `memory_controller.security.authorizer.Principal` enum instance (`HUMAN`, `AI_AGENT`, `ADMIN`).
- `query`: Raw search query string. Subjected to `check_query_size(query)` and `sanitize_query(query)`.
- `page_size`: Integer, default 10.
- `page_token`: Optional opaque string encoded via `PaginationToken` using `HMAC-SHA256` signed with `MEMORY_CONTROLLER_HMAC_SECRET`.
- `lifecycles`: Optional list of `Lifecycle` enum instances (`RAW`, `CLASSIFIED`, `NORMALIZED`, `REVIEW`, `VERIFIED`, `ACTIVE`, `RECONSOLIDATING`, `SUPERSEDED`, `ARCHIVED`).
- `types`: Optional list of strings designating note types (e.g. `["knowledge", "decision"]`).

### Return Shape (Pack Dictionary)
Constructed by `ContextPackBuilder.build(...)`:
```python
{
    "requestId": str,             # Defaults to 'search'
    "agentId": str,               # principal.value (e.g. 'ai_agent')
    "budget": {
        "soft": int,
        "hard": int
    },
    "disclosureLevel": str,       # 'metadata' | 'snippet' | 'sections' | 'full'
    "results": [
        {
            "id": str,
            "title": str,
            "lifecycle": str,
            "type": str,
            # Additional disclosure-dependent fields (content, snippet, sections, etc.)
        }
    ],
    "next_page_token": Optional[str]
}
```

---

## 3. P3 Adapter Contract (`RetrievalIntegrationAdapter`)

Inspected from `cognitive_core/integration_adapter.py`:

### Input: `IntegrationSearchRequest`
```python
@dataclass(frozen=True)
class IntegrationSearchRequest:
    query: str
    principal: Any  # Principal enum or string ("human", "ai_agent", "admin")
    page_size: int = 10
    page_token: Optional[str] = None
    lifecycles: Optional[Sequence[str]] = None
    types: Optional[Sequence[str]] = None
    disclosure_level: str = "standard"
    request_id: Optional[str] = None
    metadata: Optional[Dict[str, Any]] = None
```

### Output: `IntegrationSearchResponse`
```python
@dataclass
class IntegrationSearchResponse:
    results: List[IntegrationSearchHit]
    total_hits: int
    page_size: int
    request_id: str
    principal: str
    next_page_token: Optional[str]
    trace: Dict[str, Any]
    retrieval_mode: str
    deterministic: bool
    timestamp_utc: str
```

### Hit Structure: `IntegrationSearchHit`
```python
@dataclass(frozen=True)
class IntegrationSearchHit:
    id: str
    title: str
    score: float
    lifecycle: str
    verification: str
    type: str
    summary: str
    citation: str
    signals: Dict[str, int] = field(default_factory=dict)
```

---

## 4. Compatibility Matrix

| Parameter / Concept | `MemoryController.search()` | `RetrievalIntegrationAdapter` | Compatibility Status | Mapping Rule |
| :--- | :--- | :--- | :--- | :--- |
| **`principal`** | `Principal` Enum (`Principal.AI_AGENT`) | `Principal` Enum or lowercase `str` | **100% Compatible** | Pass directly; adapter normalizes via `principal.value.lower()` |
| **`query`** | `str` (sanitized internally) | `str` (whitespace normalized) | **100% Compatible** | Pass sanitized or raw string; adapter collapses whitespace |
| **`page_size`** | `int` (no explicit range check) | `int` bounded in `[1, 100]` | **Compatible with guard** | Pass `int`; adapter enforces `1 <= page_size <= 100` |
| **`page_token`** | HMAC string (`PaginationToken`) | Base64 HMAC string (Multi-factor) | **Token-Incompatible** | Cursors are issued by the adapter; pass through directly |
| **`lifecycles`** | `Optional[List[Lifecycle]]` | `Optional[Sequence[str]]` | **Boundary-Constrained** | Convert enum to string; adapter enforces ceiling `{ACTIVE}` |
| **`types`** | `Optional[List[str]]` | `Optional[Sequence[str]]` | **100% Compatible** | Pass directly; adapter normalizes to lowercase list |
| **`disclosure_level`** | `metadata`, `snippet`, `sections`, `full` | `summary`, `standard`, `full` | **Semantic Mapping Required** | Map `metadata`/`snippet` $\to$ `summary`, `sections` $\to$ `standard`, `full` $\to$ `full` |
| **`request_id`** | Implicit (`'search'`) | Explicit (`request_id` or auto `req-N`) | **100% Compatible** | Controller passes `'search'` or transaction UUID |
| **Return Format** | `Dict[str, Any]` (Pack Dict) | `IntegrationSearchResponse` Dataclass | **Transformation Required** | Shim converts `IntegrationSearchResponse` to Pack Dict |

---

## 5. Bidirectional Mapping Rules

### Controller Request $\to$ Adapter Request
```python
def request_from_controller(
    principal: Principal,
    query: str,
    page_size: int = 10,
    page_token: Optional[str] = None,
    lifecycles: Optional[List[Lifecycle]] = None,
    types: Optional[List[str]] = None,
    disclosure_level: Optional[str] = None,
    request_id: Optional[str] = None,
) -> IntegrationSearchRequest:
    # 1. Lifecycle conversion: enum.value -> uppercase str
    raw_lcs = None
    if lifecycles is not None:
        raw_lcs = [lc.value if hasattr(lc, "value") else str(lc) for lc in lifecycles]

    # 2. Disclosure mapping:
    # controller ('metadata', 'snippet') -> adapter 'summary'
    # controller ('sections')            -> adapter 'standard'
    # controller ('full')                -> adapter 'full'
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
    )
```

### Adapter Response $\to$ Controller Pack Dictionary
```python
def response_to_controller(
    response: IntegrationSearchResponse,
    budget: Optional[Any] = None,
    disclosure_level: str = "metadata",
) -> Dict[str, Any]:
    soft_budget = getattr(budget, "soft_context_budget", 4000)
    hard_budget = getattr(budget, "hard_context_budget", 8000)

    # Convert sanitized hits to controller result dictionaries
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
        "budget": {"soft": soft_budget, "hard": hard_budget},
        "disclosureLevel": disclosure_level,
        "results": results_list,
        "next_page_token": response.next_page_token,
        "total_hits": response.total_hits,
        "trace": response.trace,
    }
```

---

## 6. Security Boundaries & Invariants

1. **Security Ceiling Enforcement (Fail-Closed)**:
   - Only `ACTIVE` notes with `verification: "verified"` may be retrieved by any principal (`HUMAN`, `AI_AGENT`, `ADMIN`).
   - If a caller requests lifecycles outside `{ACTIVE}` (e.g. `REVIEW`, `RAW`, `ARCHIVED`), the adapter raises `IntegrationSecurityError` immediately without invoking the retriever.

2. **Early Fail-Closed Rejection**:
   - Validation and security checks occur prior to query execution.
   - Malformed queries, out-of-bounds page sizes ($<1$ or $>100$), invalid principals, and unauthorized lifecycles abort before touching the index or graph.

3. **Tamper-Evident Multi-Factor Pagination**:
   - Cursors are bound to HMAC of `(principal, query, lifecycles, types, page_size, disclosure)`.
   - Any modification to query, filters, page size, or attempting to use a cursor issued to another principal raises `CursorSecurityError`.

4. **No-Mutation Guarantee**:
   - The retrieval pipeline is purely analytical and read-only.
   - Zero modifications to SQLite databases, memory storage, or filesystem markdown files occur during any search operation.

---

## 7. Error Mapping

| Adapter Exception | Originating Condition | Controller / Caller Representation | HTTP Status Equivalent |
| :--- | :--- | :--- | :--- |
| `IntegrationSecurityError` | Requesting non-ACTIVE lifecycle, unauthorized principal, ceiling violation | `PermissionError` or security fault | `403 Forbidden` |
| `CursorSecurityError` | Tampered token, cross-principal use, expired cursor, altered filters | `InvalidPaginationTokenError` | `400 Bad Request` |
| `IntegrationRequestValidationError` | Empty query, non-integer or out-of-range `page_size`, invalid types | `ValueError` | `400 Bad Request` |
| `IntegrationAdapterError` | Internal retrieval failure | `RuntimeError` | `500 Internal Error` |

---

## 8. Open Questions & External Dependencies

1. **External SynapseStore**:
   - `05_DATA/synapses.json` and `cognitive_core/synapse_store.py` are absent in this branch.
   - Status remains: `BLOCKED_BY_EXTERNAL_SYNAPSESTORE`.
   - In-memory `CorpusGraph` and `HybridRetriever` operate via direct entity and document graph links.

2. **Upstream PR #17 / Runtime Security Lifecycle Branch**:
   - ChatGPT is finalizing lifecycle mutations and verification states on a separate workstream.
   - The `RetrievalIntegrationAdapter` was deliberately designed with zero assumptions about unmerged storage PRs, depending only on the canonical `ACTIVE + verified` interface boundary.
