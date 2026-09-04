# CODEX ground truth — Memory Engine Reality V1

Evidence scope: CODEX execution on branch `codex/memory-engine-reality-v1`, baseline `9a663213c52b971dee28d4eff729d1e93914fdce`. This is not an independent final verification.

| Item | Observation | Evidence |
|---|---|---|
| main alignment | local `main` and `origin/main` matched at baseline | RUNTIME_VERIFIED |
| Python / pytest | Python 3.14.2 / pytest 9.0.2 | RUNTIME_VERIFIED |
| discovery | 807 tests collected | TEST_VERIFIED |
| baseline suite | 805 passed, 2 skipped, 23.98s | TEST_VERIFIED |
| local provider health | Ollama model `qwen2.5-coder:3b` available | RUNTIME_VERIFIED |
| real generation | `REAL_PROVIDER_OK`; local; 35 input, 4 output, total 39 | RUNTIME_VERIFIED |
| CI | workflow definitions inspected; no new remote run in this branch | CODE_VERIFIED / UNVERIFIED |

Pre-existing untracked files were left untouched. No canonical-memory file was intentionally changed.
