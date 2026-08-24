# Comprehensive Specification Mining Report: Autonomous Cognitive Brain Architecture

**Document Version**: 1.0.0  
**Status**: Authoritative Technical Specification  
**System**: AI Memory Vault Cognitive Brain  
**Date**: 2026-08-14  

---

## 1. Executive Summary & Architecture Overview

The AI Memory Vault is evolving from a passive knowledge repository into a fully autonomous, self-improving **Cognitive Brain**. This transformation bridges reactive language model interactions with continuous, deterministic cognitive processing, long-term working memory, multi-agent least-privilege coordination, and cryptographically verifiable trust boundaries.

### Architectural Core Principles
1. **Separation of Reasoning and Truth**: The AI model is an execution and inference engine; the Vault is the persistent ground truth. Untrusted model inferences must never silently mutate or elevate canonical memory.
2. **Deterministic Governance**: Trust transitions (`RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED`) are strictly enforced by state machines and cryptographic audit trails, never by probabilistic model prompts.
3. **Multi-Agent Least Privilege**: Specialized worker agents (Router, Retrieval, Verifier, Consolidator, Critic) operate under rigid action allowlists and bounded step budgets.
4. **Resilient Persistence**: Authoritative state is stored in SQLite with Write-Ahead Logging (WAL), atomic `BEGIN IMMEDIATE` transactions, and atomic file checkpoints (`wm.json`, `plan.json`).

```
                              +-------------------------------------------------------+
                              |                      USER / TASK                      |
                              +-------------------------------------------------------+
                                                          |
                                                          v
                                     +-----------------------------------------+
                                     |    STAGE 1: OBSERVE & INTENT PARSING    |
                                     |  (QueryClassifier / Router / Triage)    |
                                     +-----------------------------------------+
                                                          |
                                                          v
                                     +-----------------------------------------+
                                     |  STAGE 2: RETRIEVAL & SPREADING ACTIV.  |
                                     |  (RecallEngine + Dynamic Graph Synapses)|
                                     +-----------------------------------------+
                                                          |
                                                          v
                                     +-----------------------------------------+
                                     |    STAGE 3: ATTENTION & WORKING MEMORY  |
                                     |  (Attention Decay & Priority Eviction)  |
                                     +-----------------------------------------+
                                                          |
                                                          v
                                     +-----------------------------------------+
                                     |      STAGE 4: REASONING & VALIDATION    |
                                     |  (Tree-of-Thought & ThoughtValidator)  |
                                     +-----------------------------------------+
                                                          |
                                                          v
                                     +-----------------------------------------+
                                     |         STAGE 5: ADAPTIVE PLANNING      |
                                     |  (ActivePlan Generation & Replanning)   |
                                     +-----------------------------------------+
                                                          |
                                                          v
                                     +-----------------------------------------+
                                     |       STAGE 6: ACT (TOOL ROUTER)        |
                                     |  (Risk Policy & Reconciliation Boundary)|
                                     +-----------------------------------------+
                                           /                       \
                               (Success)  /                         \ (Error / Blocked)
                                         v                           v
                      +-----------------------------+    +--------------------------------+
                      | STAGE 8: CONSOLIDATE & GROW |    |   STAGE 7: FORMAL REFLEXION    |
                      | (Consolidator, Deduplicator,|    | (6-Stage Error/Lesson Capture, |
                      |  LearningEngine Promotion)  |    |  SelfRefine Critique Filter)   |
                      +-----------------------------+    +--------------------------------+
                                         \                           /
                                          v                         v
                              +-------------------------------------------------------+
                              |         HARDENED MEMORY CONTROLLER & WAL DB           |
                              | (P0-P15 Invariants, Immutable Provenance, Attestation)|
                              +-------------------------------------------------------+
```

---

## 2. Requirement 1 (R1): Cognitive Loop & Autonomous Task Processing

The cognitive architecture operates as a closed-loop OODA cycle with deep memory integration across eight distinct stages.

### 2.1 Stage 1: Observe & Intent Classification
- **Component**: `cognitive_core.executive.Executive`, `memory_controller.context.query_classifier.QueryClassifier`, `cognitive_core.agents.router_agent.RouterAgent`.
- **Functionality**:
  - Ingests raw user intent / task string.
  - Decomposes complexity based on lexical tokens (`"why"`, `"how"`, `"troubleshoot"`, `"architecture"`), query length (>8 tokens = high complexity), and entity targets.
  - Binds request scope to principal tokens and determines required subagents (`retrieval`, `verifier`, `critic`, `consolidator`).

