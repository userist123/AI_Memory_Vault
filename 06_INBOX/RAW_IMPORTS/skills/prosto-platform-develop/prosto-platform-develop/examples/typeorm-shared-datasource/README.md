# Shared TypeORM DataSource Example

This application composes `RuntimeBuilder`, `TypeOrmPersistenceProvider`, a
platform descriptor, and an `orders` module against a local SQLite database.

```bash
npm run typecheck --workspace=@examples/typeorm-shared-datasource
npm run start --workspace=@examples/typeorm-shared-datasource
```

The source in `src/index.ts` is the complete composition example. It registers
descriptors in `init()`, resolves the native token in `start()`, then stops the
runtime so the provider closes its shared DataSource.

`config/app_settings.json` contains only non-secret SQLite settings. For a
deployment-local override, create `config/app_settings.local.json` from
`config/app_settings.local.example.json`; never commit the local file. A server
deployment may use that ignored override for its password or URL after changing
the safe dialect settings in `app_settings.json`.
