---
id: "f9471298-1e50-412e-bb4b-446dc143b16b"
type: knowledge
lifecycle: REVIEW
category: caching_data_pipelines
tags:
  - caching_data_pipelines
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

# Playbook: Caching și Conducte de Date

Ghid de decizie și reguli operaționale pentru `caching_data_pipelines`.

## Reguli de Decizie
1. Aplică strategii de caching cu invalidare predictivă (Cache-Aside) și TTL adecvat.
2. Protejează împotriva cache stampede prin locking sau pre-fetching probabilist.
3. Construiește conducte decuplate bazate pe Change Data Capture (CDC).

## Capcane de Evitat
- Nu folosi cache-ul ca unică sursă de adevăr pentru date critice.
- Evită cheile de cache fără TTL.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
