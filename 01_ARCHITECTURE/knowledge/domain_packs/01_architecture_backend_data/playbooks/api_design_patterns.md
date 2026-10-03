---
id: "fd28b366-ff79-414e-896a-bdafefe4d8e6"
type: knowledge
lifecycle: REVIEW
category: api_design_patterns
tags:
  - api_design_patterns
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

# Playbook: Design și Contracte API

Ghid de decizie și reguli operaționale pentru `api_design_patterns`.

## Reguli de Decizie
1. Definește contracte stricte versionate prin OpenAPI/TypeSpec sau Protobuf.
2. Implementează chei de idempotență pentru toate mutațiile (POST/PUT).
3. Gestionează rate-limiting adaptiv și răspunsuri de eroare standard (ProblemDetails).

## Capcane de Evitat
- Nu expune direct entitățile de bază de date; folosește DTO-uri dedicate.
- Nu schimba contractele existente fără compatibilitate retroactivă.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
