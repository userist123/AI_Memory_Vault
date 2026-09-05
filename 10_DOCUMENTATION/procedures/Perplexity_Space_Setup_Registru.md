---
id: "c1a01101-7291-49fa-9481-22904c10d003"
type: procedure
lifecycle: REVIEW
category: research-setup
tags:
  - perplexity
  - research
  - registru-transferuri
  - conformitate
created: 2026-08-17T23:20:00Z
updated: 2026-08-17T23:20:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/markdawn/PERPLEXITY_SPACE_INSTRUCTIONS.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:20:00Z
relations:
  - target: "[[Registru_Multi_Agent_Contracts]]"
    type: related_to
  - target: "[[Registru_de_transferuri]]"
    type: supports
---

# Procedură: Configurare Perplexity Space pentru Research — Registru de Transferuri

## TL;DR
Perplexity nu are fișier auto-încărcat per repo (ca AGENTS.md/CLAUDE.md/GEMINI.md). Echivalentul este un Space cu instrucțiuni custom și fișiere de context încărcate manual. Rolul Perplexity este exclusiv de research și verificare de conformitate cu surse oficiale — nu scrie cod de producție.

## Pași de Configurare

### 1. Creare Space
- Numele recomandat: `„Registru Militar — Remodelare UI"`.

### 2. Custom Instructions (text de lipit mot-a-mot)

> Tu ești asistentul de research și decizie tehnică pentru un proiect WPF/.NET 10 — registru militar de transferuri de date și control dispozitive air-gapped, cu direcție vizuală "Obsidian Tactical Command". Standardele obligatorii sunt HG 585/2002, NATO AC/35-D/1022, EUCI 2013/488/UE, NIST SP 800-88r2, respectiv invariantele interne P0-P18. Nu scrii cod de producție direct — rolul tău este să documentezi cu surse verificate deciziile tehnice (ex: comparații de librării .NET, recomandări de securitate, cercetare privind conformitatea), să sintetizezi noutăți despre .NET/WPF/Antigravity/Codex/Claude Code relevante pentru proiect, și să semnalezi orice conflict între o soluție propusă și standardele de mai sus. Când faci recomandări tehnice, citează sursele oficiale (Microsoft Learn, documentația NIST, publicații oficiale NATO/EUCI) și evită estimările fără sursă pe teme de conformitate.

### 3. Fișiere de Context de Încărcat
- `AGENTS.md`
- `CLAUDE.md`
- `GEMINI.md`
- `skill_security_invariants.md`
- `skill_ui_tokens.md`
- Export README al Vault-ului cognitiv (opțional, pentru întrebări de context)

### 4. Utilizări Canonice
- Research înainte de a scrie o sarcină nouă în AGENTS.md/CLAUDE.md/GEMINI.md.
- Verificarea conformității unei decizii de design față de HG 585 / NIST 800-88r2 înainte de a o transforma în cod.
- Sinteză periodică a noutăților despre Antigravity / Codex CLI / Claude Code.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
