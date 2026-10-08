---
agent: CLAUDE_SONNET
last_updated_utc: 2026-10-08T15:00:00Z
repository: userist123/AI_Memory_Vault_CODEX_READY
working_branch: research/b2m-blockers-eval
base: origin/research/b2m-d-docs (PR #224)
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
working_folder: 03_IMPLEMENTATION/packages/lifecycle/validation/, 08_RESEARCH/BOOK_TO_MEMORY/, 07_EVALUATION/b2m_*, 30_SCRIPTS/evaluation/, 20_TESTS/
current_task: Book-to-Memory evaluation-integrity blockers B03-B10 of the PR #209 audit
status: DONE in code and data; B03/B05 wait for a model run and raters, B06 waits for owner labels, B10 stays open
---

# B2M evaluation integrity (PR #209 B03-B10)

Claimed and completed 2026-10-08 by CLAUDE_SONNET on `research/b2m-blockers-eval`. The state per blocker is in
`08_RESEARCH/BOOK_TO_MEMORY/OPEN_BLOCKERS.md`; this note records what was done and what was found.

| Blocker | Done | Where |
|---|---|---|
| B07 | Leakage checker run on the real data and tested on a synthetic leak. 2 exact text overlaps (frozen v1/v2 dev vs held-out), 0 in H1; flagged, not edited | `07_EVALUATION/b2m_leakage/`, `30_SCRIPTS/evaluation/b2m_leakage_check.py` |
| B08 | `RunConfig` recorded by the H1 runners, the ablation record and the Phase 10 result; comparison and aggregation refuse differing configs | `book_to_memory_run_config.py` |
| B04 | Prompt audit; 2 leading prompts rewritten; framings and lint test | `PROMPT_AUDIT_B04.md`, `book_to_memory_prompt_audit.py` |
| B05 | Cohen / Fleiss / Krippendorff with textbook checks; rating schema; blind packet CLI | `book_to_memory_raters.py`, `b2m_blind_rating_packet.py` |
| B03 | Pre-registration written first (commit `da4f8d52b`), harness, 51-task / 204-trial packet, no answers | `PREREGISTRATION_B03_B05.md`, `b03_task_packet/` |
| B06 | 131-row labelling packet and ingest script; report says `WAITING_ON_OWNER_LABELS` | `b06_labelling_packet/`, `b2m_ingest_labels.py` |
| B09 | Phase reports reworded to what was measured; a guard test | `20_TESTS/research/test_phase_report_claims.py` |
| B10 | Left open with the reason and what would close it | `OPEN_BLOCKERS.md` |

## Findings worth knowing

* The frozen benchmarks `heldout_retrieval_benchmark_v2` (dev `D09` = held-out `H18`) and `_v1` (`D08` = `H21`) each contain one exact
  duplicate across dev and held-out. They are frozen and hash-pinned and were not edited; the duplicates are registered in
  `KNOWN_TEXT_OVERLAPS` and the checker fails on any other text overlap.
* 13 of the 28 H1 held-out cases share a gold note with an H1 development or calibration case. The baseline recall@10 is 17/28 on the
  held-out split and 9/15 on the 15 cases that share nothing. The H1 case set is frozen; the sharing is reported, not repaired.
* `h1_baseline_results.json` was committed before B08 and carries no run config; it is not comparable until the baseline is re-run.
* The graph arm of `run_h1_associative_experiment.py` is still `BLOCKED` on this vault state (graph expansion adds no nodes); unchanged.

## Not done, and why

* No model was run and no answer or rating was written: the packet is for the owner or an orchestrator.
* Runners outside the track in `07_EVALUATION/` do not take a `RunConfig`.
