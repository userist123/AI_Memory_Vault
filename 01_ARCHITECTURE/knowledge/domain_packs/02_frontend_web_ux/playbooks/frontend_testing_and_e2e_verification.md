---
id: "9951bd17-8c78-4e2a-8fa0-a70f353e3dcb"
type: knowledge
lifecycle: REVIEW
category: client_state_testing
tags:
  - client_state_testing
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

# Playbook: Testare Frontend, Component Testing și Validare E2E

Ghid de decizie și reguli operaționale pentru `client_state_testing`.

## Reguli de Decizie
1. Scrie teste centrate pe utilizator folosind React Testing Library sau Vitest (interogări prin rol și text vizibil).
2. Implementează fluxuri end-to-end critice (login, checkout, configurare) în Playwright.
3. Izolează apelurile backend externe folosind Mock Service Worker (MSW) pentru determinism absolut.

## Capcane de Evitat
- Nu testa starea internă sau metodele private ale componentelor.
- Evită pauzele statice (sleep/wait arbitrar) în testele E2E; bazează-te pe mecanismul auto-waiting din Playwright.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
