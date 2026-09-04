# Admin BFF HTTP Host Example

Executable composition root that uses `RuntimeBuilder` and wires
`@prosto/platform-adapter-admin-bff` to `@prosto/platform-adapter-http`
without creating a dependency between the two adapter packages.

`PlatformAdminBffRuntimeHost` owns the platform runtime, creates the concrete
discovery, permission and diagnostics services, converts Admin BFF handlers
into SDK route registrations, rejects anonymous identities before BFF handlers
run, and registers platform health, readiness, and composition-owned session
routes before startup.

Lifecycle ordering is `runtime.start()` then `httpServer.start()`. Shutdown
first stops the HTTP listener and then calls `runtime.stop()`, preventing new
requests from reaching a stopping runtime.

## Commands

```bash
npm run --workspace @examples/admin-bff-http-host typecheck
npm run --workspace @examples/admin-bff-http-host test
npm run --workspace @examples/admin-bff-http-host build
npm run start:local
npm run --workspace @prosto/platform-admin-shell dev
```

## Local Authentication

`npm run start:local` builds this host and starts it with
`config/local.env`. It selects local authentication, SQLite state at
`.prosto/local-auth.sqlite`, host `127.0.0.1`, and port `3001`; it does not
load `.env` and does not require any `ADMIN_BFF_AUTH_*` OIDC values.

On the first interactive start, the host prints a one-time `admin` password
directly to the TTY. It stores only an Argon2id hash and requires a password
change before allowing admin BFF access. The shell development server proxies
same-origin `/admin` and `/auth` paths to this host.

If a blank local database must be initialized by a non-interactive deployment,
run this command from an interactive terminal before starting the service:

```bash
npm run auth:bootstrap-local -- --database examples/admin-bff-http-host/.prosto/local-auth.sqlite
```

## OIDC Authentication

`npm run --workspace @examples/admin-bff-http-host start` builds the host and
runs Node with `--env-file=.env.example`. The supplied file is a template for
an explicit OIDC deployment, not a local `.env` file. Keep OIDC client secrets
and `ADMIN_BFF_SESSION_KEY_RING_JSON` in the deployment secret manager.

`installShutdownHandlers()` belongs to the runtime entry point and subscribes
to `SIGINT` and `SIGTERM`; it awaits `host.stop()` before exiting.

In OIDC mode, the host requires `ADMIN_BFF_CONFIG_DIR`, bearer OIDC configuration
(`ADMIN_BFF_AUTH_ISSUER`, `ADMIN_BFF_AUTH_JWKS_URI`, and
`ADMIN_BFF_AUTH_AUDIENCES_JSON`), browser OIDC configuration, and a deployment
injected AES key ring. Optional host values are `ADMIN_BFF_HTTP_HOST`,
`ADMIN_BFF_HTTP_PORT`, `ADMIN_BFF_ADMIN_MANIFESTS_JSON`, and
`ADMIN_BFF_ADMIN_SHELL_VERSION`.

`ADMIN_BFF_CONFIG_DIR` contains deployment-owned `app_settings.json` and
optional `app_settings.local.json`. These files enable TypeORM and select the
connection/dialect. Core persistence environment overrides remain exclusively
under `PROSTO_PERSISTENCE__TYPEORM__...`; all host-owned settings use the
`ADMIN_BFF_` namespace. The OIDC client secret and
`ADMIN_BFF_SESSION_KEY_RING_JSON` are injected by the deployment secret manager
and are never stored in this example configuration.

The non-secret `app_settings.json` persistence shape is:

```json
{
  "persistence": {
    "typeorm": {
      "enabled": true,
      "type": "postgres",
      "host": "database.internal",
      "port": 5432,
      "database": "prosto_admin",
      "username": "prosto_admin",
      "synchronize": false,
      "migrationsRun": true
    }
  }
}
```

Deployment-local secret overrides belong in `app_settings.local.json` or the
existing `PROSTO_PERSISTENCE__TYPEORM__...` core environment override, not in
this example.

OIDC browser use requires a same-origin HTTPS ingress or reverse proxy. Cookies
are always secure and CORS credentials are not configured by this host. Local
authentication permits loopback HTTP only; a public local origin must use HTTPS
and secure cookies.
