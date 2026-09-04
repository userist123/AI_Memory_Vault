# Phase 10 - Internal MVP Validation and Operability Readiness

## Phase Objective
Validate the platform in production-like staging with internal modules and hybrid admin flow, prove KPI and SLO readiness, and close pre-MVP risks before ecosystem expansion. This phase includes the TypeORM persistence adapter as the reference shared-DataSource provider composed at the `RuntimeBuilder` boundary.

## Scope Boundaries
### In Scope
- Staging pilot execution with internal modules and admin shell plugin scenarios.
- KPI and SLO measurement against acceptance thresholds.
- Incident and exception capture with corrective action loop.
- Go or no-go decision package for next phase.
- Reference persistence adapter (`@prosto/platform-adapter-typeorm`) validation with shared DataSource lifecycle, migration lock coordination, and descriptor ownership enforcement.

### Out of Scope
- Full external module onboarding at scale.
- Public release marketing and partner rollout.
- Long-term multi-team support model changes.

## Prerequisites and Dependencies
- Phases 01 through 09 completed and passing on protected branches.
- Metric and gate definitions from `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`.
- Pre-MVP objective and acceptance references from `.context/03-work-plan/pre-mvp-audit-and-execution-plan.md`.
- Hybrid admin references:
  - `.context/02-architecture-design/adr/ADR-0009-admin-ui-hybrid-shell-plugin-model.md`
  - `.context/02-architecture-design/c4/01-system-context.md`

## Detailed Ordered Implementation Steps
1. Define internal pilot module set and lock tested versions for runtime modules and UI plugins.
2. Run repeated staging deployments in both `strict` and `best-effort` startup modes.
3. Run admin-shell pilot scenarios:
   - successful plugin discovery and render
   - compatibility rejection behavior
   - permission-filtered extension behavior
   - degraded shell mode under partial plugin failures
4. Collect KPI set:
   - strict startup success rate
   - startup duration p95 drift
   - diagnostics completeness
   - contract violation rate
   - admin plugin discovery success ratio
   - rejected plugin remediation lead time
5. Capture all incidents and policy exceptions with owner and due action.
6. Run root-cause analysis for each severity-high issue and patch controls.
7. Re-run pilot cycles until stability trend is acceptable across consecutive runs.
8. Produce pre-MVP gate report with explicit go or no-go decision.

## Code Examples
### Example: KPI report record
```yaml
pilot_window: 2026-q2-internal-mvp
kpi:
  strict_startup_success_rate: 99.7
  startup_p95_drift_percent: 11
  diagnostics_completeness_percent: 100
  contract_violation_rate_per_100_runs: 1.5
  admin_plugin_discovery_success_ratio: 0.94
decision: go
```

### Example: exception register entry
```yaml
id: EX-021
scope: admin-plugin-compatibility-policy
owner: admin-platform
reason: plugin-manifest-mismatch-during-pilot
expires_at: 2026-07-31
status: approved-with-mitigation
```

## Affected Modules or Files
### Existing files likely updated
- `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`
- `.context/03-work-plan/pre-mvp-audit-and-execution-plan.md`
- `docs/compatibility/compatibility-matrix.md`
- `docs/architecture/dependency-map.md`
- `packages/platform-sdk/README.md`
- `packages/platform-sdk/API_REPORT.md`
- `packages/platform-core/README.md`
- `AGENTS.md`

### New files expected
- `docs/operations/internal-mvp-gate-report.md`
- `docs/operations/incident-register.md`
- `docs/operations/policy-exception-register.md`
- `docs/operations/admin-plugin-readiness-report.md`
- `packages/platform-adapters/platform-adapter-typeorm/README.md`
- `docs/persistence/typeorm-dialect-support.md`
- `docs/persistence/typeorm-shared-datasource-guide.md`
- `examples/typeorm-shared-datasource/*`

## Validation and Testing Approach
- Repeatability checks across consecutive staging cycles.
- Statistical validation of KPI trend, not single-run snapshots.
- Verification that all exceptions have TTL and mitigation plan.
- Formal gate review with architecture, security, runtime, and admin-platform owners.

## Data or Migration Impact
- No schema migration requirement.
- Operational data accumulation for diagnostics and reliability trend baselines.

## Risks and Mitigations
- Risk: staged environment does not represent production behavior.
  - Mitigation: production-like config parity checks and controlled load profile.
- Risk: KPI pass hides unresolved medium-severity admin integration drift.
  - Mitigation: require dedicated admin plugin readiness report even for go decision.

## Rollback Approach
- If gate fails, stay on hardening cycle and block ecosystem expansion.
- Roll back to previous known-good module and plugin set for staging baseline.
- Reopen unresolved risks in active backlog with explicit owners.

## Completion Criteria
- Internal MVP gate criteria are met or formal exception is approved.
- Reliability and diagnostics trend is stable over consecutive pilot cycles.
- Admin plugin discovery and degraded-mode behavior are stable and auditable.
- Incident and exception registers are complete with evidence links.
- Formal go or no-go decision is documented with evidence links.
