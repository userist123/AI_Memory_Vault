---
id: "5e996db2-ce48-40c7-bee7-3fc9ca7b8c87"
type: lesson
lifecycle: REVIEW
category: projects.trading_bot
tags: [mt5, backtest, requirements]
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
relations: ["[[Backtest_Single_Entry_Logic_Flaw]]"]
---

# Define Multi-Entry and Risk Requirements Before Writing Backtest Logic

## Lesson

> Cerintele de multi-entry (BUY/SELL simultan), selectie de strategie si risk management (SL/TP, dimensionare pozitie) trebuie definite explicit inainte de a scrie logica de backtest, altfel backtest-ul devine o simplificare care nu reflecta comportamentul dorit in live.

## Origin

- [[Backtest_Single_Entry_Logic_Flaw]]

## Context

Dezvoltare de boti de trading MT5 in Python, trecerea de la backtest la aplicatie live.

## Insight

Un backtest scris fara o specificatie clara a comportamentului de intrare/iesire tinde sa implementeze varianta cea mai simpla (o singura intrare), care apoi trebuie rescrisa complet.

## When It Applies

La inceperea oricarui modul de backtest sau simulare pentru sisteme de trading automate.

## How To Apply

1. Scrie explicit specificatia: numarul maxim de intrari simultane, directii permise (BUY/SELL), reguli de SL/TP, criterii de selectie a strategiei.
2. Valideaza specificatia cu utilizatorul inainte de a incepe implementarea.
3. Implementeaza backtest-ul conform specificatiei, nu conform variantei minime functionale.

## Evidence

Backtest initial EURUSD M5 cu o singura intrare, urmat de cerere explicita de rescriere completa pentru multi-entry.

## Exceptions

Pentru prototipuri exploratorii declarate explicit ca "proof of concept", o singura intrare poate fi acceptabila temporar.

## Related Lessons

-

## Related Knowledge

- [[Tech_Stack]]

## Confidence

medium

## Review

De revizuit cand aplicatia live ajunge in faza de paper trading.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