### 2.2 Stage 2: Retrieval & Spreading Activation
- **Component**: `cognitive_core.activation.ActivationEngine`, `cognitive_core.recall.RecallEngine`, `cognitive_core.semantic.DeterministicSemanticProvider`.
- **Multi-Signal Relevance Scoring Formulation**:
  The final recall score $S(n, q, WM)$ for node $n$, query $q$, and Working Memory context $WM$ is computed as:
  $$S(n, q, WM) = w_{\text{sem}} \cdot \text{Sim}(q, n) + w_{\text{wm}} \cdot \text{Sim}(WM, n) + w_{\text{conf}} \cdot C(n) + w_{\text{act}} \cdot A(n) + w_{\text{temp}} \cdot T(n)$$
  - Weights: $w_{\text{sem}} = 0.35$, $w_{\text{wm}} = 0.15$, $w_{\text{conf}} = 0.15$, $w_{\text{act}} = 0.25$, $w_{\text{temp}} = 0.10$.
  - **Confidence & Authority Blend**:
    $$C(n) = \frac{\text{Score}_{\text{confidence}}(n) + \text{Score}_{\text{authority}}(n)}{2.0}$$
    where $\text{Score}_{\text{confidence}} \in \{\text{very\_high}: 1.0, \text{high}: 0.8, \text{medium}: 0.5, \text{low}: 0.2, \text{unknown}: 0.0\}$ and $\text{Score}_{\text{authority}}$ is derived dynamically from `provenance.source_type` (e.g. `official`: 0.9, `import`: 0.8, `execution`: 0.7, `experience`: 0.6, `user`: 0.5, `ai`: 0.4, `inference`: 0.3, `unknown`: 0.2).
  - **Version-Aware Boosting & Penalties**:
    - Matching version range (`VersionRange.matches()`): $+0.3$ confidence bonus.
    - Mismatched technology/version: $-0.3$ confidence penalty.
  - **Temporal Decay**: Expired notes (`valid_until < now`) penalized by factor $0.5$ (or $0.8$ for historical queries). Future notes (`valid_from > now`) penalized by $0.5$.
  - **10% Freshness Bonus on Successor Notes**: When a `SUPERSEDED` note matches a query, the `RecallEngine` traverses `superseded_by` links via recursive lineage resolution and injects the active successor note with an inherited score boosted by $10\%$:
    $$S_{\text{active}} = \min(1.0, S_{\text{superseded}} \times 1.10)$$
  - **Unverified REVIEW Visibility**: Notes in `REVIEW` lifecycle are tagged with `_cognitive_unverified = True` and admitted into working memory so reasoning is aware of emerging hypotheses without granting them verified status.

### 2.3 Stage 3: Attention & Working Memory (WM)
- **Component**: `cognitive_core.working_memory.WorkingMemory`, `cognitive_core.attention.AttentionModel`.
- **Mechanics**:
  - Capacity-bounded buffer (default: 10 active nodes).
  - Time-decayed attention score calculation:
    $$\text{Attention}(n) = A(n) \cdot e^{-\lambda (\text{tick}_{\text{current}} - \text{tick}_n)}$$
  - Deterministic eviction: on capacity overflow, evicts nodes with the lowest attention score, tie-breaking by lexical node ID.

### 2.4 Stage 4: Reason with Tree-of-Thought (ToT)
- **Component**: `cognitive_core.reasoning.ReasoningEngine`, `cognitive_core.reasoning.TreeOfThoughtReasoner`, `cognitive_core.reasoning.ThoughtValidator`.
- **Behavior**:
  - Direct Synthesis: triggered for simple factual queries.
  - Tree-of-Thought Exploration: triggered for complex queries (trigger keywords or >10 words).
  - Generates multi-perspective reasoning branches:
    1. `branch-direct`: Direct factual analysis over context.
    2. `branch-comparative`: Causal relationships, dependencies, and root causes.
    3. `branch-counterfactual`: Constraint boundaries, failure modes, and edge cases.
  - **ThoughtValidator**: Validates branch validity, lexical grounding ratio against active context, and coherence score ($S \ge 0.4$). Best branch is selected for execution planning.

### 2.5 Stage 5: Plan & Adaptive Replanning
- **Component**: `cognitive_core.planning.Planner`, `cognitive_core.planning.ActivePlan`.
- **Behavior**:
  - Decomposes intent into ordered sequential steps with pre-conditions and expected outputs.
  - Context-Aware Multi-Step Expansion:
    - Step 1: Initial targeted retrieval / action.
    - Step 2: Verification cross-referencing if unverified items exist in context.
    - Step 3: Graph relational exploration if active nodes have relations.
  - **Automatic Replanning on Failure**: If a step fails (`ApprovalRequiredError` or execution exception), the planner creates an alternative search/action path up to `max_retries = 2`.

### 2.6 Stage 6: Act with ToolRouter & Policy Boundary
- **Component**: `cognitive_core.tool_router.ToolRouter`.
- **Policy Enforcement**:
  - Low-Risk Actions (`search`, `read`, `propose`, `update`, `archive`, `supersede` on non-human-verified notes): executed autonomously.
  - High-Risk Actions (`delete_canonical`, `modify_raw_imports`): blocked with `ApprovalRequiredError`.
  - **Reconciliation Boundary**: Modifications targeting notes with `verification = "verified"` or `provenance.source_type = "user"` require human operator approval.

