# ADR-0002: SDK Contract And Semver Governance

Date: 2026-03-24  
Status: Draft

## Context
Modules are expected to live in separate repositories and evolve independently. A stable shared contract is required to prevent runtime incompatibility and contract drift.

## Decision
Define and publish `@prosto/platform-sdk` as the contract authority:
- SDK contains module manifest types, lifecycle interfaces, typed tokens, and shared error codes.
- Modules declare SDK as `peerDependency`.
- Runtime validates `platformVersion` and compatibility ranges during startup.
- Breaking contract changes require major version bump and migration notes.
- Maintain compatibility matrix in module catalog.

## Consequences

### Positive
- Explicit contract alignment between core and external modules.
- Controlled evolution path with predictable compatibility.
- Reusable contract tests across module repositories.

### Negative
- Requires strict version discipline and release governance.
- Additional maintenance for compatibility matrix.

## Alternatives Considered
- Keep contracts only in core package: rejected because it couples modules to internals.
- Convention-based manifests without shared schema package: rejected due to ambiguity and drift risk.

## Related Artifacts
- [02 Domain And Capability Model](../02-domain-and-capability-model.md)
- [C4-02 Container View](../c4/02-container-view.md)
- [DFD-03 Module Loading L2](../dfd/03-module-loading-l2.md)
- [ADR-0008 Testing And Contract Quality Gates](./ADR-0008-test-strategy-contract-testing-and-quality-gates.md)

