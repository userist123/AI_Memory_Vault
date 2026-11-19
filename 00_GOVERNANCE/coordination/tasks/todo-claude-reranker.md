# todo-claude-reranker

- Task: measure a cross-encoder reranker over production `search()` candidates (loss-funnel follow-up).
- Branch: `claude/retrieval-reranker-experiment` (base `main` `f28d1eb93`). PR: draft, see branch.
- Status: experiment DONE; verdict ADOPT (bge-reranker-v2-m3 K=50: served hit@5 24 → 71 of 130, 47 wins / 0 losses).
  Production unchanged.

## Done
- Production-path snapshot, preregistration frozen (`77a2f8593`), amendment A1 after NO-GO review (`ab3406e35`),
  controls enforced (`4ce3d982f`), confirmatory + 2 descriptive arms scored. Report: `07_EVALUATION/reranker_experiment/REPORT.md`.

## Next
1. Wire the reranker into `MemoryController.search()` behind a flag, OFF by default; candidate model MiniLM
   (70/130, 287 ms p95) or bge (71/130, 765 ms p95); GPU only, fail closed to current order if the model is absent.
2. Harm check on the 20 work questions (`07_EVALUATION/memory_usage/`), new entrants judged blind.
3. Causal agent ablation with the flag ON (two earlier ablations were null).
4. Side finding: `search()` serves REVIEW/`inferred` notes that `memory_access.get` refuses (16 notes, 14 gold).

## Blockers / environment
- Reranker venv lives outside the repo (sentence-transformers, torch cu128). Models in a local HF cache, pinned revisions.
- The PC hit its Windows commit limit during the run (pagefile on a full drive); latency was measured without Ollama resident.

## Key files
`30_SCRIPTS/evaluation/reranker_{snapshot,rerank,replay,analyze}.py`, `07_EVALUATION/reranker_experiment/`.