### 2.7 Stage 7: Reflect with Formal 6-Stage Reflexion
- **Component**: `cognitive_core.reflection.FormalReflexion`, `cognitive_core.reflection.ReflectionPipeline`.
- **Formal 6-Stage Structure**:
  When an action fails (`error`) or is gated (`blocked`), `FormalReflexion` generates a structured markdown memory:
  ```markdown
  ## Formal Reflexion Analysis
  - **Error**: <Detailed failure message>
  - **Root Cause**: <Underlying failure trigger>
  - **Fix Applied**: <Remediation or parameter adjustments>
  - **Verification**: <Boundary check or test confirmation>
  - **Prevention Rule**: <Proactive constraint for future steps>
  - **Core Lesson**: <Generalizable operational rule>
  ```
  - Submitted as `type: "error"` or `type: "lesson"` in `lifecycle: "REVIEW"`, `verification: "unverified"`, `provenance.source_type: "inference"`.

### 2.8 Stage 8: Consolidate with SelfRefine
- **Component**: `cognitive_core.consolidation.Consolidator`, `cognitive_core.reflection.SelfRefine`, `cognitive_core.deduplication.Deduplicator`, `cognitive_core.learning.LearningEngine`.
- **Operations**:
  - **SelfRefine Critique Filter**: Evaluates candidate notes for coherence, length ($\ge 15$ characters), structured headings, and non-redundancy before memory admission.
  - **Consolidator**: Groups multiple ephemeral `REVIEW` lessons sharing common categories/keywords and merges them into synthesized canonical notes, archiving original snippets.
  - **Deduplicator**: Scans for semantic duplicates ($\text{similarity} \ge 0.85$), ensuring matching technology and version ranges before proposing deduplication links.

---

## 3. Requirement 2 (R2): Strict Trust Boundaries & Attestation Guarantees

The Vault enforces a zero-trust model where AI agents are strictly prevented from escalating privileges, forging evidence, or self-verifying knowledge.

### 3.1 Principal Permissions Matrix

| Principal | Allowed Operations | Permitted Creation Lifecycles | Allowed `provenance.source_type` | Can Self-Verify? | Can Attest? |
|---|---|---|---|---|---|
| `Principal.AI_AGENT` | `READ`, `SEARCH`, `PROPOSE`, `UPDATE`, `SUPERSEDE`* | `RAW`, `CLASSIFIED`, `NORMALIZED`, `REVIEW` | `execution`, `ai`, `inference`, `unknown` | **NO (STRICTLY BLOCKED)** | **NO (DENIED)** |
| `Principal.HUMAN` | `READ`, `SEARCH`, `PROPOSE`, `UPDATE`, `REVIEW`, `PROMOTE`, `ATTEST`, `ARCHIVE`, `SUPERSEDE` | ALL | `user`, `official`, `execution`, `experience`, `inference`, `import`, `unknown` | N/A (Human Authority) | **YES** |
| `Principal.ADMIN` | ALL | ALL | ALL | N/A (Admin Authority) | **YES** |

*\*AI_AGENT cannot supersede human-verified memories.*

### 3.2 Formal Security Invariants (P0-P15)

```
+-----------+-------------------------------------------------------------------------------------------------------+
| INVARIANT | DEFINITIVE SPECIFICATION & TEST VALIDATION CONTRACT                                                   |
+-----------+-------------------------------------------------------------------------------------------------------+
| P0-001    | AI_AGENT cannot propose notes with verification='verified' (raises ValueError; 0 storage writes).     |
| P0-002    | AI_AGENT cannot claim provenance.source_type='official' (raises ValueError; 0 storage writes).        |
| P0-003    | AI_AGENT cannot claim provenance.source_type='user' (raises ValueError; 0 storage writes).            |
| P0-004    | AI_AGENT cannot inject lifecycle='ACTIVE' at creation (raises ValueError; permitted: RAW..REVIEW).   |
| P0-005    | AI_AGENT cannot escalate verification='verified' via update() (raises ValueError; state unchanged).   |
| P0-006    | provenance.source_type is IMMUTABLE post-creation for all principals (raises ValueError on change).   |
| P0-007    | lifecycle is IMMUTABLE on update() (must use explicit review()/promote()/archive() workflow).         |
| P0-008    | Defense-in-depth: MemoryController independently blocks direct attacks even if ToolRouter is bypassed.|
| P0-009    | ToolRouter faithfully propagates security rejections to callers without swallowing exceptions.       |
| P0-010    | HUMAN can invoke attest() with mandatory reason and evidence_reference; sets verification='verified'.|
| P0-011    | ADMIN can invoke attest(); AI_AGENT calling attest() raises PermissionError.                          |
| P0-012    | LearningEngine can promote confidence to 'high' and verification to 'partially_verified', not verified|
| P0-013    | Atomic non-persistence on rejection: rejected payloads leave zero partial database or memory records.|
| P0-014    | Attestation durability: verified state survives process restarts and engine re-instantiation.         |
| P0-015    | Supersession trust isolation: superseding a verified note does NOT transfer verified status to new.   |
+-----------+-------------------------------------------------------------------------------------------------------+
```

