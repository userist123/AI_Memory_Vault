# P0 Diagnostic — Budget vs Retrieval vs Model Capability

## Implementation
- [x] Create `evaluation/retrieval_diagnostic_runner.py` with 3 isolated diagnostic experiments `[owner: antigravity | timestamp: 2026-09-01T21:04:45+03:00]`
  - [x] Experiment 1 — Budget: A1 (1-hop / 5 results) vs A2 (2-hop / 10 results) vs B (full context)
  - [x] Experiment 2 — Multi-Signal Retrieval: R1 (Semantic), R2 (Semantic + Lexical), R3 (Semantic + Lexical + Entity), R4 (Semantic + Lexical + Entity + Graph)
  - [x] Experiment 3 — Model Capability: M1 (`qwen2.5-coder:3b`) vs M2 (`qwen2.5-coder:7b`) on A1 and B
  - [x] Required-Fact Root Cause Analysis: Categorize failures into `RETRIEVAL_FAILURE`, `MODEL_CAPABILITY_FAILURE`, `BOTH`, `SUCCESS`
  - [x] Outputs generated: `evaluation/retrieval_diagnostic_report.json` and `evaluation/retrieval_diagnostic_report.md`

## Verification
- [x] Create `evaluation/tests/test_retrieval_diagnostic.py` with 4 unit tests `[owner: antigravity | timestamp: 2026-09-01T21:05:42+03:00]`
  - [x] Test fact presence checking in context
  - [x] Test failure taxonomy classification logic
  - [x] Test multi-signal retrieval layers (R1 to R4) output validity
  - [x] Test evaluation cases dataset integrity (15/15 cases validated)
  - [x] Run pytest suite: 4/4 passed in 0.02s; full suite: 1591 passed in 50.34s

## Review
- **What Changed**: Implemented isolated `evaluation/retrieval_diagnostic_runner.py` and test suite `evaluation/tests/test_retrieval_diagnostic.py`.
- **Invariants Maintained**:
  - `Council_Runtime_Profile.yaml`: 100% untouched
  - `ContextPackBuilder`: 100% untouched
  - `config/model_tiers.json`: 100% untouched
  - `cognitive_core/conflict_detector.py`: 100% untouched
  - `Planner`, `PlanComplexityAnalyzer`, `CouncilBudgetController`, `Council_Orchestrator.py`: 100% untouched
- **Proof**: Complete matrix of 135 LLM executions on local Ollama, 4/4 diagnostic tests pass, 1591 full suite tests pass.

---

# P0a — Outcome Tracker Implementation (`memory_controller/outcome_tracker.py`)


## Implementation
- [x] Create `memory_controller/outcome_tracker.py` with immutable `OutcomeRecord` schema `[owner: antigravity | timestamp: 2026-09-01T20:20:26+03:00]`
  - [x] Fields: `event_id`, `run_id`, `outcome` (success|fail|partial|unknown, default: unknown), `verification_method` (test_pass|exit_code|human_confirmed|none, default: none), `timestamp`, `task_signature`, `evidence`, `recorded_by`
  - [x] Fail-closed: `outcome="success"` with `verification_method="none"` raises `ValueError`
  - [x] Storage isolation: Writes strictly to `telemetry/outcomes/council_outcomes.jsonl` (raises `PermissionError` on canonical vault dirs)
  - [x] Append-only provenance: Multiple observations per `run_id` (e.g. automatic then human) preserved chronologically
  - [x] Zero coupling: No imports or calls to `proposal_queue`

## Verification
- [x] Create `memory_controller/tests/test_outcome_tracker.py` with test cases A-H `[owner: antigravity | timestamp: 2026-09-01T20:21:17+03:00]`
  - [x] Test A: Verified success run (`outcome=success`, `verification_method=test_pass`)
  - [x] Test B: Failed run (`outcome=fail`, `verification_method=test_pass`)
  - [x] Test C: Unverified run (`outcome=unknown`, `verification_method=none`)
  - [x] Test D: Partial run (`outcome=partial`)
  - [x] Test E: Append-only provenance preservation
  - [x] Test F: Invalid enum values rejected
  - [x] Test G: Canonical memory write isolation (PermissionError)
  - [x] Test H: Zero proposal queue coupling AST verification
  - [x] Run pytest suite: 8/8 passed in 0.06s; full suite: 1591 passed in 69.03s

