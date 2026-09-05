# P3 Retrieval Integration Readiness & Corpus Remediation

**Agent**: Antigravity  
**Branch**: `antigravity/p1-retrieval-foundation`  
**P2 Baseline Commit**: `bc6261cb72f396464b02686ec5127460073d0e51`  
**Date**: 2026-09-05  
**Production Wiring**: NOT DONE (Independent adapter layer ready for integration, runtime controller unmodified)  
**Test Suite Status**: 142/142 tests passing (100% clean across all 8 regression suites in 1.80s)

---

## 1. P2 Baseline Summary

P2 concluded with:
- **Retrieval Quality Gate**: 9/9 PASS (Known-Item R@10 0.9100, MRR 0.6000, Paraphrase R@10 0.6000, Multi-Hop Rescue Rate 0.2977, Net Gain +125, Entity R@10 0.4104, Security 0 leaks, Determinism 100%, Median Latency 0.991 ms).
- **Integration Test Harness**: 16/16 tests passing.
- **Corpus Quality Gate**: `CONDITIONAL_PASS` due to 580 unverified template duplicates in `REVIEW` status residing outside the active retrieval boundary.
- **SynapseStore**: `BLOCKED_BY_EXTERNAL_SYNAPSESTORE`.
- **Production Wiring**: NOT DONE.

---

## 2. P3-A Retrieval Integration Adapter Architecture

The `RetrievalIntegrationAdapter` (`cognitive_core/integration_adapter.py`) provides an independently importable, decoupled mediation layer between future callers (such as `MemoryController.search()`) and the underlying retrieval stack:

```
┌────────────────────────────────────────────────────────────────────────┐
│               Caller (e.g. MemoryController.search())                  │
│  - Caller Authorization: checks caller token & role                   │
│  - Builds IntegrationSearchRequest                                    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                    RetrievalIntegrationAdapter                         │
│  - Parameter Canonicalization (whitespace, types, bounds 1..100)       │
│  - Security Ceiling Gate: prevents clearance elevation                 │
│  - Multi-Factor Cursor Signing & Verification (HMAC-SHA256)           │
│  - Cross-Principal Cursor Tampering Rejection                         │
│  - Sanitized Public Search Hit Formatting (hides retriever internals)  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                     ProductionRetrievalFacade                          │
│  - Boundary-first gate & secondary defense-in-depth sanitization      │
│  - Deterministic tie-breaking (Score DESC, Note ID ASC)               │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                     RetrievalBoundaryAdapter                           │
│  - Enforces invariant ceiling: ALLOWED_LIFECYCLES = {ACTIVE}           │
│                                ALLOWED_VERIFICATION = {verified}       │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                         HybridRetriever                                │
│  - In-memory BM25 lexical + Entity extractor + CorpusGraph multi-hop   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. P3-A.1 Request Contract

The request interface is strongly typed and non-ambiguous:

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

### Canonicalization & Validation Rules
- **Query**: Stripped, consecutive whitespace collapsed. Empty or whitespace-only queries raise `IntegrationRequestValidationError`.
- **Page Size**: Integer strictly bounded in `[1, 100]`. Booleans (`True`/`False`), floats, zero, negative, or $>100$ raise `IntegrationRequestValidationError`.
- **Lifecycles**: Lowercase/whitespace normalized. Non-active requests (e.g. `REVIEW`, `RAW`) raise `IntegrationSecurityError`.
- **Types**: Canonicalized lowercase strings.
- **Disclosure Level**: Validated against `{"standard", "summary", "full"}`.
- **Request ID**: Auto-generated sequential ID (`req-N`) if omitted.

---

## 4. P3-A.2 Security Ceiling Enforcement

The adapter guarantees unidirectional clearance propagation:
$$\text{Principal} \longrightarrow \text{Authorization Context} \longrightarrow \text{Lifecycle Ceiling} \longrightarrow \text{Verification Ceiling} \longrightarrow \text{Retrieval}$$

- **Authorized Principals**: `HUMAN`, `AI_AGENT`, `ADMIN`.
- **Ceiling Invariant**: Clearance cannot exceed `ACTIVE + verified`.
- **Leakage Prevention**: Guaranteed 0 leakage of `REVIEW`, `RECONSOLIDATING`, `SUPERSEDED`, `ARCHIVED`, or `unverified` records.
- **Fail-Closed Gate**: Broadening filter requests (e.g. `lifecycles=["ACTIVE", "REVIEW"]` or `verification=["unverified"]`) trigger immediate `IntegrationSecurityError` before invoking any retriever.

---

## 5. P3-A.3 Response Contract

The public response envelope exposes only sanitized hit objects:

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

@dataclass
class IntegrationSearchResponse:
    results: List[IntegrationSearchHit]
    total_hits: int
    page_size: int
    request_id: str
    principal: str
    next_page_token: Optional[str]
    trace: Dict[str, Any]
    retrieval_mode: str  # "hybrid_boundary_secure"
    deterministic: bool  # True
    timestamp_utc: str
```

