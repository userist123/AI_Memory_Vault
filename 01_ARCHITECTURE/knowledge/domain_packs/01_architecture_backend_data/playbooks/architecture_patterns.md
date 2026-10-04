---
id: "7d7ac2c3-547b-4628-a6da-23ac35395f84"
type: knowledge
lifecycle: REVIEW
category: architecture_patterns
tags:
  - architecture_patterns
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

# Playbook: Arhitectură de Sistem și Modele Enterprise

Ghid de decizie și reguli operaționale pentru `architecture_patterns`.

## Reguli de Decizie
1. Formalizează deciziile structurale majore prin Architecture Decision Records (ADR).
2. Separă clar straturile de domeniu, aplicație și infrastructură (Clean/Hexagonal).
3. Decuplează componentele distribuite prin mesagerie asincronă (outbox, event sourcing).

## Capcane de Evitat
- Evită cuplajul bidirecțional strâns între microservicii.
- Nu introduce persistență distribuită fără garanții de consistență (Saga).

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
