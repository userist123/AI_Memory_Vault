## 2026-08-27T19:24:10Z
You are Worker 1 (Cognitive Core & Memory Storage Specialist) for the Cognitive Brain ('Creier Vorbitor') project.
Your assigned working directory is `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m1`.
The target project codebase is `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`.

Read the authoritative requirements and architecture:
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md` (specifically timestamp 2026-08-27T19:19:42Z)
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\PROJECT.md`
- `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\survey_cognitive_memory\handoff.md`

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Your task is to implement Milestone 1:
1. `jarvis/config.py`: Central settings (dataclass / Pydantic Settings) for LLM provider, Ollama URL, model names, memory paths, SQLite DB path, audio sampling rates, VAD thresholds, HA endpoints.
2. `jarvis/llm/`:
   - `base.py`: Abstract `BaseLLMProvider` with `generate()`, `chat()`, `stream()` (async token streaming), and `generate_structured()` (Pydantic schema).
   - `ollama_provider.py`: Async Ollama client targeting local `qwen2.5-coder` (or fallback model) via `http://localhost:11434/api/generate` and `/api/chat`.
   - `cloud_providers.py`: Modular providers for Gemini (Google Antigravity SDK) and Claude.
   - `mock_provider.py`: Deterministic mock provider for offline testing.
3. `jarvis/memory/`:
   - `invariants.py`: Invariants P0-P18, Principal enum (`HUMAN`, `AI_AGENT`, `ADMIN`), Operation enum, Lifecycle enum (`RAW`, `CLASSIFIED`, `NORMALIZED`, `REVIEW`, `VERIFIED`, `ACTIVE`, `SUPERSEDED`, `ARCHIVED`, `RECONSOLIDATING`).
   - `sqlite_engine.py`: Thread-safe SQLite engine with `PRAGMA journal_mode=WAL;`, `PRAGMA busy_timeout=5000;`, `PRAGMA synchronous=NORMAL;`, `BEGIN IMMEDIATE` transactions, recursive CTE lineage traversal.
   - `markdown_sync.py`: Atomic write engine for Obsidian-style Markdown notes with YAML frontmatter validation and two-way sync with SQLite.
   - `recall.py` & `activation.py`: Multi-signal associative recall (BM25 + Semantic Cosine + ACT-R base-level decay + Wikilink spreading activation).
   - `reflection.py`: 6-stage formal Reflexion (`Error -> Root Cause -> Fix -> Verification -> Prevention -> Lesson`) producing structured review notes.
   - `consolidation.py`: Lesson consolidation and plastic memory reconsolidation.
4. `jarvis/core/`:
   - `models.py`: Data models for `PerceptionEvent`, `UserIntent`, `WorkingMemory`, `ActivePlan`, `PlanStep`, `StepExecutionResult`, `OODACycleResult`.
   - `ooda.py`: The complete stateful OODA cycle (Observe, Retrieve, Reason, Plan, Act, Reflect, Consolidate).
   - `executive.py`: Cognitive Daemon coordinating the OODA loop with atomic checkpointing (`wm.json`, `plan.json`) and error recovery.
5. Unit tests in `tests/unit/`:
   - `test_llm_providers.py`: Test all LLM providers (streaming, structured output, error handling).
   - `test_memory_storage.py`: Test SQLite WAL concurrency, Markdown atomic sync, Invariants P0-P18, recursive lineage, ACT-R recall.
   - `test_ooda_loop.py`: Test complete end-to-end OODA cycle with Observe->Retrieve->Plan->Act->Reflect->Consolidate.
6. Setup `pyproject.toml` and package configuration for Python 3.12+.

Run the unit tests via pytest, verify 100% pass, write your completion report in `.agents/worker_m1/handoff.md`, and notify parent via `send_message`.
