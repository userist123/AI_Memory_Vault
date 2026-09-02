# 06 Risk Management and Mitigation Strategies

Date: 2026-03-25
Status: Draft revised

## Purpose

This document defines architectural, technical, security, operational, ecosystem, and team risks for `prosto-platform`.

It standardizes:
- risk scoring
- ownership and escalation
- mitigation planning
- acceptance and review workflow

---

## Risk Categories

| Category | Description |
|---|---|
| Architecture | Design risks that can reduce evolvability or boundary integrity |
| Security | Risks from module supply chain, dependencies, and runtime controls |
| Technical | Risks tied to implementation complexity and platform limits |
| Operational | Runtime reliability, incident response, and observability risks |
| Ecosystem | Compatibility and module adoption risks |
| Team | Knowledge concentration and delivery resilience risks |

---

## Risk Scoring Model

### Formula

Risk score uses a 5 by 5 matrix.

```text
riskScore = probabilityScore * impactScore
```

### Probability Scale

| Score | Label | Definition |
|---|---|---|
| 1 | Rare | Very unlikely in current planning horizon |
| 2 | Unlikely | Possible but not expected |
| 3 | Possible | Could happen under realistic conditions |
| 4 | Likely | Expected in multiple scenarios |
| 5 | Almost Certain | Expected without active controls |

### Impact Scale

| Score | Label | Definition |
|---|---|---|
| 1 | Negligible | Local inconvenience |
| 2 | Minor | Local rework with limited external impact |
| 3 | Moderate | Significant delay or constrained release scope |
| 4 | Major | Customer visible impact or release block |
| 5 | Critical | Platform outage, breach, or severe integrity loss |

### Severity Bands

| Score Range | Severity | Required Action |
|---|---|---|
| 1 to 5 | Low | Monitor and review in regular cadence |
| 6 to 10 | Medium | Plan mitigation and track in backlog |
| 12 to 16 | High | Active mitigation with owner and gate checks |
| 20 to 25 | Critical | Immediate mitigation and release gating |

---

## Risk Register

### Architecture Risks

| ID | Risk | Probability | Impact | Score | Owner | Primary Mitigation | Closure Criteria |
|---|---|---|---|---|---|---|---|
| A01 | Kernel scope creep | 3 | 4 | 12 | Architecture Team | Enforce ADR-0001 boundaries with dependency checks | No forbidden dependencies in CI for 3 consecutive release cycles |
| A02 | Direct module coupling | 3 | 4 | 12 | Core Team | Block direct module imports via policy checks and contract tests | Import policy violations remain zero across all maintained modules |
| A03 | Adapter lock-in | 2 | 3 | 6 | Adapter Owners | Keep adapter abstractions and ban framework types in SDK contracts | Contract review confirms no framework type leakage |
| A04 | Circular dependencies across packages | 3 | 3 | 9 | Core Team | Automated graph and cycle checks in CI | Cycle detector reports zero cycles in protected branches |
| A05 | Premature over-engineering | 3 | 2 | 6 | Architecture Team | Stage gating and driver-based adoption policy | Stage features adopted only after gate evidence is documented |

### Security Risks

