# R001 C2 — held-out retrieval benchmark status

Baseline: `2c3126876cbe579afb227615b4b6c4d4048b6d42` (`origin/main`).

The repository contains a deterministic retrieval-fusion lab and gold query
file under `07_EVALUATION/retrieval_fusion/`. The declared strategies are R1
semantic/token-overlap, R2 BM25 fusion, R3 entity boost, and R4 graph
expansion. The metric implementation provides Precision@K, Recall@K, and MRR.

Attempted execution failed before corpus loading:

```text
ModuleNotFoundError: No module named 'evaluation'
```

The failure comes from `retrieval_fusion/experiment_runner.py` importing
`evaluation.retrieval_diagnostic_runner` and `evaluation.full_context_baseline`,
while the checkout contains `07_EVALUATION/` and no importable top-level
`evaluation` package. Therefore no benchmark numbers are reported by this
lane. Precision@1/3/5, Recall@1/3/5, MRR, false-positive rate, paraphrase,
synonym, lexical-trap, and hard-negative results remain `UNVERIFIED`.

This is a reproducibility defect in the evaluation entrypoint, not evidence
that any retrieval strategy is effective or ineffective. The existing
retrieval adapters and metric functions were inspected but not promoted to a
production retrieval path.

Evidence classification: entrypoint failure `RUNTIME_VERIFIED`; strategy and
metric presence `CODE_VERIFIED`; effectiveness metrics `UNVERIFIED`.
