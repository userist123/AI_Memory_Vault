---
id: "0be22e07-e4d2-4fba-90fe-6d8dd0796aed"
type: knowledge
lifecycle: REVIEW
category: nosql_vector_databases
tags:
  - nosql_vector_databases
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

# Playbook: Baze de Date NoSQL și Vectoriale

Ghid de decizie și reguli operaționale pentru `nosql_vector_databases`.

## Reguli de Decizie
1. Selectează motorul conform modelului de acces (document, key-value, vector).
2. Optimizează indecșii vectoriali (HNSW/IVF) pentru raport optim recall/latență.
3. Aplică strategii de sharding și replicare pentru scalare.

## Capcane de Evitat
- Nu încărca payload-uri masive în vector index dacă nu sunt necesare la filtrare.
- Evită scanările complete de colecție în regăsirea semantică.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
