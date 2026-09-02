# Phase 07 - Admin Contracts and UI Plugin Manifests

## Execution Status
Completed on 2026-06-04.

Implemented package: `@prosto/platform-admin-contracts` in `packages/platform-admin-contracts`.

Delivered scope:
- Package scaffold with strict TypeScript, Vite build, declaration output, and Vitest configuration.
- UI plugin manifest contracts, schemas, validators, and validation error types.
- Admin discovery payload contracts, schemas, validators, and rejection diagnostics.
- Permission and policy contracts with role mapping, schemas, validators, and action-gate evaluation.
- Compatibility contracts and evaluator for shell/plugin version and contract mismatch decisions.
- Public package exports through `src/index.ts` and scoped subdirectory indexes.
- Unit coverage for manifest validation, discovery validation, permission policy validation, and compatibility decisions.

## Phase Objective
Implement `@prosto/platform-admin-contracts` as the contract authority for hybrid admin model integration, including UI plugin manifests, discovery payloads, permission contracts, and compatibility validation.

## Scope Boundaries
### In Scope
- Contract package `@prosto/platform-admin-contracts`.
- Versioned schema for UI plugin manifests.
- Versioned schema for admin discovery payloads.
- Permission and capability declaration contracts for admin extensions.
- Compatibility rules between `admin-shell` and UI plugin packages.

### Out of Scope
- BFF route implementation and aggregation logic.
- Admin shell runtime implementation details.
- UI framework-specific rendering concerns.

## Prerequisites and Dependencies
- Phase 03 SDK contract baseline completed and versioned.
- ADR references:
  - `.context/02-architecture-design/adr/ADR-0001-micro-core-kernel-boundary.md`
  - `.context/02-architecture-design/adr/ADR-0003-module-loading-security-allowlist-integrity.md`
  - `.context/02-architecture-design/adr/ADR-0009-admin-ui-hybrid-shell-plugin-model.md`
- Package boundary references:
  - `.context/02-architecture-design/04-package-structure-blueprint.md`

## Detailed Ordered Implementation Steps
1. Create package scaffold `packages/platform-admin-contracts` with strict TypeScript configuration.
2. Define UI plugin manifest types in `src/manifests`:
   - plugin identity
   - plugin version
   - shell compatibility range
   - required permissions and capabilities
   - trust class and review status metadata
3. Define manifest schema validators in `src/manifests` with runtime validation helpers.
4. Define admin discovery payload contracts in `src/discovery`:
   - navigation extension points
   - page/widget/action registry descriptors
   - rejection diagnostics structure
5. Define permission and policy contracts in `src/permissions` for role mapping and action gating.
6. Define compatibility rules in `src/compatibility`:
   - shell version versus plugin manifest range
   - contract version mismatch reason taxonomy
7. Add package exports and stability labels for every public symbol.
8. Add unit and contract-level tests for:
   - schema pass and fail cases
   - compatibility decisions
   - diagnostics payload shape

## Code Examples
### Example: UI plugin manifest contract
```typescript
export interface AdminUIPluginManifest {
  id: string;
  version: string;
  shellCompatibility: string;
  requiredPermissions: string[];
  extensionPoints: Array<'nav' | 'page' | 'widget' | 'action'>;
  trustClass: 'trusted' | 'internal' | 'third-party-reviewed';
}
```

### Example: discovery payload contract
```typescript
export interface AdminDiscoveryPayload {
  plugins: Array<{
    id: string;
    version: string;
    extensions: string[];
  }>;
  rejected: Array<{
    id: string;
    reasonCode: string;
    remediationHint: string;
  }>;
}
```

### Example: compatibility check result
```typescript
export interface AdminPluginCompatibilityResult {
  allowed: boolean;
  reasonCode?: 'SHELL_VERSION_MISMATCH' | 'CONTRACT_VERSION_MISMATCH';
}
```

## Affected Modules or Files
### Existing files likely updated
- `.context/02-architecture-design/04-package-structure-blueprint.md`
- `package.json`

### New files expected
- `packages/platform-admin-contracts/package.json`
- `packages/platform-admin-contracts/tsconfig.json`
- `packages/platform-admin-contracts/src/index.ts`
- `packages/platform-admin-contracts/src/manifests/*.ts`
- `packages/platform-admin-contracts/src/discovery/*.ts`
- `packages/platform-admin-contracts/src/permissions/*.ts`
- `packages/platform-admin-contracts/src/compatibility/*.ts`
- `packages/platform-admin-contracts/tests/**/*.test.ts`

## Validation and Testing Approach
- Unit tests for schema validators and compatibility rule outcomes.
- Type-level checks for public contract compatibility.
- API surface snapshot for stability and semver discipline.
- CI gate requiring contract tests to pass before publishing package version.

## Data or Migration Impact
- No business data migration.
- Contract migration path required for shell/plugin compatibility when schema changes.

## Risks and Mitigations
- Risk: contract overgrowth introduces UI framework assumptions.
  - Mitigation: keep contracts rendering-agnostic and framework-neutral.
- Risk: unstable contract evolution causes plugin churn.
  - Mitigation: stability labels, semver rules, and compatibility adapters.

## Rollback Approach
- Revert to previous contract package version when compatibility regressions are detected.
- Ship compatibility shim for non-breaking migration path where possible.
- Log rejected plugin diagnostics for operator visibility during rollback window.

## Completion Criteria
- `@prosto/platform-admin-contracts` package exists and is versioned.
- UI plugin manifest and discovery payload schemas are implemented and test-validated.
- Compatibility result taxonomy is stable and documented.
- Public exports include stability labels and pass contract checks.
