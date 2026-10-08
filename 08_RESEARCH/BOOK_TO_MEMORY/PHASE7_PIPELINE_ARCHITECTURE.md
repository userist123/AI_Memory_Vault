# Architecture Specification — Phase 7: End-to-End Book-to-Memory Pipeline

**Document Version**: 1.0.0  
**Status**: APPROVED & IMPLEMENTED  
**Authority**: `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md`  
**Module**: `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_pipeline.py`  
**Test Suite**: `20_TESTS/test_book_to_memory_pipeline.py` (16/16 PASS)  

---

## 1. Executive Summary & Purpose

The **Book-to-Memory End-to-End Pipeline** (`BookToMemoryPipeline`) orchestrates the complete extraction, schema validation, lifecycle transition, conflict checking, usage testing, ablation validation, retrieval preparation, and cryptographic attestation lifecycle defined by `POLICY-LEARNING-QUALITY-02`.

It ties together all foundational cognitive modules established in Phases 1 through 6 into a unified, deterministic, tamper-evident pipeline:

```text
[UNTRUSTED BOOK / INBOX]
           │
           ▼
[Stage 1: Book Map Registration] ─────────► Validates Book Identity, Authors, Chapters
           │
           ▼
[Stage 2: Schema & Security Gate] ────────► Checks 11 atomic schemas + Rejects command injections
           │
           ▼
[Stage 3: RAW -> UNVERIFIED Gate] ───────► Enforces initial unverified admission
           │
           ▼
[Stage 4: Conflict Cross-Check] ─────────► Queries ConflictRegistry for open HIGH/CRITICAL disputes
           │
           ▼
[Stage 5: Task-Based Usage Test] ────────► Evaluates 5-dimension rubric (Score >= 8/10 required)
           │
           ▼
[Stage 6: Paired Ablation Test] ─────────► Compares WITH_NOTE vs WITHOUT_NOTE (Delta >= 0 required)
           │
           ▼
[Stage 7: Promotion to VERIFIED] ────────► Authorizes promotion to VERIFIED state
           │
           ▼
[Stage 8: Retrieval & WM Pack] ──────────► Validates admission to Working Memory + passive tags
           │
           ▼
[Stage 9: Owner Attestation Gate] ───────► Requires cryptographic HMAC token for ACTIVE promotion
```

---

## 2. Core Architectural Components

### 2.1 `PipelineStage` Enumeration
Defines the sequential checkpoints of the ingestion pipeline:
1. `BOOK_MAP_REGISTRATION`: Validates and registers top-level book identity notes (`type: book_map`).
2. `SCHEMA_AND_SECURITY`: Verifies note structure against jsonschema and ensures zero executable keys (`tool_call`, `shell_command`, `exec`, `system_call`).
3. `PROMOTION_TO_UNVERIFIED`: Transitions candidate note from `RAW` to `UNVERIFIED`.
4. `CONFLICT_CROSS_CHECK`: Scans `ConflictRegistry` to check for open contradictions involving this note or domain.
5. `USAGE_TEST`: Executes `TaskBasedValidator` against `TaskSpecification` enforcing minimum threshold $\ge 8/10$.
6. `ABLATION_TEST`: Executes `AblationExperimentRunner` running 12 paired trials across $\ge 2$ models and $\ge 3$ repetitions with alternating order.
7. `PROMOTION_TO_VERIFIED`: Promotes note to `VERIFIED` state under Human/Admin actor.
8. `RETRIEVAL_VERIFICATION`: Verifies note adaptivity, working memory admission, token budgeting, and passive inert context delimiters.
9. `OWNER_ATTESTATION`: Evaluates cryptographic `OwnerApprovalToken`. If missing, note safely rests in `VERIFIED` (`AWAITING_OWNER_APPROVAL`).

### 2.2 `BookIngestionAuditReport`
Every pipeline run emits an immutable, tamper-evident audit report containing:
- `book_title`: Title of the source book.
- `note_id`: Unique identifier of the note.
- `initial_lifecycle` & `final_lifecycle`: Pre- and post-execution states.
- `stage_results`: Complete list of `PipelineStageResult` entries with execution status and details.
- `usage_test_score`: Numerical score (0..10).
- `ablation_delta`: Measured performance delta relative to baseline (supplied by the caller; the pipeline has no built-in scores).
- `conflicts_detected`: List of active conflicts.
- `retrieval_ready`: Boolean flag indicating Working Memory readiness.
- `integrity_digest`: Deterministic SHA-256 hash calculated across all execution facts and timestamp.

---

## 3. Strict Invariants

| Invariant ID | Policy Rule | Pipeline Enforcement Mechanism |
|---|---|---|
| **INV-P7-01** | **No Auto-Promotion** | Without explicit `OwnerApprovalToken`, candidate notes stop strictly at `VERIFIED`. Promotion to `ACTIVE` by `ai_agent` is impossible. |
| **INV-P7-02** | **Passive Data Isolation** | All notes are checked with `validate_untrusted_security`. Any dictionary containing execution directives (`tool_call`, `shell_command`, etc.) immediately triggers `SecurityInjectionError`. |
| **INV-P7-03** | **Strict Provenance Gate** | Notes must include `source_title`, `chapter`, and `page_range` (or `exact_page` for metrics/formulas). Vague or lazy citations are halted at Stage 2. |
| **INV-P7-04** | **Usage Test Hard Threshold** | Usage score must be $\ge 8/10$. Scores in failure range ($< 8$) immediately abort advancement and retain the note in `UNVERIFIED`. |
| **INV-P7-05** | **Ablation Regression Protection** | Any negative delta ($\text{Delta} < 0$) indicates clutter or regression and blocks progression past `UNVERIFIED`. |
| **INV-P7-06** | **Open Conflict Blocking** | An open `HIGH` or `CRITICAL` conflict blocks `ACTIVE` promotion, even if a valid owner approval token is presented. |
| **INV-P7-07** | **Tamper-Evident Ledger** | Every execution produces a signed SHA-256 audit digest documenting all stage results. |

---

## 4. Verification Evidence

The implementation was validated against 16 comprehensive end-to-end tests in [test_book_to_memory_pipeline.py](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/20_TESTS/test_book_to_memory_pipeline.py):
- `16 passed in 0.19s`
- Cumulative Book-to-Memory suite (Phases 1–7): `197 passed in 0.86s`
- Security Invariant suite: `33 passed, 1 skipped in 11.21s`
