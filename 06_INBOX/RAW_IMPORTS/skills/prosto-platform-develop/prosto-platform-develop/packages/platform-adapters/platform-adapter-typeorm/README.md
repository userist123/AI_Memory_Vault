# TypeORM Persistence Adapter

`@prosto/platform-adapter-typeorm` provides a shared TypeORM `DataSource` for
one Prosto Platform runtime. It is an `@alpha` adapter and is composed by an
application through `RuntimeBuilder`; platform core does not depend on TypeORM.

## Install

Install the adapter and the one driver selected by application configuration:

```bash
npm install @prosto/platform-adapter-typeorm sqlite3
```

Use `pg` for PostgreSQL, `mysql2` for MySQL or MariaDB, and `mssql` for SQL
Server. Driver certification and operational constraints are documented in
[`docs/persistence/typeorm-dialect-support.md`](../../docs/persistence/typeorm-dialect-support.md).

## Usage

```ts
import { RuntimeBuilder } from '@prosto/platform-core';
import {
  TypeOrmPersistenceProvider,
} from '@prosto/platform-adapter-typeorm';

const runtime = new RuntimeBuilder().build({
  configDir: './config',
  persistenceProvider: new TypeOrmPersistenceProvider(),
  platformPersistenceDescriptor,
  modules,
});
```

Use `createTypeOrmPersistenceDescriptor()` with explicit entity and migration
constructors during module `init()`. Resolve
`TYPEORM_DATA_SOURCE_SERVICE_TOKEN` only in `start()` or later. The token is
published after locked migrations succeed and is removed during provider
disposal.

The complete consumer flow, safe local configuration template, platform
descriptor, and module descriptor are available in
[`examples/typeorm-shared-datasource`](../../examples/typeorm-shared-datasource).
See [the shared DataSource guide](../../docs/persistence/typeorm-shared-datasource-guide.md)
for lifecycle restrictions, ownership conventions, configuration precedence,
startup failures, and forward-only migration rollback.

## Public API

### Classes
- `TypeOrmPersistenceProvider` — implements `IPersistenceProvider`; owns the shared `DataSource` lifecycle

### Utilities
- `createTypeOrmPersistenceDescriptor({ entities, migrations })` — builds an adapter-owned descriptor payload for module registration
- `getTypeOrmPersistenceDescriptorPayload(descriptor)` — extracts validated TypeORM metadata from a descriptor
- `collectValidatedTypeOrmMetadata(descriptors, dialect)` — collects and validates entities and migrations across all registered descriptors

### Tokens
- `TYPEORM_DATA_SOURCE_SERVICE_TOKEN` — typed service token for the ready shared `DataSource` (valid only after provider readiness)

### Interfaces
- `ITypeOrmPersistencePlatformConfig` — driver-neutral TypeORM persistence settings (dialect, host, port, database, credentials, pool, migration transaction mode)
- `ITypeOrmPersistenceDescriptorPayload` — TypeORM-specific descriptor payload (`entities`, `migrations`)
- `IMigrationLock` — dialect-specific database migration lock (`acquire`/`release`)
- `IMigrationLockFactoryInterface` — factory for creating migration lock instances

### Error Codes
All persistence failures surface as `PersistenceError` with structured, redacted details:
- `PersistenceRegistryNotCollecting` — descriptor registered after collection sealed
- `PersistenceDescriptorOwnerMismatch` — descriptor owner does not match registering module
- `PersistenceDuplicateDescriptor` — duplicate descriptor for same module
- `PersistenceProviderNotReady` — native token resolved before provider readiness
- `PersistenceDescriptorValidationFailed` — invalid entity or migration metadata
- `PersistenceMigrationLockTimeout` — database lock acquire timed out
- `PersistenceDriverUnavailable` — peer driver package not installed
- `PersistenceInitializationFailed` — DataSource initialization failure
- `PersistenceMigrationFailed` — migration execution failure

## Commands
- `npm run --workspace @prosto/platform-adapter-typeorm build`
- `npm run --workspace @prosto/platform-adapter-typeorm typecheck`
- `npm run --workspace @prosto/platform-adapter-typeorm test`
- `npm run --workspace @prosto/platform-adapter-typeorm test:integration` (requires `PROSTO_TYPEORM_INTEGRATION=1` and dialect environment variables)
