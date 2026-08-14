---
id: "0923cd4f-b2bd-4285-b11f-63be9d9977e8"
type: experience
lifecycle: REVIEW
category: projects.ai_trading_journal
tags: [saas, trading-journal, romania, llm-fallback]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-04-17
  source_date: 2026-04-17
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: []
---

# Designing a Zero-Dollar Bilingual (RO/EN) AI Trading Journal SaaS

## Summary

Utilizatorul a explorat construirea primului trading journal SaaS bilingv (romana-engleza) din Romania, pe un stack cu cost zero, vizand un gol de piata neocupat.

## Context

Traderii romani foloseau in prezent template-uri Excel, Google Sheets de la Binance Academy sau unelte scumpe doar in engleza; nu exista o solutie SaaS locala bilingva.

## What Happened

S-a construit un research asupra oportunitatii si apoi un prompt tehnic pentru Gemma, pentru implementarea aplicatiei conform research-ului, cu stack React, FastAPI, Python, MongoDB si integrari AI.

## My Actions

- Am definit un fallback chain de modele LLM (ex. groq llama3.3-70b, gemini-2.5-flash, cerebras llama3.1-8b, openrouter llama-3.3-70b) pentru reziliente cand un provider face throttling.
- Am impartit promptul in module mai mici (max 4096 tokeni) pentru compatibilitate cu limitele modelelor locale/gratuite.

## Outcome

Arhitectura permite mentinerea costurilor la $0 pana la aproximativ 10.000 utilizatori activi lunar (MAU), cu upgrade planificat doar dupa acest prag.

## Expected vs Actual

| Aspect | Expected | Actual |
|---|---|---|
| Cost infrastructura pana la 10k MAU | $0 | $0 (proiectat, neconfirmat in productie) |
| Compatibilitate prompt cu modele gratuite | Direct | A necesitat spargere in 8 prompturi mici din cauza limitei de 4096 tokeni |

## Observations

Limitele de tokeni ale modelelor gratuite/locale forteaza modularizarea agresiva a prompturilor de implementare.

## What I Learned

Un stack "zero-dollar" este posibil pentru un SaaS de nisa daca se proiecteaza explicit un fallback chain de LLM-uri si module mici de prompt.

## Lessons Extracted

- [[Modularize_Prompts_For_Token_Limited_Models]]

## Related

-
