# ADR-0003: Module Loading Security (Allowlist + Integrity)

Date: 2026-03-24  
Status: Draft

## Context
The platform supports external module repositories. This introduces supply-chain and compatibility risks if modules are loaded dynamically without strict controls.

## Decision
Enforce a secure module loading policy:
- Production runtime loads modules only from explicit allowlist.
- Artifacts must pass integrity checks (checksum/signature policy).
- Manifest schema and compatibility metadata are mandatory.
- Module security classification (`trusted`, `internal`, `third-party-reviewed`) is required for governance.
- Modules missing required metadata are blocked from activation.

## Consequences

### Positive
- Lower risk of unauthorized or tampered module execution.
- Clear auditability of loaded module set.
- Better incident response through explicit module identity and trust metadata.

### Negative
- Additional operational setup for signatures/checksums and catalog metadata.
- Reduced flexibility for ad-hoc dynamic loading in production.

## Alternatives Considered
- Runtime load directly from Git URLs: rejected for production due to integrity and reproducibility concerns.
- No integrity check, only semver check: rejected due to supply-chain risk.

## Related Artifacts
- [DFD-01 Context L0](../dfd/01-context-l0.md)
- [DFD-03 Module Loading L2](../dfd/03-module-loading-l2.md)
- [SEQ-04 Critical Module Failure](../sequence/04-critical-module-failure.md)
- [C4-04 Deployment View](../c4/04-deployment-view.md)

