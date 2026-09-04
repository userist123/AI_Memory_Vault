# CODEX R001 — C14 Outcome → Evidence → Memory Learning Loop

ROLE: CODEX implementation/runtime agent.

OBJECTIVE
Forensically map and test the real learning loop from OUTCOME telemetry through EVIDENCE and onward to any memory mutation or future retrieval influence.

SCOPE
- Trace existing `label_council_outcome.py` and all consumers.
- Determine whether outcomes can create evidence and whether evidence can safely affect candidate memory without bypassing lifecycle/authority gates.
- Build a controlled fixture proving one complete successful path and one rejected/unsafe path.
- Check whether future ranking/retrieval actually consumes learned evidence; do not infer this from telemetry alone.
- Implement only proven missing plumbing that is consistent with current lifecycle policy, or leave an explicit gap report.

REQUIRED EVIDENCE
- end-to-end trace with IDs/hashes
- exact test/run output
- lifecycle transitions and authorization decisions
- regression tests for any implementation change
- remote commit SHA

ACCEPTANCE
No automatic REVIEW->ACTIVE promotion. No claim that an outcome was caused by memory without controlled evidence. Preserve append-only provenance and auditability.
