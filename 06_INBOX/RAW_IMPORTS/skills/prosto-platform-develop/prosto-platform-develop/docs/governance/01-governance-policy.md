# Repository Governance Policy

## Status
- Stage: Active from Phase 01 onward
- Applies to: pull requests targeting `main` and `develop`

## Related Documents
- Required checks matrix: `docs/governance/required-checks.md`
- Architecture baseline: `.context/02-architecture-design/01-architecture-baseline.md`
- Branch and release policy: `.context/02-architecture-design/05-git-branching-strategy.md`
- Metrics and risk controls: `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`

## 1) Non-Bypassable Checks
The following checks are mandatory and configured as required branch checks:
- `FF-01 kernel boundary guard` (implemented)
- `FF-02 dependency policy guard` (implemented)
- `FF-04 runtime-policy` (implemented in Phase 05)
- `FF-03 lifecycle determinism` (implemented in Phase 05)
- `FF-05 contracts gate` (implemented in Phase 04)
- `release-readiness-evidence` (implemented)

Policy constraints:
- No direct push to `main` or `develop`.
- No merge allowed when any required check is failing or missing.
- Required checks are defined in `docs/governance/required-checks.md`.
- Check maturity status does not remove branch protection requirement.

## 2) Exception Workflow with Expiration
Exceptions are temporary and explicit. Every exception record must include:
- `id`: unique identifier
- `scope`: affected check(s) and branch
- `owner`: accountable person
- `reason`: concrete justification
- `createdAt`: timestamp in ISO-8601
- `expiresAt`: timestamp in ISO-8601
- `mitigation`: short-term risk reduction action
- `postmortemAction`: follow-up action to remove future exceptions

### 2.1 Exception Rules
- Maximum TTL: 7 calendar days.
- Expired exceptions are invalid and cannot be reused.
- Re-approval requires a new record with new `id` and fresh review.
- Every accepted exception must be referenced in release evidence artifact.

### 2.2 Exception Approval Path
1. Check Owner
2. Backup Owner
3. Engineering Manager final approval

## 3) Required PR Evidence Links
Each PR to protected branches must contain evidence links in PR description:
- Architecture policy evidence FF-01 and FF-02
- Runtime policy evidence FF-04 execution (`npm run validate:runtime-policy`)
- Quality evidence FF-03 execution (`npm run test:lifecycle-determinism`)
- Quality evidence FF-05 contracts gate execution (`npm run test:contracts`)
- Risk-control acknowledgment linked to `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`
- Release evidence artifact link or run link where `release-evidence.json` is published

## 4) Enforcement Notes
- Phase 01 governance workflows and check wiring are implemented.
- Phase 02 package boundary checks are implemented and enforced.
- Phase 03 SDK contract baseline is completed and now part of release evidence context.
- FF-05 contracts gate is implemented in Phase 04 with conformance suite execution against reference modules.
- FF-03 and FF-04 are active in CI; Phase 06 security and performance controls are completed.

## 5) Audit Trail Requirements
- Retain workflow logs and artifacts for minimum 30 days.
- Store exception records in repository PR discussion and release evidence manifest.
- Review exception usage weekly; unresolved exceptions escalate.
