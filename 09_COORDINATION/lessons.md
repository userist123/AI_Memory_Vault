# Lessons Learned

## Operational Learning Principle — Notes, Drills & Production Playbooks
- Applied School/Laboratory Learning Loop: Knowledge extracted from foundational engineering texts must not remain passive exposition. It follows a 3-stage progression: (1) extracting concise principles and invariants, (2) solving concrete, executable coding drills ("teme de laborator") covering real failure modes (e.g. crash recovery, heuristic search, tool sandboxing, XML prompt demarcation, drift detection, LoRA dynamics), and (3) formulating an instant execution playbook that dictates exact decisions, parameters, and tool invocations when production tasks are assigned.

## Antigravity R001 Traceability Suite — Retrieval Observability & Outcome Mapping
- Multi-Tier Memory Utility Gap: In analyzing 120 retrieved memories from genuine LLM execution traces, 75.0% fell into `RETRIEVED_AND_UNUSED` (dead-weight context loaded into prompts but never operationalized in actions), while 25.0% demonstrated direct functional code/command compilation (`RETRIEVED_AND_FUNCTIONAL`). This proves that measuring context retrieval alone overestimates memory utility four-fold compared to verified execution usage.
- Empirical Activation Inversion: Activating ACT-R frequency priming ($w_{\text{act}} = 0.25$) on identical candidate sets caused a full Top-1 rank inversion ($\text{Kendall's } \tau = -0.3333$, mean rank delta = 2.00). Spreading activation dramatically shifts priority from pure semantic relevance to contextual recency.
- Lifecycle Degradation Boundaries: Non-historical queries against superseded memory drop relevance by 70.0% ($0.3875 \to 0.1163$), causing superseded candidates to reliably fall below the 0.20 abstention threshold unless promoted by active successors or targeted by explicit historical intent.

## Antigravity Parallel Observability V1 — Developer Observability & Pipeline Architecture Inspection
- Component Island vs Integrated Engine: Modules can have high test coverage (`test_multi_graph.py`) while being completely detached from the production query flow (`MemoryController.search()`). Code existence must never be equated to runtime execution without empirical trace verification.
- Lexical Scorer Failure Modes: Keyword/token-overlap scorers (`RelevanceScorer`) excel at verbatim match (+0.2025 margin) but degrade rapidly on paraphrases (+0.0470), completely fail on synonyms (collapsing to abstention floor), and remain vulnerable to lexical traps (ranking false positives +0.0290 over unrelated notes).
- Template Clone Detection in Synthetic Ingestion: Large batch imports of synthetic knowledge atoms often copy identical statistical priors (`confidence=0.78`, `reliability=0.72`, `stability=0.84`, `utility=0.88`, `relations=[]`). These must be flagged as unverified template clones until attested by human authority under `I-001` and `I-004`.
- Bounded Abstention Drift: On lengthy technical notes, stopword and common particle overlaps can push unrelated queries to $0.2083$, surpassing fixed $0.2000$ abstention thresholds. Abstention gates must either normalize by length or incorporate minimum semantic similarity margins.

## Memory Ablation 01 — Controlled Memory Ablation Benchmark
- Paired Alternating Experimental Design: Running benchmark trials in counterbalanced alternating order (Control $\to$ Treatment vs. Treatment $\to$ Control) across independent fresh workspaces eliminates workspace contamination and mitigates temporal or sequence drift.
- Deterministic Benchmark Suite Fingerprinting: Calculating a canonical SHA-256 hash over the task suite (target files, test code, instructions, memory queries) ensures full experimental replication and verifies the benchmark definition was not altered between runs.
- Failure Taxonomy Categorization: Separating failures into `PROVIDER_FAILURE`, `TIMEOUT`, `MODEL_OUTPUT_INVALID`, `ACTION_UNAUTHORIZED`, `TOOL_EXECUTION_FAILURE`, and `TEST_ASSERTION_FAILURE` allows precise diagnostic attribution of where memory helps or hinders performance.

## Real Provider Execution 01 — End-to-End Real Model Inference & Action Execution
- Genuine Model Inference Verification: Testing against a running local Ollama endpoint (`qwen2.5-coder:3b`) with explicit model tag resolution proves that the harness transitions from synthetic/fake tests to empirical neural inference (1101.4ms latency) producing valid structured action JSON.
- Tag Exactness in Local LLM Endpoints: Local model servers (such as Ollama) strictly enforce precise tag specifications (e.g. `qwen2.5-coder:3b` instead of unqualified `qwen2.5-coder`). Explicitly querying `/api/tags` or providing tagged model names prevents 404 tag resolution failures.
- Zero-Credential Exposure Across Neural Inference: Verifying the full pipeline output confirms that even when real models generate code and execute subprocess commands, no HMAC secret, API key, or authorization header leaks into disk traces or reports.

## Real LLM Agent Execution 01 — Model Execution Boundary & Action Scoping
- Fail-Closed Model Execution: Enforcing distinct provider modes (`deterministic`, `fake`, `local`, `openai`) without silent fallbacks guarantees that missing credentials or unavailable endpoints are caught and traced with exact error diagnostics instead of falsely passing via synthetic mocks.
- Structured Action Validation & Least Privilege: Parsing model output into formal action contracts (`write_file`, `run_command`, `read_file`) and cross-validating against `ROLE_ALLOWED_ACTIONS` prevents unauthorized actions (such as a read-only `verifier` attempting `write_file`) and prevents path traversal escapes outside the workspace boundary.
- Recursive Secret Redaction: Sweeping in-memory trace objects against all active environment and argument credentials (`OPENAI_API_KEY`, `MEMORY_CONTROLLER_HMAC_SECRET`) prior to disk serialization ensures zero token or key leakage into persistent telemetry artifacts.

## Real Agent Execution 01 — Reproducible Agent Execution Harness
- Real Execution Contract: Decomposing agent execution into an explicit 12-step lifecycle (role validation, memory search, context binding, real command execution, workspace diffing, verification, and trace persistence) guarantees deterministic execution while cleanly decoupling orchestration, tool execution, and neural inference.
- Memory-to-Context Observation Boundary: Computing a SHA-256 digest over the exact serialized execution context captures the empirical presence of retrieved memory notes (`OBSERVED`) without conflating presence with usage or claiming unproven causal effectiveness.
- Persistence Under Error Conditions: Wrapping execution and verification in fail-safe trace persisters ensures that failed commands, non-zero exit codes, and assertion errors are recorded with full diagnostic fidelity rather than disappearing on test failure.

## CI Repair 01 — Cross-Platform CI Matrix & Dependency Hardening
- Python 3.10 vs 3.11+ Enum Compatibility: `enum.StrEnum` was only introduced in Python 3.11. Using `class MyEnum(str, Enum):` provides identical behavior while ensuring 100% backward compatibility with Python 3.10.
- Runtime Annotation Evaluation in Python <= 3.12: While Python 3.14 (PEP 649) defers annotation evaluation, Python 3.10-3.12 evaluates type annotations in function signatures at import time unless `from __future__ import annotations` is present. All referenced typing symbols (`List`, `Tuple`, `Optional`, `Any`) must be explicitly imported at the module level.
- Storage Fallback & Test Pollution: When test suites import modules like `vault_api.py`, SQLite database files (`vault_memory.sqlite3`) may be initialized on disk with valid schema but zero records. Any storage resolver (`get_memory_controller`) must verify `SELECT COUNT(*) FROM notes > 0` before selecting SQLite over fallback filesystem storage engines.
- Cross-Platform File Globbing: On POSIX filesystems, `glob.glob(os.path.join(folder, "**", "*.md"), recursive=True)` can miss top-level directory files when operating on absolute paths in certain environments. Combining `glob("*.md")` with `glob("**/*.md")` guarantees full directory traversal across Windows, macOS, and Linux.

## Task 1
- Circular import between `temporal_controller.py` and `temporal_conflict.py` prevented `tests/test_temporal_controller.py` from loading during pytest collection when `tests/` is included. Moving the import inside the function or ordering imports cleanly resolves the issue.
- Context budget limits during pagination: when large `limit` (e.g. 50,000) or high numbers of duplicate test records are stored, `apply_degradation` must dynamically prune `ordered` notes in a loop by checking both `usage(ordered) <= soft_limit_bytes` and `serialized_size(ordered) <= hard_limit_bytes` to prevent `BudgetExceededError` on legitimate search queries.
- Note frontmatter schema strictly rejects additional arbitrary top-level properties like `title` and `symbol` (`additionalProperties: False`); structured fields must be nested within `content` or represented via canonical `tags`.

## Task 2
- Ollama silent truncation hazard: Ollama drops context when requests exceed `num_ctx` without returning HTTP errors. By passing `num_ctx` in `options` and performing pre-flight token estimation in `LocalProvider.generate`, we fail closed immediately before making HTTP calls.

## Fix Pack 2 Lessons

### Task 1 — Pairwise Conflict Deduplication & Cap
- Negation token preservation: `_tokenize` previously dropped tokens with `len <= 2`, which inadvertently discarded `"nu"` (the primary Romanian negation token). Ensuring tokens in `_NEGATION_TOKENS` are retained regardless of length prevents false negative contradiction classifications.
- Pairwise comparisons: By iterating `i` from `0..n` and `j` from `i+1..n` on pre-tokenized active notes, we eliminate the previous `N * (N - 1)` duplicate evaluations down to `N * (N - 1) / 2` and enforce a fail-closed hard cap (`max_notes: int = 2000`).

### Task 2 — Sleep Consolidation Budgeting
- Age-based prioritization: When canonical notes exceed the `max_items_per_consolidation_run` profile budget, sorting eligible notes by oldest timestamp (`updated` or `created`) guarantees consistent starvation prevention across incremental sleep runs while reporting `eligible_notes` vs `processed_notes`.

### Task 3 — Multi-Graph Node Type Controlled Vocabulary
- Graph node typing: Adding `node_type` with controlled vocabulary `{"fact", "decision", "procedure", "lesson", "task", "intent", "tool", "failure", "correction", "outcome"}` and mapping existing categories (`architecture` -> `decision`, `policy-lesson` -> `lesson`) enables typing across all 4 orthogonal graphs without requiring destructive migration of existing Markdown notes.

## Fix Pack 3 Lessons

### Task 1 & 2 — Outcome Events Telemetry & Immutability
- Frozen Dataclass Immutability: Defining `OutcomeEvent` with `frozen=True` and returning copies from `CouncilRunWithExecution.outcome_events` structurally guarantees append-only semantics, preventing in-place mutations from external callers or evaluation engines.
- Minimal Automatic Telemetry: Auto-populating a single `source="exit_code", confidence="low"` event upon model execution completion provides a baseline observation without premature coupling to proposal queues or evaluation engines.

### Task 3 — Human Labeling CLI
- Disconnected Human Audit Logging: Storing CLI-generated human labels in `04_MEMORY/outcome_events.jsonl` preserves append-only event logs on disk with timestamps, run IDs, and explicit confidence scores, ready for future offline analysis without runtime overhead.

## P0a Outcome Tracker Lessons
- Strict Storage Isolation: Enforcing hard boundary checks against canonical vault directories (`00_CORE`..`05_DECISIONS`, `99_SYSTEM`) at tracker initialization guarantees that execution outcome telemetry remains decoupled from verified memory.
- Fail-Closed Success Invariant: Disallowing `outcome="success"` when `verification_method="none"` prevents silent assumption of task success.
- Append-Only Provenance: Supporting multiple observations per `run_id` without destructive overwrites preserves full chronological audit trails (e.g. initial automated observation followed by human operator attestation).

## P0 Real Pipeline Diagnostic Correction Lessons
- **Real Pipeline vs Synthetic Simulation**: When measuring production retrieval performance, simulated dictionary lookups mask real retrieval behavior. Ingesting actual Markdown disk notes into `StorageEngine` and querying through `MemoryController.search` -> `QueryClassifier` -> `RetrievalEngine` -> `RelevanceScorer` -> `ProgressiveDisclosure` -> `ContextPackBuilder` revealed that default retrieval achieved only 6.7% factual evidence coverage on complex governance queries because token overlap alone without graph expansion fails to retrieve cross-referenced policy rules.
- **Evidence Coverage vs Model Correctness**: Decoupling context factual presence (`evidence_coverage`) from model semantic accuracy (`answer_correctness`) proved that when the model is provided with full context (Real B), accuracy jumps to 63.3% on 3B and 75.9% on 7B, pinpointing the primary bottleneck in the retrieval layer rather than model reasoning capacity.
- **Multi-Signal Reality**: An empirical audit showed that while BM25, Qdrant vectors, and 4-view Multi-Graphs exist in specialized modules (`financial_search.py`, `cognitive_core/multi_graph.py`, `qdrant_retrieval.py`), they are `PARTIAL` because they are not yet unified into the default `MemoryController.search` pipeline.

## R1→R4 Retrieval Fusion Laboratory Lessons
- **Lexical BM25 (R2) Recovers Exact Terms**: Adding Okapi BM25 (`BM25Ranker`) via Reciprocal Rank Fusion boosted `SIMPLE_FACT` fact recall from 88.8% to 94.5% and `CONTRADICTION_GUARDRAIL` fact recall from 61.0% to 69.3%, confirming hypothesis `R-H002` that lexical matching resolves exact acronym/PRAGMA blindness.
- **Entity Anchoring (R3) Drives Candidate Recall**: Explicit tag and identifier matching increased overall candidate recall from 63.3% to 76.7% (+13.4%), achieving the highest guardrail coverage (77.7%).
- **Graph Expansion (R4) Doubles 7B Reasoning Accuracy**: Expanding candidate seeds through 1-hop relational neighbors (`MultiGraphMemory`) increased `qwen2.5-coder:7b` accuracy on multi-hop questions from 12.8% to 26.7% (+13.9% net accuracy), demonstrating that connected dependency nodes are vital for cross-document synthesis.
- **Packing Bottleneck (`PACKING_FAILURE`)**: Across all strategies, the dominant remaining failure mode was `PACKING_FAILURE` (7-10 cases per strategy) where candidate notes contained the required facts, but progressive disclosure truncation or aggressive section filtering pruned the evidence before passing the context pack to the LLM.
- **Zero Production Modification Invariant**: Running the fusion lab through isolated adapters (`evaluation/retrieval_fusion/`) allowed rigorous empirical validation of all 4 candidate generation signals on real vault data while keeping production cognitive core modules 100% frozen.

## P1 Context Packing Laboratory Lessons
- **Packing Loss Was the Hidden Killer**: In the production baseline (P0), `apply_degradation` discarded `76.1%` of all discovered candidate facts due to zlib binary compression of notes > 1024 bytes and content blanking on notes beyond index 3 (`max_full_documents=3`), rendering them unreadable to the LLM.
- **Section-Aware Extractive Packing (P2) Recovers 100% of Lost Context**: By parsing Markdown headers and extracting relevant section chunks rather than whole documents, Context Fact Recall jumped from `10.0%` to `76.7%` with `0.0%` packing loss.
- **Prompt Token Reduction Boosts Small Model Accuracy**: Extractive packing cut context tokens by 50% (from ~3000 to ~1470 tokens), eliminating attention noise. This lifted 3B model accuracy from `11.7%` to `61.7%` (P4) and 7B model accuracy from `15.6%` to `71.1%` (P2), completely exceeding the uncompressed Full Context baseline (`68.3%`).
- **Guardrail Negation Preservation**: Preserving critical negation words (`NOT`, `NEVER`, `CANNOT`, `GATED`, `IMMUTABLE`) restored `CONTRADICTION_GUARDRAIL` fact recall from `0.0%` in P0 to `77.7%` in P2-P4.

## P2 Temporal Memory Laboratory Lessons
- **Metadata Availability Gap**: Audit of all 832 notes showed that while `created`, `updated`, and `lifecycle` are 100% available, `valid_from`, `valid_until`, and `supersedes` are 0.0% populated in static Markdown frontmatters on disk, causing static keyword search to be temporally blind.
- **Supersession Lineage Traversal (T2) Resolves Outdated Seeds**: Dynamically traversing reciprocal `supersedes` / `superseded_by` edges lifted accuracy on superseded policy queries from 0.0% to 100.0% (boosting overall 3B accuracy from 23.8% to 40.5%).
- **Non-Overlapping Intervals Eliminate False Contradictions**: Evaluating validity intervals (`valid_from` .. `valid_until`) prevents the system from treating sequential rule revisions as simultaneous contradictions.
- **Abstention on Missing Temporal Metadata**: When timestamps are missing, the system must emit explicit `UNKNOWN` status rather than guessing temporal priority.

## External Memory Usage Audit Lessons
- **Passive Proximity vs Active Utilization**: Access to the Vault does not equal usage. Agents frequently claim "I followed the architecture" or "I checked the skills" without executing a single file read or search tool call.
- **Anti-Fabrication Principle**: Unsubstantiated agent self-reports evaluate strictly to `UNVERIFIED` (score: 0.0) across all 11 audit stages unless corroborated by tool logs, file reads, or command execution traces.
- **Mandatory Provenance Chain**: A claim of memory utilization requires an unbroken chain from `query` $\rightarrow$ `retrieval` $\rightarrow$ `decision` $\rightarrow$ `execution` $\rightarrow$ `empirical verification` $\rightarrow$ `outcome log`.

## Agent Memory Trace Emitter Protocol Lessons
- **Declared vs Observed Distinction**: Enforcing a strict schema separation between `declared` claims and `observed` events with concrete evidence references (`tool_calls`, `pytest exit_code`, `telemetry hashes`) allows automated detection of memory hallucinations.
- **Trace Completeness State Machine**: The canonical 8-stage lifecycle (`QUERY` $\rightarrow$ `RETRIEVE` $\rightarrow$ `LOAD` $\rightarrow$ `ACTIVATE` $\rightarrow$ `DECIDE` $\rightarrow$ `EXECUTE` $\rightarrow$ `VERIFY` $\rightarrow$ `OUTCOME`) immediately isolates the exact first broken link (e.g. `RETRIEVE` in WOB ART).
- **Causal Decision Verification**: A decision can only achieve `MEMORY_INFLUENCE_VERIFIED` if the governing note was retrieved, loaded into working memory, and explicitly referenced in the decision event.

## Runtime Observed Memory Trace Lessons
- **Final Context Filtering Invariant**: The observed memory trace must record only the IDs present in the final context pack after degradation and budget fitting (`pack["results"]`), ignoring candidates rejected during earlier retrieval/scoring phases.
- **Fail-Safe Passive Instrumentation**: Telemetry write failures (e.g. disk write issues) must be caught silently within `ContextPackBuilder` so that context pack delivery and model execution are never blocked.
- **Data Minimization Rule**: Persisting only `run_id`, `timestamp`, `retrieved_memory_ids`, and `retrieval_scores` eliminates any risk of prompt or private note content leakage into telemetry logs.

## Memory Trace Hardening & Proof Lessons
- **Caller Passthrough Invariance**: An audit of all 5 callers of `ContextPackBuilder.build()` confirmed that `pack["results"]` is never mutated, pruned, or reordered downstream. Thus `PACK_OBSERVED == MODEL_CONTEXT_OBSERVED`.
- **Thread-Safe Lock Isolation**: Appending observed memory traces under `_TRACE_LOCK` ensures zero corruption or record intermixing under multi-threaded concurrency.
- **No False Observation on Persistence Failure**: When telemetry writes fail, `reconcile_observed_trace` emits `OBSERVATION_FAILED`, preventing the system from falsely hallucinating an `OBSERVED` state when logs are missing.

## Memory Vault Reorganization Lessons
- **Preserved Provenance via 10_ARCHIVE**: Relocating legacy duplicate files (`*_Claude_Legacy.md`, `*_Perplexity_Legacy.md`) into `10_ARCHIVE/legacy_duplicates/` via `git mv` purges active namespace clutter while 100% preserving historical Git attribution and full provenance.
- **Master Navigational Index (`VAULT_INDEX.md`)**: Providing a centralized root index in `01_KNOWLEDGE/VAULT_INDEX.md` prevents duplicate notes from being authored by giving agents an immediate lookup table of all canonical domain notes, ADRs, runbooks, and evaluation labs.
- **Architectural Layer Separation**: Structurally decoupling machine telemetry (`telemetry/`), external conversation audits (`evaluation/memory_usage_audit/`), empirical research labs (`evaluation/`), and verified knowledge (`01_KNOWLEDGE/`) enforces clean boundaries and prevents unverified runtime artifacts from polluting canonical memory.

## Cognitive Memory Mesh Lessons
- **Unified 11-Type Object Taxonomy**: Disambiguating objects into strict, non-overlapping types (`KNOWLEDGE`, `MEMORY`, `SKILL`, `PROCEDURE`, `AGENT`, `EXPERIMENT`, `EVIDENCE`, `OUTCOME`, `TRACE`, `AUDIT`, `RESEARCH`) prevents conflating raw evidence with verified knowledge or declared skills with observed executions.
- **Directional Typed Relationships & Evidence Lineage**: Structuring typed edges (`produced_by`, `supported_by`, `tested_by`, `requires`, `uses`, `implements`, `observed_by`) enables full trace traversal from external research through experiments to canonical memory without mutating production retrieval graphs.
- **Zero Runtime Interference**: Constructing the mesh metadata index in `evaluation/vault_mesh/` and testing via `MeshValidator` keeps `cognitive_core/multi_graph.py` completely frozen while achieving 100% graph verification across 877 nodes and 2,411 edges.

## Project Ledger & Skill Effectiveness Lessons
- **Explicit project_id Invariance**: `project_id` must be established once at session initiation (by human or orchestrator) and NEVER inferred by an LLM from query or context text. Historical records without `project_id` must remain `null` to avoid synthetic attribution.
- **Empirical Skill Effectiveness**: Measuring `success_rate = (runs with outcome=success AND skill in OBSERVED_TRACE) / (total runs with skill in OBSERVED_TRACE)` grounds knowledge valuation directly in empirical usage evidence, completely bypassing synthetic benchmarks or unverified declared claims.
- **Relational Join Without Data Duplication**: `telemetry/project_sessions.jsonl` acts as a lightweight join table between `project_id` and `run_id`, querying underlying telemetry dynamically without copying bulky trace or outcome payloads.

## Capability Evidence Engine — Task 1 Schema Extension Lessons
- **Controlled Task Category Invariance**: Restricting `task_category` to a fixed 10-item controlled vocabulary (`frontend_motion`, `frontend_layout`, `backend_api`, `database`, `security_audit`, `trading_logic`, `documentation`, `testing`, `infra_devops`, `unknown`) prevents free-form LLM category hallucination and guarantees deterministic telemetry filtering.
- **Observed Capabilities Isolation**: Structuring `observed_capabilities` (`skills`, `agents`, `knowledge_refs`, `procedure_refs`) and strictly populating it from verified runtime evidence guarantees that declared-only claims in LLM prose never leak into capability effectiveness records.

## Capability Evidence Engine — Task 2 Statistical Estimators Lessons
- **Wilson Score Interval Lower Bound**: Utilizing the Wilson score interval lower bound instead of naive observed proportion provides robust, sample-size-calibrated confidence bounds that penalize low sample volumes without requiring external statistical dependencies.
- **Laplace Smoothing vs Wilson Independence**: Keeping Laplace smoothed probability $(s+1)/(n+2)$ strictly separated from Wilson confidence lower bounds ensures clear differentiation between point estimation under sparsity and conservative decision thresholds for promotion/quarantine.
- **Fail-Closed Minimum Sample Guard**: Hard-gating confidence evaluations below $N=5$ (e.g. $1/1 \rightarrow \text{INSUFFICIENT\_DATA}$) prevents overconfident capability promotions based on solitary anecdotal successes.

## Capability Evidence Engine — Task 3 Capability Effectiveness Matrix Lessons
- **Multi-Dimensional Granular Attribution**: Disaggregating capability effectiveness across controlled task categories (`(capability_type, capability_id, task_category)`) prevents erroneous global aggregation (e.g. recognizing that a skill succeeds in `frontend_motion` but fails in `backend_api`).
- **Run-Level Deduplication Invariant**: Aggregating observations strictly at the unique `run_id` level prevents duplicate telemetry emissions or multiple retrieved references from inflating success counters.
- **Empirical Association vs Causality Boundary**: Explicitly distinguishing empirical co-occurrence from causal attribution protects cognitive integrity against post-hoc reasoning fallacies.

## Capability Evidence Engine — Task 3.1 Observed Evidence Boundary Lessons
- **Strict Trace-Only Evidence Authority**: Relying exclusively on `ObservedMemoryTrace.retrieved_memory_ids` for capability attribution prevents declared claims or unverified outcome metadata (`OutcomeRecord.observed_capabilities`) from generating synthetic capability cells without physical trace verification.

## Capability Evidence Engine — Task 4 Promotion & Retirement Candidates Lessons
- **Human-Gated Recommendation Boundary**: Preserving the fundamental principle `METRIC -> CANDIDATE` (never `METRIC -> AUTOMATIC ACTION`) ensures cognitive safety, preventing autonomous destructive actions or unverified capability mutations.
- **Multi-Category Generalizability Invariant**: Requiring $\ge 2$ valid categories with Wilson lower bound $> 0.85$ prevents narrow, overfitted capabilities from qualifying for global promotion.
- **Anti-Gaming Project Dominance Cap**: Restricting maximum project contribution to $\le 40\%$ eliminates benchmark-gaming and synthetic looping vulnerabilities within single project workspaces.