## Review
- **What Changed**: Implemented standalone `memory_controller/outcome_tracker.py` and unit suite `test_outcome_tracker.py`.
- **Invariants Maintained**:
  - `config/model_tiers.json`: 100% untouched
  - `cognitive_core/conflict_detector.py`: 100% untouched
  - `proposal_queue.py`: 100% untouched
  - Canonical vault (`00_CORE`..`05_DECISIONS`): 100% untouched
- **Proof**: 8/8 tests in `test_outcome_tracker.py` pass; full suite passes with 1591 passed, 1 skipped, 0 failures.

---

# Fix Pack Implementation Tasks (Pack 3 — Outcome Labeling on Council Run)


- [x] Task 1: `OutcomeEvent` Schema & Append-Only Invariants on `CouncilRunWithExecution` `[owner: antigravity | timestamp: 2026-08-31T23:53:52+03:00]`
  - [x] Define `OutcomeEvent` (frozen dataclass) with `event_id`, `run_id`, `timestamp`, `outcome` (success|failure|partial|unknown), `source` (exit_code|test_result|human|llm_judge), `confidence` (low|medium|high), `evidence` (str), `labeled_by` (optional)
  - [x] Add `_outcome_events: List[OutcomeEvent]` and `add_outcome_event()` to `CouncilRunWithExecution` in `cognitive_core/council_model_execution.py`
  - [x] Enforce immutability and append-only semantics (no overwrite/delete methods)
- [x] Task 2: Minimal Automatic Telemetry Population (synthesis_presence) `[owner: antigravity | timestamp: 2026-09-01T00:00:10+03:00]`
  - [x] In `run_council_with_model_execution`, auto-append single `source="synthesis_presence", confidence="low"` event when execution completes (reserving `exit_code` for real process exit codes)
  - [x] Added `append_outcome_event_to_disk` helper and `persist` support to `add_outcome_event`
- [x] Task 3: Human Labeling CLI (`label-outcome`) `[owner: antigravity | timestamp: 2026-08-31T23:54:14+03:00]`
  - [x] Add `label-outcome` parser and handler in `cognitive_core/memory_v6_cli.py`
  - [x] Persist human outcome events append-only to `04_MEMORY/outcome_events.jsonl`
- [x] Task 4: Automated Verification & Unit Tests `[owner: antigravity | timestamp: 2026-09-01T00:00:18+03:00]`
  - [x] Test append-only immutability (modifying event raises error)
  - [x] Test multiple events coexistence (automatic + human) without overwrite
  - [x] Test CLI human labeling command generates valid `source="human"` event
  - [x] Test disk persistence and `synthesis_presence` telemetry
  - [x] Run full pytest suite across `cognitive_core/tests`, `memory_controller/tests`, `tests/`

---

## Fix Pack 3 Review

### What Changed and Why

1. **Task 1 & 2 (`council_model_execution.py`)**:
   - Defined `OutcomeEvent` as a `@dataclass(frozen=True)` validating controlled vocabularies: `outcome` in `{"success", "failure", "partial", "unknown"}`, `source` in `{"synthesis_presence", "exit_code", "test_result", "human", "llm_judge"}`, and `confidence` in `{"low", "medium", "high"}`.
   - Preserved `99_SYSTEM/Council_Orchestrator.py` 100% frozen and untouched.
   - Added `run_id`, `_outcome_events: List[OutcomeEvent]`, `outcome_events` property (returning a copy), and `add_outcome_event(..., persist=False, persist_path=None)` method on `CouncilRunWithExecution`.
   - In `run_council_with_model_execution`, auto-appended a single baseline observation (`source="synthesis_presence", confidence="low"`) upon successful execution completion, preserving `"exit_code"` strictly for real system process executions.
   - Implemented `append_outcome_event_to_disk` to safely append JSONL records to `04_MEMORY/outcome_events.jsonl`.

