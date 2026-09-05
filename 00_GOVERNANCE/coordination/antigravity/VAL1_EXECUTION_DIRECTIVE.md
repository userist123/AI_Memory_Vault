# Valul 1 Execution Directive

## Scope
Execute only Valul 1 until the full regression barrier is green. Do not start WP-5, WP-6, WP-7, or Audit A-E finalization before the barrier.

## WP-1 — lifecycle
Verify and implement the lifecycle decision already established in the security/retrieval architecture. Do not weaken public `ACTIVE + verified` read/search ceilings. Add regression coverage for any canonical lifecycle decision that is still only documented.

## WP-2 — retrieval cabling
Verify the production retrieval call path at the current exact head and ensure every newly built retrieval layer has a real production consumer. Any module on the retrieval path with zero production call-sites is not considered integrated.

## WP-3 — corpus
Verify corpus integrity against the current baseline. Preserve `edges_resolvable` and `duplicate_rate` as explicit measured metrics. Do not silently update baselines to make tests pass. Record regressions and causes.

## Global production-consumer rule
Before constructing a new layer over a component, verify who consumes that component in the production path:

    grep -rl "<module>" --include='*.py' . | grep -v "/tests/\|test_\|benchmarks"

If the result is empty, the component is not integrated. Do not build another layer over it. Cable it into production first, or work on another front.

This rule applies to every front and must be added to both `AGENTS.md` and `CLAUDE.md` on this branch, preserving existing guidance.

## Barrier
Run and record the complete regression suite, including `20_TESTS`. Valul 2 remains blocked until the full suite is green.

## Hard constraints
- No changes to `PROJECT_BRAIN/PROJECT_STATE.md`.
- No changes to `main`.
- No force-push/history rewrite.
- No invented CI/test evidence.
- Do not weaken security ceilings to satisfy compatibility.
- Do not start Valul 2 until the Valul 1 barrier is actually green.

## Deliverables
1. Valul 1 evidence covering WP-1/WP-2/WP-3.
2. Exact commands and observed test results.
3. Exact current branch head SHA.
4. Explicit statement whether the Valul 2 barrier is open or blocked.

## Ownership note
The indicator-series off-by-one defect and its regression test are explicitly removed from Antigravity's Valul 1 scope. They are handled independently by the security/analysis workstream.
