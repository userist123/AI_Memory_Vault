# todo-claude-r009b-split

- **Task:** stop `R009b Held-out Retrieval Benchmark` from being cancelled at its 20-minute limit while every test passes.
- **Branch:** `claude/r009b-split-timeout`
- **Cause (measured 2026-10-22):** one job ran the full suite (700–1,180 s, depending on the runner) and then the held-out benchmark (~90 s) under `timeout-minutes: 20`. Runs were cancelled on PR #319, `design-system-tokens` and `cost-router-pinned-self-update`, in the benchmark step or at the end of the suite.
- **Done:** split into two independent jobs, `zero-regression-gate` (45 min, `--durations=30`) and `heldout-benchmark` (15 min). Benchmark steps unchanged; it never imported pytest.
- **First CI run (PR #322, run 39893256883):** `heldout-benchmark` 2m25s; suite 924 s, 4,868 passed. `--durations` shows three tests in `20_TESTS/test_retrieval_loss_funnel.py` take 514 s of the 924 (56%): `test_negative_control_determinism` 306 s, `test_benchmark_file_immutability_during_permutation` 104 s, `test_negative_control_gold_injection` 104 s. Next slowest is 21 s.
- **Next:** separate change: make those three tests cheaper (shared fixture or smaller corpus), without weakening what they check.
- **Blockers:** none. `main` has no branch protection, so the job rename breaks no required check.
- **Key files:** `.github/workflows/r009b-heldout-benchmark.yml`.
