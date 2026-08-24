---
id: "653c5406-68c6-44ad-8164-3fc821ca6904"
type: system
lifecycle: REVIEW
category: architecture-review
tags: [phase-omega, formal-design, forensic-audit, unverified]
created: "2026-08-10"
updated: "2026-08-10"
provenance:
  source_type: ai
  source_ref: "perplexity-review-session-2026-08-10"
confidence: medium
verification: unverified
relations:
  - relation: related_to
    target: project
    target_id: "8c7d5c90-9c29-450b-b5a9-e2b2024db502"
---

# Phase Omega — Formal System Design Review (AI-Authored, Unverified)

## Status of this document

**This document is an AI-generated architectural analysis. It is NOT a verified fact, NOT an implementation, and NOT an authoritative statement about the codebase's correctness.** It was produced by an independent review agent (Perplexity) at repository HEAD `7d69ba1f46d4b5d7feb6ea48ac27e54aa9f9f550`, based on direct reading of source files via the GitHub API. **No source code in `cognitive_core/` or `memory_controller/` was modified to produce this document. No tests were executed against this repository during this review session** — all test-related claims herein are based on reading test source code, not on running `pytest`. This document must not be cited as proof that any described gap has been fixed, or that any described design has been implemented.

Per the source-of-truth hierarchy in `AGENTS.md`, this document ranks as tier 7 ("AI-generated or inferred information") until independently reviewed and re-classified by a human or by execution-verified evidence.

## Purpose

To persist, inside the canonical repository (not only in a disposable conversation), a structured forensic + forward-looking architectural analysis so that a successor engineering agent (human or AI) can consult it without depending on prior conversation history — consistent with the project's stated continuity principle that GitHub, not conversation, is canonical.

## 1. Confirmed repository facts at time of writing

- Repository: `userist123/AI_Memory_Vault_CODEX_READY`
- Branch: `main`
- HEAD at time of writing: `7d69ba1f46d4b5d7feb6ea48ac27e54aa9f9f550`
- Full commit history on `main` (3 commits): `852cb25` (2026-08-09, vault init) → `db958f4` (2026-08-10, cognitive core + continuity layer implementation) → `7d69ba1` (2026-08-10, docs-only update to `Continuity_Handoff.md`)
- Confirmed via GitHub API commit diff stats: the `db958f4`→`7d69ba1` change touched **only** `02_PROJECTS/Continuity_Handoff.md`. No `cognitive_core/` or `memory_controller/` file changed between those two commits.
- **Known discrepancy**: `Continuity_Handoff.md`, as of commit `7d69ba1`, still internally states `Current Commit: db958f4` and `PROJECT_COMMIT: db958f4` in its own body — i.e., the handoff document does not (and structurally cannot, without a two-phase commit process) describe the commit that contains it. This is a design limitation, not a simple typo — see section 3 below.

## 2. Summary of prior independent findings (established across three review passes this session)

These are restated here only as a compact index; full reasoning lives in the review session, not reproduced verbatim to keep this artifact maintainable.

