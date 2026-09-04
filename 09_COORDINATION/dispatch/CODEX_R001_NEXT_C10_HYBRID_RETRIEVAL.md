# CODEX R001 — C10 Hybrid Retrieval / Candidate Generation

ROLE: CODEX implementation/runtime agent.

BASE: verify current `main` SHA before work; do not assume an older baseline.

OBJECTIVE
Prove and, only where justified, implement a production-safe hybrid retrieval candidate-generation path. The current known limitation is that the default controller retrieval is lexical/deterministic and the optional semantic path is not necessarily wired into `MemoryController.search()`.

SCOPE
- Inspect the actual production retrieval path from `MemoryController.search()` through storage/candidate generation/ranking.
- Determine exactly where lexical, semantic and hybrid retrieval can participate.
- Do not replace the deterministic fallback merely to improve benchmark numbers.
- Implement the smallest defensible change that exposes hybrid candidate generation in a controlled, backwards-compatible way, or document why implementation is not yet safe.
- Preserve lifecycle, provenance, authority and security boundaries.

REQUIRED EVIDENCE
- code-level path map
- before/after behavior on held-out cases
- regression tests
- exact pytest stdout/stderr
- commit SHA and remote branch SHA
- explicit limitations and failure cases

ACCEPTANCE
No claim of semantic improvement without a real provider run. No benchmark redesign. No REVIEW->ACTIVE promotion. Security must not be weakened.
