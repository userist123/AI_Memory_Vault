# 06 - Implementation Roadmap

Date: 2026-03-23

## Phase 1: Contracts First
- Create `@prosto/platform-sdk` package:
  - module manifest schema
  - lifecycle interfaces
  - shared error codes
  - typed service tokens
- Publish first contract version (`0.x` until stabilized).

Deliverable: stable SDK contract test fixtures.

## Phase 2: Minimal Kernel
- Create `@prosto/platform-core`:
  - service registry
  - module loader
  - lifecycle orchestrator
  - compatibility validator
- Add startup policy modes (`strict`, `best-effort`).

Deliverable: kernel that can load demo modules from config allowlist.

## Phase 3: First Adapter And Example Modules
- Implement `@prosto/http-fastify` (or chosen adapter).
- Build 2-3 example module repos:
  - `module-health`
  - `module-auth` (minimal)
  - `module-content` (minimal)
- Validate end-to-end startup and shutdown lifecycle.

Deliverable: runnable reference platform with external module repositories.

## Phase 4: Ecosystem Governance
- Add module catalog document.
- Define release/compatibility policy.
- Add contract-test CI template for module repos.
- Add security review checklist for third-party modules.

Deliverable: predictable module ecosystem workflow.

## Phase 5: Hardening
- Add performance benchmarks and budgets.
- Add chaos/failure-mode integration tests.
- Add observability and startup diagnostics tooling.
- Freeze `1.0` contract once ecosystem is stable.

Deliverable: production-ready platform baseline.

## Immediate Next Actions (Pragmatic)
1. Scaffold `packages/platform-sdk` and `packages/platform-core`.
2. Implement manifest schema and lifecycle interface first.
3. Add one contract test package shared by all module repos.
4. Decide test runner standard (Vitest vs Node test) and enforce single choice.
5. Move web-specific dependencies from core root into adapter package scope.
