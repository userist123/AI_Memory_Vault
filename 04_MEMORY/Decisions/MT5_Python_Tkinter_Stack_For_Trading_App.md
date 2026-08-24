---
id: "0e95a2f1-b9f4-4f0a-81b4-17625f8034a6"
type: decision
lifecycle: REVIEW
category: projects.trading_bot
tags: [mt5, python, tkinter, decision]
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

# Use MetaTrader 5 + Python + Tkinter for the Live Trading Application

## Decision

Aplicatia de tranzactionare live va fi construita in Python 3.12 cu MetaTrader5 pentru executie si Tkinter pentru UI, nu PySide6, pentru compatibilitate maxima cu setup-ul existent.

## Context

Utilizatorul avea deja un bot de backtest MT5 in Python si a cerut trecerea la o aplicatie completa de tranzactionare live, multi-entry, BUY si SELL, cu SL/TP automat si filtru de stiri.

## Problem

Alegerea framework-ului UI (Tkinter vs PySide6) si a arhitecturii pentru trecerea de la backtest la executie live.

## Options

### Option A: Tkinter

**Pros:**
- Compatibilitate maxima cu codul si mediul existent al utilizatorului.
- Fara dependinte externe suplimentare grele.

**Cons:**
- UI mai putin modern vizual fata de PySide6.

### Option B: PySide6

**Pros:**
- UI modern, componente mai bogate.

**Cons:**
- Dependinta suplimentara, posibil overhead de instalare/compatibilitate.

## Rationale

Utilizatorul a optat pentru compatibilitate maxima cu ce avea deja instalat si functional, evitand riscul de a introduce probleme noi de mediu.

## Expected Outcome

O aplicatie live MT5 functionala, cu executie reala, multi-entry si management de risc, fara a schimba stack-ul UI de baza.

## Risks

Tkinter poate limita complexitatea vizuala viitoare a UI-ului daca cerintele cresc.

## Implementation

- [x] Confirmat de utilizator: "o tin strict in Tkinter pentru compatibilitate maxima cu ce ai deja" (parafraza din conversatie)

## Review Trigger

Daca cerintele UI devin semnificativ mai complexe (grafice interactive avansate, teme multiple), reconsidera PySide6.

## Result

Decizie luata; dezvoltarea a continuat pe stack Python + MT5 + Tkinter.

## Related

- [[Elite_Quant_Bot]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