1. **Authority/verification self-claim gap (highest severity)**: `MemoryController.propose()` allows any principal, including `AI_AGENT`, to set `provenance.source_type: "official"` and `verification: "verified"` with no check binding the claim to the caller's actual identity or evidence. This directly undermines the human-verified-protection invariant relied upon elsewhere in the system.
2. **No multi-agent coordination**: no locking, no optimistic concurrency (no version/CAS column), no lease mechanism. Concurrent writers can silently lose updates.
3. **Non-atomic WorkingMemory checkpoints**: `wm.json`/`plan.json` are written via plain `json.dump`, not the tempfile+rename pattern used correctly elsewhere (`FileStorageEngine.set`). A crash mid-write can corrupt the checkpoint and crash the *next* load attempt.
4. **Audit log has no tamper-evidence**: append-only JSONL, no hash chaining, no signature, no fsync call in the writer.
5. **Lifecycle bypass on first proposal**: the RAW→...→ACTIVE transition table in `_validate_note` is only checked when an existing note is being updated; a brand-new `propose()` call can set `lifecycle: "ACTIVE"` directly.
6. **Direct storage access from RecallEngine**: `RecallEngine.recall` iterates `self.controller.storage.id_to_path.keys()` directly, bypassing `MemoryController`'s authorization layer for that code path.
7. **Version algebra inconsistency**: `Deduplicator` compares `VersionRange` values with structural `!=` (exact equality); `RecallEngine` uses `VersionRange.matches()` (compatibility-aware). The same two version values can be judged "different" by one subsystem and "compatible" by the other.
8. **Incomplete temporal semantics**: `valid_from`/`valid_until` are compared only against `datetime.now()`. There is no way to query "what was true as of date X" or "what did the system believe as of date X" as distinct questions. `provenance.extraction_date` exists in the schema but is never read by any runtime logic.
9. **"Restart verification" is not proven multi-process**: the only restart-labeled test (`test_continuity.py`) re-instantiates `Executive` twice in the same Python process, not via `subprocess`/`sys.executable`. A separate multi-process script referenced in the handoff is not present in the tracked repository files and cannot be independently audited.
10. **Autonomous maintenance modules exist but are not invoked**: `Executive._run_maintenance` (which calls `Consolidator`, `Deduplicator`, `LearningEngine`) is defined but never called from `process_intent` or `step_loop`. These modules are unit-testable but not part of the live autonomous loop.
11. **`GitIntegration` class is fully implemented but has no caller** anywhere in the reviewed codebase.
12. **`Continuity_Handoff.md` is not a real continuity subsystem**: it borrows the canonical-note schema (including `verification: verified`), which is a category error — a session-boundary event is being represented as mutable, human-verified memory.
13. **AG-CONT-01 does not exist as executable architecture** — no code implements programmatic handoff generation.

## 3. Formal domain model (design proposal, not implemented)

The current schema flattens several distinct formal categories into one `CANONICAL_SCHEMA`. A more precise model separates:

