# Polymarket Research System — Phase 1 Prompt

## Canonical market model + immutable timestamped snapshots

You are continuing from **Phase 0** on the existing AI Memory Vault.

Before doing anything, read the complete Phase 0 artifact:

`07_EVALUATION/polymarket/PHASE_0_ARCHITECTURE_AUDIT.md`

Also read the exact existing contracts named by that audit, especially:

- `00_GOVERNANCE/VAULT_STATE.md`
- `01_ARCHITECTURE/ontology/ONTOLOGY_SPEC.md`
- `03_IMPLEMENTATION/packages/lifecycle/temporal_controller.py`
- `03_IMPLEMENTATION/packages/lifecycle/temporal_conflict.py`
- `03_IMPLEMENTATION/packages/lifecycle/temporal_ranking.py`
- `03_IMPLEMENTATION/packages/memory/evidence_bundle.py`
- `03_IMPLEMENTATION/packages/memory/evidence_verifier.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/provenance.py`
- `03_IMPLEMENTATION/packages/lifecycle/policy.py`
- `03_IMPLEMENTATION/packages/memory/controller.py`
- `03_IMPLEMENTATION/packages/retrieval/context/retrieval.py`
- `03_IMPLEMENTATION/packages/retrieval/context/candidate_generation.py`
- `07_EVALUATION/heldout_retrieval_benchmark_v2/CONTRACT.md`
- `20_TESTS/test_benchmark_v2_gold_integrity.py`

Read relevant existing tests for storage, temporal behaviour, provenance and controller search before editing any implementation.

---

## Goal

Create the **smallest correct canonical Polymarket market-state contract** needed for all later phases.

This phase is not a prediction engine, trading engine, backtester, paper trader or execution adapter.

It establishes the immutable historical boundary on which those later systems will depend.

The result must make it possible to answer, for a historical timestamp `T`:

> What exactly did we know about this Polymarket market, its current tradable state, its resolution rules and its available market data at or before `T`?

---

## Starting state discovered in Phase 0

The Vault already has a reusable temporal-memory path:

- `TemporalMemoryController` supports explicit `as_of` and `known_as_of` filtering.
- `evidence_bundle.py` can create hashed immutable evidence snapshots.
- provenance already requires `source_type` and `source_ref`.
- the canonical `MemoryController` and retrieval path already exist.
- lifecycle authority already exists in `lifecycle/policy.py`.

Do **not** create another memory, retrieval, provenance or temporal filtering subsystem.

The Phase 0 audit found no evidenced canonical Polymarket market schema or immutable market-snapshot contract. That is the gap this phase addresses.

---

## Scope

### 1. Establish the canonical domain model

Define the minimum internal representation for:

- market identity
- condition/event identity where applicable
- market question
- description
- category / market type
- outcome definitions
- market status
- creation timestamp
- trading/close timestamp where available
- expected resolution timestamp if available
- actual resolution metadata if already known in a historical snapshot
- resolution rule text
- resolution source/reference
- outcome labels and identifiers
- price observations
- market-liquidity observations where available
- source metadata
- observation timestamp
- ingestion timestamp
- `known_as_of` / information-set boundary
- data-quality state
- cancellation / invalidation state if applicable

Use explicit types and enums where the domain has a bounded vocabulary.

Do not invent fields merely because a generic Polymarket plan contains them. Verify the real source/API shape before deciding which fields are mandatory, optional or unavailable.

### 2. Establish immutable market snapshots

Create a snapshot contract that is immutable once recorded.

A snapshot must have:

- stable snapshot ID
- stable market ID
- snapshot timestamp
- acquisition timestamp
- known-information timestamp/boundary
- canonicalized market payload
- cryptographic content hash
- source/reference metadata
- schema/version identifier

A historical snapshot must never silently mutate because the live market later changed.

Do not overwrite an old snapshot with newer market data.

### 3. Establish temporal semantics

Reuse the existing Vault temporal machinery instead of creating a second temporal subsystem.

Explicitly distinguish:

- `as_of`: the market state being reconstructed
- `known_as_of`: the information that was legitimately available by that time
- later resolution/outcome information

Document how these interact with the existing `TemporalMemoryController` and evidence bundles.

If the existing temporal contracts cannot express a requirement safely, record the exact gap first and make the smallest compatible extension.

### 4. Establish source integrity

Every market snapshot must identify its source and acquisition boundary.

The system must be capable of detecting:

- duplicate snapshots
- inconsistent market IDs
- contradictory status updates
- timestamps moving backwards unexpectedly
- source payload drift
- malformed market records
- stale ingestion
- impossible lifecycle transitions

