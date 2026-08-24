---
type: project
category: trading
tags: [project, trading, mql5, python, active]
created: 2026-08-09
updated: 2026-08-09
status: review
priority: medium
related: ["[[Tech_Stack]]"]
id: "a4a4d05d-bc1b-4972-8c2d-299a4cf8ddce"
lifecycle: REVIEW
provenance_status: incomplete
provenance:
  source_type: unknown
  source_ref: ""
  redaction: not_applicable
confidence: unknown
verification: unverified
relations: []
---

# Elite Quant Bot / XAU_Kinetic

## Descriere
Sistem de trading algoritmic — Python (config, backtesting, orchestrare) + MQL5 (Expert Advisors MT5). Profil principal: XAUUSD.

## Componente cunoscute
- **Elite Quant Bot** (Python) — config system: multi-TP ladder logic, ATR multipliers, circuit breakers, ensemble thresholds, profil instituțional XAUUSD, live override system
- **XAU_Kinetic_v1** (MQL5) — EA pentru XAUUSD M15

## Constrângeri tehnice cunoscute (MQL5)
- `const string` globals
- static vs. dynamic arrays
- pattern-uri `TimeCurrent`/`TimeToStruct`
- auto-detecție filling mode per broker

## Next Steps
🔲 *De completat — status curent al dezvoltării*
