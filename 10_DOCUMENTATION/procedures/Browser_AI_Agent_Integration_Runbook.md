---
id: "proc-browser-ai-0001"
type: procedure
lifecycle: ACTIVE
category: integration
tags: [browser-ai, chatgpt, perplexity, claude-web, gemini, rest-api, runbook]
created: 2026-08-24T23:53:00Z
updated: 2026-08-24T23:53:00Z
provenance:
  source_type: official
  source_ref: "browser-ai-integration-spec"
confidence: very_high
verification: verified
relations:
  - type: related_to
    target_id: 330fa4bc-5b7c-4fb0-8d80-bcfa148a29c9
  - "99_SYSTEM/MCP_Memory_Server_Specification.md"
---

# 🌐 Runbook Canonic: Sincronizarea și Conectarea Agenților AI din Browser

Acest ghid descrie procedurile pas-cu-pas pentru conectarea tuturor agenților AI din browser (**ChatGPT, Perplexity, Claude Web, Gemini Web, Extensii de Browser / Tampermonkey**) la Vault-ul canonic de memorie.

---

## 🏗️ 1. Cele 3 Metode de Conectare

```text
                  +-----------------------------------+
                  |  BROWSER AI AGENT (Web UI)        |
                  |  (ChatGPT, Perplexity, Claude...) |
                  +-----------------------------------+
                                    |
          +-------------------------+-------------------------+
          |                         |                         |
          v                         v                         v
   [Metoda A]                [Metoda B]                [Metoda C]
Prompt de Sistem &       Server REST Local         Extensie Browser /
Markdown Injection       (http://127.0.0.1:8000)   Tampermonkey Userscript
```

---

## 📝 Metoda A: Prompt de Sistem & Markdown Injection (Fără Server)

1. Deschizi fișierul canonic de prompt-uri: [`06_INBOX/RAW_IMPORTS/markdawn/BROWSER_AI_SYSTEM_PROMPTS.md`](file:///C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/06_INBOX/RAW_IMPORTS/markdawn/BROWSER_AI_SYSTEM_PROMPTS.md).
2. Copiezi promptul specific platformei tale:
   - **ChatGPT**: Lipește în Custom GPT Instructions.
   - **Perplexity**: Lipește în Space Instructions.
   - **Claude Web**: Lipește în Claude Project Instructions.
   - **Gemini Web**: Lipește în Gemini Gem Instructions.

---

## ⚡ Metoda B: REST API Gateway Local (Serviciu Python Standalone)

Porniți serverul REST API local navigând mai întâi în dosarul proiectului:

```powershell
cd C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY
python -m memory_controller.api_server 8000
```

Sau rulați direct fișierul din orice locație terminală:

```powershell
python C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY\memory_controller\api_server.py 8000
```

### Endpoint-uri OpenAPI Expuse:
- `GET /api/v1/status` — Starea Vault-ului și numărul de note canonice
- `GET /api/v1/search?q=query` — Căutare hibridă semantică + BM25
- `GET /api/v1/note/{id}` — Preluare notă după UUID
- `POST /api/v1/propose` — Propunere notă nouă din browser (stare `REVIEW`)

---

## 🔌 Metoda C: Extensie Browser / Tampermonkey Userscript

Utilizatorii pot folosi un userscript Tampermonkey sau extensia de browser locală care intercepta interfața ChatGPT/Claude/Perplexity și trimite automat interogarea către `http://127.0.0.1:8000/api/v1/search?q=...`, injectând notele canonice direct în fereastra de chat!