2. **Task 3 (`memory_v6_cli.py`)**:
   - Added `label-outcome` command accepting `--run-id`, `--outcome`, `--evidence`, optional `--confidence` (default: `medium`), `--labeled-by` (default: `human`), and optional `--output` (default: `04_MEMORY/outcome_events.jsonl`).
   - Writes validated `OutcomeEvent` records append-only to JSONL on disk.

3. **Task 4 (`test_outcome_events.py` & Boundary Tests)**:
   - Added `cognitive_core/tests/test_outcome_events.py` verifying:
     - `OutcomeEvent` immutability via `FrozenInstanceError` and vocabulary validation errors.
     - Coexistence of multiple outcome events (automatic `synthesis_presence` + `human` + `test_result`) without overwrite.
     - Automatic `synthesis_presence` event generation when model execution is enabled.
     - Optional and CLI disk persistence to JSONL files.
     - CLI `label-outcome` command execution and disk persistence.
   - Confirmed all 12 AST boundary tests in `cognitive_core/tests/test_protected_core_boundaries.py` pass.
   - Full suite execution: **1,563 passed, 1 skipped, 0 failures** in 41.70s.
   - Full suite execution: **1,562 passed, 1 skipped, 0 failures** in 41.67s.

---



# Fix Pack Implementation Tasks (Pack 2 — Cognitive Core & Graph Optimization)

- [x] Task 1: `conflict_detector.py` Deduplication & Hard Note Cap `[owner: antigravity | timestamp: 2026-08-31T23:44:05+03:00]`
  - [x] Implement unique pair generation `(a, b)` with `a.id < b.id` (or `i < j`) to cut comparisons by 50%
  - [x] Add `max_notes: int = 2000` parameter to `ConflictDetector`
  - [x] Guard against exceeding `max_notes` by raising explicit `ValueError` instead of silent O(n²) execution
  - [x] Update `SleepConsolidator` to use optimized pair detection
  - [x] Write unit tests verifying: (a) comparisons halved on N=50 fixture; (b) explicit error when notes > max_notes
- [x] Task 2: `sleep_consolidation.py` Explicit Run Budget `[owner: antigravity | timestamp: 2026-08-31T23:43:28+03:00]`
  - [x] Add `max_items_per_run: int` to `SleepConsolidator.__init__` with configurable default (loaded from `99_SYSTEM/Council_Runtime_Profile.yaml` if available, fallback 100)
  - [x] Update `99_SYSTEM/Council_Runtime_Profile.yaml` with `max_items_per_consolidation_run: 100`
  - [x] Implement explicit age-based selection strategy in `run()` (oldest notes first up to `max_items_per_run`)
  - [x] Report `eligible_notes` vs `processed_notes` in `SleepConsolidationReport.stats`
  - [x] Write unit tests verifying run stops at budget cap and reports counts correctly
- [x] Task 3: `multi_graph.py` Node Type Controlled Schema `[owner: antigravity | timestamp: 2026-08-31T23:43:38+03:00]`
  - [x] Define controlled vocabulary `{"fact", "decision", "procedure", "lesson", "task", "intent", "tool", "failure", "correction", "outcome"}`
  - [x] Add `node_type` support to `Graph.add_node` and note-to-node resolution in `MultiGraphMemory.build_from_notes`
  - [x] Maintain full backward compatibility for notes without explicit `node_type` (fallback to category mapping or "fact")
  - [x] Write unit tests verifying graph construction with and without `node_type` and zero regression
- [x] Final Verification & Review `[owner: antigravity | timestamp: 2026-08-31T23:45:45+03:00]`
  - [x] Run full pytest suite across `cognitive_core/tests`, `memory_controller/tests`, `tests/`
  - [x] Document lessons in `tasks/lessons.md`
  - [x] Add Review section in `tasks/todo.md` with execution proofs

---

## Post-Fix Pack 2 Follow-ups (Audited & Verified)

- [x] Follow-up 1: Isolation check for `_tokenize` in `cognitive_core/conflict_detector.py` `[owner: antigravity | timestamp: 2026-08-31T23:50:00+03:00]`
  - [x] Confirmed `_tokenize` is private to `conflict_detector.py` (not exported/imported elsewhere; `semantic.py` and `skill_router.py` maintain their own isolated tokenizers).
  - [x] Verified 0 side effects across entire 1,557-test suite.
