---
id: "d94d32d6-701e-43b7-b031-761b2c6c8e43"
type: knowledge
lifecycle: REVIEW
category: trading.mt5_multi_strategy
tags: [mt5, python, multi-strategy, backtesting]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: external_documentation
  source_ref: "https://github.com/Zsunflower/Monn"
  source_date: 2023-05-31
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: []
---

# Monn — MetaTrader5 Auto-Trading Bot with Multiple Strategies and Timeframes

## Summary

Monn este un bot de trading care folosește MetaTrader5 API și poate rula simultan mai multe strategii și analiza multiple timeframes, oferind backtesting și tuning de parametri.

## Core Concept

Botul separă configurarea contului MT5, fișiere de config pentru simboluri și strategii, modul de live trading și modul de test/backtest, permițând orchestrarea mai multor strategii pe diferite timeframes.

## Key Points

- Multiple strategies: configurabile în fișiere JSON, fiecare analizată independent.
- Multiple timeframes: colectează date pentru timeframes diferite pentru decizii mai informate.
- Backtesting: modul de test folosește date istorice pentru evaluarea strategiilor.
- Parameter tuning: script dedicat pentru tuning de parametri în funcție de performanța pe date istorice.

## Caveats

- Necesită TA-Lib și configurare atentă a fișierelor JSON; nu include management de risc avansat sau observability out-of-the-box.

## Verification

- [ ] Source checked
- [ ] Scope/environment checked
- [ ] Links checked

## Changelog

- 2026-08-14: Nota creată din README-ul Monn.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
