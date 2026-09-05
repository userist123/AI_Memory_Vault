# R001 Runtime Migration Status

Branch: `main` (R001 promoted from `r001/night-runtime-migration-and-enforcement`)
Start SHA: `814ec77485a0621c388dac5f61cf0bb091707c0a`
Promotion SHA: `00c05a7446e28d097a453008d64a5e1f3b0838a9`
Current reconciliation commit: `ddbe13d54d984332d5338c5b0b3ceee087d7eaf0`
Mode: structural/runtime/security enforcement

## Current target

Move all production runtime out of repository root; classify implementation by responsibility; move tests/experiments/artifacts to numbered zones; close the tracked inbox boundary; establish automated layout and security gates.

## Decisions

- `cognitive_core/` → classified under `03_IMPLEMENTATION/packages/` by responsibility.
- `memory_controller/` → classified under `03_IMPLEMENTATION/packages/` by responsibility.
- `vault_api.py` → `03_IMPLEMENTATION/packages/memory_vault/interfaces/api/`.
- `xau_kinetic/`, `XAU_Kinetic.Desktop/`, `XAU_Kinetic_Standalone/` → `03_IMPLEMENTATION/products/xau_kinetic/`; they are treated as one active product family with engine, desktop, and standalone bundle roles, not as three root products.
- `AI_Memory_Vault_OBSIDIAN` gitlink → `04_CONFIG/obsidian/`.
- `06_INBOX/RAW_IMPORTS` and `06_INBOX/DERIVED` → removed from tracking; retention, where necessary, is represented only by security quarantine manifests.
- Existing migration history → archived under the product migration-log tree; the operational R001 status file remains here.

## Verification policy

`CODE_VERIFIED` means code exists in the committed tree. `TEST_VERIFIED` requires captured test output. `CI_VERIFIED` requires a completed GitHub Actions run on the exact current SHA. Historical benchmark claims remain immutable.

## Reconciliation status

- Structural runtime migration: promoted to `main`.
- `memory_vault` compatibility namespace: present and points at classified sibling packages.
- Repository hygiene validator: corrected so numbered domain roots may contain governed children.
- Repository hygiene regression test: restored under `20_TESTS/regression/test_repository_hygiene.py` because the workflow referenced a missing test file.
- Remote `main` verification: confirmed at the promotion SHA before reconciliation.
- Dependabot graph update on the promotion SHA: completed successfully.

## Remaining gates

1. Confirm the post-reconciliation `main` CI runs on `ddbe13d54d984332d5338c5b0b3ceee087d7eaf0`.
2. Verify graph edge-weight regression and review-memory trust-boundary regression with captured test output.
3. Generate the final machine-readable inventory from the current committed `main` SHA.
4. Produce the final R001 execution report only after the evidence is captured.
