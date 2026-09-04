# R001 Runtime Migration Status

Branch: `r001/night-runtime-migration-and-enforcement`
Start SHA: `814ec77485a0621c388dac5f61cf0bb091707c0a`
Mode: structural/runtime/security enforcement
Main is not modified by R001.

## Current target

Move all production runtime out of repository root; classify implementation by responsibility; move tests/experiments/artifacts to numbered zones; close the tracked inbox boundary; establish automated layout and security gates.

## Decisions

- `cognitive_core/` → classified under `03_IMPLEMENTATION/packages/memory_vault/`.
- `memory_controller/` → classified under `03_IMPLEMENTATION/packages/memory_vault/`.
- `vault_api.py` → `03_IMPLEMENTATION/packages/memory_vault/interfaces/api/`.
- `xau_kinetic/`, `XAU_Kinetic.Desktop/`, `XAU_Kinetic_Standalone/` → `03_IMPLEMENTATION/products/xau_kinetic/`; they are treated as one active product family with engine, desktop, and standalone bundle roles, not as three root products.
- `AI_Memory_Vault_OBSIDIAN` gitlink → `04_CONFIG/obsidian/`.
- `06_INBOX/RAW_IMPORTS` and `06_INBOX/DERIVED` → removed from tracking; retention, where necessary, is represented only by security quarantine manifests.
- Existing migration history → `99_META/migration_logs/archive/2026-09-04/`; one operational status file is kept at `99_META/migration_logs/R001_RUNTIME_MIGRATION_STATUS.md`.

## Verification policy

`CODE_VERIFIED` means code exists in the branch. `TEST_VERIFIED` requires captured test output. `CI_VERIFIED` requires a completed GitHub Actions run on the exact branch SHA. Historical benchmark claims remain immutable.

## Remaining gates

1. Complete structural commit and remote verification.
2. Run branch CI on exact SHA.
3. Fix any failing import/layout/security gates without weakening them.
4. Verify graph edge-weight regression and review-memory trust-boundary regression.
5. Generate final machine-readable inventory from the committed branch SHA.
6. Produce final R001 execution report only after evidence is available.
