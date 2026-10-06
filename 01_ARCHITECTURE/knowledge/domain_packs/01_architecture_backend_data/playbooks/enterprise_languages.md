---
id: "d7b1de86-c372-4b87-a713-3a748ca1e07d"
type: knowledge
lifecycle: REVIEW
category: enterprise_languages
tags:
  - enterprise_languages
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

# Playbook: Limbaje și Framework-uri Enterprise (Java, .NET, Python)

Ghid de decizie și reguli operaționale pentru `enterprise_languages`.

## Reguli de Decizie
1. Adoptă tipizarea statică, Dependency Injection și modularitatea.
2. Configurează mecanismele de Garbage Collection pentru latență predictibilă.
3. Folosește biblioteci mature și auditate de securitate.

## Capcane de Evitat
- Nu folosi reflexie masivă pe căile critice de execuție.
- Evită instanțierea repetată de conexiuni; folosește pooling.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