**Information Hiding**: Internal retriever representations, AST structures, raw inverted index postings, and graph pointers are completely withheld.

---

## 6. P3-A.4 No-Mutation Guarantee

- The adapter executes strictly in-memory read-only traversal.
- Validated via `test_no_mutation_guarantee`: directory tree SHA-256 state before search $\equiv$ state after search across 10 repeated multi-principal queries.
- Zero disk file mutations, zero metadata modifications, zero database writes.

---

## 7. P3-A.5 Multi-Factor Pagination Contract

Pagination tokens are tamper-evident, multi-factor bound HMAC cursors:
$$\text{Cursor} = \text{HMAC-SHA256}(\text{facade\_token} \parallel \text{principal} \parallel \text{filter\_signature} \parallel \text{timestamp})$$

### Security Properties
1. **Principal Binding**: A token issued to `HUMAN` cannot be reused by `AI_AGENT` (triggers `CursorSecurityError: Cross-principal cursor violation`).
2. **Filter & Query Binding**: Altering the query or narrowing filters between pages invalidates the cursor (triggers `CursorSecurityError: Cursor query/filter mismatch`).
3. **Integrity & Expiration**: HMAC signature verification prevents token forging; 1-hour expiration window blocks replay attacks.

---

## 8. P3-A.7 Performance Overhead Comparison

Benchmark Report: `07_EVALUATION/ci_evidence/adapter_performance_report.json`  
Evaluator: `cognitive_core/benchmarks/adapter_performance_benchmark.py`

```
┌───────────────────────────┬──────────────┬──────────────┬──────────────┐
│ Arm                       │ Median Latency│ P95 Latency  │ P99 Latency  │
├───────────────────────────┼──────────────┼──────────────┼──────────────┤
│ Facade Direct             │ 0.789 ms     │ 1.138 ms     │ 1.412 ms     │
│ Adapter Layered           │ 0.793 ms     │ 1.196 ms     │ 1.455 ms     │
├───────────────────────────┴──────────────┴──────────────┴──────────────┤
│ Absolute Overhead: +0.004 ms                                            │
│ Percentage Overhead: +0.51% (Target: < 10.0%)                           │
│ Verdict: PASS                                                          │
└────────────────────────────────────────────────────────────────────────┘
```

The adapter overhead is **$0.51\%$** ($+0.004\text{ ms}$), far below the $10.0\%$ SLA.

---

## 9. P3-B.1 Note Classifier & Categorization

Classifier: `cognitive_core/benchmarks/corpus_remediation.py` (`CorpusNoteClassifier`)

| Category | Definition & Decision Rule | Typical Confidence |
| :--- | :--- | :--- |
| **template stub** | `policy-lesson_*` stubs, `{{...}}` unhydrated markers, $<30$ tokens | $0.88 - 0.95$ |
| **canonical knowledge** | Substantive architecture & governance notes in tracked spine | $0.70 - 0.85$ |
| **duplicate** | Exact normalized body SHA-256 match with another note | $1.00$ |
| **near duplicate** | Token Jaccard similarity $\ge 0.85$ | $0.85 - 0.95$ |
| **real lesson** | Substantial retrospective / postmortem content ($\ge 10$ tokens) | $0.90$ |
| **test artifact** | Notes located in test paths or with `test_` fixture IDs | $0.98$ |
| **generated artifact** | Benchmark reports, audit logs, CI evaluation summaries | $0.92$ |

---

## 10. P3-B.2 & P3-B.3 Duplicate Clusters & Canonical Representative

Cluster Report: `07_EVALUATION/ci_evidence/duplicate_clusters_report.json`  
Total Clusters Discovered: **18 clusters**

