# TypeORM Shared DataSource Guide

`@prosto/platform-adapter-typeorm` supplies one TypeORM `DataSource` for a
platform runtime and all loaded modules. The adapter is composed by the
application; `@prosto/platform-core` and `@prosto/platform-sdk` do not import
TypeORM.

The runnable reference composition is in
[`examples/typeorm-shared-datasource`](../../examples/typeorm-shared-datasource).

## Install A Driver

Install the adapter and exactly the peer driver selected by
`persistence.typeorm.type` in the consuming application.

| Dialect | Peer dependency |
| --- | --- |
| PostgreSQL | `pg` |
| MySQL 8 | `mysql2` |
| MariaDB | `mysql2` |
| SQLite | `sqlite3` |
| SQL Server | `mssql` |

For certification images, CI variables, and engine-specific constraints, see
[TypeORM Dialect Support](typeorm-dialect-support.md). Selecting a dialect
without its installed peer fails startup with `PersistenceDriverUnavailable`.

## Application Composition

Applications compose the provider and optional platform descriptor at the
runtime boundary. TypeORM constructors stay in the application or module, not
in platform core.

```ts
const runtime = new RuntimeBuilder().build({
  configDir: './config',
  persistenceProvider: new TypeOrmPersistenceProvider(),
  platformPersistenceDescriptor,
  modules: moduleArtifacts,
});
```

The platform descriptor must use `owner: 'platform'`, `ownerId: 'platform'`,
explicit `platform_` entity table names, and `platform_` migration names.

## Module Lifecycle

Modules register one descriptor only in `init()`:

```ts
init(ctx: IPlatformModuleContext): void {
  ctx.persistence?.descriptors?.register(ctx.moduleId, {
    owner: 'module',
    ownerId: ctx.moduleId,
    payload: createTypeOrmPersistenceDescriptor({
      entities: [OrderEntity],
      migrations: [orders_create_order1710000000000],
    }),
  });
}
```

Every entity must declare a literal portable table name. A module with ID
`orders-api` owns the normalized `orders_api_` prefix. Migration constructors,
not source glob paths, are required. Descriptors are sealed after all successful
`init()` calls; they are not exposed in `start()` or `stop()`.

Resolve the full shared DataSource only after provider readiness, in `start()`
or later work:

```ts
const dataSource = ctx.services.resolveRequired(
  TYPEORM_DATA_SOURCE_SERVICE_TOKEN,
);
```

The token is published only after connection initialization, migration lock
release, and successful migration execution. It is removed when the runtime
stops and the provider disposes its DataSource.

## Configuration And Local Secrets

The builder loads configuration in this order:

1. Package `app_settings.json`.
2. Package `app_settings.{environment}.json`.
3. `configDir/app_settings.json`.
4. `configDir/app_settings.{environment}.json`.
5. `configDir/app_settings.local.json`.
6. `PROSTO_` environment variables.
7. CLI overrides.

Local discovery is limited to the explicitly supplied `configDir`; the working
directory is never scanned. Commit safe dialect settings in `app_settings.json`.
Put a password or URL only in the ignored `app_settings.local.json`, for
example:

```json
{
  "persistence": {
    "typeorm": {
      "password": "deployment-secret"
    }
  }
}
```

Do not combine `url` with structured host, port, database, username, password,
schema, or pool fields. `synchronize` must be `false`; the adapter also forces
TypeORM `migrationsRun` to `false` and controls migration timing itself.

## Ownership And Migrations

The shared DataSource is intentionally not an access-control boundary. Prefix
validation protects declarations but cannot stop raw SQL or repositories from
accessing a foreign table. Apply these mandatory conventions in module review
and conformance tests:

- A platform descriptor owns only `platform_` tables.
- A module owns only its normalized `<module-id>_` tables.
- Cross-module relations and foreign keys are unsupported.
- Do not issue raw SQL against another owner's tables.
- Use forward-only, versioned migrations for schema changes. Correct a released
  migration with a new migration; do not rewrite migration history.

## Startup And Failures

The runtime runs all permitted module `init()` handlers, validates and seals
descriptors, initializes one DataSource, acquires a dialect-specific database
lock, runs the complete migration set once, publishes the native token, and
only then calls module `start()` handlers.

Migration, descriptor, lock, driver, and connection failures abort startup in
both `strict` and `best-effort` policies. No module `start()` runs after such a
failure. PostgreSQL, MySQL/MariaDB, and SQL Server use database locks; SQLite
uses an exclusive file lock and supports one startup writer per database file.
Diagnostics are redacted and never include effective connection settings.
