---
id: "7e08e7ec-27ff-4c98-b2b7-3f31ac5014ab"
type: knowledge
lifecycle: REVIEW
category: react_nextjs_frameworks
tags:
  - react_nextjs_frameworks
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

# Playbook: Arhitectură React și Next.js App Router

Ghid de decizie și reguli operaționale pentru `react_nextjs_frameworks`.

## Reguli de Decizie
1. Păstrează componentele Server Components (RSC) ca implicite; folosește 'use client' doar pentru interactivitate (state, hooks, event handlers).
2. Optimizează transferul de date prin streaming SSR și limite Suspense fine pe blocuri dinamice.
3. Izolează componentele de layout pentru a preveni re-render-urile inutile la schimbarea rutei.

## Capcane de Evitat
- Nu declara 'use client' în nodul rădăcină al aplicației.
- Evită waterfall-uri de cereri fetch în Client Components când pot fi rulate paralel pe server.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