- **MEMORY** — durable, content-bearing, lifecycle-managed (roughly matches current schema's intent).
- **CLAIM** — an assertion with an issuer and evidence, not yet promoted to trusted MEMORY. Currently absent as a distinct concept; `hypothesis`/`lesson`/`experience` types are stored as full MEMORY records with no structural promotion gate.
- **EVENT** — immutable, timestamped occurrences (decisions, tool executions, supersessions, handoffs). Currently, decisions and handoffs are stored as mutable state rather than immutable events.
- **STATE** — ephemeral, reconstructible from event replay (working memory, task status). Correctly modeled as ephemeral today.
- **ATTESTATION** — a record of who verified what, when, and how. Does not exist today; `verification` is a bare mutable enum field instead.

### Proposed provenance state machine (design proposal)

```
CLAIMED --[requires HUMAN/ADMIN attestation]--> VERIFIED --[requires HUMAN/ADMIN or auto on contradiction]--> REVOKED
```

Key rule: `CLAIMED → VERIFIED` must require a separate `AttestationRecord` issued by `Principal.HUMAN` or `Principal.ADMIN`, never settable directly by `propose()`/`update()` regardless of calling principal. This is the structural fix for finding #1 above. **This has NOT been implemented in code as of this document's creation.**

### Proposed lifecycle FSM extension (design proposal)

Add `REVOKED` and `QUARANTINED` states to the existing `RAW/CLASSIFIED/NORMALIZED/REVIEW/VERIFIED/ACTIVE/SUPERSEDED/ARCHIVED` set. Require: no path reaches `ACTIVE` except through `VERIFIED`; no path reaches `VERIFIED` except through an `AttestationRecord` issued by `HUMAN`/`ADMIN`; `REVOKED` is terminal (a correction must be a new memory, never an in-place resurrection). **Current code violates the "no ACTIVE at creation" and "no VERIFIED by AI_AGENT" rules implied by this target model** — restated here as a tracked gap, not yet closed.

### Proposed version algebra (design proposal)

Replace ad-hoc `VersionRange(exact, prefix, unknown)` comparison logic, currently duplicated inconsistently between `Deduplicator` (structural `!=`) and `RecallEngine` (`.matches()`), with a single shared module exposing `EQUAL`, `CONTAINS`, `OVERLAPS`, `DISJOINT`, `COMPATIBLE` as the only comparison primitives, imported by every subsystem that reasons about versions. **Not yet implemented.**

### Proposed storage architecture (design proposal)

At current scale (single author, 3 commits, small vault), `FileStorageEngine` (Markdown+YAML files) is adequate. At larger scale (10,000+ memories, multiple concurrent agents), the linear-scan startup index build and the unconditional full-scan inside `RecallEngine`'s REVIEW-note inclusion loop become real bottlenecks, and the complete absence of locking becomes a correctness risk, not just a performance one. A proposed target: SQLite (WAL mode) as the transactional/query engine, with the current Markdown files regenerated as a human-readable, git-diffable export rather than the primary store. **This is a significant architectural change and has NOT been started.**

### Proposed multi-agent coordination (design proposal)

A lease table (`lease_id, agent_id, scope, acquired_at, ttl_seconds`) with atomic acquisition (`INSERT OR FAIL`), automatic expiry-based reclamation, and optimistic-concurrency version columns on mutable records. **Not yet implemented.** Until this exists, this project must be treated as **single-writer-at-a-time by convention only, with no code-level enforcement** — a second agent (or a second instance of the same agent) writing concurrently can silently overwrite the first agent's changes.

### Proposed Handoff Manifest (design proposal, replacing the current prose handoff as the source of truth)

```yaml
handoff_id: <uuid>
schema_version: "2.0"
agent_id: <string>
source_commit: <sha — must be an ancestor of, never equal to, this handoff's own eventual commit>
test_commit: <sha tests were actually run against>
test_result_digest: <hash of full test log>
audit_tail_digest: <hash-chain head at time of writing>
checkpoint_digest: <hash of most recent WorkingMemory checkpoint, if any>
pending_tasks: [...]
approval_state: PENDING | APPROVED | REJECTED
created_at: <timestamp>
published_at: <timestamp, set only by a separate, later commit>
```

Key resolution to the "handoff describes its own commit" paradox: **a handoff must only ever reference its PARENT commit as `source_commit`, never attempt to embed its own resulting commit hash.** Any successor agent must independently run `git rev-parse HEAD` and compare against `source_commit`, never trust a self-reported "current commit" field. **`Continuity_Handoff.md` in its current form does not follow this rule and is therefore structurally unable to avoid staleness — this is a design limitation of the format itself, not merely a forgotten update.**

## 4. Explicit non-claims

This document does **not** claim:

- That any of the findings in section 2 have been fixed.
- That any of the proposals in section 3 have been implemented.
- That any test suite was executed during this review session.
- That this document has been reviewed or approved by a human.
- That this document is itself free of errors — it is a single AI agent's analysis, unverified, and should be checked against the actual source code before being relied upon for any engineering decision.

## 5. Recommended next engineering task (proposal, not a commitment)

Of the thirteen findings in section 2, the highest-severity, lowest-blast-radius fix is **closing the authority/verification self-claim gap (#1)**: require that `provenance.source_type in {"official", "execution"}` and `verification == "verified"` can only be written by `Principal.HUMAN`/`Principal.ADMIN` in `MemoryController.propose()`/`update()`, with `AI_AGENT`-submitted values in those fields silently downgraded to `"claimed"`/`"unverified"` rather than rejected outright (to avoid breaking existing AI-authored proposal flows). This should be implemented with accompanying unit tests (including an explicit adversarial test: "AI_AGENT cannot create a human-verified memory") and integration tests against a real `FileStorageEngine`, then verified by actually running `python -m pytest -q` against the resulting commit — not merely by reading the new code. **This recommendation has not been acted upon in this document; it is a proposal for a future work session with test-execution capability.**

---

*End of Phase Omega review artifact. This file may be superseded by a future, more complete or corrected version — if so, that successor document should reference this one via a `relation: replaces` entry, not overwrite it in place.*

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[11 Templates and System Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
