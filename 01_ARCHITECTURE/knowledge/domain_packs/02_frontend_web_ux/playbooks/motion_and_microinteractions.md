---
id: "2ab69457-fa0e-4e1e-a79c-cb1fd0d0205d"
type: knowledge
lifecycle: REVIEW
category: motion_animation
tags:
  - motion_animation
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

# Playbook: Animații, Tranziții și Micro-interacțiuni

Ghid de decizie și reguli operaționale pentru `motion_animation`.

## Reguli de Decizie
1. Animează exclusiv proprietăți accelerate hardware: transform și opacity.
2. Orchestrează tranzițiile prin Framer Motion sau GSAP cu timpi scurți (150-300ms) pentru feedback tactil.
3. Respectă întotdeauna preferința sistemului: media query prefers-reduced-motion.

## Capcane de Evitat
- Nu anima proprietăți ce declanșează reflow (width, height, top, left, margin).
- Evită animațiile repetitive sau continue care consumă excesiv bateria utilizatorului.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
