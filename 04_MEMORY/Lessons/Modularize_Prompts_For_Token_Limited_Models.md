---
id: "6bdb2775-add5-426b-ac0c-1b1fef75f2e1"
type: lesson
lifecycle: REVIEW
category: knowledge.prompt_engineering
tags: [prompt-engineering, token-limits, llm]
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
relations: ["[[AI_Trading_Journal_Zero_Dollar_Stack]]"]
---

# Split Implementation Prompts Into Small Modules When Targeting Token-Limited Models

## Lesson

> Cand modelul tinta (ex. Gemma local, modele gratuite) are o limita stricta de tokeni (ex. 4096, prompt + raspuns inclus), promptul de implementare trebuie spart in module mici (cate o functionalitate/tab pe prompt), altfel esueaza sau trunchiaza raspunsul.

## Origin

- [[AI_Trading_Journal_Zero_Dollar_Stack]]

## Context

Generare de cod pentru aplicatii SaaS folosind modele AI locale sau gratuite cu limite mici de context/tokeni.

## Insight

Un prompt masiv, desi complet din punct de vedere functional, devine inutilizabil daca depaseste limita de tokeni a modelului tinta; impartirea pe module mentine calitatea raspunsului.

## When It Applies

La lucrul cu modele cu fereastra de context mica sau limite explicite de tokeni per request (ex. self-hosted, tier gratuit).

## How To Apply

1. Estimeaza numarul de tokeni al promptului complet.
2. Daca depaseste limita modelului tinta, imparte pe module functionale (ex. un tab/o functie per prompt).
3. Pastreaza un fir de continuitate intre module (context minim necesar reluat la fiecare prompt nou).

## Evidence

Prompt initial pentru trading journal SaaS a trebuit spart in 8 prompturi mici din cauza limitei de 4096 tokeni per model Gemma.

## Exceptions

Nu se aplica la modele cu fereastra de context mare (ex. 128k+ tokeni) unde promptul complet incape fara probleme.

## Related Lessons

-

## Related Knowledge

- [[Tech_Stack]]

## Confidence

medium

## Review
