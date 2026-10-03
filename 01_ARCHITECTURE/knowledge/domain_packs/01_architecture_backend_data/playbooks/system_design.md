---
id: "9da93656-ed7c-4a53-9919-b1b046af1e76"
type: knowledge
lifecycle: REVIEW
category: system_design
tags:
  - system_design
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

# Playbook: Design de Sistem și Scalabilitate

Ghid de decizie și reguli operaționale pentru `system_design`.

## Reguli de Decizie
1. Proiectează sisteme reziliente folosind circuit breakers, timeouts și backoff exponențial.
2. Planifică din timp partiționarea orizontală (sharding) înainte de limitele I/O.
3. Definește Service Level Objectives (SLO) măsurabile.

## Capcane de Evitat
- Nu introduce puncte unice de eșec (SPOF) pe căile critice.
- Evită stocarea stării în nodurile de calcul.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
