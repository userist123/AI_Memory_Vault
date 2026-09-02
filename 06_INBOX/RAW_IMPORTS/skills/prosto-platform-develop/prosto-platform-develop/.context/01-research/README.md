# prosto-platform Research Pack

Date: 2026-03-23
Scope: TypeScript headless micro-core platform with plugin modules in separate GitHub repositories.

## What Was Studied
- `README.md`
- `AGENTS.md`
- `.cursor/rules/general.md`
- `.cursor/rules/typescript.md`
- `.cursor/rules/testing.md`
- `package.json`
- `.editorconfig`

## Key Findings
- The project is currently in a greenfield stage (no `src/` yet).
- Architecture direction is explicit: minimal micro-core plus plugin-based expansion.
- Engineering constraints are explicit: strict TypeScript, strong testing discipline, security-first approach.
- Current package setup is minimal; this is a good moment to lock architecture boundaries before implementation.

## Research Documents
1. [Context And Constraints](./01-context-and-constraints.md)
2. [Micro-Core Architecture](./02-micro-core-architecture.md)
3. [Module Repositories Strategy](./03-module-repositories-strategy.md)
4. [Framework And Library Evaluation](./04-framework-and-library-evaluation.md)
5. [Quality Security Performance](./05-quality-security-performance.md)
6. [Implementation Roadmap](./06-implementation-roadmap.md)

## Recommended Baseline (Short Version)
- Keep core framework-agnostic and headless.
- Publish a small `@prosto/platform-sdk` package that defines plugin contracts.
- Use explicit module allowlists and signed releases for external plugin repos.
- Keep dependencies minimal in core: runtime validation, logging, metrics, and semver compatibility checks.
- Build optional adapters (HTTP, persistence, queue) as separate packages, not as core dependencies.

Continue with: [01 - Context And Constraints](./01-context-and-constraints.md).
