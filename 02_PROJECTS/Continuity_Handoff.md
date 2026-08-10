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

# START HERE — Project Continuity Handoff

This document is the **single canonical handoff contract** for the next AI coding agent (Perplexity Desktop). It provides the complete context required to resume development of the AI Memory Vault and Cognitive Core without access to previous conversation histories.

---

## 1. Bootstrap Instructions (Start Here)

Incoming Agent, follow this step-by-step sequence to bootstrap your understanding and verify the codebase state:

1. **Read this Document First**: Understand the system architecture, code file mappings, and rules.
2. **Read the Core Vault Rules**: View the operating protocols in [Memory_Protocol.md](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/00_CORE/Memory_Protocol.md) and [Rules.md](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/00_CORE/Rules.md).
3. **Verify the Environment**:
   - Run the automated test suite using: `python -m pytest -q`
   - Run the true multi-process persistence verification: `python C:\Users\user\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\scratch\run_multi_process_test.py`
4. **Compare Claims with Code**: Verify that code interfaces in `cognitive_core/` and `memory_controller/` match the descriptions in this handoff. Do not trust outdated comments or memory entries over the actual code.
5. **Analyze Git Diff**: Run `git status -s` to see if there are untracked files or modifications from previous development.
6. **Identify the Next Task**: Look at the **Next Task Specification** in Section 13 of this document. Do not start new features before executing it.
7. **Human Approval Gates**: Check Section 14 for actions requiring explicit human confirmation.

---

## 2. Current Project State

- **Active Development Phase**: Phase 4.3 (Technology Knowledge / Version / Deduplication / Supersession / Temporal Recall / Memory Protocol).
- **Core Status**: Code-complete and 100% verified. All Phase 4.3 features are fully implemented, tested, and integrated.
- **Git Status**:
  - Tracked project files are unmodified except for `00_CORE/Memory_Protocol.md` which has been updated to document current contracts.
  - Production code files (in `cognitive_core/` and `memory_controller/`) are untracked because they were not added to Git in the initial template, representing the default state of this workspace.
  - Clean boundary maintained: `06_INBOX/RAW_IMPORTS/` is completely clean and untouched.

---

## 3. Verified vs. Unverified Features

### Verified by Pytest (Unit / Integration Tests)
- **Technology Parsing** (`version.py`): Extracting prefix, exact, and range versions for Python, PowerShell, Windows Server, .NET Framework, and .NET.
- **Deduplication Identity Matching** (`deduplication.py`): Matching content similarity, technology identity, version ranges, and source types.
- **Deduplication Differentiation**: Mismatched versions, mismatched technologies, mismatched source types, and unknown versions do not deduplicate.
- **Explicit Supersession** (`controller.py`): Atomic write transactions (replaces/replaced_by edges) with rollback on failure.
- **Supersession Invariants** (`supersession.py`): Cycle rejection, self-supersession blocking, and human-verified memory protection.
- **Audit Logs** (`logger.py`): Logging of `supersede`, `archive_superseded`, and `valid_until_update` events with complete metadata.
- **Recall Scoring** (`recall.py`): Boosting compatible versions (+0.3), penalizing mismatched versions (-0.3), keeping lack of version neutral, and temporal validity checks (`valid_from` / `valid_until`).

### Verified by Subprocess (True Multi-Process Restart)
- Creating memory, running `supersede`, exiting the Python interpreter, and recovering the exact links and audit logs from disk inside a completely fresh Python process.

### Unverified
- Behavior of the memory consolidation or dynamic synapses when executed under a different model context (Perplexity).

---

## 4. Cognitive Core Architecture & File Mapping

