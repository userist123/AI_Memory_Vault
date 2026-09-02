# ADR-0009 Hybrid Admin UI Model (Shell + UI Plugins)

Date: 2026-03-26  
Status: Draft

## Context
`prosto-platform` is headless and follows micro-core boundaries. Existing architecture keeps kernel responsibilities minimal and framework-neutral. Product direction requires an extensible admin experience without coupling frontend runtime concerns to `platform-core`.

## Decision
Adopt a **hybrid admin model**:
- Admin UI runtime is delivered as the `@prosto/platform-admin-shell` package within the monorepo. It is a Vue 3 SPA built with Vite.
- Module-driven admin extensibility is provided through versioned UI plugin manifests and discovery contracts.
- Policy-aware aggregation for admin operations and plugin discovery is provided by `platform-adapter-admin-bff`.
- Contracts for admin discovery, plugin manifest schemas, permissions, and compatibility are owned by `platform-admin-contracts`.

## Boundary Rules
- `platform-core` must not depend on admin shell runtime or frontend framework packages.
- UI plugin loading must be allowlist-based with integrity and compatibility validation.
- Admin shell consumes only contract-defined discovery payloads via workspace reference to `platform-admin-contracts`.
- Feature modules may contribute UI plugins only through `platform-admin-contracts` manifests.

## Security and Governance
- UI plugins require trust class metadata and explicit allowlist approval for protected environments.
- Rejected plugins must produce structured diagnostics with reason codes.
- Contract and compatibility checks are required in CI before plugin publication.

## Consequences
### Positive
- Preserves micro-core purity.
- Centralized development workflow with unified build, test, and lint commands.
- Atomic cross-package changes for contracts, BFF, and shell.
- Scales module ecosystem UI contributions with policy control.

### Trade-offs
- Adds a dedicated contract package and BFF adapter governance surface.
- Requires compatibility management between shell and plugin manifests.

## Implementation Notes
- Add `platform-admin-contracts` and `platform-adapter-admin-bff` to package blueprint.
- Add `platform-admin-shell` (Vue 3 SPA) to package blueprint.
- Update C4 context to include Admin Shell, Admin BFF, and UI Plugin Registry.
- Extend implementation roadmap with an Admin Enablement stream after contract baseline phases.

## Related
- [ADR-0001 Micro-Core Kernel Boundary](./ADR-0001-micro-core-kernel-boundary.md)
- [ADR-0003 Module Loading Security](./ADR-0003-module-loading-security-allowlist-integrity.md)
- [ADR-0006 External Module Repository Model](./ADR-0006-external-module-repository-and-distribution-model.md)
- [ADR-0007 Observability Baseline](./ADR-0007-observability-and-operability-baseline.md)
