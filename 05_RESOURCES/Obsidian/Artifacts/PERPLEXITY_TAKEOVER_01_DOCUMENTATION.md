---
id: "1ed59fbc-9b1c-402f-a19a-e6bbb99cde46"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "PERPLEXITY_TAKEOVER_01_DOCUMENTATION.md"
confidence: high
verification: verified
relations: []
---

# Artifact: PERPLEXITY_TAKEOVER_01_DOCUMENTATION

# PERPLEXITY TAKEOVER 01 DOCUMENTATION


============================================================
FILE: 02_PROJECTS/Continuity_Handoff.md
============================================================

---
id: "8c7d5c90-9c29-450b-b5a9-e2b2024db502"
type: project
lifecycle: ACTIVE
category: continuity
tags: [handoff, agent-continuity]
created: "2026-08-10"
updated: "2026-08-10"
provenance:
  source_type: user
  source_ref: handoff
confidence: very_high
verification: verified
relations: []
---

# Agent Transfer & Continuity Handoff Package
**Vault Path**: `02_PROJECTS/Continuity_Handoff.md`  
**Version**: `1.0.0`  
**Target Agent**: Perplexity Desktop

---

## 1. INSTRUCTIONS FOR PERPLEXITY ("START HERE")

Welcome, Successor Agent (Perplexity). Do not guess or assume what the previous agent knew. Follow these exact instructions to bootstrap your execution:

1. **Read this Document First**: This handoff details the entire system architecture, runtime call graphs, and historical context.
2. **Read the Core Operating Protocols**:
   - Inspect [00_CORE/Rules.md](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/00_CORE/Rules.md) (Core rules).
   - Inspect [00_CORE/Memory_Protocol.md](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/00_CORE/Memory_Protocol.md) (Deduplication, versioning, and supersession enforcements).
3. **Verify the Environment State**:
   - Run the pytest suite immediately: `python -m pytest -q`
   - Run the multi-process restart verification: `python C:\Users\user\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\scratch\run_multi_process_test.py`
4. **Respect the Autonomy Gates**: Never attempt a `HIGH` risk action (e.g. modifying human-verified nodes or raw imports) without human approval.
5. **Do Not Overwrite Canonical Knowledge**: Verify any assumptions against the actual codebase files before modifying memory notes. Code is the source of truth.
6. **Proceed to the Next Task**: Read the **Next-Task Contract (AG-CONT-01)** in Section 11 and execute only that task.

---

## 2. Complete Project State

- **Repository Identity**: `AI_Memory_VAULT_CODEX_READY`
- **Current Git Branch**: `main`
- **Current Commit**: `db958f4` (Initialize Cognitive Core codebase and Agent Continuity Layer)
- **Remote URL**: `https://github.com/userist123/AI_Memory_VAULT_CODEX_READY.git`
- **Working-Tree Status**: Clean, except for local log files (`audit_log.jsonl`, `test_audit_log.jsonl`), scratch scripts (`proc_debug.py`), untracked `.vs/` files, and test-generated mock notes (prefixed with `test_` or `unknown_` under vault subfolders, which are not committed).
- **Current Project Phase**: Phase 4.3 (Deduplication, Supersession, and Temporal Recall).
- **Completed Phases**: P0 (Vault Foundations), P0-10 (Historical Migration), Phase 1 (Cognitive Core Integration), Phase 2 (Context Continuity), Phase 3 (Learning & Consolidation), Phase 4.3 (Technology/Version).
- **Current Task**: Handoff Verification & Audit.
- **Next Task**: `AG-CONT-01` (Programmatic Continuity Layer Integration).
- **Blocked Tasks**: None.
- **Human Approval Requirements**: Deleting canonical nodes, modifying user-sourced notes, installing new packages, changing validation schemas.

---

## 3. Architecture Map

The runtime cognitive loop operates sequentially:
`Executive -> intent -> context -> recall -> activation -> working memory -> reasoning -> planning -> ToolRouter -> execution -> reflection -> persistence -> consolidation -> learning -> future recall`

### Component Directory & Interface Tracing

#### 1. Executive
- **FILE**: [`cognitive_core/executive.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/executive.py)
- **CLASS/FUNCTION**: `Executive`
- **CALLED BY**: Task orchestrator scripts, test suites.
- **CALLS**: `ToolRouter`, `ActivationEngine`, `RecallEngine`, `WorkingMemory`, `ReasoningEngine`, `Planner`, `ReflectionPipeline`, `Consolidator`, `Deduplicator`, `LearningEngine`.
- **INPUT**: `principal: Principal`, `intent_text: str`
- **OUTPUT**: `Dict[str, Any]` (OODA loop step result details).
- **PERSISTENCE EFFECT**: Triggers Working Memory and Active Plan state writes under the local directory path.
- **SECURITY BOUNDARY**: Gates the loop entry point using authorization `Principal` contexts.
- **TEST COVERAGE**: `cognitive_core/tests/test_executive.py`, `test_cognitive_loop.py`

#### 2. WorkingMemory
- **FILE**: [`cognitive_core/working_memory.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/working_memory.py)
- **CLASS/FUNCTION**: `WorkingMemory`
- **CALLED BY**: `Executive`, `RecallEngine`
- **CALLS**: `AttentionModel.calculate_score`
- **INPUT**: `nodes_with_activation: List[Tuple[Dict[str, Any], float]]`
- **OUTPUT**: `List[Dict[str, Any]]` (active nodes sorted by attention levels).
- **PERSISTENCE EFFECT**: Serializes ticks and node IDs to `wm_state.json` via `save_state()`; loads and dynamically rehydrates notes from the Vault on `load_state()`.
- **SECURITY BOUNDARY**: Hydration queries are subject to the `Principal`'s read privileges.
- **TEST COVERAGE**: `cognitive_core/tests/test_working_memory.py`, `test_working_memory_persistence.py`