Do not silently repair contradictory source data. Record the contradiction and its disposition.

### 5. Create snapshot replay fixtures

Create a small deterministic fixture set representing a market evolving through time.

The fixture must contain at least:

- an early/open state
- a later price/status change
- a close state
- a resolved state
- at least one case where resolution information exists later than the forecast timestamp

The fixture must prove that historical reconstruction cannot see later state.

Use synthetic fixture data when real historical source data is not yet available, but label it clearly as synthetic. Do not fabricate real Polymarket historical observations.

### 6. Test historical immutability and temporal isolation

Add automated tests proving:

- snapshots are content-addressed or hash-verified
- changing current market state does not alter previous snapshots
- a snapshot at `T1` cannot return fields first known at `T2 > T1`
- resolution/outcome data cannot leak into an earlier information set
- duplicate snapshots are detected consistently
- schema changes are versioned
- canonical serialization is deterministic

### 7. Produce a phase contract

Create:

`07_EVALUATION/polymarket/MARKET_SNAPSHOT_CONTRACT.md`

The document must define:

- canonical market schema
- canonical snapshot schema
- timestamp semantics
- known-information semantics
- immutability rule
- hash/canonicalization rule
- source/provenance requirements
- contradiction handling
- schema versioning
- synthetic-vs-real-data labeling

Update the Phase 0-derived architecture documentation only where necessary to record the result of Phase 1.

---

## Reuse check — mandatory

Before adding any new helper or subsystem, explicitly inspect and decide whether the requirement can be implemented through:

- `TemporalMemoryController`
- `evidence_bundle.py`
- existing provenance validation
- existing lifecycle policy
- existing storage/index abstractions
- existing security/hash primitives

Record the reuse decision in the phase report.

Do not clone their responsibilities into new Polymarket-specific copies.

---

## Contracts to preserve

Do not weaken or bypass:

- lifecycle authorization
- provenance validation
- existing retrieval hard gates
- held-out benchmark integrity
- deterministic canonicalization requirements already used by the Vault
- existing storage identity semantics
- evidence hashing semantics

Do not change retrieval ranking in this phase.
Do not change graph expansion in this phase.
Do not introduce probability calibration in this phase.
Do not introduce trade decisions in this phase.

---

## Explicitly out of scope

Do not implement:

- prediction council
- probability calibration
- market-vs-model edge calculation
- expected-value trading decisions
- risk engine
- position sizing
- backtesting engine
- paper trading
- live trading
- wallet/signing credentials
- autonomous order placement
- production graph plasticity
- automatic learning promotion from trading outcomes

Do not add Polymarket credentials or secrets to the repository.

---

## Evidence and verification rules

The Phase 0 evidence hierarchy remains binding:

1. executed test output
2. reproducible benchmark artifact
3. actual source/code behaviour
4. versioned evaluation report
5. documentation
6. commit/branch metadata
7. agent claim

Never treat levels 6–7 as proof when stronger evidence can be obtained.

Never claim a real Polymarket API field without verifying the actual provider contract.

If an external source cannot be verified from available tooling, mark the field or behaviour `UNVERIFIED` and design the internal contract so that uncertainty is explicit rather than guessed away.

---

## Acceptance criteria

Phase 1 is done only when all of the following are true:

1. A canonical market schema exists and is versioned.
2. A canonical immutable snapshot schema exists and is versioned.
3. The snapshot records a trustworthy information-set boundary.
4. Historical snapshots cannot be silently mutated by later observations.
5. Temporal isolation is enforced by automated tests.
6. Resolution information cannot enter pre-resolution snapshots.
7. Provenance is attached to every externally sourced snapshot.
8. Duplicate/inconsistent snapshots are detected.
9. Synthetic fixtures are clearly labelled and deterministic.
10. The market/snapshot contract document exists.
11. Existing temporal/evidence infrastructure is reused rather than duplicated.
12. Existing Vault tests relevant to touched components pass.
13. New Phase 1 tests pass.
14. No Phase 2+ functionality was implemented.

A code file existing is not acceptance evidence. Test output and reproducible fixture behaviour are required.

---

## Stop rule

Do not start Phase 2.

When Phase 1 is complete, report:

- exact files changed
- exact files added
- contracts reused
- tests added
- exact test commands
- actual outputs
- unresolved source/API questions
- fields marked `UNVERIFIED`
- assumptions
- evidence level for each major claim
- whether the acceptance criteria are all satisfied

Then stop and propose only the single smallest Phase 2 slice.

No live trading and no real-money actions are permitted in this phase.
