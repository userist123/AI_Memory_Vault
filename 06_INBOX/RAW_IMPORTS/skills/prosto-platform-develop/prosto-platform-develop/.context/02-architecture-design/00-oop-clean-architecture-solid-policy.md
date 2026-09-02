# 00 OOP, Clean Architecture, and SOLID Policy

Date: 2026-04-25
Status: Draft revised

## Purpose

This document defines mandatory implementation guidance for AI agents and contributors working on `prosto-platform`.

The policy applies to all new code and all substantial refactoring.

## Policy Statement

Implementation work must:

1. Use object-oriented design where it improves clarity, extension safety, and testability.
2. Follow Clean Architecture boundaries to keep business policies independent from infrastructure and frameworks.
3. Apply SOLID principles explicitly in design decisions and code reviews.

## OOP Baseline Expectations

- Model behavior through cohesive classes and interfaces when domain and lifecycle behavior is stateful or evolving.
- Prefer composition over inheritance unless inheritance provides clear substitution value.
- Keep class responsibilities focused and bounded by package and layer boundaries.
- Use explicit contracts for collaborators instead of concrete coupling.

## Clean Architecture Expectations

- Keep dependency direction toward stable inner policies and contracts.
- Isolate framework, transport, persistence, and adapter details from core policies.
- Place cross-boundary interactions behind interfaces and adapters.
- Prevent domain logic leakage into infrastructure implementations.

## SOLID Enforcement Checklist

- **S** Single Responsibility: each type has one primary reason to change.
- **O** Open-Closed: extend behavior via composition and interfaces, avoid editing stable core behavior directly.
- **L** Liskov Substitution: substitutable implementations must preserve contract expectations.
- **I** Interface Segregation: provide narrow, consumer-focused interfaces.
- **D** Dependency Inversion: depend on abstractions, not concrete details.

## Implementation Planning Guidance

When proposing implementation steps, include:

- The target files and boundaries affected.
- The abstractions or interfaces that enforce dependency direction.
- How the change maintains micro-core constraints and package boundaries.
- Acceptance signals that demonstrate OOP, Clean Architecture, and SOLID compliance.

## Related Documents

- [Architecture Design Pack README](./README.md)
- [04 Package Structure Blueprint](./04-package-structure-blueprint.md)
- [ADR-0001 Micro-Core Kernel Boundary](./adr/ADR-0001-micro-core-kernel-boundary.md)
- [AGENTS.md](../../AGENTS.md)
