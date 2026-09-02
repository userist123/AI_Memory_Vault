# @prosto/platform-admin-shell

Vue 3 SPA for Prosto platform admin interface with plugin runtime, policy-gated rendering, and degraded-mode diagnostics.

## Status
- Phase 09 baseline completed
- Integrates with `@prosto/platform-adapter-admin-bff` discovery API
- Depends on `@prosto/platform-admin-contracts` for type-safe plugin manifests, discovery payloads, permissions, and compatibility rules
- All internal contracts are marked `@alpha`

## Overview

`platform-admin-shell` implements the admin UI runtime for Prosto platform. It loads UI plugins from the admin BFF discovery payload, validates compatibility and permissions, and renders extension points through a policy-aware plugin registry. The package follows a hybrid Feature-Sliced Design + Clean Architecture approach to separate framework-agnostic runtime logic from Vue UI components.

### Key capabilities
- Contract-driven plugin loading lifecycle with compatibility gating
- Permission-aware rendering guards based on BFF discovery payload policy metadata
- Degraded mode with operator-facing diagnostics for rejected or failed plugins
- Telemetry instrumentation for plugin load outcomes and UI extension usage
- Isolation of plugin failures from shell bootstrap
- Same-origin browser authentication with login retry and logout controls

## Architecture

The package is structured into independent slices with strict dependency rules:

```text
src/
  app/                  # Composition root: Vue app, providers, routes, config
  pages/                # Route-level components (dashboard, diagnostics)
  widgets/              # Reusable UI compositions (shell layout, diagnostics panel, plugin container)
  processes/            # Cross-feature workflows (admin shell bootstrap, startup lifecycle)
  features/             # Business capabilities (plugin runtime, permissions, diagnostics)
  entities/             # Domain state models (plugin store, diagnostics store)
  shared/               # Reusable infrastructure (BFF client, observability, locales, utilities)
```

### Dependency rules
1. `shared` imports nothing from upper layers
2. `entities` imports only `shared` and external contracts
3. `features` imports `entities`, `shared`, and external contracts
4. `processes` imports `features`, `entities`, `shared` (no UI components)
5. `widgets` imports `features`, `entities`, `shared` (no `pages` or `app`)
6. `pages` imports `widgets`, `features`, `entities`, `shared`
7. `app` imports all lower layers and serves as the only composition root

### Boundaries
- `platform-admin-shell` does not import `platform-core` or server adapters directly
- All server communication goes through framework-neutral BFF clients in `shared/api`
- Framework-agnostic logic in `features/*/model` and `shared/api` must not import Vue, Pinia, or Vuetify

## Browser authentication

- The admin BFF defaults to `window.location.origin`.
- Optional build-time `VITE_ADMIN_BFF_BASE_URL` values must resolve to the exact browser origin; cross-origin values fail startup.
- Discovery and logout requests use `credentials: 'same-origin'` and never read browser cookies or store tokens.
- A discovery `401` navigates to `/auth/login`; `/?auth=failed` instead renders a single manual retry action.

## Commands
- `npm run --workspace @prosto/platform-admin-shell dev` — start dev server with HMR
- `npm run --workspace @prosto/platform-admin-shell build` — typecheck and build for production
- `npm run --workspace @prosto/platform-admin-shell preview` — preview production build
- `npm run --workspace @prosto/platform-admin-shell typecheck` — run Vue TSC type checking
- `npm run --workspace @prosto/platform-admin-shell test` — run unit and integration tests
- `npm run --workspace @prosto/platform-admin-shell test:unit` — run unit tests with verbose reporter

## Notes
- The package is private and not published to npm registry
- Phase 09 completion criteria are met: integration with admin BFF discovery, plugin runtime with compatibility gating, permission-aware rendering, degraded mode, passing end-to-end integration tests
- All framework-agnostic logic in `features/*/model` and `shared/api` does not import Vue, Pinia, or Vuetify
