# R009b — Held-out Retrieval Benchmark

This directory contains the frozen development and held-out evaluation sets for the R009b retrieval benchmark.

The held-out set is immutable for the lifetime of benchmark v1. Its SHA-256 is recorded in `SET_SHA256.txt`; the runner refuses to execute if the computed hash differs.

Threshold tuning is allowed against `dev.json` only. The held-out set is evaluated at most once per candidate change. Held-out failures must not be inspected or used to tune thresholds. The runner enforces this procedurally by defaulting held-out execution to an explicit `--candidate-id` and a local receipt file that records the candidate fingerprint; a second run for the same candidate is refused.

The six reported query classes are:

- exact identifier lookup
- paraphrase
- synonym substitution
- lexical trap
- cross-cluster / multi-hop
- unanswerable

Metrics are reported separately for candidate recall, context recall, and answer correctness. No single aggregate score is emitted.

The benchmark is deterministic and offline. Optional model/answer components are fail-closed: unavailable components yield an explicit `OPTIONAL_COMPONENT_UNAVAILABLE` marker rather than a fallback.

Corpus size is fixed at 935 notes for the benchmark harness. This matters for power: the benchmark is not a proof that a graph change improves the full population. With finite paired cases, confidence intervals remain wide, especially for the multi-hop and abstention classes. A future comparison must therefore report the paired effect, Wilson intervals, and the minimum effect-size threshold; a numerically positive but smaller-than-threshold effect is not treated as a win.
