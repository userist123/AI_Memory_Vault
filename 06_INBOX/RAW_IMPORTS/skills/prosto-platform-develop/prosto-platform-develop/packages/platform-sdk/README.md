# @prosto/platform-sdk

Contract authority for Prosto platform module manifests, lifecycle interfaces, typed tokens, and validation primitives.

## Status
- Phase 03 baseline completed
- Phase 04 contract conformance validation is active via `@prosto/platform-contract-tests` and `npm run test:contracts`
- All exported contracts are marked `@alpha`

## Relationship to Admin Contracts

`@prosto/platform-admin-contracts` is a separate contract authority package for admin shell and UI plugin integration. It defines:
- UI plugin manifests
- Discovery payloads
- Permission policies
- Compatibility rules

Both packages follow the same contract-first methodology and stability levels.

## Public API

### Constants
- `MODULE_LIFECYCLE_STAGES`
- `STARTUP_POLICIES`
- `MODULE_ID_PATTERN`
- `MODULE_CAPABILITY_PATTERN`
- `MODULE_SECURITY_CLASSES`
- `MODULE_CRITICALITY_LEVELS`
- `SERVICE_TOKEN_NAME_PREFIX`
- `EVENT_TOKEN_NAME_PREFIX`

### Types
- `PlatformModuleLifecycleStageType`
- `PlatformStartupPolicyType`
- `PlatformModuleLifecycleResultType`
- `ModuleIdentifierType`
- `SemverVersionType`
- `SemverRangeType`
- `ModuleSecurityClassType`
- `ModuleCriticalityType`
- `ModuleCapabilityType`
- `ServiceTokenType<TService>`
- `EventTokenType<TPayload>`
- `EventHandlerType<TPayload>`
- `PlatformModuleManifestValidationResultType`
- `PlatformModuleCompatibilityValidationResultType`
- `PlatformModuleManifestInputType`
- `PlatformModuleManifestOutputType`
- `PlatformModuleCompatibilityIssueCodeType`

### Interfaces
- `IServiceRegistry`
- `IEventBus`
- `IEventMetadata`
- `IEventEnvelope<TPayload>`
- `IPlatformModuleLogger`
- `IPlatformModuleContext`
- `IPlatformModule`
- `IPlatformModuleIdentity`
- `IPlatformModuleCompatibility`
- `IPlatformModuleDependency`
- `IPlatformModuleManifest`
- `IPlatformRuntimeVersionContext`
- `IPlatformModuleManifestValidationIssue`
- `IPlatformModuleManifestValidationSuccess`
- `IPlatformModuleManifestValidationFailure`
- `IPlatformModuleCompatibilityValidationIssue`
- `IPlatformModuleCompatibilityValidationSuccess`
- `IPlatformModuleCompatibilityValidationFailure`
- `IPlatformModuleManifestValidator`
- `IPlatformModuleCompatibilityValidator`

### Schemas
- `SemverVersionSchema`
- `SemverRangeSchema`
- `CapabilitySchema`
- `PlatformModuleDependencySchema`
- `PlatformModuleManifestSchema`

### Utilities
- `isSemverVersion`
- `isSemverRange`
- `isSemverSatisfied`
- `getServiceTokenKey`
- `getEventTokenKey`
- `createServiceToken`
- `createEventToken`
- `resolveNestedValue`

### Validators
- `PlatformModuleManifestValidator`
- `PlatformModuleCompatibilityValidator`

### Errors
- `PlatformSdkError`
- `PlatformModuleManifestValidationError`
- `PlatformModuleCompatibilityValidationError`
- `PersistenceError`
- `PersistenceNotReadyError`

### Persistence
- `PersistenceDescriptorRegistry` — in-memory registry enforcing descriptor ownership and immutability
- `IPersistenceDescriptor` — generic persistence declaration with owner, ownerId, payload, and required driver capabilities
- `IPersistenceDescriptorRegistry` — contract for collecting persistence declarations during module init
- `IPersistenceInitializationInput` — input supplied after descriptor collection is sealed
- `IPersistenceProvider` — shared persistence adapter lifecycle contract (initialize/dispose/state)
- `IPersistenceModuleContext` — persistence surface exposed in module context
- `PersistenceProviderStateType` — lifecycle states: `collecting` | `initializing` | `ready` | `failed` | `disposed`
- `PersistenceOwnerType` — `platform` | `module`

## Usage

```ts
import {
  PlatformModuleCompatibilityValidator,
  PlatformModuleManifestValidator,
  createEventToken,
  createServiceToken,
} from '@prosto/platform-sdk';

const manifestValidator = new PlatformModuleManifestValidator();
const compatibilityValidator = new PlatformModuleCompatibilityValidator();

const manifest = manifestValidator.parse({
  id: 'module-health',
  version: '1.2.3',
  sdkVersion: '^0.1.0',
  criticality: 'standard',
  securityClass: 'internal',
  capabilities: ['feature.health'],
  dependencies: [],
});

const compatibility = compatibilityValidator.validate(manifest, {
  sdkVersion: '0.1.5',
});

const healthEventToken = createEventToken<{ status: 'ok' | 'failed' }>('health.updated');
const healthServiceToken = createServiceToken<{ ping: () => 'ok' | 'failed' }>('health.service');

eventBus.subscribe(healthEventToken, ({ payload }) => {
  if (payload.status === 'ok') {
    logger.info('Healthy');
  } else {
    logger.warn('Unhealthy');
  }
});

serviceRegistry.register(healthServiceToken, {
  ping: () => 'ok',
});

const healthService = serviceRegistry.resolve(healthServiceToken);
```

## Commands
- `npm run --workspace @prosto/platform-sdk build`
- `npm run --workspace @prosto/platform-sdk typecheck`
- `npm run --workspace @prosto/platform-sdk test`
- `npm run --workspace @prosto/platform-sdk test:unit`
- `npm run --workspace @prosto/platform-sdk test:types`

## Notes
- This package is contract-only and does not implement runtime module loading.
- The SDK exposes optional persistence contracts that `platform-core` consumes for the shared persistence adapter lifecycle.
- Runtime lifecycle orchestration and persistence composition belong to `@prosto/platform-core`.
- Runtime validation primitives depend only on `zod` and `semver`.
