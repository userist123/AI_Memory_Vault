# R001 C2 — held-out retrieval benchmark status

Baseline: `2c3126876cbe579afb227615b4b6c4d4048b6d42` (`origin/main`).

The repository contains a deterministic retrieval-fusion lab and gold query file under `07_EVALUATION/retrieval_fusion/`. The declared strategies are R1 semantic/token-overlap, R2 BM25 fusion, R3 entity boost, and R4 graph expansion. The metric implementation provides Precision@K, Recall@K, and MRR.

Attempted execution failed before corpus loading:

```text
ModuleNotFoundError: No module named 'evaluation'
```

The failure came from `retrieval_fusion/experiment_runner.py` importing `evaluation.retrieval_diagnostic_runner` and `evaluation.full_context_baseline`, while the checkout contains `07_EVALUATION/` and no importable top-level `evaluation` package. The imports were corrected to the checkout's actual module layout without changing retrieval or metric semantics.

The repaired deterministic run loaded 15 cases and produced:

```text
R1 P5=0.1333 R5=0.6000 MRR=0.5500
R2 P5=0.1333 R5=0.6333 MRR=0.5356
R3 P5=0.1733 R5=0.7667 MRR=0.6333
R4 P5=0.1333 R5=0.6333 MRR=0.5278
```

Full metric output from the same run:

```text
R1 P1=0.4667 P3=0.2000 P5=0.1333 R1=0.4667 R3=0.5667 R5=0.6000 MRR=0.5500
R2 P1=0.4667 P3=0.2000 P5=0.1333 R1=0.4667 R3=0.5667 R5=0.6333 MRR=0.5356
R3 P1=0.4667 P3=0.2889 P5=0.1733 R1=0.4333 R3=0.7667 R5=0.7667 MRR=0.6333
R4 P1=0.4000 P3=0.2000 P5=0.1333 R1=0.4000 R3=0.6000 R5=0.6333 MRR=0.5056
```

These are corpus-level deterministic adapter results. The gold file contains positive cases only, so false-positive rate is not estimable. The corpus is repository-authored rather than independently held out; paraphrase, synonym, lexical-trap, and hard-negative subsets remain `UNVERIFIED`.

This is a reproducibility defect in the evaluation entrypoint, not evidence that any retrieval strategy is effective or ineffective. The existing retrieval adapters and metric functions were inspected but not promoted to a production retrieval path.

Evidence classification: entrypoint failure and repaired run `RUNTIME_VERIFIED`; strategy and metric presence `CODE_VERIFIED`; reported P5/R5/MRR values `RUNTIME_VERIFIED`; broader effectiveness claims `UNVERIFIED`.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
