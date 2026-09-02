# Local Authentication Operations Guide

## Scope

Local authentication is a supported platform authentication mode for
development, test, and production. It verifies username/password credentials;
it is not a development bypass. The default example stores local state in
`examples/admin-bff-http-host/.prosto/local-auth.sqlite`.

The state database contains Argon2id password hashes and SHA-256 hashes of
opaque session and CSRF tokens. It does not contain plaintext passwords, raw
session identifiers, or raw CSRF tokens.

## Startup And Bootstrap

For local development, start the BFF with `npm run start:local` and the shell
with `npm run --workspace @prosto/platform-admin-shell dev`. A first start with
an interactive stdout creates one `admin` account and prints its one-time
password only to that TTY. The account must change that password before it can
access admin BFF endpoints.

An empty database started without an interactive TTY fails before HTTP routes
open. Initialize it from an interactive terminal instead:

```bash
npm run auth:bootstrap-local -- --database examples/admin-bff-http-host/.prosto/local-auth.sqlite
```

The bootstrap command prints a credential only when it creates the first
account. Re-running it against an initialized database does not reveal or
replace an existing password.

## Account Recovery And Password Rotation

There is no self-service password reset, email recovery, or MFA flow. An
operator who has lost all administrative credentials must use the approved
deployment recovery procedure. For disposable non-production state, stop the
BFF, delete the `.prosto` directory, then bootstrap again interactively. This
destroys every local account and session and must not be used as a production
recovery shortcut.

Administrators rotate a known password through the password-change page. A
successful password change invalidates that account's existing sessions and
issues a new session, so open browser sessions must sign in again. To remove a
compromised account or recover production access, use a reviewed, audited
database change procedure that preserves the Argon2id password-hash policy; do
not insert plaintext credentials or copy session values into SQLite.

## Backup, Restore, And Sessions

Back up SQLite only while it is quiesced or through a SQLite-consistent backup
mechanism. Include the database together with its SQLite journal/WAL files when
the selected backup method requires them. Protect backups like authentication
data because they contain account password hashes and session-token hashes.

A restore can resurrect database-backed sessions that existed at backup time.
After a restore, invalidate all local sessions before returning the BFF to
service, then require users to sign in again. Validate the restored database
and its migrations in an isolated environment before production use.

## Production File Ownership And Transport

The service identity must own the state directory and database files. Restrict
directory access to that identity and backup operators; do not place `.prosto`
in a shared web root or source checkout available to other users. Monitor free
space and perform restore drills.

Local authentication on a public origin requires HTTPS and secure cookies.
Loopback HTTP is the only plaintext exception. TLS termination remains the
responsibility of the trusted ingress or reverse proxy.

## Migration To OIDC

OIDC remains an explicit alternative. Configure `ADMIN_BFF_AUTH_MODE=oidc` and
provide all existing bearer, browser OIDC, client-secret, and AES key-ring
settings through deployment-owned configuration and secret management. Validate
the OIDC deployment in a staging environment before cutover.

Local accounts and opaque local sessions are not OIDC identities or tokens and
must not be migrated into an identity provider. Plan account provisioning and
role mapping in the identity provider, then retire local access only after OIDC
administrator access is verified. Retain the local SQLite backup according to
the security retention policy.

## Destructive Reset

Deleting `.prosto` permanently destroys local accounts, password hashes,
failed-login state, and sessions. It is appropriate only for intentionally
discarded non-production state. Never treat deletion as a routine production
maintenance action.
