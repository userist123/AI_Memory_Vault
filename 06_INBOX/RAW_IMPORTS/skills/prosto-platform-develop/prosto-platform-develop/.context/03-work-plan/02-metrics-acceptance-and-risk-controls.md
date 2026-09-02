# 02 Metrics, Acceptance Criteria, and Risk Controls

## 1. Success Metrics Framework

## 1.1 Product and Delivery Metrics

| Metric | Definition | Stage 1 Target Internal MVP | Stage 2 Target Ecosystem Expansion | Owner |
|---|---|---|---|---|
| Time to First Module | Time from clean repo to first loaded valid module | <= 1 working day | <= 4 hours with template | Platform DevEx |
| Module Integration Lead Time | Time from module PR merge to successful integration in staging | <= 2 working days | <= 1 working day | Platform Core |
| Contract Violation Rate | Failed contract checks per 100 module CI runs | <= 5 | <= 2 | QA and Core |
| Release Readiness Pass Rate | Releases passing all required gates on first run | >= 80 percent | >= 90 percent | Release Manager |

## 1.2 Reliability and Operability Metrics

| Metric | Definition | Target | Alert Threshold | Owner |
|---|---|---|---|---|
| Strict Startup Success Rate | Successful strict boot attempts over rolling window | >= 99.5 percent | < 99.0 percent | Core Runtime |
| Startup Duration p95 | Bootstrap time for reference module set | baseline plus <= 15 percent drift | > 20 percent drift | Core Runtime |
| Critical Module Runtime Failure | Critical module failures after successful startup | 0 | >= 1 event | Platform Operations |
| Startup Diagnostics Completeness | Required diagnostic fields present per startup report | 100 percent | < 100 percent | Observability Owner |

## 1.3 Security and Ecosystem Metrics

| Metric | Definition | Target | Alert Threshold | Owner |
|---|---|---|---|---|
| Allowlist Compliance | Production modules with approved allowlist status | 100 percent | < 100 percent | Security Team |
| Integrity Verification Coverage | Modules with verified checksum signature | 100 percent in production | < 100 percent | Security Team |
| Critical Vulnerability Backlog | Open critical vulnerabilities above SLA | 0 | >= 1 | Security Team |
| External Module Onboarding Success | External modules passing first governance gate | >= 70 percent | < 50 percent | DevRel and Core |

## 1.4 Internal MVP Phase 10 KPI Closure

| KPI | Phase 10 Target | Phase 10 Observed Trend | Status | Evidence |
|---|---:|---:|---|---|
| Strict Startup Success Rate | >= 99.5 percent | 99.9 percent | pass | `docs/operations/internal-mvp-gate-report.md` |
| Startup Duration p95 Drift | <= 15 percent | 11.0 percent | pass | `docs/performance/regression-budgets.md` |
| Diagnostics Completeness | 100 percent | 100 percent | pass | `npm run validate:runtime-policy` |
| Contract Violation Rate | <= 5 per 100 runs | 1.0 per 100 runs | pass | `npm run test:contracts` |
| Admin Plugin Discovery Success Ratio | >= 0.90 | 0.96 | pass | `docs/operations/admin-plugin-readiness-report.md` |
| Rejected Plugin Remediation Lead Time | <= 2 business days | 1 business day | pass | `docs/operations/policy-exception-register.md` |

## 2. Acceptance Criteria by Horizon

## 2.1 Quick Wins Acceptance

1. CI pipeline blocks merge on boundary, contract, and policy violations.
2. One standardized test runner and baseline suite are enforced in all platform packages.
3. Root dependency scope is aligned with package responsibilities and documented.
4. Internal MVP KPI dashboard contract is published and reviewed.
5. Release readiness checklist is executable as a required gate artifact.

## 2.2 Mid-Term Acceptance

1. SDK contract package is implemented and versioned.
2. Contract test package is implemented and reusable by external module repositories.
3. Lifecycle policy engine executes strict and best-effort rules deterministically.
4. Startup report and telemetry schema are versioned contracts with validation.
5. Module template repository exists with CI blueprint and governance checks.

## 2.3 Strategic Acceptance

1. Internal-to-ecosystem stage gate model is approved and used in planning.
2. Third-party security maturity model is applied to onboarding workflow.
3. Error budget policy actively influences release and change decisions.
4. Architecture compliance checks are reusable across all platform and module repos.

## 3. Implementation Risk Map and Mitigations

## 3.1 Risk Register for Proposed Changes

| Risk ID | Risk | Probability | Impact | Priority | Mitigation |
|---|---|---|---|---|---|
| R-01 | Teams bypass CI policy gates for urgent delivery | Medium | High | High | Protected branches plus exception process with expiry and owner |
| R-02 | Contract package evolves too fast for module maintainers | Medium | High | High | Semver discipline plus migration guides and deprecation windows |
| R-03 | Performance gates produce noisy false positives | Medium | Medium | Medium | Warmup calibration and rolling baseline windows |
| R-04 | Security controls slow ecosystem onboarding | Medium | Medium | Medium | Progressive trust levels and fast-track path for reviewed maintainers |
| R-05 | Observability schema changes break downstream tooling | Low | High | Medium | Versioned schema and compatibility adapters |
| R-06 | Documentation and implementation drift reappears | Medium | High | High | Docs-as-code checks linked to pipeline evidence |

## 3.2 Risk Reduction Controls

1. Every critical gate has named owner and backup owner.
2. Every exception has scope, reason, expiration, and postmortem action.
3. Every strategic change has pilot phase before full rollout.
4. Every policy check is validated on reference repositories before enforcement expansion.

## 4. Evidence Required for Go No-Go

## Internal MVP Gate
- SDK contract package available and consumed by at least two internal modules.
- Contract tests and architecture checks pass on protected branches.
- Startup diagnostics and reliability metrics are observable and stable.
- Phase 10 internal MVP evidence package records a `go` decision with linked incident, exception, compatibility, and admin readiness evidence.

## Ecosystem Expansion Gate
- Module template repository and onboarding guide are production-ready.
- Third-party security workflow is operational with signed artifact checks.
- Compatibility matrix and policy checks are reusable outside monorepo.
- KPI trend confirms healthy internal platform loop before external scale-out.
