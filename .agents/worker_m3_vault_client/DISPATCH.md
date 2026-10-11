## 2026-08-25T19:33:03Z

<USER_REQUEST>
You are Worker 3 (AI Memory Vault REST Client Specialist).
Working Directory: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m3_vault_client
Original Request Path: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\ORIGINAL_REQUEST.md
Project Master Plan: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\orchestrator\PROJECT.md
Survey Spec: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\spec_miner_survey_1\handoff.md
Target File: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_web\js\vault_client.js

Exclusive Write Ownership: `projects/jarvis_web/js/vault_client.js`

Tasks:
1. Read ORIGINAL_REQUEST.md, PROJECT.md, and spec_miner_survey_1/handoff.md.
2. Implement `projects/jarvis_web/js/vault_client.js`:
   - `VaultClient` class conforming to PROJECT.md interface.
   - Async HTTP fetch client targeting `http://127.0.0.1:8000/api/v1/search?q=...` with fast retrieval and `AbortController` timeout (e.g. 50ms configurable).
   - In-memory LRU search cache for instant sub-millisecond repeated queries.
   - Robust offline fallback cache embedding core system knowledge (Identity, Rules, Operating Contract, Invariants P0-P18, Subagent Council Registry, common procedures) for 100% functional standalone usage when the Python server is offline.
   - Note Inspector & Citation Parser: extracts frontmatter, confidence, verification, lifecycle, category, tags, and summary snippet.
   - Memory proposal client: formats proposed note payloads conforming to canonical frontmatter and dispatches to `POST /api/v1/propose`.
   - Live server status health check: `getStatus()` querying `GET /api/v1/status` with connectivity flag and active node count.
3. Document all implementation details in `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m3_vault_client\handoff.md`.
4. Send a completion message to the orchestrator.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.
</USER_REQUEST>