| ID | Risk | Probability | Impact | Score | Owner | Primary Mitigation | Closure Criteria |
|---|---|---|---|---|---|---|---|
| S01 | Malicious module in production set | 2 | 5 | 10 | Security Team | Allowlist approvals, integrity checks, signed artifacts | All production modules pass allowlist and integrity verification in CI and runtime |
| S02 | Dependency supply-chain compromise | 3 | 5 | 15 | Security Team | Lockfile discipline, vulnerability scanning, minimal dependency footprint | No open critical vulnerabilities in protected branches |
| S03 | Secret leakage in logs | 3 | 4 | 12 | Core Team | Redaction policy and structured logging validation | Redaction checks pass and leak tests show zero secret exposure |
| S04 | Isolation bypass in worker model | 2 | 4 | 8 | Core Team | Constrained worker policies and restricted capabilities | Security tests validate denied escape patterns |
| S05 | Unauthorized registry access | 3 | 4 | 12 | Core Team | Tokenized access model and policy validation | Access tests confirm unauthorized operations are denied |
| S06 | Forged, malformed, or mis-scoped OIDC JWT accepted as a delegated identity | 3 | 5 | 15 | Security Team | Exact issuer/audience/algorithm validation, bounded claims, HTTPS redirect-denying JWKS retrieval | Bearer and OIDC token validation tests reject invalid issuer, audience, algorithm, signature, temporal claims, and malformed identity claims |
| S07 | Application-held key-ring material or durable refresh secrets exposed or misused | 3 | 5 | 15 | Security Team | Deployment-injected versioned AES-GCM key ring, encrypted bounded secret storage, redacted failures | Key-ring and session tests show AAD-bound encryption, corruption rejection, and no raw secret persistence or logging |
| S08 | Unsafe key rotation or key compromise response leaves active sessions vulnerable | 2 | 5 | 10 | Security Team | Dual-key staged rotation, lazy re-encryption only for normal rotation, global session invalidation for compromise | Rotation tests cover old-key decrypt/re-encryption; incident runbook requires session invalidation and cookie-version increment |
| S09 | Browser session cookie theft, fixation, or cross-site request abuse | 3 | 5 | 15 | Security Team | Same-origin HTTPS-only `__Host-` cookies, HttpOnly/SameSite policy, one-time PKCE transaction and strict callback parsing | SDK cookie, session runtime, and host composition tests reject malformed cookies and enforce cookie/CSRF flow invariants |

### Technical Risks

| ID | Risk | Probability | Impact | Score | Owner | Primary Mitigation | Closure Criteria |
|---|---|---|---|---|---|---|---|
| T01 | Runtime performance degradation with module growth | 3 | 4 | 12 | Core Team | Benchmark gates, caching, controlled lazy init | Performance baseline meets target SLO across supported module sets |
| T02 | Memory leaks in long-lived runtime | 3 | 4 | 12 | Core Team | Profiling, leak tests, lifecycle cleanup enforcement | Leak tests stable and memory trend remains within approved baseline |
| T03 | TypeScript compatibility drift | 2 | 3 | 6 | Core Team | Shared tsconfig and compatibility validation | Version matrix and CI confirm cross-package compatibility |
| T04 | Monorepo build-time growth | 3 | 3 | 9 | DevEx Team | Incremental builds and affected-only workflows | Build duration remains under agreed CI budget |
| T05 | Slow contract conformance suite | 3 | 3 | 9 | QA Team | Parallelization and selective suite execution | Contract suite execution remains within approved pipeline budget |

### Operational Risks

| ID | Risk | Probability | Impact | Score | Owner | Primary Mitigation | Closure Criteria |
|---|---|---|---|---|---|---|---|
| O01 | Unclear startup failure diagnostics | 3 | 4 | 12 | Core Team | Structured startup report and error mapping | Startup report includes deterministic failure reasons for all blocked modules |
| O02 | Module version conflicts in production | 3 | 4 | 12 | Platform Team | Compatibility matrix validation before deploy | Deployment blocks incompatible module sets with clear diagnostics |
| O03 | Insufficient runtime observability | 3 | 3 | 9 | Platform Team | Mandatory telemetry baseline and trace propagation | Core observability checks pass in staging and production gates |
| O04 | Failed graceful shutdown under load | 2 | 4 | 8 | Core Team | Shutdown policies and forced fallback controls | Shutdown tests pass under defined load profile |
| O05 | Configuration drift across environments | 3 | 3 | 9 | DevOps Team | Schema validation and environment parity checks | Config parity checks pass across all deployment environments |

### Ecosystem Risks

| ID | Risk | Probability | Impact | Score | Owner | Primary Mitigation | Closure Criteria |
|---|---|---|---|---|---|---|---|
| E01 | Low third-party module adoption | 3 | 4 | 12 | DevRel Team | Better docs, templates, and onboarding flow | Adoption KPI meets target threshold for active module maintainers |
| E02 | Delayed contract update adoption | 3 | 3 | 9 | Core Team | Deprecation policy, migration guides, compatibility windows | Deprecated contract usage falls below defined threshold |
| E03 | Fragmented module compatibility in ecosystem | 3 | 3 | 9 | Core Team | Runtime compatibility checks and published matrix | Catalog shows tested compatibility for supported module versions |
| E04 | Abandoned critical module | 2 | 3 | 6 | Platform Team | Support status tracking and fallback module strategy | Critical capabilities have at least one maintained fallback option |
| E05 | Version fragmentation across consumers | 3 | 2 | 6 | Core Team | Semver governance and deprecation cadence | Fragmentation trend is stable or decreasing over review cycles |

