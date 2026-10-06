---
id: "34fd01e6-4816-4d7f-85ca-b05a64939f9f"
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

# Playbook: Stare Client și Sincronizare Asincronă de Date

Ghid de decizie și reguli operaționale pentru `client_state_testing`.

## Reguli de Decizie
1. Separă starea de server (TanStack Query) de starea de interfață pură a clientului (Zustand).
2. Folosește chei de query ierarhice și predictibile pentru invalidare selectivă.
3. Implementează mutații optimiste cu mecanism robust de rollback în caz de eroare rețea.

## Capcane de Evitat
- Nu copia manual server state în store-uri locale Zustand sau Redux.
- Evită re-render-urile globale prin folosirea de selectori atomici în store-uri.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
