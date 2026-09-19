---
id: 7b6aae13-83e6-43ec-80df-7c8a202251fd
type: procedure
lifecycle: REVIEW
category: ingestion.books
tags: ['copyright', 'quoting', 'provenance', 'public-repository']
created: "2026-09-20"
updated: "2026-09-20"
provenance:
  source_type: 'execution'
  source_ref: 'session 2026-09-20: 33 verbatim quotes up to 539 chars found committed in 07_EVALUATION/book_corpus_conversion'
confidence: high
verification: unverified
relations:
  - type: part_of
    target_id: slot-06-procedures
---

# Quoting from copyrighted sources

This repository is public. Books ingested from `06_INBOX/Carti/` are not: they
are ordinary copyrighted works. The PDFs and the extracted text are kept out of
git, and `07_EVALUATION/book_corpus_conversion/FINDINGS.md` says so. The quotes
are the part that slipped through: grounding checks store the sentence a concept
came from, and those artefacts are committed.

They were 33 verbatim sentences from one book, up to 539 characters each, before
this procedure existed.

## Why the quotes exist at all

A quote is how a fabricated concept is caught. `verify_agent_submission.py`
requires the quote to appear verbatim in the source text, so a model that
invents a definition cannot produce evidence for it. Removing quotes entirely
would remove the anti-fabrication check, so they are kept — short.

## The quota

- **At most 180 characters per quote** in any committed artefact. Long enough to
  identify a sentence, short enough not to redistribute the work.
- **At most 40 distinct quotes per work, and 6,000 characters in total**, across
  all committed artefacts. Scattered sentences identify what a concept came
  from; that budget cannot reconstruct a section. The *Cryptoassets* evidence
  sits at 31 quotes and 5,013 characters after redaction.
- No two committed quotes may be adjacent passages of the same work assembled
  into a longer run.

Quotes from notes the vault itself owns — audit samples over vault notes, for
instance — are not covered: they quote this repository's own content.

## What a redacted quote keeps

`30_SCRIPTS/ingestion/redact_long_quotes.py` cuts an over-long quote at a word
boundary and records, beside it:

- `<field>_sha256` — the SHA-256 of the full quote as it was verified, so the
  grounding decision stays auditable;
- `<field>_chars` — its original length.

The truncated text is still a prefix of the source sentence, so
`verify_agent_submission.py` keeps matching it against the local source text.

## Licences are claimed, not verified

Provenance records a `source_license`, and for books that string is whatever the
ingesting agent wrote. The six `ashby_*` notes declare "Public Domain / Open
Educational Access" with no verification behind it; they were archived for a
different reason (`scrise de agent fără citate din sursă`). Treat a licence
field on a book-derived note as an unverified claim until the owner confirms it
against the publisher.

OpenStax material is the exception: it is CC BY and the manifest carries the
licence URL.

## Enforcement

- `20_TESTS/test_quote_budget.py` fails if a committed book-derived artefact
  carries a quote over the limit, or more than the per-work quota.
- `python 30_SCRIPTS/ingestion/redact_long_quotes.py` reports violations;
  `--apply` fixes them.