### Team Risks

| ID | Risk | Probability | Impact | Score | Owner | Primary Mitigation | Closure Criteria |
|---|---|---|---|---|---|---|---|
| TM01 | Low bus factor in critical areas | 3 | 4 | 12 | Tech Lead | Shared ownership and knowledge transfer rituals | At least two maintainers cover each critical platform area |
| TM02 | Slow onboarding to architecture model | 4 | 3 | 12 | Tech Lead | Guided onboarding and architecture walkthroughs | New maintainers complete onboarding checklist successfully |
| TM03 | On-call overload and burnout | 2 | 3 | 6 | Engineering Manager | Rotation, escalation policy, and incident hygiene | On-call load remains within staffing policy thresholds |
| TM04 | Turnover in critical delivery phase | 2 | 4 | 8 | Engineering Manager | Cross-training and documented ownership transfer | No critical area remains without active owner during transition |

---

## Mitigation Execution Model

### Critical and High Risks

Required controls:
1. Named owner and backup owner.
2. Active mitigation ticket set with traceable status.
3. Gate checks in CI or release checklist.
4. Escalation path if closure criteria are missed.

### Medium Risks

Required controls:
1. Backlog item with owner.
2. Milestone assignment.
3. Review in regular architecture or platform cadence.

### Low Risks

Required controls:
1. Keep in register.
2. Monitor trend.
3. Re-score during scheduled review.

---

## Example Mitigation Plan S01

```markdown
## Risk S01 Mitigation Plan

### Owner
Security Team Lead

### Trigger
Any production module onboarding or module version update

### Actions
- [ ] Validate module against allowlist policy
- [ ] Validate artifact integrity and signature
- [ ] Confirm dual approval for allowlist changes
- [ ] Run runtime policy verification in release gate

### Milestones
- [ ] Allowlist workflow implemented in CI
- [ ] Integrity verification enabled in runtime bootstrap
- [ ] Signature verification enabled for production artifacts

### Rollback and Contingency
- [ ] Block deployment on failed integrity checks
- [ ] Revert to previous approved module set
- [ ] Execute incident response procedure for suspicious provenance

### Done Criteria
- [ ] All production modules pass governance checks
- [ ] No policy bypass in latest two release cycles
```

---

## Contingency Playbooks

### CP01 Critical Module Failure

**Trigger**: critical module failure after startup.

**Response**:
1. Detect via health signal and error telemetry.
2. Isolate failing module path when supported.
3. Route to degraded mode or execute controlled shutdown.
4. Roll back to previous known-good module artifact.
5. Complete incident review and update risk register.

### CP02 Security Incident via Module

**Trigger**: confirmed malicious or compromised module behavior.

**Response**:
1. Remove module from effective allowlist.
2. Block or revoke artifact resolution.
3. Deploy emergency hotfix if required.
4. Scope impact and notify stakeholders.
5. Patch controls and update governance checks.

### CP03 Breaking Core Change Published Incorrectly

**Trigger**: breaking behavior released under non-breaking version bump.

**Response**:
1. Confirm blast radius and affected versions.
2. Publish corrective patch or rollback release line.
3. Notify module maintainers with compatibility guidance.
4. Add preventive gate to release workflow.

### CP04 Critical Knowledge Loss

**Trigger**: departure or long unavailability of key maintainer.

**Response**:
1. Transfer ownership and access immediately.
2. Capture architecture and operational runbooks.
3. Reassign delivery scope to covered maintainers.
4. Update bus-factor controls in risk register.

---

## Monitoring and Review Cadence

| Meeting | Cadence | Participants | Focus |
|---|---|---|---|
| Architecture risk review | Weekly | Architecture and core leads | Critical and high risk progression |
| Security risk review | Bi-weekly | Security and platform leads | Security controls and incident trend |
| Platform reliability review | Weekly | Core, SRE, DevOps | Operational and technical risk indicators |
| Full risk reassessment | Quarterly | Cross-functional stakeholders | Re-score register and adjust controls |

### Leading Indicators and Trigger Thresholds

