# CODEX R001 — Parallel Batch C9–C14

Execute these work fronts independently where repository state permits. Before each lane, re-check current `main` and avoid overwriting unrelated work. Each lane must produce its own evidence artifact and commit.

- C9 — production graph integration
- C10 — hybrid retrieval / candidate generation
- C11 — memory poisoning / trust boundary
- C12 — temporal validity + provenance-aware retrieval
- C13 — calibration + selective retrieval
- C14 — outcome → evidence → memory learning loop

Rules:
- Real execution only; never fabricate test output.
- Every implementation change requires regression evidence.
- No REVIEW→ACTIVE promotion.
- No benchmark definition changes.
- No security bypasses or Defender exclusions.
- If a lane is not safely implementable, produce a gap/forensics artifact instead of speculative code.
- Report exact base SHA, resulting commit SHA, tests, and remaining limitations per lane.
