# Contract-First Development Rules

## SDK Contract Priority

### Implementation Order

**ALWAYS implement in this order:**

1. **Define types in `platform-sdk`** BEFORE implementing in `platform-core`
2. **Define manifest schema** BEFORE implementing module loader
3. **Define lifecycle interfaces** BEFORE implementing orchestrator
4. **Define service tokens** BEFORE implementing service registry
5. **Define error types** BEFORE implementing error handling

### Example: Module Contract Development

```typescript
// ✅ Step 1: Define contract in platform-sdk
// packages/platform-sdk/src/interfaces/platform-module-manifest.interfaces.ts
export interface IPlatformModuleManifest {
  id: string;
  version: string;
  platformVersion: string;
  criticality: 'critical' | 'standard' | 'optional';
  securityClass: 'trusted' | 'internal' | 'third-party-reviewed';
  capabilities: string[];
  dependencies: string[];
  services: IServiceDefinition[];
  hooks: IHookDefinition[];
}

// ✅ Step 2: Define lifecycle interface
// packages/platform-sdk/src/interfaces/platform-module.interface.ts
export interface IPlatformModule {
  readonly manifest: IPlatformModuleManifest;
  
  // Lifecycle phases (in order)
  register(ctx: IModuleContext): Promise<void>;
  init(ctx: IModuleContext): Promise<void>;
  start(ctx: IModuleContext): Promise<void>;
  stop(ctx: IModuleContext): Promise<void>;
}

// ✅ Step 3: Implement in platform-core using contracts
// packages/platform-core/src/lifecycle/module-lifecycle.orchestrator.ts
import { IPlatformModule, IModuleContext } from '@prosto/platform-sdk';

export class ModuleLifecycleOrchestrator {
  async executePhase(module: IPlatformModule, phase: LifecyclePhase, ctx: IModuleContext): Promise<void> {
    // Implementation uses contract types
  }
}
```

---

## Breaking Changes Policy (ADR-0002)

### When Breaking Changes Are Allowed

**Breaking changes to `platform-sdk` public API require:**

1. ✅ ADR reference documenting rationale
2. ✅ Compatibility statement (migration path)
3. ✅ Deprecation period (minimum 1 minor version)
4. ✅ Migration guide for module developers

### Breaking Change Examples

```typescript
// ❌ Breaking: Remove required field
interface IModuleManifest {
  // Removed: id: string;
}

// ✅ Non-breaking: Add optional field
interface IModuleManifest {
  id: string;
  description?: string; // New optional field
}

// ❌ Breaking: Change field type
interface IModuleManifest {
  id: number; // Was: string
}

// ✅ Non-breaking: Extend union type
type TCriticality = 'critical' | 'standard' | 'optional' | 'experimental'; // Added 'experimental'
```

### Deprecation Process

```typescript
// Step 1: Mark as deprecated (minor version)
/**
 * @deprecated Use `TServiceToken` instead. Will be removed in v1.0.0
 */
export type ServiceToken = string;

// Step 2: Provide migration path
export type TServiceToken = `${string}:${string}`;

// Step 3: Remove in next major version
```

---

## Stability Levels

### Required Annotations

**ALL public exports MUST be marked with stability level:**

```typescript
/**
 * @stable
 * Platform module interface - stable since v0.1.0
 */
export interface IPlatformModule {
  // ...
}

/**
 * @beta
 * Extended lifecycle hooks - may evolve in minor releases
 */
export interface IExtendedLifecycleHooks {
  // ...
}

/**
 * @experimental
 * Worker isolation API - no compatibility guarantee
 */
export interface IWorkerIsolation {
  // ...
}

/**
 * @internal
 * Internal utility - not public API, can change without notice
 */
export function _internalHelper(): void {
  // ...
}
```

### Stability Level Definitions

