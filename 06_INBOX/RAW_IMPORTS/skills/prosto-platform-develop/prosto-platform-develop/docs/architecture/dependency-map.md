# Package Dependency Map (Phase 10)

## Purpose
This document captures the enforceable package dependency boundaries through Phase 10 implementation.

## Status
- Phase 10 baseline is completed and active in repository validation scripts.

## Sources
- `.context/04-implementation-plan/02-phase.md`
- `.context/02-architecture-design/04-package-structure-blueprint.md`
- `.context/02-architecture-design/adr/ADR-0001-micro-core-kernel-boundary.md`
- `.context/02-architecture-design/adr/ADR-0002-sdk-contract-and-semver-governance.md`
- `.context/02-architecture-design/adr/ADR-0009-admin-ui-hybrid-shell-plugin-model.md`

## Workspace Packages
- `@prosto/platform-sdk`
- `@prosto/platform-core`
- `@prosto/platform-adapter-typeorm`
- `@prosto/platform-contract-tests`
- `@prosto/platform-cli`
- `@prosto/platform-adapter-http`
- `@prosto/platform-admin-contracts`
- `@prosto/platform-adapter-admin-bff`
- `@prosto/platform-admin-shell`

## Allowed Internal Dependencies (Phase 10)

| Package | Depends On |
|---------|-----------|
| `@prosto/platform-sdk` | none |
| `@prosto/platform-core` | `@prosto/platform-sdk` |
| `@prosto/platform-adapter-typeorm` | `@prosto/platform-sdk` |
| `@prosto/platform-contract-tests` | `@prosto/platform-sdk` |
| `@prosto/platform-cli` | `@prosto/platform-sdk` |
| `@prosto/platform-adapter-http` | `@prosto/platform-sdk` |
| `@prosto/platform-admin-contracts` | `@prosto/platform-sdk` |
| `@prosto/platform-adapter-admin-bff` | `@prosto/platform-sdk`, `@prosto/platform-admin-contracts` |
| `@prosto/platform-admin-shell` | `@prosto/platform-admin-contracts` |

## Ownership Notes
- Fastify and HTTP/security middleware dependencies are owned by `@prosto/platform-adapter-http`; its public API remains free of Fastify types.
- TypeORM persistence (shared DataSource, migration locks, descriptor registry) is owned by `@prosto/platform-adapter-typeorm`.
- Admin UI plugin contracts (manifests, discovery, permissions, compatibility) are owned by `@prosto/platform-admin-contracts`.
- Admin BFF adapter (discovery aggregation, permission mapping, diagnostics, observability) is owned by `@prosto/platform-adapter-admin-bff`.
- Admin UI shell runtime (Vue 3 SPA, plugin runtime, permission guards, degraded mode) is owned by `@prosto/platform-admin-shell`.
- Root `package.json` is orchestration-only for workspace scripts and governance checks.

## Boundary Rules
- `@prosto/platform-core` MUST NOT import from adapters or modules.
- Modules and adapters MUST NOT import from `@prosto/platform-core`.
- `@prosto/platform-adapter-typeorm` depends only on `platform-sdk` contracts and the TypeORM driver; it does not import `platform-core`.
- `@prosto/platform-admin-shell` does not import `@prosto/platform-core` or server adapters directly.
- Framework-agnostic logic in `platform-admin-shell` `features/*/model` and `shared/api` must not import Vue, Pinia, or Vuetify.

## Enforcement Scripts
- `npm run lint:architecture`
- `npm run validate:dependency-policy`
- `npm run validate:module-graph`
- `npm run validate:public-api-boundary`
- `npm run validate:runtime-policy`

Current scripts enforce topology and baseline dependency constraints across all packages including admin enablement stream.