#### 3. RecallEngine
- **FILE**: [`cognitive_core/recall.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/recall.py)
- **CLASS/FUNCTION**: `RecallEngine`
- **CALLED BY**: `Executive`
- **CALLS**: `MemoryController.search`, `parse_technology_version`
- **INPUT**: `principal: Principal`, `query: str`, `activated_nodes: List[Tuple[Dict[str, Any], float]]`, `working_memory: WorkingMemory`
- **OUTPUT**: `List[Tuple[Dict[str, Any], float]]` (scored and ranked notes).
- **PERSISTENCE EFFECT**: None (read-only ranker).
- **SECURITY BOUNDARY**: Passes the calling principal context down to `MemoryController.search`.
- **TEST COVERAGE**: `cognitive_core/tests/test_recall.py`, `memory_controller/tests/test_supersession_phase43.py`

#### 4. Deduplicator
- **FILE**: [`cognitive_core/deduplication.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/deduplication.py)
- **CLASS/FUNCTION**: `Deduplicator`
- **CALLED BY**: `Executive`
- **CALLS**: `MemoryController.search`, `ToolRouter.execute`
- **INPUT**: `principal: Principal`
- **OUTPUT**: `List[Tuple[str, str]]` (duplicate ID pairs).
- **PERSISTENCE EFFECT**: Flagged duplicate notes are transitioned to the `REVIEW` lifecycle state.
- **SECURITY BOUNDARY**: Write actions must be authorized through `ToolRouter`.
- **TEST COVERAGE**: `cognitive_core/tests/test_deduplication.py`

#### 5. ReflectionPipeline
- **FILE**: [`cognitive_core/reflection.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/reflection.py)
- **CLASS/FUNCTION**: `ReflectionPipeline`
- **CALLED BY**: `Executive`
- **CALLS**: `MemoryController.propose`, `MemoryController.update`
- **INPUT**: `principal: Principal`, `intent: Dict[str, Any]`, `action: Dict[str, Any]`, `result: Dict[str, Any]`
- **OUTPUT**: `Optional[str]` (newly proposed error or lesson node UUID).
- **PERSISTENCE EFFECT**: Writes new unverified lesson or error Markdown files to the Vault, and updates dynamic synapse edges.
- **SECURITY BOUNDARY**: Controlled by `Principal` permissions during writes.
- **TEST COVERAGE**: `cognitive_core/tests/test_reflection.py`, `test_dynamic_synapses.py`

#### 6. ToolRouter
- **FILE**: [`cognitive_core/tool_router.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/tool_router.py)
- **CLASS/FUNCTION**: `ToolRouter`
- **CALLED BY**: `Executive`, `Deduplicator`, `Consolidator`, `LearningEngine`
- **CALLS**: `MemoryController` CRUD interfaces.
- **INPUT**: `principal: Principal`, `action: str`, `kwargs: dict`
- **OUTPUT**: `Any` (returns execution output or raises `ApprovalRequiredError`).
- **PERSISTENCE EFFECT**: Proxies mutations directly to the controller.
- **SECURITY BOUNDARY**: Enforces `RiskLevel` constraints (gating `HIGH` risk commands).
- **TEST COVERAGE**: `cognitive_core/tests/test_executive.py` (indirect), `test_cognitive_loop.py`

#### 7. Consolidator
- **FILE**: [`cognitive_core/consolidation.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/consolidation.py)
- **CLASS/FUNCTION**: `Consolidator`
- **CALLED BY**: `Executive`
- **CALLS**: `MemoryController.search`, `ToolRouter.execute`
- **INPUT**: `principal: Principal`
- **OUTPUT**: `List[str]` (archived lesson UUIDs).
- **PERSISTENCE EFFECT**: Replaces highly similar lesson entries with a single merged note.
- **SECURITY BOUNDARY**: Writes via `ToolRouter`.
- **TEST COVERAGE**: `cognitive_core/tests/test_consolidation.py`

#### 8. LearningEngine
- **FILE**: [`cognitive_core/learning.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/learning.py)
- **CLASS/FUNCTION**: `LearningEngine`
- **CALLED BY**: `Executive`
- **CALLS**: `MemoryController.search`, `ToolRouter.execute`
- **INPUT**: `principal: Principal`
- **OUTPUT**: `List[str]` (promoted note UUIDs).
- **PERSISTENCE EFFECT**: Promotes confidence and verification metadata parameters of notes.
- **SECURITY BOUNDARY**: Mutations verified by `ToolRouter`.
- **TEST COVERAGE**: `cognitive_core/tests/test_learning.py`

