---
id: "e7b38b72-1813-4c53-97a6-f37f65de8d0a"
type: knowledge
lifecycle: REVIEW
category: relational_databases
tags:
  - relational_databases
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

# Playbook: Baze de Date Relaționale și Persistență

Ghid de decizie și reguli operaționale pentru `relational_databases`.

## Reguli de Decizie
1. Modelează scheme normalizate și adaugă indecși acoperitori pe baza planurilor EXPLAIN.
2. Folosește migrări versionate, testate și executate atomic.
3. Configurează nivelul adecvat de izolare a tranzacțiilor.

## Capcane de Evitat
- Evită interogările N+1 prin JOIN-uri sau batching adecvat.
- Nu rula migrări blocante fără lock timeout și recreare concurentă de indecși.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
