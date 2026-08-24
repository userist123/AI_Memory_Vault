---
id: "c1a01101-7291-49fa-9481-22904c10d090"
type: knowledge
lifecycle: ACTIVE
category: multi-agent-orchestration
tags:
  - 50k-skills-registry
  - vault-core
  - mcp-tools
  - voltagent
  - composio
created: 2026-08-24T18:24:00Z
updated: 2026-08-24T18:24:00Z
provenance:
  source_type: official
  source_ref: "global-50k-agent-skill-registries"
confidence: very_high
verification: verified
---

# Index Canonic Master: Ecosistemul Global de 50.000+ Skill-uri & Unelte MCP

Acest document reprezintă **Registrul Indexat al celor 50.000+ de Skill-uri și Integrări MCP** accesibile Consiliului de Agenți AI din AI Memory Vault.

---

## 1. Structura Ecosistemului de 50.000+ Skill-uri

```
                           AI MEMORY VAULT
                                 │
     ┌───────────────────────────┴───────────────────────────┐
     ▼                                                       ▼
198 SKILL-URI LOCALE CRITICE                      50.000+ SKILL-URI & MCP GLOBAL
(.agents/skills/ - Încărcate instanțiat)           (Descărcare & Execuție Dinamică)
     │                                                       │
     ├── 88 Creative Web & 3D Skills                         ├── 1.000+ VoltAgent Skills
     ├── 21 Game Engine ARPG Skills                          ├── 10.000+ Composio Tool Actions
     ├── 20 Backend Microservices Skills                     ├── 35.000+ MCP Servers & Integrations
     ├── 15 Core Web Vitals & Quality Skills                 └── 1.000+ Copilot Marketplace Plugins
     ├── 14 SecOps & DFIR Security Skills
     └── 40 Language & Architecture Skills
```

---

## 2. Catalogul Registrelor Globale Indexate

1. **VoltAgent Skill Library (1.000+ Skills)**
   - *Index*: `https://github.com/VoltAgent/awesome-agent-skills`
   - *Categorii*: DevOps, Cloud Architecture, DB Tuning, Code Generation, Security Auditing.

2. **Composio Action & Tool Registry (10.000+ Tool Actions)**
   - *Index*: `https://github.com/ComposioHQ/awesome-claude-skills`
   - *Categorii*: Integrări GitHub, Slack, Jira, Figma, Postgres, AWS, GCP, Notion, Linear APIs.

3. **Awesome GitHub Copilot Plugins & Instructions (1.000+ Custom Instructions)**
   - *Index*: `https://awesome-copilot.github.com/llms.txt`
   - *Categorii*: Custom Agent Personas, Lifecycle Hooks, Instructions per limbaj.

4. **Model Context Protocol (MCP) Open Registry (35.000+ MCP Integrations)**
   - *Index*: `mcp_config.json` / MCP Registry.
   - *Categorii*: Unelte de fișiere, terminal, analizoare statice, inspecții securitate.

---

## 3. Strategia de Consumare (Progressive Disclosure)

- Agenții utilizează skill-ul `global-skill-registry-router` pentru a căuta în acest index de 50.000+ skill-uri atunci când o sarcină necesită o unealtă ultra-specializată neprezentă în cele 198 de skill-uri locale.
- Fereastra de context LLM este protejată: skill-urile externe sunt descărcate doar la momentul executării sarcinii relevante.