#### 9. ActivationEngine
- **FILE**: [`cognitive_core/activation.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/activation.py)
- **CLASS/FUNCTION**: `ActivationEngine`
- **CALLED BY**: `Executive`
- **CALLS**: `MemoryController.search`, `MemoryController.read`
- **INPUT**: `principal: Principal`, `query: str`
- **OUTPUT**: `List[Tuple[Dict[str, Any], float]]` (nodes matching spreading activation criteria).
- **PERSISTENCE EFFECT**: None (read-only spreading activation explorer).
- **SECURITY BOUNDARY**: Reads vault contents using `MemoryController` access policies.
- **TEST COVERAGE**: `cognitive_core/tests/test_activation.py`

#### 10. Planner
- **FILE**: [`cognitive_core/planning.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/planning.py)
- **CLASS/FUNCTION**: `Planner`, `ActivePlan`
- **CALLED BY**: `Executive`
- **CALLS**: None.
- **INPUT**: `goal: str`, `context: List[Dict[str, Any]]`
- **OUTPUT**: `ActivePlan`
- **PERSISTENCE EFFECT**: Writes active plan structures to `plan.json`.
- **SECURITY BOUNDARY**: None.
- **TEST COVERAGE**: `cognitive_core/tests/test_planning.py`, `test_cognitive_loop.py`

---

## 4. Memory Architecture & Storage Boundaries

### Architectural Divisions
1. **Canonical Memory**: Stored permanently on disk in vault directories. Formatted with canonical frontmatter.
2. **Cognitive Memory**: Retrieval views using `cognitive_read()` where `REVIEW` nodes are decorated with the `_cognitive_unverified = True` parameter.
3. **Working Memory**: Attention-bounded RAM buffer holding currently active context nodes.
4. **Checkpoint State**: Ephemeral JSON tracking stored to `wm.json` and `plan.json` to handle restart recovery.
5. **Audit State**: Audit records logged line-by-line to `audit_log.jsonl`.

### Memory Controller CRUD Protocol

```text
       CRUD MUTATION             READ RETRIEVAL
   +-------------------+      +------------------+
   | propose()         |      | read()           | -> strict canonical
   | update()          |      | cognitive_read() | -> injects _cognitive_unverified
   | supersede()       |      | search()         | -> handles pagination
   | archive()         |      +------------------+
   +-------------------+
             │                         │
             v                         v
   +---------------------------------------------+
   | MemoryController (Schema & Path validation) |
   +---------------------------------------------+
             │
             ▼
   +---------------------------------------------+
   | FileStorageEngine (Git isolating layer)     |
   +---------------------------------------------+
             │
             ▼
      [ VAULT ROOT ]
```

---

## 5. Cognitive Core Inventory

| Module | Implemented | Integrated | Tested | E2E Verified | Restart Verified |
|---|:---:|:---:|:---:|:---:|:---:|
| **ActivationEngine** | YES | YES | YES | YES | YES |
| **WorkingMemory** | YES | YES | YES | YES | YES |
| **RecallEngine** | YES | YES | YES | YES | YES |
| **Planner** | YES | YES | YES | YES | YES |
| **ReasoningEngine** | YES | YES | YES | YES | YES |
| **ReflectionPipeline** | YES | YES | YES | YES | YES |
| **ToolRouter** | YES | YES | YES | YES | YES |
| **Consolidator** | YES | YES | YES | YES | YES |
| **Deduplicator** | YES | YES | YES | YES | YES |
| **LearningEngine** | YES | YES | YES | YES | YES |

---

## 6. Phase 4.3 Audit Check

| Phase 4.3 Requirement | Implementation Status | Evidence / Location | Verification |
|---|:---:|---|---|
| **Technology Deduplication** | **PASS** | Evaluates similarity, technology name, version range, and source tier in `scan_for_duplicates`. | `cognitive_core/tests/test_deduplication.py` |
| **Version Parser** | **PASS** | Regexp parser supporting Python, PowerShell, Windows Server, and .NET ranges. | `cognitive_core/tests/test_version_parsing.py` |
| **Version-Aware Recall** | **PASS** | Adds compatibility boosts (`+0.3`) and penally ranks mismatches (`-0.3`) in `RecallEngine`. | `memory_controller/tests/test_supersession_phase43.py` |
| **Temporal Recall** | **PASS** | Evaluates note lifetime boundaries (`valid_from` / `valid_until`) during search. | `memory_controller/tests/test_supersession_phase43.py` |
| **Supersession Invariants** | **PASS** | Cycle-detection check, self-supersession blocking, and target node existence gates. | `memory_controller/validation/supersession.py` |
| **Supersession Audit** | **PASS** | Emits distinct log structures (`supersede`, `archive_superseded`) in `controller.py`. | `memory_controller/tests/test_supersession_phase43.py` |
| **Human-Verified Protection** | **PASS** | Enforces enforcer checks blocking AI agents from mutating user-sourced nodes. | `memory_controller/validation/supersession.py` |
| **Reciprocal Links** | **PASS** | Bidirectional relations and properties updated atomically. | `memory_controller/controller.py` |
| **Cycle Detection** | **PASS** | Performs Graph DFS check to prevent cycles before writing notes. | `memory_controller/validation/supersession.py` |
| **Memory Protocol** | **PASS** | Updated specification details inside `00_CORE/Memory_Protocol.md`. | Checked manually. |

---

## 7. Test Baseline

Running `python -m pytest -q` yields:
```text
........................................................................ [ 47%]
........................................................................ [ 94%]
.........                                                                [100%]
153 passed, 34 warnings in 3.68s
```
- **Passed**: 153
- **Failed**: 0
- **Skipped**: 0
- **Warnings**: 34 (Deprecation warnings in `test_lifecycle.py` and `test_recall_valid_from_filtering` due to datetime operations).
- **Failing Tests**: None.

