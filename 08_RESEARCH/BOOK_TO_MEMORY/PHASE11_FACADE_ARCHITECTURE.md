# Phase 11: Unified Book-to-Memory Facade Architecture

## 1. Overview & Objective

The **Book-to-Memory Facade** (`lifecycle.validation.book_to_memory_facade.BookToMemoryFacade`) provides a single, cohesive, and cryptographically verified control plane that unifies all 10 preceding research and validation phases into an immutable, audit-ready interface.

### Architectural Invariants Enforced:
1. **Passive Data Plane Isolation**: Book text remains strictly inert data (`UNTRUSTED_INPUT`). Executable directives (such as injected keys `exec`, `tool_call`, `override_lifecycle`, `shell_command`) are rejected upfront by `validate_untrusted_security()`.
2. **Epistemic Separation Chain**: Biological facts cannot directly constitute engineering mechanisms. They must strictly follow `BIOLOGICAL_FACT -> ENGINEERING_HYPOTHESIS -> CONTROLLED_EXPERIMENT -> OWNER_ATTESTED_CHANGE`.
3. **No Automated Promotion**: `Principal.AI_AGENT` cannot self-promote candidate notes to `ACTIVE` (GATE-06 / I-001) or self-attest hypotheses to `CLOSED_CHANGE_VALIDATED`. Only `Principal.HUMAN` or `Principal.ADMIN` with an authentic cryptographic HMAC token may approve active transitions.
4. **Usage & Ablation Quality Gates**: Notes must achieve `usage_test_score >= 8/10` and `ablation_delta >= +0.10` without degrading accuracy or security boundaries.
5. **Reversible Lineage & Catalog Synchronization**: Every ingested note updates the reversible catalog and maintains forward and backward lineage references.
6. **Master Cryptographic Digest**: A SHA-256 state digest is dynamically maintained over the entire catalog and problem matrix state.

---

## 2. Component Composition

`BookToMemoryFacade` orchestrates:

| Phase | Component | Class / Module | Role |
|---|---|---|---|
| **Phase 1** | Schema & Ontology | `book_to_memory_schema.py` | Validates 11 note types, frontmatter parity, passive data plane, and injection rejection. |
| **Phase 2** | Lifecycle State Machine | `book_to_memory_lifecycle.py` | Enforces GATE-01..GATE-08, state transitions, and cryptographic HMAC owner approval tokens. |
| **Phase 3** | Conflict Registry | `book_to_memory_conflict.py` | Deterministic `CONFLICT-<domain>-<slug>` identity, dual preservation, severity gating. |
| **Phase 4** | Usage Test Engine | `book_to_memory_usage_test.py` | Task-based validation without book access (>= 8/10 score threshold). |
| **Phase 5** | Paired Ablation Engine | `book_to_memory_ablation.py` | WITH_NOTE vs WITHOUT_NOTE ablation delta measurement and degradation protection. |
| **Phase 6** | Retrieval & Working Memory | `book_to_memory_retrieval.py` | Query-sensitive retrieval, similarity reranking, lifecycle filtering, budget bounds. |
| **Phase 7** | End-to-End Pipeline | `book_to_memory_pipeline.py` | Full multi-gate ingestion pipeline producing `BookIngestionAuditReport`. |
| **Phase 8** | Corpus Catalog | `book_to_memory_catalog.py` | 20-book raw corpus registration, chapter coverage, and reversible unpublishing. |
| **Phase 9** | Problem Matrix | `book_to_memory_hypothesis.py` | Canonical vault bottlenecks and hypothesis lifecycle tracking. |
| **Phase 10** | Controlled Experimentation | `book_to_memory_experiment.py` | Multi-sample shadow mode testing, statistical significance, and owner decision attestation. |

---

## 3. Public API Methods

```python
class BookToMemoryFacade:
    VERSION = "1.0.0"

    def register_book(self, source_identity: str, title: str, authors: List[str], ...) -> Dict[str, Any]
    def ingest_note(self, note_dict: Dict[str, Any], task_spec: TaskSpecification, ...) -> BookIngestionAuditReport
    def retrieve_context(self, query: str, working_memory_budget: int = 1500, ...) -> RetrievalTrace
    def register_hypothesis(self, hypothesis_id: str, problem_slug: str, ...) -> HypothesisCard
    def execute_controlled_experiment(self, config: ExperimentConfig, case_evaluator: Callable, actor: Principal) -> ExperimentResult
    def finalize_decision(self, experiment_id: str, hypothesis_id: str, approved: bool, rationale: str, actor: Principal) -> HypothesisCard
    def get_track_status(self) -> Dict[str, Any]
```

---

## 4. Test Receipts

All 7 facade unit and integration tests are verified passing in `20_TESTS/test_book_to_memory_facade.py`:
- `test_facade_initialization` (PASS)
- `test_facade_book_registration` (PASS)
- `test_facade_ingest_note_and_link_to_catalog` (PASS)
- `test_facade_ingest_note_rejects_malicious_directive` (PASS)
- `test_facade_hypothesis_and_controlled_experiment_approval` (PASS)
- `test_facade_hypothesis_rejects_ai_agent_attestation` (PASS)
- `test_facade_hypothesis_negative_outcome` (PASS)
