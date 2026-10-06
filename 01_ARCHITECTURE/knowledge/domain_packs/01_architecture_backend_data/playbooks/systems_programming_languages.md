---
id: "56569e1f-f043-40eb-b92b-e32b4ccf1eeb"
type: knowledge
lifecycle: REVIEW
category: systems_programming_languages
tags:
  - systems_programming_languages
  - architecture-backend
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: Limbaje de Programare de Sistem (Rust, Go, C++)

Ghid de decizie și reguli operaționale pentru `systems_programming_languages`.

## Reguli de Decizie
1. Asigură siguranța memoriei la nivel de compilator (Rust borrow checker, Go race detector).
2. Gestionează resursele prin RAII și eliberare imediată.
3. Minimizează copierea datelor prin zero-copy networking și parsing.

## Capcane de Evitat
- Evită unsafe fără garanții stricte de limite și dovezi.
- Nu lăsa goroutine-uri sau thread-uri orfane.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
