---
id: "c501887d-f6c5-4196-8e18-44dd4371e6df"
type: knowledge
lifecycle: REVIEW
category: ux_flows_heuristics
tags:
  - ux_flows_heuristics
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

# Playbook: Euristi de UX, Fluxuri de Onboarding și Optimizare Conversie

Ghid de decizie și reguli operaționale pentru `ux_flows_heuristics`.

## Reguli de Decizie
1. Oferă feedback vizual instantaneu pentru fiecare acțiune (loading spinners, mesaje toast, stări disabled).
2. Folosește principiul dezvăluirii progresive (progressive disclosure) pentru a reduce încărcarea cognitivă.
3. Concepe stările goale (empty states) ca oportunități de acțiune cu ghidare directă.

## Capcane de Evitat
- Nu forța utilizatorii să completeze formulare lungi într-un singur pas fără validare pas cu pas.
- Evită alerte generice de eroare de tip 'A apărut o problemă' fără instrucțiuni clare de rezolvare.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
