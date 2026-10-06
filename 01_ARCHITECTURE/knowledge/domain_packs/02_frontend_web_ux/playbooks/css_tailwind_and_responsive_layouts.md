---
id: "a37e77d0-8147-4835-a6d3-4443befdc747"
type: knowledge
lifecycle: REVIEW
category: css_tailwind_styling
tags:
  - css_tailwind_styling
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

# Playbook: Stilizare Utilitară, Tailwind v4 și Layout-uri Responsive

Ghid de decizie și reguli operaționale pentru `css_tailwind_styling`.

## Reguli de Decizie
1. Adopta o strategie strictă mobile-first (breakpoint-uri min-width crescătoare).
2. Folosește container queries pentru componente ce trebuie să se adapteze la lățimea părintelui.
3. Asigură suport complet dark mode prin mapare de token-uri semantice CSS.

## Capcane de Evitat
- Nu utiliza reguli !important pentru a suprascrie specificitatea CSS.
- Evită lățimi fixe în pixeli pe ecrane mobile sau layout-uri flexibile.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