### 3.3 Attestation Protocol
To transition a note from `unverified` / `partially_verified` to `verified`:
1. `Principal.HUMAN` or `Principal.ADMIN` calls `controller.attest(principal, note_id, verification_reason, evidence_reference)`.
2. Both `verification_reason` and `evidence_reference` must be non-empty strings.
3. The controller updates `verification = "verified"`, sets `verification_source = principal.value`, records `last_verified = ISO_DATE`, and logs a structured `attest` audit event.
4. The note can then be promoted to `ACTIVE` via `controller.promote(principal, note_id)`.

---

## 4. Requirement 3 (R3): Storage, Persistence, Concurrency & Audit Integrity

### 4.1 SQLite WAL Storage Engine Specification
- **Engine**: `memory_controller.storage.sqlite_engine.SQLiteStorageEngine`.
- **Database Configuration & PRAGMAs**:
  - `PRAGMA journal_mode = WAL;` (Enables concurrent readers and writers without blocking).
  - `PRAGMA synchronous = NORMAL;` (Ensures durability across OS crashes with optimal write throughput).
  - `PRAGMA busy_timeout = 5000;` (5-second lock acquisition timeout to eliminate `SQLITE_BUSY` errors).
  - `PRAGMA foreign_keys = ON;`
- **Concurrency & Transactions**:
  - Thread-safe connection management via `threading.local()`.
  - All write operations (`set`, `delete`) wrapped in explicit `BEGIN IMMEDIATE;` ... `COMMIT;` blocks with automated `ROLLBACK;` on error.
- **Relational Schema & SQL CHECK Constraints**:
  ```sql
  CREATE TABLE IF NOT EXISTS notes (
      id TEXT PRIMARY KEY,
      type TEXT NOT NULL CHECK(type IN ('knowledge', 'project', 'procedure', 'decision', 'experience', 'error', 'lesson', 'preference', 'resource', 'hypothesis', 'system', 'core', 'index')),
      lifecycle TEXT NOT NULL CHECK(lifecycle IN ('RAW', 'CLASSIFIED', 'NORMALIZED', 'REVIEW', 'VERIFIED', 'ACTIVE', 'SUPERSEDED', 'ARCHIVED')),
      category TEXT NOT NULL,
      tags TEXT NOT NULL,
      created TEXT NOT NULL,
      updated TEXT NOT NULL,
      source_type TEXT NOT NULL CHECK(source_type IN ('user', 'official', 'execution', 'experience', 'ai', 'inference', 'import', 'unknown')),
      source_ref TEXT NOT NULL,
      confidence TEXT NOT NULL CHECK(confidence IN ('very_high', 'high', 'medium', 'low', 'unknown')),
      verification TEXT NOT NULL CHECK(verification IN ('verified', 'partially_verified', 'unverified', 'inferred')),
      valid_from TEXT,
      valid_until TEXT,
      version_range TEXT,
      applies_to TEXT,
      supersedes TEXT,
      superseded_by TEXT,
      conflicts_with TEXT,
      last_verified TEXT,
      verification_source TEXT,
      relations TEXT NOT NULL,
      provenance TEXT NOT NULL,
      content TEXT NOT NULL,
      raw_json TEXT NOT NULL
  );

  CREATE INDEX IF NOT EXISTS idx_notes_lifecycle ON notes(lifecycle);
  CREATE INDEX IF NOT EXISTS idx_notes_type ON notes(type);
  CREATE INDEX IF NOT EXISTS idx_notes_source_type ON notes(source_type);
  CREATE INDEX IF NOT EXISTS idx_notes_superseded_by ON notes(superseded_by);
  ```

### 4.2 Recursive CTE Lineage Resolution
The storage engine natively resolves long supersession chains using SQLite recursive common table expressions:
```sql
WITH RECURSIVE lineage(current_id, next_id, depth) AS (
    SELECT id, superseded_by, 0 FROM notes WHERE id = ?
    UNION ALL
    SELECT n.id, n.superseded_by, l.depth + 1
    FROM notes n
    JOIN lineage l ON n.id = l.next_id
    WHERE l.next_id IS NOT NULL AND l.depth < 50
)
SELECT current_id FROM lineage ORDER BY depth DESC LIMIT 1;
```

