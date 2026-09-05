---
id: "c1a01101-7291-49fa-9481-22904c10d001"
type: knowledge
lifecycle: REVIEW
category: multi-agent-orchestration
tags:
  - claude-code
  - gemini-cli
  - antigravity
  - registru-transferuri
  - agent-contracts
  - obsidian-tactical
created: 2026-08-17T23:20:00Z
updated: 2026-08-17T23:20:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/markdawn/CLAUDE.md + GEMINI.md"
confidence: very_high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:20:00Z
relations:
  - target: "[[Registru_Transferuri_Development_Standards]]"
    type: depends_on
  - target: "[[Registru_de_transferuri]]"
    type: implements
  - target: "[[CSharp_WPF_Enterprise_Desktop]]"
    type: related_to
---

# Contracte Multi-Agent — Registru de Transferuri (Claude Code / Gemini CLI / Antigravity)

## TL;DR
Proiectul Registru de Transferuri folosește trei agenți AI de cod în paralel (Claude Code via `CLAUDE.md`, Gemini CLI via `GEMINI.md`, Antigravity via `AGENTS.md`), fiecare primind automat instrucțiuni specifice din rădăcina repo-ului. Perplexity operează ca agent de research extern, nu de execuție.

## Key Facts
- **Rolul fiecărui agent este distinct**: Claude Code și Gemini CLI scriu cod de producție; Perplexity face doar research și verificare de conformitate cu surse citate.
- **Puntea cu Vault-ul Cognitiv**: Agentii de cod construiesc exclusiv `Services/CognitiveVaultClient.cs` (HttpClient → `127.0.0.1:{port}`) și `Services/VaultProcessSupervisor.cs` (supervizor de proces `vault_api.py`) — **nu reimplementează** logica cognitivă (attention/consolidation/reasoning) din Python.
- **Paleta Extinsă ObsidianTactical (completă, din CLAUDE.md)**:
  - `BgDeep #080C14` · `BgBase #0D1322` · `BgCard #121A2D` · `BgElevated #18233C` · `BgHighlight #223254`
  - `BorderDefault #1E2C48` · `BorderSubtle #2D3F66`
  - `FocusViolet #7C3AED` · `FocusCyan #00E5FF`
  - `Emerald #10B981 / #064E3B` (air-gapped OK, audit integru)
  - `Amber #F59E0B / #78350F` (Secret de Serviciu / NATO Confidential)
  - `Crimson #EF4444 / #7F1D1D` (Strict Secret, operațiuni distructive)
  - `TextPrimary #F8FAFC`
- **Specificații ControlTemplate (din GEMINI.md)**: ScrollBar cu thumb 6px; ComboBox fără chrome de sistem; TextBox 36–42px; DataGrid cu rând minim 40px.

---

## 1. Claude Code — Contract Operațional (`CLAUDE.md`)

**Identitate**: Senior WPF/.NET 10 engineer. Respectă sobrietatea vizuală "Obsidian Tactical Command".

**Reguli de lucru specifice Claude Code**:
1. Verifică existența testelor pentru zona afectată **înainte** de a scrie cod; dacă nu există, le scrie mai întâi.
2. Explică pe scurt planul înainte de a scrie cod.
3. Nu instalează pachete noi și nu modifică configurarea de rețea fără semnalizare explicită.
4. Loghează orice acțiune destructivă (sanitizare, ștergere cheie MEK) ca eveniment de audit **înainte** de execuție.

**Interdicții absolute**:
- Nu scoate aplicația din regim air-gapped.
- Nu introduce combinații de contrast sub 4.5:1.
- Nu șterge invariantele P0–P18 fără marcaj explicit de impact.

---

## 2. Gemini CLI — Contract Operațional (`GEMINI.md`)

**Caracteristici distinctive**:
- Suport pentru fișiere `GEMINI.md` la nivel de subdirector (ex: `src/Theme/GEMINI.md`, `src/Modules/Module4_Oracol/GEMINI.md`) — cel mai specific are prioritate.
- Detaliază dimensiunile exacte ale ControlTemplate-urilor custom.

**Reguli identice** cu Claude Code: air-gapped, WCAG AA, fără telemetrie.

---

## 3. Perplexity Space — Rol de Research (`PERPLEXITY_SPACE_INSTRUCTIONS.md`)

**Rol**: Research și verificare de conformitate cu surse citate oficiale (Microsoft Learn, NIST, NATO/EUCI). **Nu scrie cod de producție.**

**Utilizări canonice**:
- Research înainte de scrierea unor sarcini noi în fișierele de agent.
- Verificarea conformității unei decizii de design față de HG 585/NIST 800-88r2.
- Sinteză periodică a noutăților despre platformele de cod AI.

---

## 4. Relații și Sinapse Cognitive
- `depends_on`: [[Registru_Transferuri_Development_Standards]] — Standardele de bază ale proiectului.
- `implements`: [[Registru_de_transferuri]] — Proiectul activ care consumă aceste contracte.
- `related_to`: [[CSharp_WPF_Enterprise_Desktop]] — Fundamentele MVVM și async.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
