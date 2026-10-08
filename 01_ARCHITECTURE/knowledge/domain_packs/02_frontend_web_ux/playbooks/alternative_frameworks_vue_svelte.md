---
id: "1ce095e1-a7b9-48d2-af31-dbb6589db71f"
type: knowledge
lifecycle: REVIEW
category: vue_svelte_frameworks
tags:
  - vue_svelte_frameworks
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

# Playbook: Framework-uri Alternative (Vue, Svelte) și Strategii de Migrare

Ghid de decizie și reguli operaționale pentru `vue_svelte_frameworks`.

## Reguli de Decizie
1. Adopta Composition API cu <script setup> și Pinia în proiectele Vue 3 moderne.
2. Folosește SvelteKit pentru aplicații axate pe viteză maximă și încărcătură JS minimă.
3. Proiectează componente agnostice de design tokens pentru interoperabilitate multi-framework.

## Capcane de Evitat
- Nu amesteca Options API cu Composition API în aceeași componentă Vue.
- Evită manipularea directă a nodurilor DOM native în interiorul componentelor reactive Svelte.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
