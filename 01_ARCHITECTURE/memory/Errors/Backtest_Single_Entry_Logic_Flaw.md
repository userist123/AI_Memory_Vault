---
id: "7a873fba-2d99-4f1a-bde9-44edc8239e6e"
type: error
lifecycle: REVIEW
category: projects.trading_bot
tags: [mt5, backtest, strategy-logic]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-06-10
  source_date: 2026-06-10
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: ["[[Elite_Quant_Bot]]"]
---

# Quant Bot Backtest Only Executed Single Entries, Not Multi-Entry BUY/SELL

## Summary

Backtest-ul initial pe EURUSD (M5, 5000 bare) rula doar cu o singura intrare per semnal, in loc sa suporte intrari multiple pe BUY si SELL cu TP/SL si selectie de strategie.

## Environment

Python, MetaTrader5, backtest/run_backtest.py, simbol EURUSD, timeframe M5.

## Expected

Bot-ul trebuia sa faca mai multe intrari (nu doar una), sa aleaga intre BUY/SELL cu TP/SL si sa selecteze dinamic strategia si atributele folosite.

## Actual

Rezultatele de backtest aratau o singura intrare per configurare, semn ca logica de intrare era prea restrictiva/simplista.

## Reproduction

Rulare: `python backtest/run_backtest.py --symbol EURUSD --bars 5000`.

## Root Cause

Logica de generare a semnalelor nu permitea intrari multiple concurente si nu expunea clar strategia si atributele selectate la fiecare intrare.

## Contributing Factors

Structura initiala a proiectului (single-file/simplificata) facea dificila extinderea logicii de intrare.

## Fix

Restructurare completa a proiectului in module separate (config, broker, indicators, strategy, news_filter, risk, engine, ui) si trecere la o aplicatie live cu suport multi-entry.

## Verification

- [ ] Verificare ulterioara ca noua arhitectura suporta efectiv multi-entry BUY/SELL cu TP/SL in productie/paper trading.

## Prevention

Defineste explicit cerintele de multi-entry si risk management inainte de a scrie logica de backtest, nu dupa.

## Lesson Extracted

- [[Define_MultiEntry_Requirements_Before_Backtest]]

## Related

- [[Elite_Quant_Bot]]

## Sources

- Conversatie AI din 2026-06-10

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
