# BRIEFING — 2026-08-25T19:41:00Z

## Mission
Implement production-grade AI Memory Vault REST Client & Offline Cache (`projects/jarvis_web/js/vault_client.js`) for the JARVIS Web Ecosystem, with sub-50ms search retrieval, AbortController timeout handling, in-memory LRU cache, rich offline memory fallback containing core vault knowledge & invariants P0-P18, note inspector/citation parser, and memory proposal capabilities.

## 🔒 My Identity
- Archetype: worker
- Roles: implementer, qa, specialist
- Working directory: c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\worker_m3_vault_client
- Original parent: 95f7bf7e-c539-4492-b214-af221cca8379
- Milestone: M3 (AI Memory Vault REST Client & Cache)

## 🔒 Key Constraints
- Target File: `projects/jarvis_web/js/vault_client.js` (Exclusive write ownership)
- Zero external paid APIs; 100% free web standards (Fetch API, AbortController, ES6 module / UMD browser-compatible, Node.js testable)
- Must conform to `PROJECT.md` interface:
  - `search(query: string, options?: { timeoutMs?: number, limit?: number }): Promise<{ source: 'live' | 'offline_cache', results: Array<Note>, latencyMs: number }>`
  - `getStatus(): Promise<{ online: boolean, indexedNotes: number, service?: string, vaultRoot?: string }>`
  - `proposeNote(notePayload: object): Promise<{ success: boolean, noteId?: string, error?: string }>`
  - `inspectNote(note: object): object`
  - `formatCitation(note: object): object`
- Real logic only: DO NOT hardcode test results, dummy/facade implementations, or bypass real execution logic.
- Robust offline cache embedding real Vault Core facts (Identity, Rules, Operating Contract, Invariants P0-P18, Subagents Council Registry, Common Procedures).
- In-memory LRU cache with configurable capacity and TTL for instant repeated queries.

## Current Parent
- Conversation ID: 95f7bf7e-c539-4492-b214-af221cca8379
- Updated: 2026-08-25T19:41:00Z

## Task Summary
- **What to build**: `projects/jarvis_web/js/vault_client.js` containing `VaultClient`, LRU Cache, Offline Knowledge Index, Note Inspector & Citation Parser, and Propose Client.
- **Success criteria**:
  - Connects to `http://127.0.0.1:8000/api/v1/search?q=...` with configurable timeout (e.g. 50ms default or 1000ms max).
  - Falls back seamlessly to offline knowledge cache on fetch failure / timeout.
  - Sub-millisecond LRU cache hit resolution.
  - Note Inspector extracts clean frontmatter and markdown body details.
  - `proposeNote` validates canonical schema (sets `lifecycle: "REVIEW"`, `verification: "unverified"`, etc.) and posts to `/api/v1/propose`.
  - Works in both modern browser and Node.js environments.
- **Interface contracts**: `PROJECT.md § Interface Contracts § VaultClient ↔ App`
- **Code layout**: `projects/jarvis_web/js/vault_client.js`

## Loaded Skills
- **Source**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\skills\vault-operations\SKILL.md`
- **Core methodology**: Vault query, propose, review, and citation extraction workflows.

## Change Tracker
- **Files modified**: `projects/jarvis_web/js/vault_client.js` — Implemented VaultClient, LRUCache, NoteInspector, OFFLINE_KNOWLEDGE_BANK, multi-tier ranking, citation formatter, P0-P18 validation, and offline resilience.
- **Build status**: PASS (48/48 feature, boundary, and pairwise tests passed)
- **Pending issues**: None

## Quality Status
- **Build/test result**: 48 passed, 0 failed across F12, F13, F14, F15 and integration suites.
- **Lint status**: Clean (ES Module standard syntax)
- **Tests added/modified**: Automated verification via `node --test`
