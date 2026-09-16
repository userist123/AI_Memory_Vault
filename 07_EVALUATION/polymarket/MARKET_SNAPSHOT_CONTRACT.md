# Polymarket Market Snapshot Contract — v1

Status: Phase 1 implementation contract.
Schema version: `polymarket-market-snapshot.v1`.
Data classes defined in `03_IMPLEMENTATION/packages/polymarket/market_snapshot.py`.

## 1. Purpose

This contract defines the historical boundary for later Polymarket research phases. It is not a prediction, calibration, trading, backtesting, paper-trading or execution contract.

The contract must support the question: **what market state and information could legitimately be known by time T?**

## 2. Canonical market model

`PolymarketMarket` contains:

- `market_id`: required stable provider market identity.
- `condition_id`: provider condition identity when available.
- `question`: required market question.
- `description`, `category`, `market_type`: optional descriptive/provider fields.
- `outcomes`, `outcome_ids`: explicit outcome labels and corresponding token identifiers.
- `lifecycle`: bounded enum: `open`, `closed`, `resolved`, `cancelled`, `invalidated`, `unknown`.
- `created_at`, `start_at`, `close_at`, `expected_resolution_at`: optional ISO-8601 UTC timestamps.
- `resolution_source`, `resolution_rule_text`: rule metadata; the rule text may remain unavailable/unverified until a source payload or historical source supplies it.
- `cancellation_state`, `slug`: optional lifecycle/source metadata.

Current Gamma API fields used by the adapter are restricted to fields verified in the current provider documentation. Current resolution fields such as `umaResolutionStatus` or `resolvedBy` are not historical proof by themselves and are not promoted into `ResolutionMetadata` without an explicit `known_at` boundary.

## 3. Snapshot model

`MarketSnapshot` contains:

- `snapshot_id`: content-derived stable identifier (`PMS-` + first 24 hex characters of SHA-256).
- `schema_version`: exact schema identifier.
- `market`: immutable canonical market payload.
- `snapshot_at`: state timestamp being represented.
- `acquired_at`: time the source payload was acquired.
- `known_as_of`: information-set boundary. Information first known after this instant must not appear in the snapshot.
- `price_observations`: timestamped source observations keyed by outcome ID.
- `liquidity`, `volume`: provider observations when available.
- `source_type`, `source_ref`: mandatory provenance identifiers.
- `data_quality`: one of `verified`, `synthetic`, `unverified`, `contradictory`.
- `source_payload_hash`: source-payload integrity marker.
- `resolution`: optional resolution metadata with its own `known_at` timestamp.
- `extra_source_metadata`: source fields that must be preserved without being promoted to stronger historical semantics.
- `content_hash`: SHA-256 over deterministic canonical snapshot content, excluding `snapshot_id` and `content_hash` itself.

## 4. Timestamp semantics

`as_of` is the requested historical market-state boundary in later consumers.

`known_as_of` is the information-availability boundary used by this contract. It is stricter than merely asking what the provider currently reports for the market.

`snapshot_at` identifies the state timestamp represented by the snapshot.

`acquired_at` records when the source observation entered this system and therefore cannot precede `snapshot_at`.

`resolution.known_at` states when resolution information became legitimately available. A resolution object whose `known_at` is later than `known_as_of` is invalid and must be rejected.

A later resolution must never mutate an earlier snapshot.

## 5. Immutability and hashing

Snapshots are serialized with sorted keys, compact JSON separators and UTF-8 encoding. The SHA-256 digest of the canonical payload is the snapshot content hash.

`SnapshotStore` stores one JSON object per derived snapshot ID. Existing bytes are never overwritten. Rewriting a file externally is detected on read because the stored `content_hash` no longer matches the canonical payload.

A snapshot with the same ID and different serialized bytes is treated as an immutable collision and rejected.

## 6. Provenance

Every snapshot requires both `source_type` and `source_ref`, matching the Vault provenance requirement. Synthetic fixtures use an explicit synthetic source type and are never represented as real Polymarket observations.

The implementation does not invent provider fields or historical timestamps. Unknown historical availability is represented by `UNVERIFIED` at the phase-report level rather than guessed.

## 7. Consistency and contradiction handling

The snapshot validator rejects malformed fields, missing identity, invalid timestamps, invalid schema versions, unsupported data-quality values and resolution leakage.

`validate_snapshot_transition(previous, current)` additionally rejects:

- market-ID mismatch,
- condition-ID mismatch,
- non-forward snapshot timestamps,
- backwards lifecycle transitions,
- transitions out of terminal states.

Contradictory source payloads must therefore be represented as `data_quality=contradictory` or rejected by the transition validator. The system does not silently repair contradictory historical data.

## 8. Provider boundary

The Phase 1 Gamma adapter is intentionally a field parser, not a historical reconstruction engine. The current provider contract exposes market identity, question/description, condition ID, outcomes, current prices, lifecycle/date fields, liquidity/volume, resolution-source metadata and CLOB token identifiers. Those provider fields are accepted only as current source observations unless an acquisition timestamp establishes their historical availability.

No real historical observation is fabricated by the Phase 1 implementation.

## 9. Synthetic versus real data

`data_quality=synthetic` is reserved for deterministic test fixtures such as `07_EVALUATION/polymarket/fixtures/phase1_market_history.json`.

Synthetic values are never evidence of a Polymarket market having actually exhibited those states or prices.

## 10. Versioning

`polymarket-market-snapshot.v1` is a versioned compatibility boundary. Any incompatible field meaning, canonicalization rule, timestamp semantic or hash input change requires a new schema version rather than silent reinterpretation of stored snapshots.

## 11. Reuse of existing Vault infrastructure

Phase 1 does not create a second retrieval, memory, lifecycle, temporal ranking or provenance subsystem. The model is a domain boundary that preserves the existing Vault provenance/hash conventions and is intended to feed existing temporal/evidence machinery in later phases.

The Phase 1 implementation deliberately does not modify retrieval ranking, graph expansion, confidence semantics or lifecycle authorization.
