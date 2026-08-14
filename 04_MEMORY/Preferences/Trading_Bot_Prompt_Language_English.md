---
id: "1328c5fb-3316-4c02-ae53-511eb3d3f96b"
type: preference
lifecycle: REVIEW
category: projects.trading_bot
tags: [trading-bot, prompting, mt5, python]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-06-13
  source_date: 2026-06-13
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: ["[[Elite_Quant_Bot]]"]
---

# Trading Bot Prompts Should Be Written in English, In Exhaustive Detail

## Preference

> Cand cere prompturi tehnice pentru bot-uri de trading (MT5 + Python), utilizatorul vrea ca promptul final sa fie scris in engleza, ca sa nu se piarda detalii importante prin traducere, si sa fie extrem de detaliat si exhaustiv, acoperind fiecare parte ceruta.

## Context

Aplicabil la generarea de prompturi tehnice complexe (ex. Elite Quant Bot, multi-file, MT5) destinate altor modele AI sau reutilizarii ulterioare.

## Why

Engleza reduce ambiguitatea si pierderea de precizie tehnica fata de romana, mai ales pentru termeni specifici de trading si programare.

## What To Prefer

- Prompt tehnic complet in engleza.
- Fiecare cerinta functionala enumerata explicit (ce se intampla inainte de trade, la executie, dupa TP/SL).
- Structura multi-file explicita in prompt.

## What To Avoid

- Prompturi scurtate care omit reguli MT5 sau management de risc.
- Traduceri automate ale termenilor tehnici care pot introduce ambiguitate.

## Flexibility

Pentru discutii curente si explicatii, utilizatorul comunica in continuare in romana; preferinta se aplica strict la promptul tehnic final.

## Evidence

Conversatie din 2026-06-13 in care utilizatorul cere explicit "scrie-mi promptul asta ... fara sa pierzi esentialul" pentru promptul Elite Quant Bot in engleza.

## Related

- [[Elite_Quant_Bot]]
