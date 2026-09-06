# R001 C13 — Calibration Forensics

Evidence level: `CODE_VERIFIED` + `TEST_VERIFIED` for the implementation and deterministic statistical guards; `RUNTIME_VERIFIED` is not claimed for score quality because this lane did not execute a labelled production outcome set.

## Baseline

- Branch: `codex/r001-c13-calibration-v1`
- Baseline: `061c61ea0dcca24a9e517a9d47b24becd667bbdd`
- Working tree was created from `origin/main` at that SHA.

## Inspected implementation

- `memory_controller/effectiveness_stats.py` implements Laplace smoothing, Wilson lower bounds, strict argument validation, and a minimum sample-size guard (`MIN_SAMPLE_SIZE=5`).
- `memory_controller/capability_effectiveness.py` attributes observed capability outcomes only from `ObservedMemoryTrace.retrieved_memory_ids`, deduplicates by run, and marks cells below the sample threshold as `INSUFFICIENT_DATA`.
- These mechanisms are statistical summaries of observed outcomes; they are not evidence that the book-atom numeric metadata is calibrated.

## Independent artifact probe

The current consolidated file contains 31 atoms. All 31 have the required calibration/provenance fields present and all have status `READY_FOR_HUMAN_REVIEW`. There are 11 distinct cluster IDs and two atom types: `SINGLE_SOURCE` and `SYNTHESIS`.

The seven numeric metadata fields use only two complete signatures:

| Signature (`confidence | utility | reliability | reuse | stability | misleading risk | retention cost`) | Count |
|---|---:|
| `0.78 | 0.88 | 0.72 | 0.9 | 0.84 | 0.32 | 0.2` | 10 |
| `0.55 | 0.55 | 0.55 | 0.5 | 0.7 | 0.4 | 0.3` | 21 |

The human-gated promotion list contains 10 entries and assigns priority `0.5984` to every entry. This makes the displayed priority unable to order review work, although the list remains human-gated.

## Tests

The existing focused test files were located at:

- `memory_controller/tests/test_effectiveness_stats.py`
- `memory_controller/tests/test_capability_effectiveness.py`

No scoring implementation was changed in this lane. The observed issue is a calibration/evidence-quality finding, not a proven runtime exception. A future fix should derive values from explicit evidence and add labelled tests before changing ranking or promotion behavior.

## Finding and boundary

Finding: `PARTIALLY_PROVEN` — numeric metadata is structurally present and sample-size safeguards exist, but evidence-sensitive calibration is not demonstrated. Repeated template values and tied review priorities are `CODE_VERIFIED` observations, not proof that every atom is incorrect.

No automatic canonical-memory promotion was performed.

## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
