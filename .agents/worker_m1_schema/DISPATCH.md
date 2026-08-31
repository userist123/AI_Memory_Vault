## 2026-08-26T16:04:56Z
You are Worker M1 (teamwork_preview_worker).
Your working directory is: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m1_schema`

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Scope & Mission:
Implement Milestone 1: Financial Schema & Domain Models in `memory_controller/financial_schema.py`.

Authoritative Documents:
- Read `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md`
- Read `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`
- Read `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\spec_miner_survey_2\survey_spec.md` (specifically Section 6 for the complete Draft-07 JSON Schema and Pydantic v2 models).

Requirements:
1. Populate `memory_controller/financial_schema.py` with:
   - `FINANCIAL_NOTE_SCHEMA`: Complete Draft-07 JSON Schema covering canonical frontmatter (`id`, `type`, `lifecycle`, `category`, `tags`, `created`, `updated`, `provenance`, `confidence`, `verification`, `relations`) and financial payload (`symbol`, `category`, `indicators`, `signals`, `risk_metrics`, `narrative`, `raw_content`).
   - `validate_financial_note(data: dict) -> tuple[bool, list[str]]`: Function validating input dictionary against `FINANCIAL_NOTE_SCHEMA` using `jsonschema.Draft7Validator`.
   - Pydantic models: `FinancialFrontmatter`, `FinancialIndicators`, `TradeSignal`, `RiskMetrics`, `FinancialNotePayload`, etc.
   - Strict UUID enforcement for `id`.
   - Invariant enforcement: AI agents cannot produce `verification="verified"` (allowed: `"partially_verified"`, `"unverified"`), `lifecycle="REVIEW"`, `provenance.source_type="execution"`.
2. Exclusively own `memory_controller/financial_schema.py`.
3. Run tests / verify syntax using pytest / python validation.
4. Write handoff report in your working directory and notify parent via send_message.
