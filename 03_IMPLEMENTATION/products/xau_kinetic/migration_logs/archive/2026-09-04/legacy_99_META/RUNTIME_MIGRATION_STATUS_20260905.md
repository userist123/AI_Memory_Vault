# Runtime Migration Status — 2026-09-05

## Status

`BLOCKED_FOR_EXECUTION_VERIFICATION`

## Decision

`cognitive_core` and `memory_controller` remain at repository root. They are live Python package roots and are directly imported by `vault_api.py`. Their relocation would change import resolution and therefore cannot be treated as a documentation-only move.

## Required evidence before move

1. Complete repository-wide import/path inventory.
2. Compatibility strategy recorded in source-control.
3. Real pytest execution against the migrated tree.
4. Real service/entry-point import smoke test.
5. Remote verification that the root package paths were removed only after the preceding evidence succeeds.

## Current evidence

`vault_api.py` imports both runtime packages directly.

No executable repository checkout is available in the current GitHub-only editing session, so no new runtime pass is claimed.
