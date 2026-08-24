---
id: "spec-mcp-server-0001"
type: resource
lifecycle: ACTIVE
category: system-architecture
tags: [mcp-server, http-native, stateless-protocol, tool-calling, 2026-spec]
created: 2026-08-24T23:20:00Z
updated: 2026-08-24T23:20:00Z
provenance:
  source_type: official
  source_ref: "mcp-2026-stateless-standard"
confidence: very_high
verification: verified
relations:
  - "00_CORE/System_Architecture.md"
  - "00_CORE/Memory_Protocol.md"
---

# 🔌 Specificație Canonică: Adaptor Server MCP Stateless (Jul 2026 Spec)

Acest document definește adaptorul de server **Model Context Protocol (MCP)** stateless pentru `AI_Memory_Vault_CODEX_READY`, permițând oricărui client AI (Claude Code, Cursor, VS Code, Ollama, Antigravity) să acceseze uneltele de memorie canonică prin protocoale HTTP-native de înaltă performanță.

---

## 🛰️ 1. Arhitectură & Transport Stateless

- **Transport**: HTTP/1.1 & HTTP/2 (fără necesitate de conexiuni WebSockets persistente).
- **Endpoint-uri Canonice**:
  - `POST /mcp/v1/tools/call` — Execuție unelte de memorie
  - `GET /mcp/v1/resources/list` — Listare resurse și MOC-uri
  - `POST /mcp/v1/prompts/get` — Preluare prompt-uri de agenți
- **Headers de Securitate & Ruta**:
  - `Mcp-Version: 2026-07-28`
  - `Mcp-Principal: HUMAN | AI_AGENT | ADMIN`
  - `Mcp-Signature: SHA-256 HMAC`

---

## 🧰 2. Unelte MCP Expuse de MemoryController

| Unealtă MCP | Parametri Input | Descriere & Acțiune |
|---|---|---|
| `search_memory` | `query: str`, `page_size: int` | Căutare hibridă semantică + BM25 în memoriile canonice |
| `read_memory` | `note_id: str` | Citire securizată cu verficare a stării de viață |
| `propose_memory` | `note_data: dict` | Propunere notă nouă în stare `REVIEW` |
| `challenge_memory` | `note_id: str`, `evidence: dict` | Re-volatilizare memorie contrazisă în stare `RECONSOLIDATING` |
| `attest_memory` | `note_id: str`, `verifier: str` | Atestare umană / admin (`verification = "verified"`) |
| `supersede_memory` | `old_id: str`, `new_id: str` | Înlocuire atomică cu păstrarea istoricului bi-temporal |

---

## 🛡️ 3. Conformitate cu Invariantele P0-P18

Adaptorul MCP este învelit peste `MemoryController` și impune strict:
1. Blocarea oricărei încercări a agenților AI de a auto-atesta memoriile (`attest_memory` permisă exclusiv principalilor `HUMAN` / `ADMIN`).
2. Protecția provenienței privilegiate (`user`, `official` pot fi atribuite doar de oameni).
3. Înregistrarea tuturor apelurilor de unelte în jurnalul criptografic imutabil SHA-256 (`audit_log.jsonl`).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[11 Templates and System Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
