---
id: "c1a01101-7291-49fa-9481-22904c10d070"
type: knowledge
lifecycle: ACTIVE
category: agentic-ecosystem
tags:
  - awesome-copilot
  - github-copilot
  - mcp-servers
  - agent-skills
  - context-engineering
created: 2026-08-24T18:22:00Z
updated: 2026-08-24T18:22:00Z
provenance:
  source_type: official
  source_ref: "github.com/github/awesome-copilot"
confidence: very_high
verification: verified
---

# Raport Canonic: Ghidul Master al Ecosistemului GitHub Copilot (`github/awesome-copilot`)

Sinteza completă a primitivelor de customizare, ingineriei de context, agenților custom, skill-urilor progresive, hook-urilor de ciclu de viață și integrărilor MCP.

---

## 1. Cele 5 Primitive de Customizare

1. **Custom Instructions (`copilot-instructions.md`, `*.instructions.md`)**: Regulamente de cod la nivel de workspace sau pe directoare specifice via glob patterns (`applyTo`).
2. **Custom Agents (`*.agent.md`)**: Personaje de domenii specializate cu instrucțiuni de sistem, selecție de modele și unelte/MCP-uri alocate.
3. **Agent Skills (`SKILL.md`)**: Pachete de capacități cu **Progressive Context Disclosure** (încărcate la cerere pentru economisirea contextului).
4. **Hooks (`hooks.json`)**: Poți de securitate pe evenimente (`sessionStart`, `preToolUse`, `postToolUse`, `sessionEnd`).
5. **Plugins (`plugin.json`)**: Pachete de distribuție pentru distribuirea agenților și skill-urilor prin Marketplace.

---

## 2. Tipare Cheie de Prompting & Context Engineering

- **Context Engineering vs Prompt Engineering**: În loc de prompt-uri lungi repetitive, regulile se stochează în fișiere persistente de proiect (`AGENTS.md`, `GEMINI.md`).
- **Progressive Context Disclosure**: Doar numele și descrierea skill-urilor sunt încărcate în contextul inițial; detaliile și scripturile grele sunt citite doar la declanșare.
- **Model Context Protocol (MCP)**: Standardul deschis pentru conectarea securizată a agenților la baze de date, API-uri și servicii externe.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
