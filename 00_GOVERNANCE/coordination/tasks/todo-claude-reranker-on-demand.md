# todo-claude-reranker-on-demand

Status: PHASE 1 DONE (2026-11-23, claude main session). Branch `claude/retrieval-reranker-experiment` (PR #392, open).
The reranker is not on main; it lives only in this PR, so work continued here instead of a new worktree.

## Done
1. On-demand reranker (commit d20553c65): `30_SCRIPTS/retrieval/reranker_launcher.py` (stdlib) listens on 18765,
   starts `reranker_server.py --port 0` only on /rerank, forwards only to the port its own child reports, stops it after
   `--idle-minutes` (default 15). While warming: 503 {"error":"warming"}; `HttpReranker` keeps the production order and
   retries after 5 s (other errors keep the 60 s pause). POST needs Content-Type application/json (no CSRF start).
   Tests: `20_TESTS/test_reranker_launcher.py` + `test_controller_reranker.py` 40/40.
   Live on the owner's PC (E:\reranker, backup of the old setup in E:\reranker\backup-20261123): idle 19 MB instead of
   ~6.2 GB commit; cold start measured 35 s and 70 s (memory pressure); /rerank 0.09 s once warm.
2. Metadata (commit 64ce9b782, worker on Sonnet, diff reviewed in main session): the 21 notes of
   `07_EVALUATION/reranker_experiment/results/ineligible_gold_notes.json` now indexed and servable to AI_AGENT.
   Owner decision recorded: all get `lifecycle: REVIEW` + `verification: unverified` (never ACTIVE without attest()).
   9 new ids: agent-memory-librarian, agent-memory-skill-router, skill-memory-sync, skill-skill-discovery,
   spec-jarvis-desktop-wpf-command-center, spec-jarvis-web-command-center, jarvis-web-walkthrough,
   loganalyzer-dfir-phase1-status, spec-registru-transferuri-workspace. Allowlist fixture 207 -> 205.
   Full suite: 5339 passed, 55 skipped, 9 xfailed (worker run).

## Next
- Remap the benchmark v3 gold `path:` ids to the 9 new slugs, re-snapshot, and measure served hit@5 on the 130 cases
  (preregister first, 07_EVALUATION convention). Expected: up to 25 cases newly reachable.
- Aliases/keywords for the ~12 zero-overlap misses: storage does not index `aliases` today (code change needed).
- Smaller model: mMiniLM K=100 measured descriptively 74/130 vs bge K=50 71/130 (REPORT.md §11) - preregister before switching.
- Owner: attest the 21 REVIEW notes that should be ACTIVE (Identity, Rules, protocols).

## Key files
30_SCRIPTS/retrieval/reranker_launcher.py, reranker_server.py, 03_IMPLEMENTATION/packages/retrieval/reranker.py,
20_TESTS/test_reranker_launcher.py, E:\reranker\start_reranker.cmd, stop_reranker.cmd, README.txt
