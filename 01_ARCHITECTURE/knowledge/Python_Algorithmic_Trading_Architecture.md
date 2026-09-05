---
id: "c1a01101-7291-49fa-9481-22904c10b004"
type: knowledge
lifecycle: REVIEW
category: quant-trading
tags:
  - algorithmic-trading
  - metatrader5
  - risk-management
  - python
  - backtesting
created: 2026-08-17T23:00:00Z
updated: 2026-08-17T23:00:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/coding/python-trading-systems/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:00:00Z
relations:
  - target: "[[Elite_Quant_Bot]]"
    type: implements
---

# Arhitectura Sistemelor de Tranzacționare Algoritmică în Python (MT5 & Risc)

## TL;DR
Un bug în codul de trading generează pierderi financiare directe. Arhitectura impune separarea decuplată a 5 module: `data/`, `strategy/`, `risk/`, `execution/` și `journal/`. Modulul de risc deține drept de veto absolut asupra oricărui semnal generat de strategie; backtesting-ul interzice cu strictețe bias-ul de tip *look-ahead*.

## Key Facts
- **Separare Strictă pe 5 Straturi**:
  1. `data/`: Feed-uri de preț, lumânări și normalizare (fără logică de decizie).
  2. `strategy/`: Semnale pure (`generate_signal(data) -> Signal`), fără side-effects sau apeluri de broker.
  3. `risk/`: Dimensionare poziție, calcul Stop-Loss din procent fix de echitate, drawdown maxim — **VETO absolut**.
  4. `execution/`: Ordine MetaTrader 5, verificare `retcode`, slippage tracking și retry.
  5. `journal/`: Salvarea contextului decizional complet *înainte* de trimiterea ordinului în piață.
- **Reguli Anti-Look-Ahead în Backtest**:
  - Semnalul de pe bara `N` folosește exclusiv date până la bara `N-1` închisă.
  - Calculul include obligatoriu spread-ul real, comisioanele și slippage-ul estimat.
  - Metricile raportate obligatoriu: *Profit Factor*, *Max Drawdown*, *Win Rate*, număr total tranzacții.
- **Controlul Riscului**: Stop de urgență (*Kill Switch*) accesibil instant; limită de pierdere zilnică (*Daily Loss Limit*) cu oprire automată a tranzacționării.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
