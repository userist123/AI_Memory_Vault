# Valul 1 Execution Directive

## Scope
Execute only Valul 1 until the full regression barrier is green. Do not start WP-5, WP-6, WP-7, or Audit A-E finalization before the barrier.

## WP-1 — lifecycle
Verify and implement the lifecycle decision already established in the security/retrieval architecture. Do not weaken public `ACTIVE + verified` read/search ceilings. Add regression coverage for any canonical lifecycle decision that is still only documented.

## WP-2 — retrieval cabling
Verify the production retrieval call path at the current exact head and ensure every newly built retrieval layer has a real production consumer. Any module on the retrieval path with zero production call-sites is not considered integrated.

## WP-3 — corpus
Verify corpus integrity against the current baseline. Preserve `edges_resolvable` and `duplicate_rate` as explicit measured metrics. Do not silently update baselines to make tests pass. Record regressions and causes.

## WP-4 — pandas / indicator-series defect
Find and fix the actual off-by-one defect in construction of an indicator series. This is a code defect, not test drift.
Requirements:
- identify the exact producer and the incorrect boundary/index arithmetic;
- fix production code only as needed;
- add a regression test asserting the expected series length;
- the existing 11 relevant tests plus the new regression test must pass;
- preserve the rest of the behavior and avoid changing expected lengths in tests merely to obtain green results.

## Global production-consumer rule
Before constructing a new layer over a component, verify who consumes that component in the production path:

    grep -rl "<module>" --include='*.py' . | grep -v "/tests/\|test_\|benchmarks"

If the result is empty, the component is not integrated. Do not build another layer over it. Cable it into production first, or work on another front.

This rule applies to every front and must be added to both `AGENTS.md` and `CLAUDE.md` on this branch, preserving existing guidance.

## Barrier
Run and record the complete regression suite, including `20_TESTS`. Valul 2 remains blocked until the full suite is green and the WP-4 11+1 acceptance is green.

## Hard constraints
- No changes to `PROJECT_BRAIN/PROJECT_STATE.md`.
- No changes to `main`.
- No force-push/history rewrite.
- No invented CI/test evidence.
- Do not weaken security ceilings to satisfy compatibility.
- Do not start Valul 2 until the Valul 1 barrier is actually green.

## Deliverables
1. Narrow production fix + regression test for WP-4.
2. Valul 1 evidence covering WP-1/WP-2/WP-3/WP-4.
3. Exact commands and observed test results.
4. Exact current branch head SHA.
5. Explicit statement whether the Valul 2 barrier is open or blocked.


## 🔗 Legături Sinaptice
- [[Governance_Repository_Spine_Specification|Governance]]
- [[00 Core Map]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
