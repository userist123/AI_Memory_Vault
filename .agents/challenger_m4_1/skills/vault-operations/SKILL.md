---
name: vault-operations
description: Runbook and multi-step procedure for interacting with the AI Memory Vault cognitive operating system.
---

# Vault Operations Skill

This skill provides step-by-step procedures for querying, proposing, verifying, and maintaining knowledge within the AI Memory Vault.

## Workflow 1: Associative and Semantic Recall
To retrieve relevant knowledge while preserving active lineage across superseded notes:
1. Initialize `MemoryController` with the appropriate `StorageEngine` (`SQLiteStorageEngine` or `FileStorageEngine`).
2. Instantiate `RecallEngine` with `DeterministicSemanticProvider`.
3. Call `recall_engine.recall(principal, query, activated_nodes, working_memory)`.
4. Notes in `REVIEW` are flagged with `_cognitive_unverified = True`.
5. If a superseded note scores high, its active successor inherits the relevance score with a 10% freshness boost.

## Workflow 2: Safe Memory Proposal (AI Agent)
1. Construct note metadata adhering to `_CANONICAL_SCHEMA`.
2. Ensure `lifecycle = "REVIEW"`, `verification = "unverified"`, and `provenance.source_type = "inference"` (or `"execution"`).
3. Submit note via `controller.propose(Principal.AI_AGENT, note_data)`.

## Workflow 3: Attestation (Human / Admin)
1. Verify evidence reference and factual accuracy.
2. Call `controller.attest(Principal.HUMAN, note_id, verification_reason, evidence_reference)`.
3. Promote note to active state via `controller.promote(Principal.HUMAN, note_id)`.

## Workflow 4: Reflexion on Error or Blocked Action
1. Extract error details, root cause, fix applied, verification check, prevention rule, and lesson.
2. Structure reflection using `FormalReflexion.format_reflection(...)`.
3. Submit as `error` or `lesson` memory in `REVIEW` lifecycle.
