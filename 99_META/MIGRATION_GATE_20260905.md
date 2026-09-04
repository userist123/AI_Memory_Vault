# Runtime Migration Gate — 2026-09-05

## Current main

`200b994f5f2dc65f0164ee198844978c3b5c243a`

## Scope

Audit the compatibility boundary for the runtime packages `cognitive_core` and `memory_controller` before relocation into `03_IMPLEMENTATION`.

## Evidence collected

- `vault_api.py` imports `MemoryController` and `SQLiteStorageEngine` from `memory_controller`, and `MultiAgentDispatcher` from `cognitive_core`.
- Root operating contracts still describe these runtime package names as importable top-level packages; they must not be renamed without updating execution/tooling contracts.
- `cognitive_core` is a real Python runtime package with protected-core modules and tests; `memory_controller` is the other primary runtime package. These are not documentation trees.
- The repository is currently in structural migration; no local executable verification is available through the GitHub-only editing path in this session.

## Decision

Do not relocate `cognitive_core` or `memory_controller` yet.

The safe migration requires either:
1. a compatibility-preserving packaging/import layer, or
2. a complete path/import normalization followed by executable pytest verification.

Because the current execution environment cannot run the repository's full Python test suite against the mutated tree, blind relocation would violate the no-fabrication and executable-verification requirements.

## Next exact action

Before any package move, inventory all repository references to:
- `cognitive_core`
- `memory_controller`
- `vault_api.py`
- `pytest.ini`
- Python module entry points
- CI commands and working directories

Then implement the smallest compatibility-preserving path strategy and verify with the real test suite before deleting the root package paths.
