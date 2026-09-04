# R001 C12 — Temporal and provenance-aware retrieval

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED`.

Baseline: `061c61ea0dcca24a9e517a9d47b24becd667bbdd`.

## Observed implementation

`TemporalMemoryController` supports explicit `as_of` and `known_as_of`
filters, valid-from/valid-until intervals, extraction-date filtering,
authorized lineage resolution, supersession-aware ranking, conflict reporting,
and signed temporal pagination.

`validate_provenance()` enforces only the required `source_type` and
`source_ref` fields. Provenance is preserved in returned notes, but source
authority is not itself a ranking signal. The default controller path is not
bitemporal unless the temporal wrapper is explicitly used.

## Tests

```text
python -m pytest -q tests/test_temporal_controller.py tests/test_evidence_verifier.py memory_controller/tests/test_supersession_phase43.py cognitive_core/tests/test_evaluation_and_recall_lineage.py
......................                                                   [100%]
22 passed, 1 warning in 0.47s
```

The warning is the existing invalid `\\s` escape in
`cognitive_core/activation.py`.

## Classification

```text
valid-time filtering = TEST_VERIFIED
known-time extraction filtering = TEST_VERIFIED
supersession lineage = TEST_VERIFIED
provenance presence validation = CODE_VERIFIED
provenance-aware ranking/authority = UNVERIFIED
complete historical recall beyond the base candidate window = UNVERIFIED
```

No speculative changes were made. A future C12 benchmark should include
current, superseded, expired, future, unknown, and contradictory records with
fixed candidate sets and explicit provenance authority assertions.
