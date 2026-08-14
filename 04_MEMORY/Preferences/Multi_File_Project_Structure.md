---
id: "0d0d9f30-fa32-416f-95e3-0149006dae79"
type: preference
lifecycle: REVIEW
category: projects.software_architecture
tags: [architecture, python, code-structure]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-06-10_2026-06-13
  source_date: 2026-06-13
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: ["[[Elite_Quant_Bot]]", "[[Tech_Stack]]"]
---

# Prefer Multi-File Project Structure Over Single-File Scripts

## Preference

> Utilizatorul prefera aplicatii Python structurate in mai multe fisiere/module (ex. main.py, config.py, core/, ui/) in locul unui singur script monolitic.

## Context

Aplicabil la proiectele de trading bots (MT5, Tkinter) si alte aplicatii desktop/backend Python.

## Why

Structura multi-file este mai usor de mentinut, testat si extins pentru aplicatii de productie complexe (risc, executie, UI separate).

## What To Prefer

- Separare clara: config, core logic, UI, backtest.
- Module reutilizabile (broker.py, indicators.py, strategy.py, risk.py).

## What To Avoid

- Livrarea unui singur fisier gigant "Everything_I_Know_About_X.py".

## Flexibility

Pentru prototipuri foarte mici, un singur fisier poate fi acceptabil daca este declarat explicit ca proof-of-concept.

## Evidence

Cereri repetate pentru "arhiva completa a aplicatiei" cu fisiere separate (config.py, broker.py, indicators.py, strategy.py, news_filter.py, risk.py, engine.py, ui/main_window.py, main.py, backtest/run_backtest.py) in conversatia din 2026-06-10.

## Related

- [[Elite_Quant_Bot]]
