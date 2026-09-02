# Pre-MVP Audit and Execution Plan

## Objective

Execute a controlled internal MVP that validates core platform value and quality gates before external ecosystem scaling.

## Scope for Pre-MVP Window

- SDK contract baseline
- Core runtime baseline
- Contract and architecture quality gates
- Security loading controls baseline
- Startup diagnostics and reliability baseline
- Persistence adapter contracts in `@prosto/platform-sdk` (IPersistenceProvider, IPersistenceDescriptor, IPersistenceModuleContext)

## Phase Plan

### Phase A Foundation Controls

Actions:
1. Enable mandatory CI checks for architecture boundaries, contracts, and type safety.
2. Standardize test runner and minimum required test suites.
3. Establish branch protection with non-bypassable required checks.

Deliverables:
- CI policy manifest
- quality gate matrix
- branch protection policy record

### Phase B Contract-First Implementation

Actions:
1. Implement minimal SDK contracts and manifest validation schema.
2. Implement reusable contract test package.
3. Validate with two internal reference modules.

Deliverables:
- `@prosto/platform-sdk` baseline
- `@prosto/platform-contract-tests` baseline
- compatibility and migration policy draft

### Phase C Runtime Baseline

Actions:
1. Implement minimal core runtime with deterministic lifecycle.
2. Implement startup policy modes strict and best-effort.
3. Implement startup report payload with failure reason taxonomy.

Deliverables:
- `@prosto/platform-core` baseline
- lifecycle integration tests
- startup diagnostics schema and examples

### Phase D Security and Performance Hardening

Actions:
1. Implement allowlist and artifact integrity checks in startup path.
2. Add secret redaction tests for diagnostics and logs.
3. Add startup and event-dispatch benchmark gates.

Deliverables:
- security policy checks
- redaction validation suite
- benchmark baseline and thresholds

### Phase E Internal MVP Validation

Actions:
1. Run staging pilots with internal modules.
2. Measure KPI set and compare to thresholds.
3. Collect incidents and policy exceptions, then execute corrective actions.

Deliverables:
- KPI report
- incident and exceptions register
- transition recommendation for ecosystem stage

Phase 10 closure evidence:
- `docs/operations/internal-mvp-gate-report.md`
- `docs/operations/incident-register.md`
- `docs/operations/policy-exception-register.md`
- `docs/operations/admin-plugin-readiness-report.md`

## Impact and Effort Backlog for Pre-MVP

| Item | Impact | Effort | Priority |
|---|---|---|---|
| CI architecture gates | 5 | 2 | P1 |
| Test standardization | 5 | 2 | P1 |
| SDK plus contract-tests | 5 | 3 | P1 |
| Runtime lifecycle baseline | 5 | 3 | P1 |
| Startup diagnostics contract | 4 | 3 | P2 |
| Security loading controls | 5 | 3 | P1 |
| Performance budget gates | 4 | 3 | P2 |

## Pre-MVP Acceptance Gate

All conditions below must be true:

1. Contract tests pass for internal reference modules.
2. Strict startup success rate meets target baseline.
3. Architecture boundary checks report zero violations on protected branches.
4. Allowlist and integrity checks are enforced in staging pipeline.
5. Startup diagnostics completeness is 100 percent for required fields.

Phase 10 result: all conditions are met with a formal `go` decision documented in `docs/operations/internal-mvp-gate-report.md`.

## Principal Risks During Pre-MVP and Mitigations

| Risk | Mitigation |
|---|---|
| Governance bypass for speed | Protected branches plus time-bound exception workflow |
| Contract churn destabilizes modules | Semver discipline and migration templates |
| Missing operational signal during pilot | Mandatory diagnostics schema and dashboard checklist |
| Security controls reduce developer velocity | Progressive enforcement with clear developer guidance |

## Exit Decision

At pre-MVP end, choose one path:
1. Proceed to ecosystem expansion readiness if all gate criteria are met.
2. Continue hardening cycle if any critical reliability, security, or contract gate fails.

Current Phase 10 decision: proceed to ecosystem expansion readiness. No critical or high-severity incident remains open, no policy exception is expired or open-ended, and admin plugin readiness is documented as `go`.