| Indicator | Category | Trigger Threshold | Action |
|---|---|---|---|
| Boundary violations detected in CI | Architecture | >= 1 on protected branch | Immediate release gate block and owner escalation |
| Open critical vulnerabilities | Security | >= 1 older than SLA window | Freeze release branch until remediation or approved exception |
| Contract test failure rate trend | Technical | Increasing trend for 2 consecutive review cycles | Mandatory compatibility remediation plan |
| Startup failure rate in staging | Operational | Above agreed baseline for 2 consecutive runs | Block production promotion and run incident-style review |
| Compatibility block events | Ecosystem | Spike above rolling baseline | Trigger catalog and module governance audit |
| Bus factor below minimum coverage | Team | Any critical area with single maintainer | Activate knowledge transfer contingency plan |

### Risk Burndown Governance

Risk burndown is tracked per severity band and reviewed as a trend, not snapshot only.

| Burndown Metric | Definition | Desired Trend | Governance Use |
|---|---|---|---|
| Critical risk count | Number of open risks with score 20..25 | Down or stable at zero | Release go/no-go checkpoint |
| High risk exposure | Sum of scores for high risks 12..16 | Down across review cycles | Architecture prioritization input |
| Mean time to mitigation start | Time from risk detection to active mitigation | Down | Team responsiveness signal |
| Mean time to closure | Time from risk creation to closure criteria met | Down with quality controls maintained | Delivery and governance health signal |
| Accepted risk aging | Age of accepted risks without renewal | No overdue accepted risks | Leadership oversight and exception management |

### Risk Metrics Dashboard

```yaml
architecture_health:
  circular_dependencies: 0
  boundary_violations: 0
  architecture_gate_failures: tracked

security_health:
  critical_vulnerabilities_open: 0
  integrity_check_failures: tracked
  allowlist_policy_bypasses: 0

operational_health:
  startup_failure_rate: tracked
  recovery_time_from_incident: tracked
  compatibility_block_events: tracked

risk_burndown:
  critical_risks_open: tracked
  high_risk_total_score: tracked
  mitigation_start_time: tracked
  mitigation_closure_time: tracked
```

---

## Risk Acceptance Process

### When Acceptance Is Allowed

Risk acceptance requires formal approval when at least one condition is true:
- mitigation cannot be completed before planned release gate
- risk is inherent to selected architecture tradeoff
- temporary exposure is necessary to enable higher-priority safety control

### Rules by Severity

| Severity | Acceptance Policy |
|---|---|
| Low | Team-level acceptance with documented review date |
| Medium | Tech lead and product owner acceptance |
| High | Architecture lead plus product owner and explicit expiration |
| Critical | Acceptance is exceptional and requires leadership approval with compensating controls |

### Risk Acceptance Template

```markdown
## Risk Acceptance Request

Risk ID: A05
Risk Description: Premature over-engineering
Current Score: 6
Severity: Medium

### Justification
Feature is deferred until stage gate evidence exists.

### Compensating Controls
- Keep boundary checks active
- Re-evaluate at next architecture gate

### Expiration Trigger
- Triggered when architecture gate conditions change risk context

### Approval
- [ ] Tech Lead
- [ ] Product Owner

### Review Trigger
- [ ] Next quarterly risk reassessment
```

---

## Post-Incident and Lessons Learned

### Incident Review Template

```markdown
## Incident Report

### Timeline
- Detection
- Triage
- Mitigation
- Recovery

### Root Cause

### What Worked

### What Failed

### Action Items
- [ ] Action item with owner and milestone

### Risk Register Update
- [ ] Existing risk rescored
- [ ] New risk added if needed
```

### Quarterly Retrospective Template

```markdown
## Quarterly Risk Retrospective

### Closed Risks

### New Risks

### Score Changes

### Process Improvements

### Next Cycle Priorities
```

---

## Related Documents

- [ADR-0003 Module Loading Security](./adr/ADR-0003-module-loading-security-allowlist-integrity.md)
- [ADR-0007 Observability and Operability](./adr/ADR-0007-observability-and-operability-baseline.md)
- [01 Architecture Baseline](./01-architecture-baseline.md)
- [03 Architecture Evolution Path](./03-architecture-evolution-path.md)

---

## Revision History

| Date       | Version | Change        | Author            |
|------------|---------|---------------|-------------------|
| 2026-03-25 | 0.1     | Initial draft | Architecture Team |