### 4.3 Atomic Checkpointing Protocol
- **Target Files**: `wm.json` (Working Memory active state), `plan.json` (ActivePlan execution state).
- **Atomicity Protocol**:
  1. Open temporary file in same directory using `tempfile.mkstemp(prefix=".tmp_")`.
  2. Serialize JSON state with `json.dump(state, f, indent=2)`.
  3. Flush application buffers: `f.flush()`.
  4. Force OS disk write: `os.fsync(f.fileno())`.
  5. Close file descriptor.
  6. Atomically replace target file using `os.replace(temp_path, target_path)`.
  7. On error: clean up temporary file; target file remains uncorrupted.

### 4.4 Tamper-Evident SHA-256 Chained Audit Logging
- **Component**: `memory_controller.audit.logger.AuditLogger`.
- **Hash Chain Algorithm**:
  For each audit event $E_i$:
  $$\text{prev\_hash}_i = \begin{cases} \text{"GENESIS"}, & i = 1 \\ \text{entry\_hash}_{i-1}, & i > 1 \end{cases}$$
  $$\text{entry\_hash}_i = \text{SHA256}(\text{CanonicalJSON}(E_i \setminus \{\text{entry\_hash}\} \cup \{\text{prev\_hash}: \text{prev\_hash}_i\}))$$
- **Verification**: `AuditLogger.verify_integrity()` verifies every line in `audit_log.jsonl`, computing expected vs stored hashes and reporting any tampering, line deletion, or corruption.

---

## 5. Requirement 4 (R4): Specialized Multi-Agent Worker Coordination

The multi-agent system uses an Orchestrator-Worker pattern with bounded execution and strict capability constraints.

### 5.1 Worker Roles and Least-Privilege Action Matrix

| Agent Role | Class | Permitted Actions | Model Tier | Max Steps | Primary Responsibility |
|---|---|---|---|---|---|
| **Router** | `RouterAgent` | `["search", "read"]` | Light | 2 | Analyzes query intent, determines complexity, selects worker subagents. |
| **Retrieval** | `RetrievalAgent` | `["search", "read"]` | Light | 3 | Spreading activation, multi-signal recall, supersession lineage resolution. |
| **Verifier** | `VerifierAgent` | `["read"]` | Light | 2 | Audits provenance claims, flags unverified notes, detects provenance violations. |
| **Consolidator** | `ConsolidatorAgent` | `["search", "read", "propose", "archive"]` | Standard | 4 | Executes deduplication scans and consolidates ephemeral REVIEW lessons. |
| **Critic** | `CriticAgent` | `["read", "propose"]` | Standard | 3 | Evaluates execution failures (Reflexion) and critiques memory quality (SelfRefine). |
| **Synthesizer** | `SubagentSpec` | `["read"]` | Heavy | 2 | Synthesizes verified multi-agent context into final user-facing responses. |

### 5.2 Multi-Agent Execution Flow
1. **Triage**: `RouterAgent.process_task()` analyzes query complexity.
2. **Retrieve**: If search keywords are present, `RetrievalAgent` executes recall, applying the 10% freshness bonus on successor notes.
3. **Verify**: `VerifierAgent` inspects retrieved notes, separating verified ground truth from unverified hypotheses.
4. **Reason & Plan**: `Executive` coordinates Tree-of-Thought reasoning and step generation.
5. **Critique / Reflect**: On failure or candidate generation, `CriticAgent` generates formal Reflexions or filters candidates.
6. **Maintenance**: `ConsolidatorAgent` runs background deduplication and consolidation.

---

## 6. Acceptance Criteria & Evaluation Benchmarks

### 6.1 Quantitative Retrieval Quality (TRACe Framework)
- **Utilization**: $\text{Utilization} = \frac{|\{n \in \text{Retrieved} \mid n \text{ used in response}\}|}{|\text{Retrieved}|} \ge 0.70$.
- **Relevance**: Semantic cosine similarity between query and retrieved note embeddings $\ge 0.65$.
- **Adherence**: Factual grounding score of generated response against source notes $\ge 0.85$.
- **Completeness**: Recall against gold-standard reference IDs $= 1.0$.

### 6.2 Standard Information Retrieval (IR) Benchmarks
- **Precision@K** ($K=5$): Fraction of top-K results that are relevant $\ge 0.60$.
- **Recall@K** ($K=5$): Fraction of all relevant documents found in top-K $\ge 0.80$.
- **Mean Reciprocal Rank (MRR)**: Average reciprocal rank of first relevant note $\ge 0.75$.
- **NDCG@K** ($K=5$): Normalized Discounted Cumulative Gain $\ge 0.80$.

### 6.3 Continual Learning & Catastrophic Forgetting Prevention
- **ContinualLearningGuard**:
  - Registers canonical ground truth anchor memories (`register_anchor_node`).
  - `verify_no_catastrophic_regression()` checks active storage against anchors.
  - Zero tolerance for accidental deletion or demotion of verified anchor knowledge.
