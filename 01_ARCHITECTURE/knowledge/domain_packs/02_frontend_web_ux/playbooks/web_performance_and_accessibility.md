---
id: "49ff71f6-bca4-4aec-a156-a6be3ad5cd61"
type: knowledge
lifecycle: REVIEW
category: performance_accessibility_seo
tags:
  - performance_accessibility_seo
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

# Playbook: Performanță Web (Core Web Vitals) și Accesibilitate WCAG

Ghid de decizie și reguli operaționale pentru `performance_accessibility_seo`.

## Reguli de Decizie
1. Optimizează LCP (Largest Contentful Paint) prin preloading fonturi critice și dimensiuni explicite la imagini.
2. Asigură conformitatea WCAG 2.1 AA: contrast minim 4.5:1, navigare completă prin tastatură, focus outline vizibil.
3. Structurează semantic documentul (main, nav, header, article) și etichetează corect ARIA landmarks.

## Capcane de Evitat
- Nu ascunde starea de focus cu outline: none fără un indicator vizual echivalent.
- Evită schimbările bruște de layout (CLS) cauzate de injectarea târzie a reclamelor sau imaginilor.

## Verificări și Validare
- Verifică integritatea UI și compatibilitatea cross-browser.
- Măsoară impactul asupra performanței și bugetului de tokeni.
- Încarcă maximum 2 documente în contextul de lucru al agentului.
