---
id: "754ebe69-c791-469f-b46c-bf4b84b08598"
type: knowledge
lifecycle: REVIEW
category: backend_concurrency_services
tags:
  - backend_concurrency_services
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

# Playbook: Servicii Backend și Concurență

Ghid de decizie și reguli operaționale pentru `backend_concurrency_services`.

## Reguli de Decizie
1. Folosește modele asincrone non-blocante pentru operațiuni I/O intense.
2. Izolează resursele partajate prin mecanisme atomice sau mutex-uri fine.
3. Asigură oprirea grațioasă (graceful shutdown) cu golirea conexiunilor.

## Capcane de Evitat
- Nu bloca bucla asincronă de evenimente cu procesări sincrone grele.
- Evită alocările excesive de memorie în procesarea stream-urilor mari.

## Verificări și Validare
- Verifică contractele prin teste automate de integrare.
- Auditează memoria și căile de execuție critice.
- Încarcă maximum 2 documente în contextul de lucru.
