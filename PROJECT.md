# Project: AI Memory Vault Autonomous Cognitive Brain

## Architecture
The Cognitive Brain transforms the AI Memory Vault into an autonomous, self-improving cognitive system operating across two foundational layers:

1. **Memory Controller Layer (`memory_controller/`)**:
   - **Authoritative Storage**: SQLite with Write-Ahead Logging (`PRAGMA journal_mode=WAL;`, `PRAGMA busy_timeout=5000;`, `PRAGMA foreign_keys=ON;`) and explicit `BEGIN IMMEDIATE` transactions.
   - **Security Invariants (P0-001 through P0-015)**: Strict trust boundaries separating `Principal.AI_AGENT`, `Principal.HUMAN`, `Principal.ADMIN`, and `Principal.SYSTEM`. Enforces immutable provenance, attestation gates, and rejects AI self-verification.
   - **Tamper-Evident Audit Logging**: Cryptographic SHA-256 hash chaining (`prev_hash`, `entry_hash`) across all operational mutations.
   - **Lineage Traversal**: Recursive CTE (`resolve_active_lineage`) resolving superseded notes up to depth 50.

2. **Cognitive Core Layer (`cognitive_core/`)**:
   - **Autonomous OODA Execution Loop (`cognitive_core/executive.py`)**: Observe -> Retrieve -> Attend -> Reason -> Plan -> Act -> Reflect -> Consolidate.
   - **Multi-Branch Reasoning (`cognitive_core/reasoning.py`)**: `TreeOfThoughtReasoner` (direct, comparative, counterfactual branches) paired with `ThoughtValidator` lexical consistency checking.
   - **Memory Recall & Scoring (`cognitive_core/recall.py`)**: Multi-signal scoring with a 10% freshness bonus on successor nodes.
   - **Formal Reflection & Critique (`cognitive_core/reflection.py`, `cognitive_core/consolidation.py`)**: 6-stage `FormalReflexion` (Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson) and `SelfRefine` critique filter.
   - **Continual Learning & Metrics (`cognitive_core/learning.py`, `cognitive_core/evaluation.py`)**: `ContinualLearningGuard` anchor memory protection, execution evidence gating for `very_high` confidence, TRACe framework and IR ranking benchmarks.
   - **Specialized Multi-Agent Coordination (`cognitive_core/agents/`)**: Least-privilege worker subagents (Router, Retrieval, Verifier, Consolidator, Critic).

---

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Python Typing & Import Hygiene | Fix missing typing imports (`Tuple`) in `learning.py` and `reflection.py`, clean dead code in `budget.py` | M1 | Survey |
| 2 | SQLite WAL Mode & Concurrency | Multi-threaded SQLite WAL storage with `busy_timeout=5000` and `BEGIN IMMEDIATE` atomic transactions | M2 | R3 / Survey |
| 3 | Atomic File Checkpointing | Atomic persistence of `wm.json` and `plan.json` via temp file + `os.fsync` + `os.replace` | M2 | R3 / Survey |
| 4 | SHA-256 Audit Log Chaining | Cryptographic hash chaining of audit events with 0 tampering anomalies | M2 | R3 / AC |
| 5 | Recursive Lineage Traversal | SQL recursive CTE resolving multi-hop supersession lineages | M2 | R3 / Survey |
| 6 | P0-P15 Trust Boundary Invariants | Rejection of AI self-verification (`verification="verified"`) and privileged provenance forging (`user`, `official`) | M3 | R2 / AC |
| 7 | Attestation Gate Protocol | Human/Admin-only promotion of notes to `ACTIVE` and `verified` via `Operation.ATTEST` | M3 | R2 / Survey |
| 8 | ToolRouter Security Gating | Principal-aware tool invocation and capability bounds | M3 | R2 / Survey |
| 9 | OODA Cognitive Execution Loop | Autonomous Observe -> Retrieve -> Attend -> Reason -> Plan -> Act -> Reflect -> Consolidate cycle | M4 | R1 / Survey |
| 10 | Tree-of-Thought Reasoning | 3-branch hypothesis generation with ThoughtValidator consistency checks | M4 | R1 / AC |
| 11 | Memory Recall with Freshness Boost | Multi-signal scoring with 10% freshness bonus on successor notes | M4 | AC / Survey |
| 12 | 6-Stage Formal Reflexion | Structured error analysis (Error, Root Cause, Fix, Verification, Prevention, Lesson) | M4 | R1 / Survey |
| 13 | SelfRefine Memory Critique | Canonical note refinement and deduplication filter before consolidation | M4 | R1 / AC |
| 14 | Multi-Agent Worker Coordination | Least-privilege subagent workers (Router, Retrieval, Verifier, Consolidator, Critic) | M4 | R4 / Survey |
| 15 | ContinualLearningGuard | Anchor memory regression detection and prevention | M5 | AC / Survey |
| 16 | Confidence Promotion Gating | Confidence promotion to `very_high` strictly requiring verified execution evidence | M5 | AC / Survey |
| 17 | TRACe & IR Benchmark Evaluation | Quantitative evaluation metrics (TRACe + Precision@K, Recall@K, MRR, NDCG@K) | M5 | AC / Survey |
| 18 | Full 197+ Pytest Suite Verification | 100% passing test suite across all 37 test modules with 0 failures | M5 | AC / Survey |