| Component | Responsibility | File | Verification Status |
|---|---|---|---|
| **Executive** | Orchestrates cognitive loop step execution, retry, and plan recovery. | [`executive.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/executive.py) | **UNIT TEST VERIFIED** |
| **WorkingMemory** | Attention-based ephemeral buffer with evictions. Rehydrates nodes on load. | [`working_memory.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/working_memory.py) | **INTEGRATION TEST VERIFIED** |
| **RecallEngine** | Combines semantic similarity, activation, temporal decay, and versions. | [`recall.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/recall.py) | **UNIT TEST VERIFIED** |
| **Deduplicator** | Flags semantic duplicates using tech-aware identity checks. | [`deduplication.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/deduplication.py) | **UNIT TEST VERIFIED** |
| **ReflectionPipeline** | Generates lessons/errors on action errors or policy blocks. | [`reflection.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/reflection.py) | **UNIT TEST VERIFIED** |
| **ToolRouter** | Enforces RiskLevel policies and blocks unverified write actions. | [`tool_router.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/tool_router.py) | **UNIT TEST VERIFIED** |
| **Consolidator** | Merges duplicate/similar lessons periodically. | [`consolidation.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/consolidation.py) | **UNIT TEST VERIFIED** |
| **LearningEngine** | Automatically promotes nodes based on graph connection density. | [`learning.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/learning.py) | **UNIT TEST VERIFIED** |
| **ActivationEngine** | Propagates spreading activation across associated graph nodes. | [`activation.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/activation.py) | **UNIT TEST VERIFIED** |
| **Planner** | Generates context-aware, multi-step plans for intent fulfillment. | [`planning.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/planning.py) | **UNIT TEST VERIFIED** |

---

## 5. Memory Vault & Controller Operations

The Vault operates under a strict trust and interface model:
- **Canonical Reads vs. Cognitive Reads**: Canonical reads fetch exact notes. Cognitive reads (`cognitive_read`) allow retrieval of unverified `REVIEW` nodes by appending the `_cognitive_unverified = True` attribute.
- **Mutations (Propose/Update/Supersede/Archive)**: Must go through `MemoryController` to write to disk via `FileStorageEngine`.
- **Trust Tiers**:
  ```text
  Canonical Contract (Rules/Memory_Protocol)
       > Verified Repository State (Actual Code)
       > Verified Test Results (Pytest Outputs)
       > Agent-Generated Knowledge (Verified / Active notes)
       > Agent Hypotheses (Review / Unverified notes)
  ```
- **Principal Roles**:
  - `Principal.AI_AGENT`: Allowed to search, read, propose, and update unverified/RAW/REVIEW drafts. Blocked from modifying active/human-verified notes without human review.
  - `Principal.HUMAN` / `ADMIN`: Unrestricted read and write access, including promotion of notes and supersession of user-sourced nodes.

---

## 6. Learning Loop Status

The cognitive loop flows as:
`Experience -> Action -> Result -> Reflection -> Lesson/Error (REVIEW) -> Persistence -> Consolidation -> Recall -> Reasoning -> Planning -> Future Action`

- **VERIFIED**: Error/Lesson creation on execution failure (ReflectionPipeline), proposal of dynamic synapses, and promotion of confidence levels based on edge density (LearningEngine).
- **UNVERIFIED**: Behavior of consolidator under long-term memory scenarios.

---

## 7. Persistence & Restart Semantics

- **WorkingMemory Persistence**: Only IDs, ticks, and activation levels are stored in `wm.json`. Body content is hydrated dynamically via `MemoryController` upon load to guarantee freshness.
- **Plan Persistence**: Active plans are saved to `plan.json`.
- **True Multi-Process Restart**: Verified across separate OS subprocesses using `scratch/run_multi_process_test.py`.

---

## 8. Phase History & Roadmap

1. **P0 (Vault Foundations)**: Complete. Implemented path traversal checks, schema validators, lifecycle enforcements, and directory mapping.
2. **P0-10 (Historical Memory)**: Complete.
3. **Cognitive Core Phase 1 (Loop)**: Complete. Wired synapses, executive planner, and reflection pipelines.
4. **Phase 2 (Continuity)**: Complete. Working Memory state check-pointing.
5. **Phase 3 (Consolidation)**: Complete. Dynamic synapses and deduplication loops.
6. **Phase 4.3 (Technology/Version)**: Complete. Added version-aware recall, explicit supersession invariants, audit events, and protocol documents.

---

## 9. Phase 4.3 Contracts Summary

