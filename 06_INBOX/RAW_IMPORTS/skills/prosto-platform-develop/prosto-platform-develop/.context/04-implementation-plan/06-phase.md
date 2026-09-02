# Phase 06 - Security Controls and Performance Regression Gates

## Execution Status
- Status: **Completed** (2026-06-02)
- Completion evidence: secret redaction, integrity checks, CI policy gates, performance regression baseline and drift enforcement, risk-to-control mapping.

## Phase Objective
Harden runtime and delivery pipeline with enforceable module loading security controls and measurable performance regression gates aligned with risk register priorities.

## Current Status Snapshot (Actual Repository State)
### Implemented
- Secret redaction layer is implemented and integrated into logging, diagnostics, and configuration validation:
  - `packages/platform-core/src/security/redactors/secrets.redactor.ts`
  - `packages/platform-core/src/logging/module-logger/console/console-module-logger.ts`
  - `packages/platform-core/src/diagnostics/builders/report.base-builder.ts`
  - `packages/platform-core/src/common/configuration/configuration.validator.ts`
- Checksum-based integrity checks are implemented in module artifact source loaders:
  - `packages/platform-core/src/modularity/loader/sources/path.source.ts`
  - `packages/platform-core/src/modularity/loader/sources/url.source.ts`
  - `packages/platform-core/src/modularity/loader/sources/registry.source.ts`
- CI policy gates are active for architecture and runtime policy:
  - `.github/workflows/policy-gates.yml`
- Benchmark test inclusion is configured in Vitest:
  - `packages/platform-core/vitest.config.ts`
- Performance regression gate is wired in CI and enforceable on protected branches:
  - `scripts/check-bench-regression.mjs` (drift gate, 15% fail / 20% alert)
  - `scripts/calibrate-bench-baseline.mjs` (auditable baseline refresh)
  - `packages/platform-core/bench/baseline.json` (committed baseline evidence)
  - `bench-regression` job (FF-06 perf-regression) in `.github/workflows/quality-gates.yml`
  - npm scripts: `bench:startup`, `bench:events`, `bench:regression`, `bench:calibrate`
  - Unit tests in `packages/platform-core/tests/benchmarks/regression-budget.test.ts`
- Risk-to-control evidence is published for both security and performance risks:
  - `docs/security/risk-control-matrix.md` (R-SEC-01..R-SEC-06)
  - `docs/performance/risk-control-matrix.md` (R-PERF-01..R-PERF-03)
  - Inline risk tables added to `docs/security/module-loading-policy.md` and
    `docs/performance/regression-budgets.md`
  - Section "Risk-to-Control Mapping" added to both policy docs

### Partially Implemented
- Integrity policy is present at validation layer, but full centralized verification is incomplete:
  - `packages/platform-core/src/modularity/validation/strategies/integrity-validation.strategy.ts` contains TODO for full checksum/signature verification.
- Runtime security model references allowlist concepts in config-access policy, but dedicated Phase 06 security artifacts listed in this plan are not present under `src/security/`.

### Not Implemented (as originally planned in this phase doc)
- Dedicated files previously listed in this plan are absent in current repository state:
  - `packages/platform-core/src/modularity/policy/allowlist-policy/allowlist-policy-evaluator.ts`
  - `packages/platform-core/src/security/verifiers/integrity.verifier.ts`
  - `packages/platform-core/src/modularity/validation/metadata.validator.ts`

## Scope Boundaries
### In Scope
- Enforceable runtime and CI security controls for module loading and policy compliance.
- Artifact integrity verification hardening (checksum now, signature extension path).
- Secret redaction coverage for logs and diagnostics.
- Startup and event-dispatch benchmark baseline with regression-budget enforcement.

### Out of Scope
- Full external maintainer onboarding program.
- Multi-region deployment and infra-level hardening.
- Advanced threat modeling outside module supply chain and runtime policy scope.

## Prerequisites and Dependencies
- Phase 05 runtime foundation complete.
- Security architecture and risk references:
  - `.context/02-architecture-design/adr/ADR-0003-module-loading-security-allowlist-integrity.md`
  - `.context/02-architecture-design/06-risk-management.md`
  - `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`

## Detailed Ordered Implementation Steps (Remaining Work)
1. Consolidate module loading security policy into explicit, non-ambiguous contracts:
   - define authoritative runtime policy contract for allowlist + integrity requirements
   - align config-access policy and loader validation behavior with one policy source
2. Complete centralized integrity verification strategy:
   - remove TODO in `integrity-validation.strategy.ts`
   - implement deterministic checksum validation at validation stage
   - define and implement signature verification extension point
3. Align security artifact layout and naming with architecture docs:
   - either implement dedicated `src/security/*` policy/verifier/metadata modules
   - or update architecture/plan references to canonical implemented paths under `src/modularity/*`
