# Active Knowledge Engine (AKE): Milestone 0 repository reality audit

- **Status:** `DRAFT / READ-ONLY AUDIT`. No code, schema, policy or canonical note was changed.
- **Date (UTC):** 2026-11-21
- **Auditor:** Claude Code (main session)
- **HEAD:** `aa42387f269d80e306853db0241700c37f49b661` (`origin/main`, merge of PR #393), clean worktree on branch `claude/ake-m0-reality-audit`
- **Local runtime:** Python 3.14.2, pytest 9.0.2. CI runs Python 3.11, so local results are not CI results.
- **Input:** the handoff `AI_Memory_Vault_ACTIVE_KNOWLEDGE_ENGINE_RESEARCH_AND_HANDOFF.md` (owner upload, not in the repository) and an external Gemini deep-research report (owner upload). Both are treated as **hypotheses**, not evidence.

## 1. Verdict

`PARTIAL`. Most of what AKE needs already exists. Much of it is **not wired**, and one existing learning component **contradicts** the AKE invariants. AKE is buildable as a thin, read-only orchestration layer over existing components. It does not need a new store, queue, lifecycle or evaluator framework. Before any orchestration code is written, a few reuse decisions in section 5 need the owner.

## 2. Evidence executed

| Command | Result |
|---|---|
| `git fetch origin main`; `git worktree add -b claude/ake-m0-reality-audit … origin/main` | HEAD `aa42387f2` |
| Production-consumer grep from `VAULT_STATE.md` §2, run for 33 modules (excludes tests, `07_EVALUATION`, `08_RESEARCH`, `.agents`, `.claude`) | table in §3 |
| `python -m pytest -q 20_TESTS/test_closed_loop_learning.py test_evaluation_and_recall_lineage.py test_memory_access.py test_planning_influence_isolation.py test_proposal_queue_attestation.py test_rest_proposal_flow.py test_retrieval_trace_contract.py test_sleep_consolidation.py test_vault_state_accuracy.py` | **116 passed, 1 xfailed, 65.9 s** (local, Python 3.14) |
| `python -m pytest --co -q 20_TESTS` | 5362 tests collected (full suite **not** run in this audit) |
| `gh run list --commit aa42387f2…` | every push workflow `success`: ACTIVE note integrity, Architecture Boundaries, R009b Held-out Retrieval Benchmark, Repository Hygiene, Secret Scan, Security Boundary Tests, Untrusted content guard, Vault access routes, CodeQL, APIsec, Fortify. Scheduled `Scheduled Memory Consolidation` `success` (runs 44896146388, 44725534811). The only failures are Dependabot `uv` updates under `06_INBOX/RAW_IMPORTS/` and are unrelated |

## 3. Reality map for AKE reuse points

Legend: **REAL** = in a production path with a consumer. **NOT WIRED** = code and tests, no production consumer. **CONFLICT** = contradicts an AKE invariant. **UNKNOWN** = not established here.

### 3.1 Canonical write and authority boundary (AKE must go through it, never around it)

| Component | State | Evidence | AKE implication |
|---|---|---|---|
| `memory/controller.py` (`search`, `propose`, `update`, `attest`, `promote`, …) | REAL | `VAULT_STATE.md` §3; ontology slots refuse every principal | sole apply boundary |
| `lifecycle/policy.py` | REAL, sole authority | r001: 7/7 mutation paths gated (AST-verified) | no second lifecycle |
| `lifecycle/proposal_queue.py` + `queue_promoter.py` | REAL, end to end | consumers: `interfaces/api_server.py`, `interfaces/memory_v6_cli.py`; approval requires a typed owner `Principal` (HUMAN/ADMIN), a reviewer and an evidence ref; `test_proposal_queue_attestation.py`, `test_rest_proposal_flow.py` pass | AKE's "Proposal Composer" = `enqueue()` only. A promotion produces RAW/unverified and verifies nothing |
| MCP `memory_propose` (`interfaces/memory_mcp_server.py`, launched by `.mcp.json`) | REAL (process entry point, not imported) | candidate = REVIEW + `unverified` | the alternative proposal path for an agent-facing worker |
| `security/runtime_enforcer.py`, `ApprovalBroker`, `PersistentNonceStore`, revision/content binding | **NOT WIRED** | `test_vault_state_accuracy.py::test_runtime_authority_layer_has_no_production_consumer` passes | AKE **cannot** claim hash-bound approval, replay protection or nonce checks at runtime. The handoff's §7 stage 4 ("approval bound cryptographically") does not hold today |
| `lifecycle/learning_promotion_gate.py` | NOT WIRED | no consumer | do not adopt as a second promotion path |
| `learning/closed_loop_learning.py` (`ClosedLoopLearner`) | **NOT WIRED + CONFLICT** | `record_outcome()` sets `status = "withdrawn"` and calls `self.storage.set(...)` directly as `Principal.AI_AGENT` (`closed_loop_learning.py:326`, `:333`; rollback `:358`). That bypasses `MemoryController` and `lifecycle/policy.py` | its belief-update maths and `AntiSelfConfirmationGuard` are reusable as **pure functions**. Its mutation step must **never** be wired. The r001 "7/7 paths gated" count holds only because this path has no caller |

### 3.2 Read, retrieval and trace

| Component | State | Evidence | AKE implication |
|---|---|---|---|
| `interfaces/memory_access.py` (`memory_search`/`memory_get`) | REAL | consumers: `memory_mcp_server.py`, `vault_cli.py`; `test_memory_access.py` passes | the only read path for LLM workers |
| `observability/retrieval_trace.py` (v1.1.0) | REAL | consumer: `memory/controller.py`, `30_SCRIPTS/evaluation/retrieval_loss_funnel.py`; `test_retrieval_trace_contract.py` passes | extend it compatibly. Do not invent a parallel trace format |
| Graph expansion | wired, **OFF**, no-op in owner view | `VAULT_STATE.md` §3 | no graph benefit claimed without a separate benchmark |
| `retrieval/interference_gate.py` | NOT WIRED | no consumer | — |
| `observability/evaluation.py` `RetrievalEvaluator` (P@K, R@K, MRR, nDCG) | NOT WIRED (metric library) | no consumer outside tests/evaluation | usable as Loop B metric functions. Its "TRACe" metrics are substring heuristics and are not a judge |

### 3.3 Existing evaluation, maintenance and learning assets (overlap with AKE loops)

| AKE loop | Existing asset | State | Notes |
|---|---|---|---|
| A: Integrity | `learning/sleep_consolidation.py` via `memory_v6_cli consolidate` | REAL, **read-only and advisory by docstring and test**; scheduled daily by `.github/workflows/memory-consolidation.yml` (`cron 0 3 * * *`), last runs `success` | **This is already a scheduled read-only AKE Loop A.** It uses `security/conflict_detector.py`. AKE should wrap or extend it, not duplicate it |
| A: Integrity | `FileStorageEngine.load_report` / `storage_diagnostics()`; `untrusted_content_guard.py`; `active-note-integrity.yml`; `write-path-audit.yml` | REAL | deterministic checks already exist and run in CI |
| C: Conflict | `lifecycle/conflict_review.py` (`ConflictReviewWorkflow`, never mutates) | NOT WIRED | matches the handoff's "Conflict Analyst" output contract. Wire before rebuilding |
| C: Conflict | `lifecycle/temporal_conflict.py` / `temporal_controller.py` / `temporal_ranking.py` | consumed only by one another | UNKNOWN whether reachable from `search()`; needs a check before reuse |
| B: Retrieval | held-out benchmark v2 (`07_EVALUATION/heldout_retrieval_benchmark_v2/`), gold verified; CI `r009b-heldout-benchmark.yml` `success` on HEAD; benchmark v3 + loss funnel (`07_EVALUATION/loss_funnel/`) | REAL as evaluation assets | v1 is INVALID. Loss funnel: 71.56% of misses are `PAGINATION_CUT` (single run). This is the highest-value concrete Loop B target |
| D: Influence | `07_EVALUATION/luna/` planning-influence MVE v3, CI `planning-influence-mve.yml` (byte-reproducible tables, N=200/cell) | REAL, **simulated** (`simulate_puct_benchmark.py`) | a simulation, not a real agent. Per the handoff §12 M5 it cannot be cited as planner influence in a real agent |
| D: Influence / eval integrity | `book_to_memory/` (paired statistics, raters, leakage, blind packet, real-model ablation harness, prompt audit, human labels) | NOT WIRED, research-only; returns `INSUFFICIENT_DATA` without observations | **the most complete eval-integrity toolkit in the repo** (leakage, paired stats, multi-rater). The B03 packet (51 tasks, 204 trials) has never been run. Reuse these modules for Milestone 2 instead of writing new ones |
| Outcome feedback | `observability/outcome_tracker.py` (append-only, `success` requires verification) → `capability_effectiveness.py` → `promotion_candidates.py` (Wilson bounds, REVIEW_REQUIRED) | chain consumed only within itself and `memory/project_ledger.py`, which has **no consumer** | NOT WIRED end to end. A good contract match for AKE's EvaluationResult/feedback |
| Workers | `learning/agents/` (consolidator, critic, retrieval, router, verifier `BaseAgent`) | NOT WIRED (only its own `__init__`) | an earlier role-based agent layer. Decide: reuse or retire. Do not add a third |
| Routing | `routing/` agent router + dispatcher | real and tested, NOT WIRED; `PENDING_VERIFICATION` is terminal | AKE must not assume a verifier is ever dispatched |
| Bridge | `agent_bridge/` package | NOT WIRED, no transport | a separate **local daemon** "AI Agent Bridge" (HTTP MCP on loopback, outside this repository) exists on the owner's machine. It is **not** this package and is UNKNOWN as an audited component |

### 3.4 Orchestration primitives AKE would need

| Primitive | Exists? | Evidence |
|---|---|---|
| Job record / state machine | **MISSING** | no `JobStore`, `idempotency_key`, `dead_letter` or lease in `03_IMPLEMENTATION/packages` |
| Checkpoint / resume | partial | `memory/executive.py`, `storage/sqlite_engine.py` mention checkpoints. Not a job runtime |
| Append-only audit | REAL in library | `security/audit_trail.py`, consumed by `audit_pipeline.py` and the unwired runtime adapters |
| Scheduler | REAL via GitHub Actions cron only | `memory-consolidation.yml`. No local scheduler |
| Budgets | config exists, **not loaded** | `04_CONFIG/agent_budgets.json` is never loaded (`VAULT_STATE.md` §5); the council token caps are not enforced |
| Execution policy | `SEQUENTIAL_HANDOFF` | stated in `00_GOVERNANCE/coordination/projects/AI_MEMORY_VAULT/CURRENT.md`, which is marked `HISTORICAL_SNAPSHOT`. The live rule is in `CLAUDE.md` ("one implementation agent at a time") |

## 4. Corrections to the handoff (checked on HEAD)

1. The handoff links `github.com/userist123/AI_Memory_VAULT`. The working repository is `userist123/AI_Memory_Vault_CODEX_READY`.
2. "AKE learning loop PARTIAL/OPEN in README": the README row is **"Învățare continuă complet închisă — PARȚIAL / DESCHIS"** (`README.md:49`). It is the outcome→canonical-mutation loop, and it is open by design.
3. §7 stage 4 (cryptographic approval binding) and §10.2 (hash/revision binding, anti-replay) are **library-only** (`ApprovalBroker` not wired). The real gate today is a typed owner `Principal` + reviewer + evidence ref in the proposal queue. Exception: the research-only `book_to_memory` token binds a note SHA-256.
4. §5.2 role 15 "Reverter": the plasticity rollback restores the in-memory store, not the repository (`VAULT_STATE.md` §5). A canonical rollback is `git revert` or supersession.
5. The handoff does not mention that a scheduled read-only maintenance loop (sleep consolidation) **already runs daily in CI**, nor that an earlier learning engine (`closed_loop_learning.py`) contains a direct-write path.

## 5. Real data flow today (relevant to AKE)

```text
agent ──MCP vault-memory──> memory_access ──> MemoryController.search() ──> RetrievalTrace
agent ──memory_propose────> MemoryController.propose()  (REVIEW, unverified)
extractor ──> MemoryProposalQueue.enqueue() ──owner Principal+reviewer+evidence──> mark(APPROVED)
          ──> QueuePromoter.promote_approved() ──> controller.propose() (RAW, unverified)
owner ──> controller.attest()/promote()  ──> lifecycle/policy.py (sole gate)
cron 03:00 ──> memory_v6_cli consolidate ──> sleep_consolidation (read-only) ──> report artifact (30 days)

NOT WIRED: closed_loop_learning (direct storage.set!), conflict_review, learning_promotion_gate,
           outcome_tracker→promotion_candidates chain, learning/agents, routing, agent_bridge,
           runtime authority layer, book_to_memory eval toolkit, RetrievalEvaluator
```

## 6. Design alternatives (for owner decision; nothing implemented)

| | A. Thin AKE over sleep consolidation (recommended) | B. New `active_knowledge_engine` package with its own SQLite job store | C. LangGraph POC |
|---|---|---|---|
| What | extend the existing `consolidate` pipeline into an `ake run --dry-run` CLI: snapshot hash, deterministic checks (existing), `ConflictReviewWorkflow` (wire it), Loop B on held-out v2 + loss funnel, findings → `MemoryProposalQueue.enqueue()` only. Job record = one append-only JSON file per run under `08_OBSERVABILITY/` | handoff §13 layout: models, orchestrator, job_store (WAL, idempotency keys, leases with fencing tokens), worker registry | graph runtime with checkpointer and interrupts |
| Reuses | consolidation, conflict detector, proposal queue, RetrievalTrace, b2m eval modules, CI cron | the same, plus new infrastructure | the same, plus a framework |
| New code | small (~1 module + CLI + tests) | medium–large | medium + dependency |
| Risk | low; keeps one writer (the controller) | duplicate-infrastructure risk; job store must be proven necessary | framework drift, offline/Python-version fit unknown, CI on 3.11 |
| Gaps left | no resume within a run (runs are short and idempotent by snapshot hash) | none structurally | none structurally |
| When | now (Milestone 1) | only if A shows runs that are long, resumable or concurrent | only if B's checkpoint/resume proves hard |

The handoff's 16-role catalogue maps onto existing code as follows. Roles 3, 4 and 13 = existing deterministic checks. 6 = `conflict_detector` + `conflict_review`. 8 = held-out v2 + `RetrievalEvaluator`. 9/10 = `book_to_memory` harness (unrun). 12 = `proposal_queue.enqueue`. 14 = `QueuePromoter` (owner-gated). 16 = `RetrievalTrace` + `outcome_tracker`. The genuinely missing pieces are 1 (job record), 2 (corpus scout/snapshot manifest), 5/7 (LLM quality/applicability analysts) and 11 (aggregator).

## 7. Owner decisions needed before Milestone 1

1. Adopt alternative A, B or C.
2. `closed_loop_learning.py`: quarantine its `record_outcome()` mutation (e.g. a test that fails if it gains a production consumer, mirroring the runtime-authority guard) vs. refactor it to emit proposals only.
3. `learning/agents/`, `learning_promotion_gate.py`, `conflict_review.py`: wire, reuse or retire.
4. Whether AKE may claim approval binding before `ApprovalBroker` is wired (recommended: no, state it as a limitation).
5. Load `04_CONFIG/agent_budgets.json` (changes every benchmark operating point; already an open owner decision).
6. First Loop B target: the `PAGINATION_CUT` reranker hypothesis from the loss funnel (pre-registered rule exists).
7. Provider routing in AKE: deterministic + local model only in M1 (recommended), no cloud egress.

## 8. Final report block

```text
VERDICT: PARTIAL
HEAD SHA: aa42387f269d80e306853db0241700c37f49b661
WORKTREE: clean on claude/ake-m0-reality-audit (this file only)
FILES CHANGED: 07_EVALUATION/active_knowledge_engine/REPOSITORY_REALITY_AUDIT.md (new),
               00_GOVERNANCE/coordination/tasks/todo-claude-ake.md (new)
COMMANDS EXECUTED: see section 2
TEST OUTPUT: 116 passed, 1 xfailed (9 targeted files, local Python 3.14.2); full suite not run
CI RUN IDS / URLS: push workflows on aa42387f2 all success; Scheduled Memory Consolidation 44896146388 success
RUNTIME EVIDENCE: none (no AKE runtime exists); no live agent turn observed
SECURITY GATES: runtime authority layer NOT WIRED; closed_loop_learning direct-write path found (unwired)
KNOWN FAILURES: Dependabot uv updates under 06_INBOX/RAW_IMPORTS (unrelated)
UNVERIFIED CLAIMS: temporal_* reachability from search(); external Gemini report figures (not checked)
NEXT ACTION: owner picks section 7 items 1-3; then Milestone 1 with tests written first
```