---

## 8. True Restart Verification Proof

### OS Subprocess State Rehydration Test
The multi-process test executed via `scratch/run_multi_process_test.py` proves rehydration across interpreter boundaries:

```text
Running Process 1 (Step 1)...
Process 1 Output:
STEP 1 COMPLETE

Running Process 2 (Step 2)...
Process 2 Output:
STEP 2 COMPLETE

TRUE MULTI-PROCESS VERIFICATION SUCCESSFUL!
```

### Cognitive Learning Loop Restart Cycle
1. **Process 1**: `Executive` runs intent, experiences a tool block, `ReflectionPipeline` writes a new `lesson` node (`REVIEW` lifecycle status) to disk, persists working memory state, and terminates.
2. **Process 2**: Launches a fresh interpreter process, initializes a fresh controller, rehydrates `wm.json` containing the lesson ID, and uses the lesson node to skip blocked tools on subsequent planning cycles.
- **Evidence**: Verified by `cognitive_core/tests/test_working_memory_persistence.py` and `test_cognitive_loop.py`.

---

## 9. Known Defects, Roots & Lessons

### 1. Circular Imports
- **PROBLEM**: Import error loading `Lifecycle` enum inside `SupersessionEnforcer`.
- **ROOT CAUSE**: Circular imports between `controller.py`, `core.py`, and `supersession.py`.
- **FAILED APPROACH**: Re-importing inside methods.
- **FINAL SOLUTION**: Checked enum states using string literals (`"SUPERSEDED"`) inside `supersession.py`, completely avoiding importing `Lifecycle`.
- **AFFECTED FILES**: [`memory_controller/validation/supersession.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/memory_controller/validation/supersession.py).
- **LESSON**: Enums in cross-module boundary validators should be checked as string values when they represent serialization attributes.

### 2. Propose Method Field Dropping
- **PROBLEM**: Proposing a note with `version_range` or `valid_until` dropped those keys.
- **ROOT CAUSE**: `MemoryController.propose` originally populated note dictionaries by copy-indexing only a static default parameters list.
- **FAILED APPROACH**: Manually defining each new parameter in `propose`.
- **FINAL SOLUTION**: Restructured `propose` to initialize defaults and overlay the incoming `note_data` dictionary comprehensively.
- **AFFECTED FILES**: [`memory_controller/controller.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/memory_controller/controller.py).
- **LESSON**: Controller proposal handlers must dynamically merge complete incoming payloads to support version upgrades.