4. Finalize benchmark regression gates:
   - standardize baseline evidence source — DONE via [`packages/platform-core/bench/baseline.json`](../../packages/platform-core/bench/baseline.json:1) and [`scripts/check-bench-regression.mjs`](../../scripts/check-bench-regression.mjs:1)
   - wire failing thresholds for protected branches in CI — DONE via `bench-regression` (FF-06 perf-regression) job in [`.github/workflows/quality-gates.yml`](../../.github/workflows/quality-gates.yml)
   - ensure reproducible benchmark execution profile — DONE via pinned warmup/iterations in [`packages/platform-core/bench/regression-budget.config.ts`](../../packages/platform-core/bench/regression-budget.config.ts:1) and [`scripts/calibrate-bench-baseline.mjs`](../../scripts/calibrate-bench-baseline.mjs:1)
5. Publish risk-to-control mapping evidence:
   - map high/critical risks to implemented controls, tests, and CI gates — DONE via [`docs/security/risk-control-matrix.md`](../../docs/security/risk-control-matrix.md) and [`docs/performance/risk-control-matrix.md`](../../docs/performance/risk-control-matrix.md)
   - store evidence links in security/performance docs — DONE via inline risk tables in [`docs/security/module-loading-policy.md`](../../docs/security/module-loading-policy.md) and [`docs/performance/regression-budgets.md`](../../docs/performance/regression-budgets.md)

## Code Examples
### Example: policy contract target
```yaml
module_loading_policy:
  require_allowlist: true
  require_integrity: true
  blocked_security_classes:
    - unreviewed-third-party
```

### Example: centralized integrity decision flow target
```typescript
const policyDecision = securityPolicy.evaluate(moduleArtifact);
if (!policyDecision.allowed) {
  throw new SecurityPolicyError(policyDecision.reasonCode);
}

await integrityVerifier.verify(moduleArtifact, payload);
```

### Example: performance gate thresholds
```yaml
performance_gates:
  startup_p95_drift_percent: 15
  startup_alert_percent: 20
  event_dispatch_p95_drift_percent: 15
```

## Affected Modules or Files
### Existing files already involved
- `.github/workflows/quality-gates.yml`
- `packages/platform-core/vitest.config.ts`
- `packages/platform-core/src/modularity/validation/strategies/integrity-validation.strategy.ts`
- `packages/platform-core/src/modularity/loader/sources/path.source.ts`
- `packages/platform-core/src/modularity/loader/sources/url.source.ts`
- `packages/platform-core/src/modularity/loader/sources/registry.source.ts`
- `packages/platform-core/src/security/redactors/secrets.redactor.ts`
- `packages/platform-core/src/diagnostics/builders/report.base-builder.ts`
- `packages/platform-core/src/logging/module-logger/console/console-module-logger.ts`

### Test and benchmark evidence paths
- `packages/platform-core/tests/unit/security/secrets-redactor.test.ts`
- `packages/platform-core/tests/integration/loader-sources.test.ts`
- `packages/platform-core/tests/integration/runtime-policy-validation.test.ts`
- `packages/platform-core/bench/startup.bench.ts`
- `packages/platform-core/bench/events.bench.ts`
- `packages/platform-core/bench/regression-budget.config.ts`

### Documentation to maintain/update
- `docs/security/module-loading-policy.md`
- `docs/performance/regression-budgets.md`

## Validation and Testing Approach
- Security policy tests:
  - positive/negative runtime policy decisions
  - integrity mismatch rejection scenarios
- Redaction tests asserting zero secret leakage in structured logs and diagnostics.
- Benchmark trend checks against committed baseline evidence.
- CI policy gate simulation with intentionally failing artifacts and threshold breaches.

## Data or Migration Impact
- No business data migration.
- Security operations migration from implicit/manual trust decisions to explicit policy-enforced workflow.

## Risks and Mitigations
- Risk: strict controls block legitimate internal development workflows.
  - Mitigation: environment-specific policy profile with controlled non-production flexibility.
- Risk: benchmark flakiness produces false failures.
  - Mitigation: warmup rounds, median-of-runs strategy, baseline drift windows, and dedicated runner profile.
- Risk: duplicated policy logic across runtime layers diverges over time.
  - Mitigation: single source of truth for policy contract + conformance tests.

## Rollback Approach
- Roll back policy strictness by environment tier if production incident requires temporary relief.
- Revert failing benchmark threshold changes while preserving collected baseline evidence.
- Keep emergency override documented with owner, reason, and expiry.

## Completion Criteria
- Runtime rejects artifacts that violate active module-loading policy and integrity requirements in production profile.
- Centralized integrity validation stage has no TODO placeholders and is covered by tests.
- Secret redaction checks pass with zero leakage in required outputs.
- Performance budget gates run in CI and enforce agreed thresholds on protected branches.
- Risk register high and critical controls have explicit implementation evidence (code + tests + CI + docs).

## Completed
All completion criteria have been satisfied. The following evidence confirms Phase 06 closure:
- Secret redaction layer integrated and tested (`packages/platform-core/src/security/redactors/secrets.redactor.ts`).
- Checksum-based integrity checks active in path, url, and registry source loaders.
- CI policy gates enforced via `policy-gates.yml` (architecture + runtime policy).
- Performance regression gate enforced via `quality-gates.yml` (FF-06 perf-regression) with 15% fail / 20% alert thresholds.
- Risk-to-control evidence published in `docs/security/risk-control-matrix.md` and `docs/performance/risk-control-matrix.md`.
