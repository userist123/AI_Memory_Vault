# Phase 03 - SDK Contract Baseline and Manifest Validation

## Execution Status
- Status: Completed
- Completed on: 2026-03-29
- Validation date: 2026-03-30
- Repository evidence:
  - `packages/platform-sdk/package.json`
  - `packages/platform-sdk/src/index.ts`
  - `packages/platform-sdk/src/types/manifest.types.ts`
  - `packages/platform-sdk/src/types/lifecycle.types.ts`
  - `packages/platform-sdk/src/types/tokens.types.ts`
  - `packages/platform-sdk/src/constants/lifecycle.constants.ts`
  - `packages/platform-sdk/src/constants/manifest.constants.ts`
  - `packages/platform-sdk/src/constants/tokens.constants.ts`
  - `packages/platform-sdk/src/interfaces/platform-module.interface.ts`
  - `packages/platform-sdk/src/interfaces/platform-module-manifest.interfaces.ts`
  - `packages/platform-sdk/src/interfaces/module-context.interface.ts`
  - `packages/platform-sdk/src/interfaces/service-registry.interface.ts`
  - `packages/platform-sdk/src/interfaces/event-bus.interfaces.ts`
  - `packages/platform-sdk/src/interfaces/module-logger.interface.ts`
  - `packages/platform-sdk/src/errors/platform-sdk.error.ts`
  - `packages/platform-sdk/src/errors/manifest-validation.error.ts`
  - `packages/platform-sdk/src/errors/compatibility-validation.error.ts`
  - `packages/platform-sdk/src/schemas/manifest.schema.ts`
  - `packages/platform-sdk/src/utils/semver.utils.ts`
  - `packages/platform-sdk/src/utils/tokens.utils.ts`
  - `packages/platform-sdk/src/validation/platform-module-manifest.validator.ts`
  - `packages/platform-sdk/src/validation/platform-compatibility.validator.ts`
  - `packages/platform-sdk/tests/manifest-validation.test.ts`
  - `packages/platform-sdk/tests/compatibility-validation.test.ts`
  - `packages/platform-sdk/tests/tokens-utils.test.ts`
  - `packages/platform-sdk/tests/tokens.type-test.ts`
  - `packages/platform-sdk/vitest.config.ts`
  - `packages/platform-sdk/API_REPORT.md`
  - `packages/platform-sdk/README.md`

## Phase Objective
Implement `@prosto/platform-sdk` as the single contract authority for modules, manifests, lifecycle interfaces, service tokens, and validation primitives.

## Scope Boundaries
### In Scope
- Define core public types and interfaces for module lifecycle.
- Define manifest schema and compatibility metadata model.
- Define service and event token strategy.
- Add stability-level annotations for exported API surfaces.

### Out of Scope
- Runtime loader implementation.
- Full observability pipeline implementation.
- External module template repository.

## Prerequisites and Dependencies
- Phase 02 workspace and package skeleton complete.
- ADR and domain model references:
  - `.context/02-architecture-design/adr/ADR-0001-micro-core-kernel-boundary.md`
  - `.context/02-architecture-design/02-domain-and-capability-model.md`
  - `.context/02-architecture-design/adr/ADR-0002-sdk-contract-and-semver-governance.md`

## Delivered Implementation Steps
1. Implemented manifest types in `platform-sdk/src/types`:
   - module identity
   - version ranges
   - security class
   - criticality
   - capabilities
2. Implemented lifecycle and context interfaces in `platform-sdk/src/interfaces`:
   - `PlatformModule`
   - `ModuleContext`
   - `ServiceRegistry`
   - `EventBus`
3. Implemented token model via `platform-sdk/src/types` (token brands), `src/constants` (prefixes), and `src/utils` (typed token helpers).
4. Implemented manifest schema and semantic validation helpers in `platform-sdk/src/validation`.
5. Defined explicit error model for validation and compatibility failures.
6. Added package-level API report and stability labels for each exported symbol.
7. Added unit and type-level tests for:
   - schema validation pass/fail cases
   - semver compatibility validation
   - token uniqueness and typing behavior

## Code Examples
### Example: module contract interface
```typescript
export interface PlatformModule {
  manifest: PlatformModuleManifest;
  register(ctx: ModuleContext): Promise<void> | void;
  init(ctx: ModuleContext): Promise<void> | void;
  start(ctx: ModuleContext): Promise<void> | void;
  stop(ctx: ModuleContext): Promise<void> | void;
}
```

### Example: manifest schema contract
```typescript
export const PlatformModuleManifestSchema = z.object({
  id: z.string().min(3),
  version: z.string(),
  sdkVersion: z.string(),
  criticality: z.enum(['standard', 'critical']),
  securityClass: z.enum(['trusted', 'internal', 'third-party-reviewed']),
  capabilities: z.array(z.string()).min(1)
});
```

### Example: typed service token
```typescript
export type ServiceToken<T> = symbol & { readonly __type?: T };

export const SERVICE_TOKEN_NAME_PREFIX = 'PRST_PL_SERVICE_'

export function createServiceToken<T>(name: string): ServiceToken<T> {
  return Symbol.for(SERVICE_TOKEN_NAME_PREFIX + name) as ServiceToken<T>;
}
```

## Delivered Files Overview
- `packages/platform-sdk/package.json`
- `packages/platform-sdk/src/index.ts`
- `packages/platform-sdk/src/types/*.ts`
- `packages/platform-sdk/src/interfaces/*.ts`
- `packages/platform-sdk/src/constants/*.ts`
- `packages/platform-sdk/src/utils/*.ts`
- `packages/platform-sdk/src/schemas/*.ts`
- `packages/platform-sdk/src/validation/*.ts`
- `packages/platform-sdk/src/errors/*.ts`
- `packages/platform-sdk/tests/*.test.ts`
- `packages/platform-sdk/vitest.config.ts`
- `packages/platform-sdk/API_REPORT.md`

## Validation and Testing Approach
- Package tests run through `npm run --workspace @prosto/platform-sdk test` (Vitest unit tests plus type-level checks).
- Build and declaration checks run through `npm run --workspace @prosto/platform-sdk build` and `typecheck`.
- Public API report recorded in `packages/platform-sdk/API_REPORT.md` to track contract surface drift.

## Data or Migration Impact
- No runtime data migration.
- Contract migration impact appears when modules adopt SDK version; include migration notes for breaking changes.

## Risks and Mitigations
- Risk: over-expanding SDK with runtime implementation concerns.
  - Mitigation: enforce contract-only boundary and API review gate.
- Risk: unstable contracts early on causing downstream churn.
  - Mitigation: stability labels and semver governance policy from day one.

## Rollback Approach
- If newly introduced contract proves incorrect, rollback via minor/patch deprecation when possible.
- For critical contract mistakes, ship explicit migration helper and compatibility adapter in next release line.

## Completion Criteria
- SDK exports lifecycle, manifest, token, and error contracts required by architecture docs.
- Manifest schema validates mandatory governance fields.
- Stability labels exist for all public exports.
- Unit and type-level tests pass with CI evidence.
