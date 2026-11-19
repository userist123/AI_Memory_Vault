# todo-claude-reranker

- Task: reranker over production `search()` candidates: experiment, wiring behind a flag, harm check, causal QA ablation.
- Branch: `claude/retrieval-reranker-experiment` (base `main` `f28d1eb93`). PR #392 (draft).

## Done
1. Preregistered experiment: served hit@5 24 → 71 of 130 (47 wins / 0 losses), verdict ADOPT
   (`07_EVALUATION/reranker_experiment/REPORT.md`).
2. Wired: `MemoryController(reranker=...)` OFF by default, `AI_MEMORY_VAULT_RERANKER_URL` (loopback only) in
   `controller_factory`, model server `30_SCRIPTS/retrieval/reranker_server.py`; wired path reproduces the experiment
   160/160. Review NO-GO (proxy bypass) fixed in `71e55490e` with tests.
3. Harm check on 20 real work questions: 3 wins / 0 losses, two blind judges, kappa 0.92.
4. Causal QA ablation (preregistered `777f61525`): PASSES_THROUGH. Correct answers A 7 / B 24 / C 52 of 130, C vs B
   29 wins / 1 loss, judges kappa 0.95, hallucinations 3/1/0 of 30 (`07_EVALUATION/memory_qa_ablation/REPORT.md`).

6. Diagnosis: 25 of 37 never-candidate gold cases are note metadata (no id / no or non-standard lifecycle), list in
   `07_EVALUATION/reranker_experiment/results/ineligible_gold_notes.json`; K = 100 descriptive 79/130 (rerank p95 1.58 s).
   `AI_MEMORY_VAULT_RERANKER_K` added (default 50).

7. Ready to use: client circuit breaker (no 2 s penalty per search while the server is down), port 18765, verified end to
   end through the real MCP server (ON = experiment lists, OFF = baseline). Full suite 5318 passed; CI 46/46 green.

## Next
- Owner: merge PR #392 (agent merge was refused by the permission system), then enable on the PC with the local
  install script (autostart + `AI_MEMORY_VAULT_RERANKER_URL`); the agent was not allowed to create autostart/env entries.
- Open research: an agent that decides when to search (not forced retrieval); multi-hop (3/40); Romanian gap;
  human calibration of the LLM judges.

## Open owner decisions (not changed by this work)
- Metadata of the 21 gold notes in `ineligible_gold_notes.json` (9 without id, 8 without lifecycle, 4 NORMALIZED/raw).
- `memory_get` withholds REVIEW notes whose verification is not `unverified` (e.g. `inferred`) while `search()` serves them
  (`interfaces/memory_access.py` `_review_candidate`): 16 such notes, 14 of them v3 gold. Trust-boundary change → owner.
- Turning the reranker ON by default (it needs the GPU server; CPU is > 30 s per query).

5. Co-residency verified: reranker server + qwen2.5:7b on the 8 GB GPU, wired path 160/160 identical, search p95 1.07 s.
   Server now refuses an occupied port (another session held 8765 on 2026-11-19; use `--port 8766` if so).

## Environment
- Reranker venv and HF cache live outside the repo (drive E:). The server commits ~6.4 GB; it runs next to a 7B Ollama
  model since 24 GB were freed on D: (old folders moved to E:\D_offload with junctions, page file can grow).
