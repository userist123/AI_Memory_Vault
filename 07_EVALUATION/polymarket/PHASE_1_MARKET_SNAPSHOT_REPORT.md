# Phase 1 — Market Snapshot Report

Status: **ACCEPTED / RUNTIME VERIFIED**
Branch: `r047-pm-phase1-fix`
Acceptance-fix head: `48dd9ab914fe25e446b75648dbce614e4565bc81`
Phase 1 implementation merge: PR #81, merge commit `3aea55ca7aa5cd05a18daa655f52ef2ced4d0273`
Phase 0 base: `r046/polymarket-phase0-audit` at `2e0ca03d692a55a4b1ebed0d94981def6a0f590e`

## Scope completed

Phase 1 implemented only the canonical Polymarket market model and immutable historical snapshot boundary. No prediction, calibration, market-vs-model edge, risk, trading, paper trading or live execution behavior was added.

## Changed paths

- `03_IMPLEMENTATION/packages/polymarket/__init__.py`
- `03_IMPLEMENTATION/packages/polymarket/market_snapshot.py`
- `20_TESTS/polymarket/test_market_snapshot.py`
- `07_EVALUATION/polymarket/fixtures/phase1_market_history.json`
- `07_EVALUATION/polymarket/MARKET_SNAPSHOT_CONTRACT.md`
- `.github/workflows/polymarket-phase1-tests.yml`
- this report

## Evidence and design

### Canonical market model

Evidence level 3 (actual source/code behavior): `market_snapshot.py` defines a versioned `PolymarketMarket` model with market identity, condition identity, question/description, category/type, explicit outcomes and outcome IDs, lifecycle state, timestamps, expected resolution, resolution source/rule fields and cancellation metadata.

### Immutable timestamped snapshot

Evidence level 3: `MarketSnapshot` requires `snapshot_at`, `acquired_at`, `known_as_of`, provenance, data quality, price observations and a content hash. Canonical JSON serialization is deterministic and SHA-256 content addressing produces the stable `PMS-...` snapshot ID.

### Historical information boundary

Evidence level 3: resolution metadata has `known_at`; a resolution object known after `known_as_of` is rejected. Current Gamma resolution fields are parsed as provider fields but are not promoted into historical resolution metadata automatically.

### Immutability/tamper detection

Evidence level 3: `SnapshotStore` does not overwrite an existing snapshot. Reads now fail closed before parsing when stored bytes are malformed/tampered, and valid reads verify the stored content hash.

### Contradictory transitions

Evidence level 3: `validate_snapshot_transition` rejects market-ID mismatch, condition-ID mismatch, non-forward snapshot timestamps, backwards lifecycle transitions, and transitions out of terminal states.

### Synthetic fixture

Evidence level 3: `07_EVALUATION/polymarket/fixtures/phase1_market_history.json` contains a deterministic open → closed → resolved timeline and explicitly records that later resolution knowledge must not enter the earlier snapshot.

### Provider contract

Evidence level 3 + current official provider documentation: the adapter is constrained to currently documented Gamma fields. No historical availability is inferred from a current resolved market response. Current provider fields such as `resolvedBy` or `umaResolutionStatus` require an explicit historical `known_at` boundary before they can become historical information.

## Test evidence

### Dedicated Phase 1 runtime gate

Evidence level 1: GitHub Actions workflow run `34654945084` on commit `8abeecaedccca66cec3828b309f62cb4820ebfc7` completed successfully.

Executed command:

`python -m pytest -q 20_TESTS/polymarket/test_market_snapshot.py`

Observed result:

`12 passed in 0.06s`

The first runtime attempt exposed one defect in malformed snapshot handling (`KeyError: 'market'`). The implementation was corrected to fail closed with a validation error, and the dedicated gate was re-run successfully.

### Relevant repository CI

The Phase 1 branch also ran the repository's standard checks. The dedicated Phase 1 runtime gate is the acceptance evidence for the new snapshot tests. Existing repository-wide CI contains unrelated historical failures in other benchmark/enforcement workflows; those are not used to claim or deny the Phase 1 snapshot contract itself.

## Acceptance matrix

| Criterion | Status | Evidence |
|---|---|---|
| Canonical market schema exists | PASS | Evidence level 3 |
| Immutable snapshot schema exists | PASS | Evidence level 3 |
| Stable content hash / deterministic serialization | PASS | Evidence level 3 + runtime tests |
| `as_of` / `known_as_of` / later resolution separated | PASS | Evidence level 3 + runtime tests |
| Provenance required | PASS | Evidence level 3 + runtime tests |
| Historical resolution leakage rejected | PASS | Evidence level 3 + runtime tests |
| Duplicate/tamper detection | PASS | Evidence level 3 + runtime tests |
| Contradictory identity/lifecycle transitions rejected | PASS | Evidence level 3 + runtime tests |
| Synthetic timeline fixture exists | PASS | Evidence level 3 |
| New automated tests execute successfully | PASS | Evidence level 1, run `34654945084` |
| No Phase 2+ behavior introduced | PASS | Scope review / Evidence level 3 |

## Known limitations / unresolved

1. Historical availability of Gamma fields remains an ingestion concern. The snapshot contract records `known_as_of`, but a real historical ingestion adapter must supply trustworthy availability metadata before real historical replay can be claimed.
2. The implementation does not yet provide historical market reconstruction. That is Phase 2 scope.
3. Real Polymarket observations are not asserted or fabricated by this phase.

## Phase boundary decision

**PHASE 1 ACCEPTED.**

Acceptance is based on executed runtime evidence, not code presence alone. The smallest next slice is Phase 2: deterministic historical replay and information-set reconstruction using the Phase 1 snapshot contract.
