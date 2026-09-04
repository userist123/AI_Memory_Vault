# ADR-0008: Test Strategy, Contract Testing, And Quality Gates

Date: 2026-03-24  
Status: Draft

## Context
The platform depends on independent module repositories and strict SDK contracts. Without shared testing strategy and mandatory quality gates, compatibility drift and security regressions can reach production despite successful local builds.

Research and architecture baseline already define test pyramid, contract testing, and startup/runtime critical controls that should become explicit architectural policy.

## Decision
Adopt a unified testing strategy with mandatory quality gates for core and module repositories:
- Use a three-layer test model: unit, integration, and contract tests.
- Publish and maintain shared contract suite (`@prosto/platform-contract-tests`) for module conformance validation.
- Module CI must run contract tests against declared compatible core/SDK version range.
- Manifest schema validation and compatibility checks are mandatory pre-release gates.
- Security-critical and compatibility-critical paths must be fully covered by deterministic automated tests.
- Release publishing is allowed only from tagged CI pipelines where all required gates pass.

## Consequences

### Positive
- Lower risk of contract drift between core and external modules.
- Earlier detection of compatibility and lifecycle failures before runtime deployment.
- Consistent release quality across module ecosystem.
- Clear governance criteria for release readiness and rollback decisions.

### Negative
- Increased CI complexity and execution cost, especially for compatibility matrices.
- Additional maintenance burden for shared contract fixtures and evolving test scenarios.
- Higher onboarding effort for module teams to satisfy all required gates.

## Alternatives Considered
- Unit/integration tests only, without shared contract suite: rejected due to weak cross-repository compatibility guarantees.
- Optional contract tests for module repositories: rejected because it allows inconsistent release quality.
- Manual release verification instead of blocking CI gates: rejected due to non-deterministic quality and scale limits.

## Related Artifacts
- [02 Domain And Capability Model](../02-domain-and-capability-model.md)
- [C4-01 System Context](../c4/01-system-context.md)
- [C4-02 Container View](../c4/02-container-view.md)
- [ADR-0002 SDK Contract And Semver Governance](./ADR-0002-sdk-contract-and-semver-governance.md)
- [ADR-0006 External Module Repository And Distribution Model](./ADR-0006-external-module-repository-and-distribution-model.md)