- **Confidence Promotion**:
  - Promotion to `very_high` strictly requires `source_type == "execution"` and $\ge 9$ relations ($\ge \text{threshold} \times 3$).

---

## 7. Features Discovered

| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|---|---|---|---|---|---|---|
| 1 | Cognitive Loop | Query Classification | Classifies user intent into target lifecycle and type filters | `query: str` | `classified: Dict[str, Any]` | Fallback to default budget & filters | `QueryClassifier.classify` |
| 2 | Cognitive Loop | Spreading Activation | Traverses knowledge graph to activate semantically related nodes | `query: str, principal: Principal` | `List[Tuple[Dict, float]]` | Empty list if graph disconnected | `ActivationEngine.activate_from_query` |
| 3 | Cognitive Loop | Associative Recall | Multi-signal weighted recall scoring with temporal and version adjustments | `query, activated_nodes, wm` | `List[Tuple[Dict, float]]` | Returns empty list if no notes | `RecallEngine.recall` |
| 4 | Cognitive Loop | Supersession Freshness Boost | Traverses superseded notes and boosts active successor score by 10% | `superseded_note, score` | `(active_successor, score * 1.1)` | Keeps original note if no successor | `RecallEngine.recall` |
| 5 | Cognitive Loop | Working Memory Eviction | Manages bounded active context with attention decay | `nodes_with_activation` | Updated buffer | Lowest attention node evicted | `WorkingMemory.admit` |
| 6 | Cognitive Loop | Tree-of-Thought Reasoning | Explores direct, comparative, and counterfactual reasoning branches | `query: str, context: List[Dict]` | Best branch + evaluated tree | Fallback to direct conclusion | `TreeOfThoughtReasoner.reason` |
| 7 | Cognitive Loop | Thought Validation | Evaluates branch grounding ratio against working context | `branch: Dict, context: List[Dict]` | `(is_valid, score, critique)` | Rejects ungrounded thoughts | `ThoughtValidator.validate_branch` |
| 8 | Cognitive Loop | Adaptive Replanning | Reformulates failed execution steps into alternative actions | `goal, context, failed_action, error` | `ActivePlan` | Caps retries at `max_retries=2` | `Planner.replan` |
| 9 | Cognitive Loop | Formal 6-Stage Reflexion | Encapsulates Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson | Failure / blocked outcome | Structured markdown note | No-op if status is success | `FormalReflexion.format_reflection` |
| 10 | Cognitive Loop | SelfRefine Critique | Filters candidate memories for substance and structure | `candidate: Dict` | `(passed, refined_candidate)` | Rejects notes < 15 chars | `SelfRefine.refine_memory` |
| 11 | Cognitive Loop | Lesson Consolidation | Merges ephemeral REVIEW lessons into permanent canonical notes | `principal: Principal` | `consolidated_note_id: str` | No-op if < 2 lessons | `Consolidator.consolidate_lessons` |
| 12 | Cognitive Loop | Tech-Aware Deduplication | Detects duplicate notes matching technology and version ranges | `principal: Principal` | `List[str]` (flagged IDs) | No-op if no duplicates | `Deduplicator.scan_for_duplicates` |
| 13 | Trust & Security | AI Self-Verify Block | AI_AGENT cannot propose or update verification to 'verified' | `Principal.AI_AGENT, payload` | Rejected | Raises `ValueError` | `MemoryController.propose` |
| 14 | Trust & Security | Privileged Provenance Guard | AI_AGENT cannot claim 'user' or 'official' provenance | `Principal.AI_AGENT, payload` | Rejected | Raises `ValueError` | `MemoryController.propose` |
| 15 | Trust & Security | Creation Lifecycle Guard | AI_AGENT can only propose into RAW, CLASSIFIED, NORMALIZED, REVIEW | `Principal.AI_AGENT, payload` | Rejected | Raises `ValueError` | `MemoryController.propose` |
| 16 | Trust & Security | Provenance Immutability | `provenance.source_type` cannot be modified after creation | `updates: Dict` | Rejected | Raises `ValueError` | `MemoryController.update` |
| 17 | Trust & Security | Lifecycle Immutability | `lifecycle` cannot be directly modified via update() | `updates: Dict` | Rejected | Raises `ValueError` | `MemoryController.update` |
| 18 | Trust & Security | Human / Admin Attestation | Explicit human attestation gate with mandatory evidence citation | `principal, note_id, reason, ref` | Note updated to verified | Raises `PermissionError` for AI | `MemoryController.attest` |
| 19 | Trust & Security | Supersession Trust Isolation | Superseding a verified note does not transfer verified trust | `old_id, new_id` | Reciprocal links updated | Raises `ValueError` on cycles | `MemoryController.supersede` |
| 20 | Trust & Security | Reconciliation Boundary | Prevents automated AI modification of human-verified memories | `action, kwargs` | Gate check | Raises `ApprovalRequiredError` | `ToolRouter._check_knowledge_reconciliation_boundary` |
| 21 | Persistence | SQLite WAL Engine | High-concurrency relational storage with BEGIN IMMEDIATE transactions | Note dictionary | Persistent record | Raises `IntegrityError` on invalid enums | `SQLiteStorageEngine` |
| 22 | Persistence | Recursive Lineage Resolution | Traverses `superseded_by` chains up to depth 50 via SQLite CTE | `note_id: str` | Final active `note_id: str` | Returns input ID if no successor | `SQLiteStorageEngine.resolve_active_lineage` |
| 23 | Persistence | Atomic Checkpoints | Writes `wm.json` / `plan.json` atomically via tempfile + fsync + replace | In-memory state | Atomic file write | Tempfile cleaned up on failure | `WorkingMemory.save_state` |
| 24 | Persistence | Tamper-Evident Audit Chain | Computes SHA-256 hash chains across audit log entries | Log entry payload | Appended JSONL line | Detects tampering / gaps | `AuditLogger._write_entry` |
| 25 | Multi-Agent | Router Subagent | Triage and subagent dispatch based on query complexity | `task: Dict` | `dispatched_agents: List[str]` | Fallback to retrieval | `RouterAgent.process_task` |
| 26 | Multi-Agent | Retrieval Subagent | Hybrid graph activation and semantic recall | `task: Dict` | `results: List[Dict]` | Empty list if not found | `RetrievalAgent.process_task` |
| 27 | Multi-Agent | Verifier Subagent | Audits provenance and verification consistency | `task: Dict` | `violations: List[str]` | Flags illegitimate claims | `VerifierAgent.process_task` |
| 28 | Multi-Agent | Consolidator Subagent | Executes deduplication and lesson consolidation | `task: Dict` | `results: Dict` | Empty dict if nothing to consolidate | `ConsolidatorAgent.process_task` |
| 29 | Multi-Agent | Critic Subagent | Executes 6-stage Reflexion and SelfRefine candidate critique | `task: Dict` | `refined_candidate: Dict` | Filters out low quality notes | `CriticAgent.process_task` |
| 30 | Quality & IR | TRACe Metrics Evaluator | Evaluates Utilization, Relevance, Adherence, Completeness | `retrieved, response, gold_ids` | Float scores [0.0, 1.0] | Returns 0.0 on empty inputs | `RetrievalEvaluator` |
| 31 | Quality & IR | IR Benchmarks Evaluator | Calculates Precision@K, Recall@K, MRR, NDCG@K | `rankings, relevant_sets, scores` | Metric values [0.0, 1.0] | Returns 0.0 on empty inputs | `RetrievalEvaluator` |
| 32 | Quality & IR | Continual Learning Guard | Replay anchor verification to prevent catastrophic forgetting | `storage_notes: List[Dict]` | `(ok: bool, violations: List)` | Returns False if anchor lost | `ContinualLearningGuard` |
| 33 | Context Economy | Progressive Disclosure | Dynamically degrades context levels: full -> sections -> snippet -> metadata | `notes: List[Dict], budget` | Disclosed items | Downgrades level on soft budget exceed | `ProgressiveDisclosure` |
| 34 | Security | HMAC Pagination Token | Signs pagination cursor with HMAC-SHA256 bound to query and principal | `payload, secret` | Base64 URL-safe token | Raises `InvalidPaginationTokenError` on tampering | `PaginationToken` |

