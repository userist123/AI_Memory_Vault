# Phase 6 — Market-vs-Model Edge Report

## Status

**PENDING ACCEPTANCE**

Phase 6 adds leakage-safe retrospective comparison between Phase 3 prediction probabilities and historical market prices. It intentionally does not create a calibrated per-prediction probability, expected value, trading recommendation, risk model, or execution path.

## Scope

- selects the latest historical market price known at or before each prediction cutoff;
- rejects post-cutoff observations and post-cutoff knowledge;
- rejects contradictory same-timestamp market prices;
- computes raw probability edge as `model_probability - market_price`;
- preserves market price provenance and deterministic content-bound edge IDs;
- summarizes mean edge and sign counts without converting edge into a monetary return.

## Acceptance gate

The dedicated `Polymarket Phase 6 Tests` workflow runs Phase 1–6 Polymarket tests together.

## Acceptance evidence

To be filled after the dedicated workflow executes on the final branch revision.

## Known repository-level warning

The repository has a pre-existing malformed Obsidian submodule reference under `04_CONFIG/obsidian/AI_Memory_Vault_OBSIDIAN`. If GitHub Actions cleanup reports that warning, it is outside Phase 6 scope and must not be confused with a Phase 6 test failure.

## Next phase

Phase 7 — Risk + Abstention, after Phase 6 is accepted. It must remain analysis-only and should define decision thresholds, abstention, and bounded sizing diagnostics without enabling execution.