### Canonical Representative Selection Hierarchy
1. **Verification Status**: `verified` ($+2$) $>$ `unverified` ($+1$)
2. **Lifecycle State**: `ACTIVE` ($+3$) $>$ `REVIEW` ($+2$) $>$ `RAW` ($+1$)
3. **Provenance Quality**: `official` ($+4$) $>$ `user` ($+3$) $>$ `experience` ($+2$) $>$ `ai/inference` ($+1$)
4. **Information Density**: Longest substantive body text
5. **Deterministic Tie-Break**: Lowest alphabetical note ID

All lineage is preserved in the cluster metadata; zero files are deleted.

---

## 11. P3-B.4 Cleanup Policy Simulation & P3-B.5 Safety Rule

Report: `07_EVALUATION/ci_evidence/cleanup_policies_report.json`

```
┌────────────────────────────┬────────────────┬────────────────┬────────────────┐
│ Metric                     │ CONSERVATIVE   │ BALANCED       │ STRICT         │
├────────────────────────────┼────────────────┼────────────────┼────────────────┤
│ Notes Retained             │ 258            │ 241            │ 233            │
│ Notes Removed              │ 580            │ 597            │ 605            │
│ Duplicates Removed         │ 580            │ 580            │ 580            │
│ Template Stubs Removed     │ 0              │ 17             │ 17             │
│ Test Artifacts Removed     │ 0              │ 0              │ 0              │
│ ACTIVE Verified Retained   │ 72 (100%)      │ 72 (100%)      │ 72 (100%)      │
│ ACTIVE Verified Removed    │ 0              │ 0              │ 0              │
│ Safety Rule Satisfied      │ TRUE           │ TRUE           │ FALSE (Averted)│
│ Relation Density           │ 0.2946         │ 0.3154         │ 0.2876         │
│ Provenance Completeness    │ 55.43%         │ 59.34%         │ 60.09%         │
│ Known-Item Recall@10       │ 1.0000         │ 1.0000         │ 1.0000         │
│ Known-Item MRR             │ 0.7383         │ 0.7328         │ 0.7328         │
│ Median Latency             │ 0.755 ms       │ 0.652 ms       │ 0.653 ms       │
└────────────────────────────┴────────────────┴────────────────┴────────────────┘
```

### Safety Rule Enforcement (P3-B.5)
- Under `CONSERVATIVE` and `BALANCED`, exactly 0 active verified notes are targeted for removal.
- Under `STRICT`, the policy attempted to target Obsidian mirror notes, but the safety guard intercepted and protected all 72 active verified notes.
- **Recommended Policy for downstream consolidation**: **`BALANCED`** (removes 580 exact duplicate stubs + 17 unhydrated template stubs while preserving all 72 active verified notes and near-duplicate knowledge variants).

---

## 12. P3-B.6 Golden Regression Dataset

Generated File: `07_EVALUATION/ci_evidence/corpus_golden_regression_dataset.json`  
Contains structured, frozen references for:
- Canonical active verified notes
- Review unverified notes
- Orphan notes
- Dangling relations
- Exact duplicate cluster samples
- Template cluster samples
- Near duplicate cluster samples

---

## 13. P3-C SynapseStore Blocker Status

- `05_DATA/synapses.json`: **ABSENT**
- `cognitive_core/synapse_store.py`: **ABSENT**
- Status: **`BLOCKED_BY_EXTERNAL_SYNAPSESTORE`**
- Fallback to intrinsic corpus graph (`CorpusGraph`) remains active, verified, and passing all quality benchmarks. Zero mock data added.

---

## 14. Remaining Blockers & Next Implementation Step

### Remaining Blockers
1. **External SynapseStore**: Upstream delivery of `05_DATA/synapses.json` and `cognitive_core/synapse_store.py` (owned by Claude Code / external track).
2. **Runtime Wiring Authorization**: Coordination sign-off to connect `MemoryController.search()` to `RetrievalIntegrationAdapter`.

### Exact Production Wiring Prerequisite
When the council authorizes runtime activation:
1. `MemoryController` instantiates `RetrievalIntegrationAdapter(facade)`.
2. `MemoryController.search(...)` delegates query execution to `adapter.search(IntegrationSearchRequest(...))`.
3. `MemoryController.search_financial(...)` can map financial domain queries with `types=["financial"]`.

### Exact Next Commit to be Made by Integration Owner
```bash
git commit -m "feat(memory_controller): wire search() through RetrievalIntegrationAdapter"
```
