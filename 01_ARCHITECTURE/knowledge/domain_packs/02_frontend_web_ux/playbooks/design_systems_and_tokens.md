---
id: "bc31affe-87af-4fe7-8a14-f30585fbafc7"
type: knowledge
lifecycle: REVIEW
category: design_systems_components
tags:
  - design_systems_components
  - frontend-web-ux
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

# Playbook: Design Systems, Token-uri și Componente Primitive

Ghid de decizie și reguli operaționale pentru `design_systems_components`.

## Reguli de Decizie
1. Construiește pe primitive accesibile (Radix UI / Shadcn) fără stilizare rigidă hardcodată.
2. Centralizează token-urile de design (culori, spațiere, rază, umbre) în variabile CSS.
3. Compune componente atomice folosind CVA (Class Variance Authority) pentru variante de stil.

## Capcane de Evitat
- Nu hardcoda culori hex sau spațieri arbitrare în clase utilitare ad-hoc.
- Evită duplicarea structurilor de componente pentru mici variații de stare.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
