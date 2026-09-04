# ADR-0005: Core Runtime Stack (Node TS + Zod + Pino)

Date: 2026-03-24  
Status: Draft

## Context
Core needs minimal but reliable dependencies for runtime safety and operability. Heavy frameworks in core conflict with micro-core neutrality.

## Decision
Adopt lightweight baseline stack for core:
- Node.js + TypeScript strict mode (ESM).
- Zod for runtime boundary/config validation.
- Pino for structured logging.
- Custom typed service registry in kernel (DI-lite).
- Optional adapter-level libraries for transport/persistence (for example Fastify in adapter package, not kernel).

## Consequences

### Positive
- Small dependency surface in core.
- Strong runtime type safety at boundaries.
- Better operational diagnostics with structured logs.

### Negative
- Team must maintain custom typed registry lifecycle as complexity grows.
- Additional integration work in adapters for framework-specific concerns.

## Alternatives Considered
- Heavy framework in kernel: rejected due to lock-in and unnecessary coupling.
- No runtime validation library: rejected due to config/input safety risks.
- Generic logging abstraction with heavy dependency tree: rejected due to complexity for baseline stage.

## Related Artifacts
- [01 Architecture Baseline](../01-architecture-baseline.md)
- [C4-02 Container View](../c4/02-container-view.md)
- [C4-03 Core Component View](../c4/03-component-view-kernel.md)
- [DFD-02 Runtime L1](../dfd/02-runtime-l1.md)