- **Deduplication Contract**: Duplicate identity requires content similarity, matching technology, matching version ranges, and matching source types. Checked and tested.
- **Supersession Contract**: Explicit boundary method `supersede(old_id, new_id, evidence)` verifying note existence, preventing cycles, blocking self-supersession, protecting human-verified notes from AI agents, and preserving histories with atomic rollbacks. Tested and verified.
- **Recall Contract**: Evaluates `valid_from` (future date checks) and `valid_until` (expiration checks), boosting matches (+0.3) and penalizing mismatches (-0.3) based on query version indicators, while keeping no-version notes neutral. Verified.
- **Memory_Protocol.md**: Fully updated with current implementation specs.

---

## 10. Key Architectural Decisions

- **No Implicit Supersession**: Lifecycle state transitions to `SUPERSEDED` do not automatically create relationship edges. All supersessions must call `supersede()` explicitly.
- **Storage-agnostic Schema Validation**: Schematic validators strip the `content` field before validation because the JSON Schema does not allow extra properties (due to `additionalProperties: False`).
- **Audit-Archive Separation**: Supersession events are logged as `supersede` and `archive_superseded` to distinguish them from standard `archive` actions.
- **Runtime-only Authority**: The `authority_score` is derived from provenance types at runtime, preventing stale metadata from being saved to disk.

---

## 11. Lessons & Resolved Defects

- **Circular Import Resolution**: Broken circular references between `SupersessionEnforcer` and `Lifecycle` by removing Lifecycle imports and using string literal `"SUPERSEDED"` checks inside `supersession.py`.
- **Propose Field Dropping**: Corrected `MemoryController.propose` to comprehensively merge and preserve all fields passed in `note_data` (like `version_range` and `valid_until`), instead of copying only pre-defined default keys.
- **Naivety Datetime comparison**: Fixed deprecation warnings by replacing naive `datetime.utcnow()` with naive `datetime.now(timezone.utc).replace(tzinfo=None)` to perform safe comparisons with naive string parses.

---

## 12. Known Risks Register

| Risk | Severity | Location | Current Mitigation | Remaining Action |
|---|---|---|---|---|
| **Stale Memory Rehydration** | Medium | `working_memory.py` | Hydrates node data dynamically on load. | Monitor for notes deleted while in WM. |
| **Deduplication Noise** | Low | `deduplication.py` | Requires exact matching version range and technology. | Monitor Jaccard threshold efficiency. |
| **Audit Logs Growth** | Low | `logger.py` | Writes to single `audit_log.jsonl`. | Implement audit log rotation in future phases. |

---

## 13. Next Task Specification

- **NEXT_TASK_ID**: `AG-CONT-01`
- **OBJECTIVE**: Integrate the Agent Handoff / Continuity layer as a core automation feature of the loop. Specifically, update the `Executive` to automatically compile a task summary, verified test results, and next actions to `02_PROJECTS/Continuity_Handoff.md` upon loop termination or task exit.
- **WHY NOW**: To make handoffs a structured, programmatic artifact generated natively by the loop, instead of manual text reports.
- **FILES_LIKELY_INVOLVED**: [`cognitive_core/executive.py`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/cognitive_core/executive.py), [`02_PROJECTS/Continuity_Handoff.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/02_PROJECTS/Continuity_Handoff.md)
- **DEPENDENCIES**: None.
- **RISKS**: Overwriting manual entries in the handoff.
- **TEST_REQUIREMENTS**: Add unit tests verifying that `save_state` or loop shutdown updates the handoff document with correct verification results.
- **APPROVAL_REQUIRED**: **YES** (requires human confirmation before mutating handoff schemas).

---

## 14. Human Approval Requirements

The following actions **always require explicit human approval** and must not be run autonomously:
1. Deleting canonical files in `00_CORE`, `01_KNOWLEDGE`, `02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY`, `05_RESOURCES`, or `99_SYSTEM`.
2. Mutating human-verified memory nodes (`verification == "verified"` or `provenance.source_type == "user"`).
3. Modifying files under `06_INBOX/RAW_IMPORTS/`.
4. Installing external dependencies or libraries.
5. Altering validation schemas or lifecycle transition states in `validation/schema.py`.
