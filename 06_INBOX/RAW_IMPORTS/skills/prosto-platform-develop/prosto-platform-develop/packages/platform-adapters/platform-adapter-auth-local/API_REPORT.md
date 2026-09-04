# @prosto/platform-adapter-auth-local API Report

Local Authentication Phase 2 public surface (`@alpha`):

- `createPlatformLocalAuthRuntime`
- `PlatformArgon2idPasswordHasher`
- local account, session, store, clock, randomness, password-hasher, limiter and logger ports
- local runtime configuration and runtime facade contracts
- cookie, CSRF, Argon2id policy and session constants
- cookie parsing, secret comparison and username normalization utilities
