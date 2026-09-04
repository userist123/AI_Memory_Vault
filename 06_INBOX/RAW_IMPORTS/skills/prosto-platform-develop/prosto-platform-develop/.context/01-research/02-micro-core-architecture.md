# 02 - Micro-Core Architecture

Date: 2026-03-23

## Target Architecture

```text
platform-kernel (core)
  |- config engine
  |- service registry (DI-lite)
  |- lifecycle manager
  |- module loader
  |- hook/event bus
  |- capability/compatibility checker

adapters (optional packages)
  |- http adapter
  |- persistence adapter
  |- queue adapter
  |- auth adapter

modules/plugins (external repos)
  |- feature modules
  |- integration modules
  |- admin/backoffice modules
```

## Core Responsibilities (Keep Minimal)
- Bootstrapping and controlled shutdown.
- Plugin discovery from configured module descriptors.
- Contract validation (manifest, version compatibility, required capabilities).
- Deterministic lifecycle order (`register -> init -> start -> stop`).
- Shared service container and event bus with typed tokens.

## Things Core Should Not Own
- HTTP framework specifics.
- Database ORM specifics.
- Vendor API clients.
- Domain-specific business features.

## Proposed Plugin Contract

```ts
export type TModuleDependency = {
  /** Module ID */ 
  id: string;
  /** Module version */
  version: string;
}

export type TModuleAuthor = {
  name: string;
  url?: string;
  email?: string
}

export type TModuleOwner = TModuleAuthor
  
export interface PlatformModuleManifest {
  /** Module ID */
  id: string;
  /** Module version */
  version: string;
  /** Minimum supported version of the platform */
  platformVersion: string;
  /** Displayed name */
  title: string;
  /** Description of the module */
  description?: string;
  /** Enumeration of the module's capabilities */
  capabilities?: string[];
  /** The URL of the module icon */
  iconUrl?: string;
  /** Dependencies on other modules */
  dependencies?: TModuleDependency[];
  authors?: (string|TModuleAuthor)[];
  owners?: (string|TModuleOwner)[];
  criticality?: 'standard' | 'critical';
}

export interface PlatformModule {
  manifest: PlatformModuleManifest;
  register(ctx: RegisterContext): Promise<void> | void;
  init?(ctx: InitContext): Promise<void> | void;
  start?(ctx: StartContext): Promise<void> | void;
  stop?(ctx: StopContext): Promise<void> | void;
}
```

## Lifecycle Rules
- `register`: define services, hooks, routes, commands.
- `init`: validate configuration and prepare resources.
- `start`: activate listeners/workers.
- `stop`: graceful cleanup with timeout.

No module should execute side effects at import time.

## Dependency Management Strategy
- Use `peerDependencies` for `@prosto/platform-sdk` to enforce contract version alignment.
- Core validates module `platformVersion` at startup.
- Module dependency graph must be acyclic.

## Extension Model
- Typed extension points:
  - `hooks.beforeStart`
  - `hooks.afterStart`
  - `hooks.beforeStop`
  - domain hooks (defined by adapters/modules)
- Prefer explicit contracts over convention-only extension.

## Error Boundaries
- Module load failures should be isolated and include structured metadata:
  - module name
  - lifecycle phase
  - error code
  - remediation hint
- Platform boot policy should be configurable:
  - `strict`: fail startup when critical module fails
  - `best-effort`: continue without non-critical module

## Recommended Core Packages
- `@prosto/platform-sdk`: types/interfaces/errors/tokens.
- `@prosto/platform-core`: runtime kernel.
- `@prosto/platform-cli`: scaffolding, diagnostics, validation commands.

Continue with: [03 - Module Repositories Strategy](./03-module-repositories-strategy.md).
