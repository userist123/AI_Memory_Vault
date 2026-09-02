## Summary
- What changed?
- Why is this needed now?
- What should reviewers focus on?

## Change Type
- [ ] Feature
- [ ] Fix
- [ ] Refactor
- [ ] Docs
- [ ] CI/Policy
- [ ] Breaking change

## Scope
- Packages touched: <!-- e.g. @prosto/platform-sdk, @prosto/platform-core -->
- Non-package files touched: <!-- e.g. scripts/, .github/workflows/, docs -->

## Related Context
- Issue/Task: <!-- link -->
- ADR/Architecture doc (if applicable): <!-- link -->
- Baseline architecture reference: `.context/02-architecture-design/01-architecture-baseline.md`
- Risk controls reference: `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`

## Architecture and Boundary Checklist
- [ ] Changes preserve micro-core boundaries (ADR-0001 intent).
- [ ] No adapter imports from `platform-core`.
- [ ] No forbidden cross-package imports introduced.
- [ ] Public API boundaries remain valid (`validate:public-api-boundary`).
- [ ] Contract-first approach is respected (types/contracts before runtime coupling).

## Security and Risk Checklist
- [ ] External input handling is validated/sanitized where applicable.
- [ ] Logs/diagnostics do not expose secrets.
- [ ] I reviewed R-01..R-06 controls in `.context/03-work-plan/02-metrics-acceptance-and-risk-controls.md`.
- [ ] Rollout and rollback impact is documented for non-trivial behavior changes.
- [ ] If any gate is bypassed, an exception record is attached.

## Exception Record (Only If Needed)
```json
{
  "id": "EX-YYYYMMDD-001",
  "scope": ["FF-0X", "branch:develop"],
  "owner": "<name>",
  "reason": "<justification>",
  "createdAt": "<ISO-8601>",
  "expiresAt": "<ISO-8601>",
  "mitigation": "<short-term control>",
  "postmortemAction": "<follow-up action>"
}
```