---

## 8. Edge Cases & Validation Matrix

| # | Feature | Input Scenario | Observed & Specified Behavior |
|---|---|---|---|
| 1 | AI Propose Verified | `propose(Principal.AI_AGENT, {"verification": "verified"})` | Raises `ValueError: Verification status 'verified' cannot be set via propose. Use attest() instead.` Zero writes to storage. |
| 2 | AI Propose Official | `propose(Principal.AI_AGENT, {"provenance": {"source_type": "official"}})` | Raises `ValueError: Principal 'ai_agent' is not permitted to claim provenance source_type 'official'`. Zero writes to storage. |
| 3 | AI Propose ACTIVE | `propose(Principal.AI_AGENT, {"lifecycle": "ACTIVE"})` | Raises `ValueError: Principal 'ai_agent' cannot set lifecycle to 'ACTIVE' at creation. Permitted creation states: RAW, CLASSIFIED, NORMALIZED, REVIEW.` |
| 4 | AI Update Verified | `update(Principal.AI_AGENT, note_id, {"verification": "verified"})` | Raises `ValueError: Verification status 'verified' cannot be escalated via update. Use attest() instead.` Note remains unchanged. |
| 5 | Provenance Mutation | `update(Principal.HUMAN, note_id, {"provenance": {"source_type": "official"}})` on note with `source_type="user"` | Raises `ValueError: Field provenance.source_type is immutable post-creation (existing: 'user', attempted: 'official')`. |
| 6 | Attestation Without Reason | `attest(Principal.HUMAN, note_id, verification_reason="", evidence_reference="rfc")` | Raises `ValueError: Attestation requires a non-empty verification_reason`. |
| 7 | Attestation by AI | `attest(Principal.AI_AGENT, note_id, "reason", "ref")` | Raises `PermissionError: ai_agent not allowed to perform attest`. |
| 8 | Supersession Trust Transfer | Supersede a verified note with an unverified new note | Old note transitioned to `SUPERSEDED`; new note remains `unverified`. Verified status is NOT transferred. |
| 9 | Direct Controller Bypass | Bypass `Executive` & `ToolRouter` by calling `MemoryController.propose()` directly with malicious payload | `MemoryController` independently rejects payload before validation and before `storage.set()`. Defense-in-depth confirmed. |
| 10 | ToolRouter AI Verified Propose | `router.execute(AI_AGENT, "propose", {"note_data": {..., "verification": "verified"}})` | `ToolRouter` propagates `ValueError` from controller without swallowing. Storage remains empty. |
| 11 | Partial DB Persistence on Failure | Any invalid propose/update payload | Exception caught before `storage.set()`, audit event logged with `outcome="error"`, storage count remains unchanged (0 partial records). |
| 12 | Storage Engine ID Mismatch | `storage.set("id-1", {"id": "id-2", ...})` | Raises `ValueError: ID mismatch: storage key 'id-1' must equal note id 'id-2'`. |
| 13 | SQLite CHECK Constraint Violation | `storage.set(nid, {"lifecycle": "INVALID_STATE", ...})` | SQLite raises `sqlite3.IntegrityError: CHECK constraint failed: notes`. `BEGIN IMMEDIATE` automatically executes `ROLLBACK`. |
| 14 | Concurrent Readers & Writers | 4 concurrent writer threads + 3 reader threads under WAL mode | All writers complete successfully with zero database locking errors (`PRAGMA busy_timeout=5000`); readers execute concurrently. |
| 15 | Deep Supersession Lineage | Note A -> superseded by B -> superseded by C -> superseded by D | `resolve_active_lineage("A")` returns `"D"`. Querying for A automatically retrieves D with a 10% score boost. |
| 16 | Checkpoint Crash Simulation | Interrupted write during checkpoint saving | Temporary `.tmp_` file is written and fsynced before `os.replace`; target `wm.json` / `plan.json` never left in partial/corrupted state. |
| 17 | Audit Log Line Tampering | Modify any byte, timestamp, or actor in `audit_log.jsonl` | `AuditLogger.verify_integrity()` returns `(False, ["Line X: entry_hash mismatch..."])`. |
| 18 | Working Memory Overflow | Admit 15 nodes into a WM with capacity 10 | Attention decay computed for all 15 nodes; lowest 5 attention nodes evicted; deterministic tie-breaking by note ID. |
| 19 | Tree-of-Thought Empty Thought | Branch generated with thought `""` or `"ok"` | `ThoughtValidator.validate_branch` returns `(False, 0.0, "Thought is too sparse or empty")`. |
| 20 | Replanning on Blocked Action | Step fails due to `ApprovalRequiredError` | `Planner.replan` constructs alternative safe search step, resets retry counter, and logs reflection memory. |
| 21 | SelfRefine Under-Sized Memory | Candidate note with `content = "short"` (< 15 characters) | `SelfRefine.refine_memory` returns `(False, candidate)`. Note is rejected from canonical consolidation. |
| 22 | Catastrophic Forgetting Detection | Storage note list missing a registered anchor memory | `ContinualLearningGuard.verify_no_catastrophic_regression` returns `(False, ["Anchor memory X was removed..."])`. |
| 23 | Confidence Promotion Guard | Propose note promotion to `very_high` with `source_type="ai"` | Promotion rejected: confidence promotion to `very_high` strictly requires `source_type="execution"`. |
| 24 | HMAC Pagination Token Query Tampering | Reuse pagination token with a different query string | Raises `InvalidPaginationTokenError: Token query fingerprint does not match current request`. |
| 25 | Subagent Action Violation | `RouterAgent` attempting to invoke `"archive"` | `_execute_worker_action` raises `PermissionError: Subagent 'router' is not permitted to perform action 'archive'`. |

---

## 9. Conclusion & Implementation Roadmap

The specification analysis confirms that the AI Memory Vault possesses a complete, hardened, and mathematically rigorous architecture for full cognitive autonomy. The system satisfies all requirements R1-R4, upholds invariants P0-P15, and guarantees complete tamper-evident memory governance.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