### 3. Jaccard Tokenization Overlap
- **PROBLEM**: Semantic provider scored notes with slightly changed spacing poorly.
- **ROOT CAUSE**: Tokenizer split words directly on whitespace without casing normalization.
- **FAILED APPROACH**: Regex splitting without normalization.
- **FINAL SOLUTION**: Normalizing alphanumeric tokens via lowercase casts.
- **AFFECTED FILES**: [`cognitive_core/semantic.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/semantic.py).
- **LESSON**: Semantic mock metrics must normalise text content before scoring.

---

## 10. Technology Knowledge Base

| Technology | Era / Version | Standard | Purpose | Obsolescence Risk | Recheck Criteria |
|---|---|---|---|---|---|
| **Python** | 3.14.2 | PEP 8 / PEP 484 | Core runtime language. | Low. | Keep compatibility with standard library libraries. |
| **Pytest** | 9.0.2 | GTest style | Automated test harness. | Low. | Verify runner compatibilities. |
| **JSON Schema** | Draft 7 | IETF JSON-Schema | Note metadata structure verification. | Low. | Keep rules inside `schema.py` aligned. |
| **Base64url** | RFC 4648 | base64url encoding | Pagination token serialization. | Very Low. | Keep token padding parsing clean. |

---

## 11. Next-Task Contract

- **TASK ID**: `AG-CONT-01`
- **OBJECTIVE**: Integrate the Agent Handoff / Continuity layer as a core automation feature of the loop. Specifically, update the `Executive` to automatically compile a task summary, verified test results, and next actions to `02_PROJECTS/Continuity_Handoff.md` upon loop termination or task exit.
- **WHY IT IS NEXT**: Handing off projects must be a programmatic, tool-driven event native to the loop, instead of manual write-ups.
- **FILES LIKELY INVOLVED**: [`cognitive_core/executive.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/executive.py), [`02_PROJECTS/Continuity_Handoff.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/02_PROJECTS/Continuity_Handoff.md).
- **DEPENDENCIES**: Completed Phase 4.3 architecture.
- **INVARIANTS**: Writing continuity state must never alter human-verified files or bypass path traversal checkers.
- **ACCEPTANCE CRITERIA**:
  1. Call `Executive.save_state` triggers update to `Continuity_Handoff.md`.
  2. The output lists current phase, next actions, and test baselines.
- **TESTS REQUIRED**: Add unit tests verifying that `save_state` updates handoff contents correctly.
- **RESTART VERIFICATION REQUIRED**: Verify across subprocesses that process 1 writes state, and process 2 reads the compiled handoff.
- **HUMAN APPROVAL REQUIRED**: **YES** (requires initial approval to modify `Continuity_Handoff.md` schemas).

---

## 12. Agent Operating Rules

### What an Agent MAY Do
- Perform read-only inspections, searches, and semantic queries.
- Propose new `hypothesis`, `lesson`, or `error` notes in `REVIEW` lifecycle states.
- Run tests and temporary scratch scripts.

### What an Agent MUST NOT Do
- Delete or modify files under `06_INBOX/RAW_IMPORTS/`.
- Overwrite or mutate human-verified nodes without permission.
- Install new external dependencies or libraries.

### Verification and Git Protocol
- Run `python -m pytest` before staging changes.
- Never force-push or reset branch history.
- Stage directories explicitly: do not run `git add .` or commit cache folders.

---

## 13. Antigravity → Perplexity → Antigravity Loop

```text
  [ Antigravity ]
        │
        ▼ (writes core code and updates Continuity_Handoff.md)
  [ Git Push / GitHub Remote ]
        │
        ▼ (clones main, runs verification scripts, checks Hand-off)
  [ Perplexity ]
        │
        ▼ (audits architecture, executes next task spec, updates handoff)
  [ Git Push / GitHub Remote ]
        │
        ▼ (pulls main, validates changes, continues core loop)
  [ Antigravity ]
```

---

## 14. Final Handoff Status

- **HANDOFF_VERSION**: `1.0.0`
- **PROJECT_COMMIT**: `db958f4`
- **PROJECT_PHASE**: Phase 4.3 (Complete)
- **CURRENT_TASK**: Handoff Verification
- **NEXT_TASK**: `AG-CONT-01`
- **PYTEST_BASELINE**: `153 passed`
- **MULTI_PROCESS_STATUS**: `PASS`
- **PHASE_4_3_STATUS**: `PASS`
- **KNOWN_RISKS**: `Stale memory rehydration`
- **BLOCKERS**: `None`

---

### HANDOFF STATUS: READY FOR PERPLEXITY


============================================================
FILE: AGENTS.md
============================================================

# AGENTS.md — AI Memory System Operating Contract

## 0. Mission

This repository is the persistent memory and knowledge base for the user's AI system.

The goal is not to store every conversation. The goal is to preserve useful, reusable, verifiable knowledge and the history needed to understand how that knowledge was obtained.

The AI must protect the integrity of this memory.

---

# 1. Source of Truth Hierarchy

When information conflicts, prefer sources in this order:

1. Explicitly confirmed by the user
2. Directly verified by execution/test
3. Official primary documentation
4. Project documentation maintained in this Vault
5. Repeated successful experience
6. Other external sources
7. AI-generated or inferred information

Never silently replace a stronger source with a weaker one.

When a conflict cannot be resolved, preserve both claims and create a conflict record.

---

# 2. Core Files

Before changing memory behavior, read:

- `00_CORE/Identity.md`
- `00_CORE/Rules.md`
- `00_CORE/Memory_Protocol.md`
- `00_CORE/Confidence_Model.md`
- `00_CORE/System_Architecture.md`

For retrieval/import tasks also read:

- `99_SYSTEM/Classification_Protocol.md`
- `99_SYSTEM/Import_Pipeline.md`
- `99_SYSTEM/Quality_Control.md`

These files define the operating contract.

---

# 3. Memory Is Not Conversation History

Do NOT automatically convert conversations into permanent memory.

A conversation may contain:

- temporary reasoning;
- mistakes;
- duplicate explanations;
- abandoned ideas;
- outdated information;
- speculation;
- irrelevant context.

Permanent memory must contain information that is useful after the original conversation is gone.

Use the following memory types:

- `knowledge`
- `project`
- `procedure`
- `decision`
- `experience`
- `error`
- `lesson`
- `preference`
- `resource`
- `hypothesis`

---

# 4. Before Creating a Note

Always:

1. Search for an existing note covering the same concept.
2. Check related notes.
3. Check for contradictions.
4. Determine the correct memory type.
5. Preserve provenance.
6. Assign confidence.
7. Add relevant `[[wikilinks]]`.
8. Avoid duplicating information.

If an existing note is substantially the same, update it instead of creating a duplicate.

---

# 5. Atomic Notes

Prefer one main concept per note.

Good:

`PowerShell_ExecutionPolicy.md`

Bad:

`Everything_I_Know_About_Windows.md`

A note may contain related information, but its purpose must be obvious from the title and frontmatter.

---

# 6. Frontmatter

Permanent notes should normally contain:

```yaml
---
id: "<stable UUID>"
type:
lifecycle: REVIEW
category:
tags: []
created:
updated:
provenance:
  source_type:
  source_ref:
confidence:
verification:
relations: []
---
```

Do not invent metadata values when they are unknown.

Use:

- `verified`
- `partially_verified`
- `unverified`
- `inferred`

for verification state.

---

# 7. Provenance

Whenever possible preserve:

- source platform;
- source conversation;
- source date;
- original file;
- URL;
- verification method.

Imported AI content must not be presented as independently verified merely because an AI generated it.

---

# 8. Import Rules

All new external AI memories enter:

`06_INBOX/RAW_IMPORTS/`

First preserve the raw source. Then, for a derivative only:

```text
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED
```

`RAW` remains permanently in `06_INBOX/RAW_IMPORTS/`; it is never canonical memory and is never indexed as canonical knowledge. See `[[Memory Lifecycle]]` and `[[Canonical Frontmatter]]`.

---

# 9. Deduplication

Before creating a new memory note, compare:

- title;
- subject;
- entities;
- claims;
- semantic similarity;
- project;
- date;
- source quality.

Similarity alone is not enough to merge notes.

Two similar notes may describe different environments, versions, projects, or outcomes.

---

# 10. Contradictions

If two memories disagree:

1. Do not choose arbitrarily.
2. Compare provenance.
3. Compare dates.
4. Compare environment/version.
5. Check whether both statements can be true under different conditions.
6. If unresolved, preserve both.
7. Mark the conflict.
8. Ask the user when the conflict materially affects a decision.

Never hide a contradiction.

---

# 11. Confidence

Use:

- `very_high`
- `high`
- `medium`
- `low`
- `unknown`

Confidence is not the same as truth.

A high-confidence statement should still have provenance.

AI inference normally starts at `low` or `medium` unless independently verified.

---

# 12. Retrieval Strategy

When searching memory, use a layered approach:

1. exact keyword search;
2. full-text/BM25 search;
3. semantic similarity;
4. metadata filtering;
5. graph relationships;
6. recency;
7. confidence;
8. project relevance.

Prefer a small set of highly relevant notes over a large amount of weak context.

Do not load the entire Vault into the model context.

---

# 13. Knowledge Graph / Synapses

Use `[[wikilinks]]` to represent relationships.

Useful relationship concepts include:

- `related_to`
- `depends_on`
- `caused_by`
- `solved_by`
- `supports`
- `contradicts`
- `implements`
- `used_by`
- `derived_from`
- `replaces`

Do not create links merely to increase graph density.

A link should have semantic meaning.

---

# 14. Projects

Project notes represent current state.

Project information belongs under:

`02_PROJECTS/`

Projects may link to:

- knowledge;
- procedures;
- decisions;
- errors;
- lessons;
- resources.

When a project decision becomes generally reusable, extract it into permanent knowledge/procedure rather than leaving it buried in the project.

---

# 15. Procedures

A procedure must describe:

- purpose;
- scope;
- preconditions;
- dependencies;
- actions;
- expected results;
- failure handling;
- verification;
- rollback when applicable.

Never label an untested procedure as verified.

---

# 16. Errors and Learning

When an error is resolved, preserve:

```text
Error
  -> Root Cause
  -> Fix
  -> Verification
  -> Prevention
  -> Lesson
```

If the lesson is reusable, create a separate `lesson` note and link it to the original error.

Repeated errors should increase the priority of the corresponding lesson/procedure.

---

# 17. Decisions

A decision should preserve:

- problem;
- context;
- options;
- chosen option;
- rationale;
- expected outcome;
- risks;
- review trigger;
- result.

Do not erase previous decisions just because the system later changes direction.

Archive superseded decisions and explain why they were replaced.

---

# 18. User Preferences

Only store preferences that are:

- explicitly stated;
- stable enough to matter later;
- useful for future work.

Do not infer sensitive personal attributes.

Do not turn temporary instructions into permanent preferences unless clearly requested or repeatedly established.

---

# 19. Security

NEVER store:

- passwords;
- API keys;
- access tokens;
- private keys;
- authentication secrets;
- credentials.

If such material appears during import:

1. do not copy it into permanent memory;
2. flag it;
3. remove/redact it from processed memory;
4. preserve only the fact that a secret existed if that fact is useful.

---

# 20. Destructive Changes

Before deleting or mass-modifying memory:

- inspect affected files;
- preserve history;
- prefer Git;
- create a backup when appropriate;
- report what will change.

Do not perform destructive cleanup merely because files appear unused.

---

# 21. AI Goal Discipline

For every substantial task:

```text
INTENT
  -> CONSTRAINTS
  -> RELEVANT MEMORY
  -> PLAN
  -> ACTION
  -> VALIDATION
  -> MEMORY UPDATE
```

If reasoning starts drifting away from the user's objective:

1. stop;
2. restate the actual objective internally;
3. discard irrelevant branches;
4. continue from the last valid state.

Do not optimize for producing more text. Optimize for solving the actual task.

---

# 22. Tool Use

Before executing commands or changing infrastructure:

- inspect the environment;
- verify target;
- understand expected result;
- use the smallest sufficient action;
- capture actual output;
- validate after execution.

Never claim an operation succeeded without observing evidence of success.

---

# 23. Memory Write Decision

After completing meaningful work, ask:

> Is there something here that will make a future task materially better?

If no: do not write memory.

If yes, determine whether it is:

- knowledge;
- procedure;
- decision;
- error;
- lesson;
- experience;
- project state.

Store the smallest reusable representation.

---

# 24. Git

The Vault should be treated as a versioned knowledge repository.

Recommended workflow:

```text
Change
  -> Review diff
  -> Validate
  -> Commit
```

Never commit secrets.

---

# 25. Canonical Memory vs Raw Memory

Canonical memory:

- `00_CORE`
- `01_KNOWLEDGE`
- `02_PROJECTS`
- `03_PROCEDURES`
- `04_MEMORY`
- `05_RESOURCES`

Raw/import memory:

- `06_INBOX/RAW_IMPORTS`

System specifications:

- `99_SYSTEM`

Templates:

- `90_TEMPLATES`

Raw imports are evidence, not automatically trusted knowledge.

---

# 26. Final Validation

Before finishing a memory operation, verify:

- correct folder;
- correct memory type;
- no unnecessary duplicate;
- provenance preserved;
- confidence assigned;
- relevant links added;
- secrets excluded;
- contradictions handled;
- source preserved;
- Markdown remains valid.

The system should prefer a smaller, cleaner, trustworthy memory over a larger polluted one.

---

# 27. Future Memory Controller

When the Memory Controller is implemented, it should expose operations conceptually equivalent to:

```text
search_memory(query)
read_memory(note)
find_related(note)
find_conflicts(note)
create_memory(note)
update_memory(note)
link_memory(a, b, relation)
archive_memory(note, reason)
validate_memory(note)
```

The controller must apply this document and the files in `00_CORE/` before writing canonical memory.

---

# 28. Prime Directive

The purpose of the memory is not to make the AI remember everything.

The purpose is to make the AI:

- remember the right things;
- retrieve the right things;
- know how confident those things are;
- understand how they are connected;
- learn from mistakes;
- preserve decisions;
- avoid repeating failures;
- remain aligned with the user's actual objective.

**Better memory beats more memory.**


============================================================
FILE: README.md
============================================================

---
type: system
category: memory
status: active
version: 1.0.0
id: "fa4f3c56-dc66-42a0-872c-19cdf302cb2a"
document_kind: system_document
document_status: active
provenance_status: incomplete
relations: []
---

# AI Memory Vault

> Codex operating contract: [[AGENTS.md]]

Memorie externa, baza de cunostinte si strat de continuitate pentru sistemul AI.

## Principiu

Vault-ul separa:

- **Knowledge** — ceea ce este cunoscut si reutilizabil
- **Projects** — lucrurile construite in prezent
- **Procedures** — cum se executa lucrurile
- **Memory** — experiente, erori, lectii, decizii si preferinte
- **Resources** — surse si referinte
- **Inbox** — informatii noi, inca neclasificate
- **System** — regulile de retrieval, clasificare, graf si validare

## Regula principala

Nu trimite intregul Vault catre un LLM. Retrieval-ul trebuie sa selecteze numai contextul relevant.

## Ordinea de lucru

`Inbox -> Classify -> Deduplicate -> Validate -> Link -> Store -> Retrieve`

## Structura

- [[00_CORE/Identity]]
- [[00_CORE/Rules]]
- [[00_CORE/Goals]]
- [[00_CORE/System_Architecture]]
- [[00_CORE/AI_Operating_Protocol]]
- [[00_CORE/Memory_Protocol]]
- [[01_KNOWLEDGE/README]]
- [[02_PROJECTS/_Projects_Index]]
- [[03_PROCEDURES/README]]
- [[04_MEMORY/README]]
- [[05_RESOURCES/README]]
- [[06_INBOX/README]]
- [[99_SYSTEM/RAG_KnowledgeGraph_Architecture]]
- [[99_SYSTEM/Knowledge_Graph_Schema]]
- [[99_SYSTEM/RAG_Structure]]
- [[99_SYSTEM/Import_Pipeline]]
- [[99_SYSTEM/Classification_Protocol]]
- [[99_SYSTEM/Quality_Control]]`r`n- [[99_SYSTEM/Storage_Conventions]]`r`n- [[99_SYSTEM/Canonical_Frontmatter]]`r`n- [[99_SYSTEM/Memory_Lifecycle]]`r`n- [[99_SYSTEM/Integrity_Check]]`r`n- [[99_SYSTEM/Document_Object_Schemas]]


============================================================
FILE: 00_CORE/Rules.md
============================================================

---
type: core
category: rules
status: active
version: 1.0.0
id: "e08b0d08-8527-4ddf-a260-09f5f6f7c499"
document_kind: policy
document_status: active
provenance_status: incomplete
relations: []
policy_scope: vault-governance
---

# Rules

## 1. Information Integrity

- Nu inventa informatii.
- Daca ceva nu este verificat, marcheaza-l explicit.
- Pastreaza sursa si data cand sunt disponibile.
- Nu transforma o ipoteza in fapt.
- Nu suprascrie o informatie verificata cu una mai slaba fara motiv.

## 2. Memory

- Citeste memoria relevanta inainte de a crea continut nou.
- Preferă actualizarea unei note existente atunci cand acelasi concept exista deja.
- Foloseste note atomice: un concept principal per nota.
- Pastreaza legaturi `[[wikilinks]]`.
- Nu stoca parole, token-uri, chei API sau secrete.
- Nu sterge istoria critica; arhiveaza cu motiv.

## 3. Retrieval

Retrieval-ul trebuie sa combine, cand este disponibil:

- semantic similarity;
- keyword match;
- metadata;
- tags;
- graph relationships;
- recency;
- confidence;
- project relevance.

## 4. Reasoning

Inainte de actiuni importante:

1. identifica obiectivul;
2. identifica constrangerile;
3. recupereaza memoria relevanta;
4. construieste un plan;
5. verifica planul;
6. executa;
7. valideaza rezultatul;
8. extrage lectiile.

## 5. Goal Drift

Daca raspunsul sau planul incepe sa se indeparteze de obiectiv:

- opreste ramificarea;
- revino la obiectiv;
- noteaza schimbarea daca este relevanta;
- cere confirmare pentru schimbari majore de directie.

## 6. Destructive Actions

Actiunile cu risc de pierdere, modificare masiva sau impact asupra infrastructurii necesita:

- identificarea exacta a tintei;
- verificarea preconditiilor;
- backup / rollback unde este posibil;
- validare dupa executie.

## 7. Import

Datele brute importate din alte AI-uri intra mai intai in:

`06_INBOX/RAW_IMPORTS/`

Nu intra direct in memoria permanenta.

## 8. Quality

O nota buna trebuie sa fie:

- clara;
- reutilizabila;
- suficient de atomica;
- legata de alte note;
- cu metadata;
- cu sursa cand exista;
- cu confidence.

## 9. Security

Nu stoca:

- parole;
- API keys;
- private keys;
- token-uri;
- date de autentificare;
- identificatori personali inutili.

## 10. Completion

Nu declara un task "Done" doar pentru ca ai generat un raspuns. "Done" inseamna ca rezultatul a fost verificat la nivelul potrivit de risc.


============================================================
FILE: 00_CORE/Memory_Protocol.md
============================================================

---
type: core
category: memory
status: active
version: 1.0.0
id: "54b48919-d58a-4502-a20f-2717b022d375"
document_kind: policy
document_status: active
provenance_status: incomplete
relations: []
policy_scope: vault-governance
---

# Memory Protocol

## Memory Classes

| Type | Meaning |
|---|---|
| knowledge | fapt / concept reutilizabil |
| project | stare si context de proiect |
| procedure | pasi verificati |
| decision | alegere si rationale |
| experience | eveniment sau experienta |
| error | esec analizat |
| lesson | regula invatata din experienta |
| preference | preferinta stabila |
| resource | sursa externa |
| hypothesis | idee neconfirmata |

## Write Rules

Create a new note when the information is:

- reusable;
- distinct;
- stable enough;
- relevant to future work.

Update an existing note when:

- the same concept exists;
- the new information improves accuracy;
- the old version should remain as history.

Do not store when:

- it is trivial;
- it is duplicated;
- it is purely conversational noise;
- it contains secrets;
- it is obsolete without historical value.

## Memory Lifecycle

```text
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED
```

`RAW` is permanent source evidence in `06_INBOX/RAW_IMPORTS/`. Only a derivative can be classified, normalized, reviewed, verified, and promoted. Raw evidence is never rewritten, deleted, or indexed as canonical memory.

## Provenance

Every imported memory should retain, when possible:

- source platform;
- source conversation;
- source date;
- extraction date;
- confidence;
- verification state.

Use the canonical schema in [[Canonical Frontmatter]]. Any normalized or redacted derivative must reference its original raw path. Promotion to `ACTIVE` follows [[Promotion and Human Review]].

## Technology and Version Handling

To maintain memory integrity, the Vault employs technology-aware deduplication and version-aware recall:
- **Technology and Version Metadata**: Notes can include metadata fields `version_range` (specifying version scope, e.g., `"Python 3.12"`, `"PowerShell 7.x"`) and `applies_to` (specifying target technology/product).
- **Deduplication Identity**: Duplicate memory detection requires content similarity above a configured threshold (default `0.85`), matching technology/product identity, matching version ranges, and matching provenance source types.
- **Differentiation**: Memories targeting different technology versions or from different source tiers (e.g. user-sourced vs. AI-inferred) are kept separate. Unknown versions must never cause deduplication overlap.
- **Version-Aware Recall**: Queries containing specific technology versions (e.g., "Python 3.12") boost matching memories (+0.3 confidence score), penalize mismatched versions (-0.3 confidence score), and treat notes lacking version ranges as neutral.
- **Runtime Authority Score**: The system derives an `authority_score` at runtime based on `provenance.source_type` (e.g., `official` has higher authority than `ai`). This score is combined with the note's confidence to rank results during recall, and is **never** persisted in canonical frontmatter metadata.
- **Temporal Validity (`valid_from` / `valid_until`)**: Notes can define `valid_from` (start date of validity) and `valid_until` (expiration date). Notes not yet valid (future `valid_from`) or expired (past `valid_until`) are penalized during recall, but remain retrievable via historical queries.

## Supersession Invariants and Enforcement

Establishing a relationship where a new memory replaces an old one must follow the explicit supersession protocol:
1. **Explicit Request**: An explicit operation request (`old_id`, `new_id`, `evidence`) must be initiated. Lifecycle transitions do not implicitly create supersession links.
2. **Invariants**:
   - Both predecessor and successor memories must exist.
   - Self-supersession is prohibited (`old_id != new_id`).
   - Cyclic supersession paths are prevented.
   - Reciprocal links are automatically updated and kept consistent (`new.supersedes = old`, `old.superseded_by = new`, with matching relation items `replaces` and `replaced_by` in their respective `relations` list).
   - The predecessor note's content, UUID, provenance, and extraction date must remain unchanged.
   - Human-verified memories cannot be automatically superseded by an AI agent.
   - Superseded memory is kept as historical record (lifecycle set to `SUPERSEDED`) and is never physically deleted.
3. **Atomicity**: The transaction must write changes to both notes atomically. If any write fails, the entire transaction is rolled back.

## Audit Event Logging

Critical memory updates and transitions emit structured log entries to `audit_log.jsonl`:
- `supersede`: Emitted upon successful execution of the explicit supersession flow.
- `archive_superseded`: Emitted when the predecessor memory lifecycle is transitioned to `SUPERSEDED`.
- `valid_until_update`: Emitted when the expiration date (`valid_until`) of an active memory is updated.
- `conflict`: Emitted when overlapping, incompatible, or cyclic relations are proposed.


============================================================
END OF FILE
============================================================

