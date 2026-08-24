---
id: "art-d2635d16"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "implementation_plan.md"
confidence: high
verification: verified
relations: []
---

# Artifact: implementation_plan

# Integrarea Exporturilor Claude, Perplexity și a Profilurilor Globale de Agent

Acest plan detaliază modul în care vom procesa și integra cele 45+ fișiere din `claude_original`, `perplexity_original` și `GLOBAL_*.md` în Nucleul Canonic de Memorie, respectând cu strictețe Invariantele și Contractul Vault-ului (AGENTS.md).

## User Review Required

> [!WARNING]
> **Risc de Suprascriere a Cunoștințelor**
> Multe fișiere din `claude_original/` și `perplexity_original/` au aceleași nume cu fișierele deja existente și rafinate din `00_CORE/` (ex: `Rules.md`, `Identity.md`).
> Conform regulii *„Nu suprascrie o informație verificată cu una mai slabă fără motiv”*, **NU** vom înlocui fișierele active. În schimb, vom crea fișiere distincte (ex: `Rules_Claude_Legacy.md`) sau le vom integra ca anexe pentru a păstra istoricul și a permite deduplicarea treptată.
> Ești de acord cu această abordare de tip „adăugare fără distrugere”?

## Open Questions

> [!TIP]
> Exporturile din `claude_original` și `perplexity_original` reprezintă structura veche/paralelă a Vault-ului. Dorești să fie clasificate și etichetate toate cu `lifecycle: ARCHIVED` (ca material de referință istorică) sau vrei să le punem în `lifecycle: REVIEW` pentru a le consolida manual în viitor cu cele curente?
> (Recomandarea AI: `lifecycle: ARCHIVED` / `confidence: medium` pentru a nu polua căutările curente cu reguli duplicate).

## Proposed Changes

Voi folosi un script Python dedicat (Rulează pe Node-ul Local) pentru a executa o ingestie controlată, transformând zecile de fișiere automat, adăugând metadatele corecte de trasabilitate.

### 1. Agenții Globali (Din `markdawn/`)

Aceste profiluri globale descriu cum vrei tu să lucreze agenții indiferent de proiect. Vor fi adăugate ca standarde canonice:

#### [NEW] [Global_Antigravity_Agent_Profile.md](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_KNOWLEDGE/Global_Antigravity_Agent_Profile.md)
#### [NEW] [Global_Claude_Code_Profile.md](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_KNOWLEDGE/Global_Claude_Code_Profile.md)
#### [NEW] [Global_Gemini_CLI_Profile.md](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_KNOWLEDGE/Global_Gemini_CLI_Profile.md)

### 2. Exporturile `claude_original/` și `perplexity_original/`

Scriptul de ingestie va itera prin toate cele 43 de fișiere, va curăța prefixele (ex: `00_CORE__Rules.md` devine `Rules.md`) și le va plasa în folderul corespunzător.

- **Dacă fișierul nu există** în Vault-ul curent: Este creat direct cu starea `REVIEW`.
- **Dacă fișierul există deja** (ex. `00_CORE/Rules.md`): Noul fișier va fi salvat cu sufixul sursei (ex. `00_CORE/Rules_Claude.md` sau `Rules_Perplexity.md`) și va fi marcat ca `ARCHIVED` sau `REVIEW` (în funcție de răspunsul tău) pentru a preveni coliziunile și contradicțiile de instrucțiuni pentru agenți.

Toate vor avea frontmatter injectat automat:
```yaml
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/..."
verification: inferred
```

### 3. Jurnalizare și Coada de Atestare

- Fișierele noi vor fi listate automat în `REVIEW_QUEUE.md`.
- Toate modificările vor fi jurnalizate și comise prin Git într-un singur bloc pentru a menține arborele de commit-uri curat.

## Verification Plan

### Automated Tests
- Validarea existenței metadatelor YAML obligatorii (P0-P15) pe toate cele 46 de fișiere procesate, prin intermediul scriptului de ingestie.
- `git status` pentru a valida că fișierele originale din `06_INBOX/RAW_IMPORTS/` nu au fost șterse (imutabilitatea dovezilor brute).

### Manual Verification
- Verificarea listei generate în `REVIEW_QUEUE.md` pentru a confirma că niciun fișier critic din `00_CORE/` (cum ar fi regulile de operare curente) nu a fost suprascris accidental de versiunile importate.