---

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Codebase Hygiene & Typing Validation | Fix `Tuple` imports in `learning.py` and `reflection.py`, clean dead code in `budget.py`, verify type introspection | none | DONE |
| 2 | Storage, WAL & Audit Integrity | Verify SQLite WAL, BEGIN IMMEDIATE transactions, atomic checkpointing, and SHA-256 audit chaining | M1 | DONE |
| 3 | Security Invariants & Attestation Gates | Verify P0-P15 invariant enforcement, AI self-verification rejection, and Human Attestation gates | M1 | DONE |
| 4 | Cognitive Loop & Multi-Agent Coordination | Verify OODA loop, Tree-of-Thought, 10% freshness bonus, Formal Reflexion, and specialized worker agents | M2, M3 | DONE |
| 5 | Continual Learning, TRACe & E2E Hardening | ContinualLearningGuard, TRACe/IR benchmarks, full 197+ pytest pass, adversarial challenger & forensic audit | M4 | IN_PROGRESS |

---

## Interface Contracts
### `MemoryController` ↔ `CognitiveExecutive`
- **Search / Recall**: `controller.search(query, filter_dict, principal)` -> `List[MemoryNote]`
- **Proposal**: `controller.propose(note_data, principal=Principal.AI_AGENT)` -> `MemoryNote` (lifecycle restricted to `REVIEW`)
- **Attestation**: `controller.attest(note_id, principal=Principal.HUMAN)` -> `MemoryNote` (promotes to `ACTIVE`, `verified`)
- **Supersede**: `controller.supersede(old_id, new_id, principal)` -> `bool`

### `CognitiveExecutive` ↔ `SpecializedAgents`
- **Router Agent**: `route(goal, state)` -> `ExecutionRoute` (Read/Search only)
- **Retrieval Agent**: `retrieve(query, context)` -> `RecallResult` (Read/Search only)
- **Verifier Agent**: `audit_provenance(note)` -> `VerificationReport` (Read only)
- **Consolidator Agent**: `consolidate(notes, critique)` -> `List[MemoryNote]` (Propose, Archive)
- **Critic Agent**: `critique(plan_or_thought)` -> `CritiqueFeedback` (Read, Propose)

---

## Code Layout
```
AI_Memory_Vault_CODEX_READY/
├── memory_controller/
│   ├── controller.py
│   ├── models.py
│   ├── storage/
│   │   ├── sqlite_engine.py
│   │   └── file_engine.py
│   ├── audit/
│   │   └── logger.py
│   ├── context/
│   │   └── budget.py
│   └── tests/
├── cognitive_core/
│   ├── executive.py
│   ├── reasoning.py
│   ├── reflection.py
│   ├── recall.py
│   ├── planning.py
│   ├── working_memory.py
│   ├── consolidation.py
│   ├── learning.py
│   ├── evaluation.py
│   ├── agents/
│   │   ├── base_agent.py
│   │   ├── router_agent.py
│   │   ├── retrieval_agent.py
│   │   ├── verifier_agent.py
│   │   ├── consolidator_agent.py
│   │   └── critic_agent.py
│   └── tests/
├── 00_CORE/
├── 99_SYSTEM/
└── tests/
```

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