| Level | Meaning | Compatibility | Allowed Consumers |
|-------|---------|---------------|-------------------|
| `@stable` | Default public contract | Backward compatible within major version | All modules and adapters |
| `@beta` | Candidate public contract | May evolve in minor releases with migration notes | Early adopters by opt-in |
| `@alpha` | Early public contract | May evolve in minor releases with migration notes | Early adopters by opt-in |
| `@experimental` | Exploration surface | No compatibility guarantee | Internal use and controlled pilots |
| `@internal` | Not public API | Can change without notice | Package maintainers only |

### Labeling Rules

1. **Every export in `@prosto/platform-sdk`** must have stability level tag
2. **`platform-core` internals** are `@internal` unless explicitly promoted via SDK
3. **Beta and Experimental** contracts require sunset or promotion criteria in release notes

---

## Contract Testing

### Contract Test Structure

```typescript
// packages/platform-contract-tests/src/lifecycle/lifecycle.contract.ts
import { IPlatformModule, IModuleContext } from '@prosto/platform-sdk';

export function createLifecycleContractTests(module: IPlatformModule): void {
  describe('Lifecycle Contract', () => {
    it('should have valid manifest', () => {
      expect(module.manifest.id).toMatch(/^[a-z][a-z0-9-]*$/);
      expect(module.manifest.version).toMatch(/^\d+\.\d+\.\d+$/);
    });

    it('should execute phases in order', async () => {
      const ctx = createMockContext();
      await module.register(ctx);
      await module.init(ctx);
      await module.start(ctx);
      await module.stop(ctx);
    });
  });
}
```

### Module Contract Validation

```typescript
// Module repository test
import { createModuleContractTests } from '@prosto/platform-contract-tests';
import { HealthModule } from '../../src/health.module';

describe('HealthModule Contract Compliance', () => {
  createModuleContractTests(new HealthModule());
});
```

---

## Type Safety Rules

### No `any` Type

```typescript
// ❌ Bad: Using any
function processModule(data: any): void {
  // ...
}

// ✅ Good: Using union types and type guards
type TModuleData = IModuleManifest | unknown;

function isValidManifest(data: unknown): data is IModuleManifest {
  return (
    typeof data === 'object' &&
    data !== null &&
    'id' in data &&
    'version' in data
  );
}

function processModule(data: TModuleData): void {
  if (isValidManifest(data)) {
    // Type-safe access
  }
}
```

### Explicit Return Types

```typescript
// ❌ Bad: Implicit return type
async function loadModule(id: string) {
  return module;
}

// ✅ Good: Explicit return type
async function loadModule(id: string): Promise<IModuleManifest> {
  return module;
}
```

---

## Validation at Boundaries

### Runtime Validation with Zod

```typescript
// packages/platform-sdk/src/validation/manifest.schema.ts
import { z } from 'zod';

export const ModuleManifestSchema = z.object({
  id: z.string().regex(/^[a-z][a-z0-9-]*$/),
  version: z.string().regex(/^\d+\.\d+\.\d+$/),
  platformVersion: z.string(),
  criticality: z.enum(['critical', 'standard', 'optional']),
  securityClass: z.enum(['trusted', 'internal', 'third-party-reviewed']),
  capabilities: z.array(z.string()),
  dependencies: z.array(z.string()),
  services: z.array(ServiceDefinitionSchema),
  hooks: z.array(HookDefinitionSchema),
});

export type TModuleManifest = z.infer<typeof ModuleManifestSchema>;

// Usage in module loader
function validateManifest(raw: unknown): TModuleManifest {
  return ModuleManifestSchema.parse(raw);
}
```

### Input Validation at Module Boundaries

```typescript
// ✅ Good: Validate external input
class ModuleLoader {
  async loadModule(config: unknown): Promise<void> {
    // Validate at boundary
    const manifest = validateManifest(config);
    
    // Now type-safe to use
    await this.initializeModule(manifest);
  }
}
```

---

## Related Documents

- [ADR-0002 SDK Contract And Semver Governance](../../.context/02-architecture-design/adr/ADR-0002-sdk-contract-and-semver-governance.md)
- [03 Phase - SDK Contract Baseline](../../.context/04-implementation-plan/03-phase.md)
- [04 Phase - Contract Conformance](../../.context/04-implementation-plan/04-phase.md)
