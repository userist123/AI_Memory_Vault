# CODEX R001 — C12 Temporal Validity + Provenance-Aware Retrieval

ROLE: CODEX implementation/runtime agent.

OBJECTIVE
Establish whether temporal validity, supersession and provenance are correctly preserved through retrieval and ranking, and fix only proven defects.

SCOPE
- Trace timestamps, supersession links, source provenance and lifecycle from stored memory into retrieved context and final ranking.
- Create fixtures with old/new conflicting facts, explicit supersession and multiple provenance levels.
- Verify stale/superseded content cannot outrank valid newer evidence merely because of lexical overlap.
- Verify provenance metadata survives context packing and ranking.
- Add deterministic regression tests before any ranking change.

REQUIRED EVIDENCE
- temporal/provenance test matrix
- exact test stdout/stderr
- before/after ranking traces for affected cases
- remote commit SHA
- explicit cases where the engine abstains or cannot decide

ACCEPTANCE
No hand-tuned score changes without a reproducible defect. Preserve lifecycle and auditability. Do not silently rewrite historical evidence.
