# ADR-0001: Micro-Core Kernel Boundary

Date: 2026-03-24  
Status: Draft

## Context
Project requirements define a headless platform with module-based expansion. Research documents emphasize that core should remain minimal and long-lived while feature growth happens in modules/adapters.

Without a strict boundary, kernel scope can expand into domain logic, transport frameworks, and storage specifics, reducing extensibility and increasing coupling.

## Decision
Adopt a strict micro-core boundary:
- Kernel owns only lifecycle orchestration, service registry, event/hook bus, configuration validation, module loading, and compatibility checks.
- Kernel does not own HTTP framework specifics, ORM/persistence specifics, vendor integrations, or feature domain logic.
- Transport and integration concerns are implemented as optional adapters and modules.

## Consequences

### Positive
- Stable and reusable kernel with low coupling.
- Modules stay portable across deployment contexts.
- Easier testing and version governance.

### Negative
- More package boundaries and contracts to maintain.
- Initial setup overhead for adapter/module developers.

## Alternatives Considered
- Framework-first core (for example embedding a web framework directly): rejected due to lock-in and boundary erosion.
- Monolithic feature-rich core: rejected due to weak extensibility.

## Related Artifacts
- [01 Architecture Baseline](../01-architecture-baseline.md)
- [C4-02 Container View](../c4/02-container-view.md)
- [C4-03 Core Component View](../c4/03-component-view-kernel.md)
- [SEQ-02 HTTP Request Through Module](../sequence/02-http-request-module-flow.md)
