## 2026-08-25T19:37:26Z
You are M1 Worker 2 for Milestone 1 Remediation (Financial Ingestion Pipeline).
Your working directory is `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\m1_worker_2`.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Authority and feedback files:
- `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md`
- `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`
- `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\AGENTS.md`
- `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\rules\vault_cognitive_rules.md`
- Challenger 1 handoff: `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\m1_challenger_1\handoff.md`

Your task: Fix the 3 edge-case defects identified by Challenger 1 in `xau_kinetic/financial_ingestion/indicators.py`:
1. **Missing Volume Column in `compute_all_indicators`**: Handle cases where `hist` lacks a `Volume` column or has non-numeric volume data using `pd.to_numeric(hist["Volume"], errors="coerce").fillna(0) if "Volume" in hist.columns else pd.Series(0, index=hist.index)`.
2. **Numeric Coercion in `compute_all_indicators`**: Safely coerce `Open`, `High`, `Low`, `Close` with `pd.to_numeric(..., errors="coerce")` and drop NaNs before extracting prices to prevent `TypeError` and `ValueError` on string/corrupt values.
3. **Flat-Price RSI Equilibrium in `calc_rsi`**: When price standard deviation is zero (`clean_prices.std() == 0` or zero price movement), return `50.0` (market equilibrium) instead of `0.0`.
4. Run the test suites:
   `python -m pytest tests/financial/test_challenger1_ingestion.py`
   `python -m pytest tests/financial/`
   `python -m pytest`
5. Write your handoff report to `C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\m1_worker_2\handoff.md`.
6. Send a message to parent when done.
