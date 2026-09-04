# Phase 01 - Governance Activation and Delivery Guardrails

## Execution Status
- Status: Completed
- Completed on: 2026-03-29
- Repository evidence:
  - `.github/workflows/policy-gates.yml`
  - `.github/workflows/quality-gates.yml`
  - `.github/workflows/release-readiness.yml`
  - `docs/governance/required-checks.md`
  - `.github/pull_request_template.md`
  - `scripts/generate-release-evidence.mjs`

## Phase Objective
Establish enforceable governance and quality guardrails so architectural intent from `.context/02-architecture-design` becomes executable policy before runtime implementation starts.

## Scope Boundaries
### In Scope
- Repository-level CI policy skeleton.
- Branch protection policy definition for `main` and `develop`.
- Mandatory evidence artifacts for architecture and release checks.
- Initial quality gate matrix aligned with architecture fitness functions FF-01..FF-05.

### Out of Scope
- Implementing runtime kernel logic.
- Implementing SDK contracts.
- Implementing full benchmark suites.

## Prerequisites and Dependencies
- Architecture baseline available in `.context/02-architecture-design/01-architecture-baseline.md`.
- Branch and release policy available in `.context/02-architecture-design/05-git-branching-strategy.md`.
- Risk controls available in `.context/02-architecture-design/06-risk-management.md`.

## Detailed Ordered Implementation Steps
1. Create CI workflow skeletons under `.github/workflows/` for:
   - `policy-gates.yml`
   - `quality-gates.yml`
   - `release-readiness.yml`
2. Define required checks list and map each check to owner and escalation path.
3. Add repository policy document in `.context/04-implementation-plan` describing:
   - non-bypassable checks
   - exception workflow with expiration
   - required evidence links per PR
4. Define initial script contracts in `package.json` for future implementation:
   - `lint:architecture`
   - `validate:dependency-policy`
   - `validate:runtime-policy`
   - `test:contracts`
5. Set pull request template with architecture and risk-control checklist.
6. Define release evidence manifest format consumed by release workflow.

## Code Examples
### Example: policy gate job contract
```yaml
name: policy-gates
on:
  pull_request:
    branches: [main, develop]
jobs:
  architecture-policy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 22
      - run: npm ci
      - run: npm run lint:architecture
      - run: npm run validate:dependency-policy
```

### Example: release evidence artifact schema
```json
{
  "releaseVersion": "0.1.0",
  "checks": [
    { "id": "FF-01", "status": "pass", "evidence": "link" },
    { "id": "FF-05", "status": "pass", "evidence": "link" }
  ],
  "exceptions": []
}
```

## Affected Modules or Files
### Existing files likely updated
- `package.json`
- `.context/02-architecture-design/05-git-branching-strategy.md`
- `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`

### New files expected
- `.github/workflows/policy-gates.yml`
- `.github/workflows/quality-gates.yml`
- `.github/workflows/release-readiness.yml`
- `.github/pull_request_template.md`
- `docs/governance/required-checks.md`

## Validation and Testing Approach
- Dry-run workflow validation on draft PR.
- Confirm blocked merge when required checks fail.
- Confirm exception record requires owner, reason, expiry.
- Confirm evidence artifact is produced and attached in CI.

## Data or Migration Impact
- No product data migration.
- Process migration: manual governance to CI-enforced governance.

## Risks and Mitigations
- Risk: teams bypass controls for urgent delivery.
  - Mitigation: protected branches + mandatory exception workflow with TTL.
- Risk: too many early gates reduce throughput.
  - Mitigation: phase in non-critical checks as warning first, then blocking.

## Rollback Approach
- Keep workflow versioned behind feature branch.
- If delivery is blocked by misconfigured gate, rollback by reverting workflow commit and restoring last known-good gate set.
- Preserve incident note for gate tuning before re-enabling.

## Completion Criteria
- PR merge to `main`/`develop` is blocked without required checks.
- Required checks map to documented owners and escalation path.
- Release evidence artifact is generated in CI.
- Governance exception process is documented and test-validated.
