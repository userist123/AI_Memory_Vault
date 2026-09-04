# CODEX R001 — C13 Calibration + Selective Retrieval

ROLE: CODEX implementation/runtime agent.

OBJECTIVE
Measure whether retrieval confidence/relevance scores are calibrated well enough for selective retrieval and abstention decisions.

SCOPE
- Inspect current confidence, relevance and authority signals; do not conflate them.
- Build a held-out evaluation separating retrieval relevance from answer correctness where the existing harness permits.
- Measure score distributions, precision/coverage trade-offs and abstention behavior.
- Identify threshold brittleness and any deterministic calibration bug.
- Implement only proven defects or safe instrumentation; do not tune thresholds solely for benchmark gains.

REQUIRED EVIDENCE
- evaluation protocol and dataset identity
- score distribution/coverage metrics
- exact run output
- regression tests for any fix
- remote commit SHA

ACCEPTANCE
Report calibration limitations explicitly. No fabricated probabilities. No benchmark mutation to obtain desired coverage.
