# ADR-0010 Local Authentication Adapter And Opaque Sessions

Date: 2026-08-12  
Status: Accepted

## Context

The admin BFF must run without an external OpenID Connect provider while
retaining the platform's micro-core, contract-first boundaries. Adding
credential verification, persistence, HTTP cookies, or UI dependencies to
`platform-core` would violate ADR-0001 and couple the generic lifecycle kernel
to one authentication mechanism.

The local mode also requires session security without introducing a long-lived
local JWT signing secret that operators must provision, rotate, and protect.

## Decision

Local authentication is composed from a framework-neutral adapter and a
persistence module:

- `@prosto/platform-adapter-auth-local` owns credential verification, opaque
  session, cookie, and CSRF policies behind injected ports.
- `@prosto/platform-module-auth-local-session` owns TypeORM entities,
  migrations, stores, bootstrap lifecycle, and the adapter's persistence
  wiring.
- `@prosto/platform-sdk` provides only generic authentication-provider and HTTP
  contracts. `@prosto/platform-admin-contracts` owns BFF payload contracts.
- The executable BFF host selects either the local provider or the OIDC
  provider. Neither provider imports the admin shell.
- Local sessions are random, database-backed opaque values. The database stores
  SHA-256 hashes of session and CSRF tokens, not raw tokens. Server-side lookup
  and revocation therefore avoid a local signing key while supporting expiry,
  rotation, and account-wide invalidation.

## Consequences

### Positive

- `platform-core` remains free of authentication, ORM, HTTP, and UI concerns.
- Local mode runs without OIDC issuer, JWKS, client-secret, or signing-key
  configuration.
- Session revocation, forced password changes, and restore recovery are
  immediate database operations rather than token-lifetime exceptions.
- OIDC remains available as an explicit provider with its existing redirect and
  validation behavior.

### Trade-offs

- Local authentication needs durable database availability and backup
  procedures; SQLite is not a shared multi-host session store.
- Operators must protect database files and invalidate sessions after restore.
- There is no self-service password recovery in this mode.

## Related

- [ADR-0001 Micro-Core Kernel Boundary](./ADR-0001-micro-core-kernel-boundary.md)
- [ADR-0002 SDK Contract And Semver Governance](./ADR-0002-sdk-contract-and-semver-governance.md)
- [ADR-0009 Hybrid Admin UI Model](./ADR-0009-admin-ui-hybrid-shell-plugin-model.md)
- [Local Authentication Operations Guide](../../../docs/operations/local-authentication.md)
