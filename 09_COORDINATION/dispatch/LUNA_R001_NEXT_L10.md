# LUNA R001 — NEXT TASK L10

## Mission
Perform the next independent adversarial verification pass against the **current main**, using outputs from C8/A8/P8 only as claims to challenge, never as proof.

## Start from
Resolve and record the exact `origin/main` SHA at the beginning. This SHA is the only baseline for this pass.

## Primary targets
1. C8 held-out retrieval results: reproduce the benchmark and inspect corpus leakage, hard-negative quality, denominator correctness, abstention behavior, and score/rank causality.
2. A8 production graph differential: verify whether graph activation is genuinely called for SQLite/File storage and whether any observed rank change is attributable to graph propagation rather than candidate-set differences.
3. CODEX edge-weight repair: independently inspect and test positive/zero/negative/bounded weights; ensure no unintended score dominance.
4. Lifecycle trap GAP-011: verify `verified` vs `unverified` classification behavior and ensure REVIEW is not accidentally excluded.
5. Production fallback: distinguish successful graph execution from silent fallback.
6. Provenance and REVIEW authority containment.

## Adversarial methods
- Construct counterexamples and minimal reproductions.
- Re-run a subset of claims with a fixed corpus under controlled conditions.
- Where runtime is unavailable, mark `BLOCKED` or `UNVERIFIED`; do not substitute static inference.
- Look specifically for benchmark leakage, cherry-picked examples, synthetic seeds, hidden defaults, swallowed exceptions, and reporting denominators.

## Evidence required
Create:
`07_EVALUATION/luna/L10_CURRENT_MAIN_ADVERSARIAL_REVIEW.md`

Include:
- BASE_MAIN_SHA
- claims inspected
- reproduction commands + raw output
- evidence classification per claim
- accepted/rejected/rework decisions
- newly discovered blockers
- whether each C8/A8 result survives independent reproduction

## Acceptance gate
Use only:
`ACCEPT`, `ACCEPT_WITH_GAPS`, `REWORK_REQUIRED`, `REJECT`, `BLOCKED`.

Do not modify production code in the LUNA lane. Do not modify CODEX/Antigravity/Perplexity artifacts. Do not approve your own prior findings merely because they are consistent.
