---
id: "50d556ce-ec38-4dfa-8e50-a720d4b00527"
type: knowledge
lifecycle: REVIEW
category: compilers_ast_tooling
tags:
  - compilers_ast_tooling
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

# Playbook: Compilatoare, AST și Tooling de Dezvoltare

Ghid de decizie și reguli operaționale pentru `compilers_ast_tooling`.

## Reguli de Decizie
1. Construiește analizoare de cod pe arbori de sintaxă abstractă (AST).
2. Validează transformările de cod prin teste de echivalență semantică.
3. Automatizează generarea de cod respectând convențiile idiomatice.

## Capcane de Evitat
- Nu manipula codul sursă prin regex când este disponibil un parser AST.
- Evită mutațiile AST nevalidate.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