- [x] Follow-up 2: Verified physical diff in `99_SYSTEM/Council_Runtime_Profile.yaml` `[owner: antigravity | timestamp: 2026-08-31T23:50:00+03:00]`
  - [x] Verified `max_items_per_consolidation_run: 100` is written in YAML file under `memory:` section.
- [x] Follow-up 3: Real vault categories verification for `resolve_node_type` `[owner: antigravity | timestamp: 2026-08-31T23:50:00+03:00]`
  - [x] Scanned all Markdown frontmatters across vault and expanded `DEFAULT_CATEGORY_NODE_TYPES` for plural and canonical categories (`decisions`, `procedures`, `lessons`, `errors`, `goals`, `rules`, `protocol`, `consolidated-knowledge`, `soc-tooling`, etc.).
  - [x] Added automated test `test_real_vault_categories_resolve_to_valid_controlled_node_types` in `test_multi_graph.py`.

---

## Fix Pack 2 Review

### What Changed and Why

1. **Task 1 (`conflict_detector.py`)**:
   - Implemented `detect_pairs(self, notes)` computing combinations with `i < j` on pre-tokenized active notes, cutting comparisons from $N \times (N - 1)$ to $N \times (N - 1) / 2$ (e.g. 1,225 vs 2,450 for $N=50$).
   - Added `max_notes: int = 2000` parameter with fail-closed `ValueError` guard when input size exceeds the limit.
   - Fixed `_tokenize` to retain negation tokens like `"nu"` regardless of string length.
   - Added dedicated tests in `cognitive_core/tests/test_conflict_detector.py`.

2. **Task 2 (`sleep_consolidation.py`)**:
   - Added `max_items_per_run` parameter to `SleepConsolidator.__init__` with automatic profile resolution from `99_SYSTEM/Council_Runtime_Profile.yaml` (`max_items_per_consolidation_run: 100`).
   - Implemented deterministic oldest-first selection strategy using `updated` or `created` timestamp.
   - Instrumented `SleepConsolidationReport.stats` to report `total_notes`, `eligible_notes`, `processed_notes`, `budget_cap`, and `budget_exhausted`.
   - Updated `test_sleep_consolidation.py` with budget cap and age prioritization test cases.

3. **Task 3 (`multi_graph.py`)**:
   - Added `CONTROLLED_NODE_TYPES = {"fact", "decision", "procedure", "lesson", "task", "intent", "tool", "failure", "correction", "outcome"}` and validation via `validate_node_type`.
   - Added `resolve_node_type` mapping existing categories without requiring schema migrations.
   - Added `node_types` dictionary attribute to `Graph` and exported dictionary in `to_dict()`.
   - Updated `test_multi_graph.py` with node type resolution and validation test cases.

### Verification Results
- `pytest cognitive_core/tests/test_conflict_detector.py cognitive_core/tests/test_sleep_consolidation.py cognitive_core/tests/test_multi_graph.py -v` -> **17 passed** in 0.07s.
- `pytest cognitive_core/tests memory_controller/tests tests/ -q` -> **1,557 passed, 1 skipped, 0 failures** in 47.70s (100% PASS).

---

# Previous Fix Pack 1 Review (Completed)

- Baseline Test Verification `[owner: antigravity | timestamp: 2026-08-31T23:05:30+03:00]`
- Task 1: CI Context-Budget Enforcement Coverage `[owner: antigravity | timestamp: 2026-08-31T23:05:45+03:00]`
- Task 2: LocalProvider (Ollama) Fail-Closed Context & num_ctx Enforcement `[owner: antigravity | timestamp: 2026-08-31T23:06:20+03:00]`
- Task 3: AST-based Protected Cognitive Core Boundary Tests `[owner: antigravity | timestamp: 2026-08-31T23:08:10+03:00]`
- Review & Documentation `[owner: antigravity | timestamp: 2026-08-31T23:12:32+03:00]`
- Full Suite Verification: 1,550 passed, 1 skipped, 0 failures.


