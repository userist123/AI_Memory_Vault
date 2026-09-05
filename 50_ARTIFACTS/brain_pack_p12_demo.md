# BRAIN PACK — bp_649cfe243c81

> Task: Cum functioneaza mecanismul de plasticitate al sinapselor si ce inseamna verified externally?
> Generated: 2026-09-05T10:16:26+00:00 :: budget 4000 tok :: used 1753 tok :: 5 nodes

**Contract:** this is the context retrieved for this task. If a needed fact is not here, say explicitly that memory does not contain it — do not invent it. Notes marked `UNVERIFIED` are hypotheses, not truth. observed retrieval != proven causal influence.

## PROCEDURES

### Promotion and Human Review
<!-- id:27f72d97-a5f0-4217-beb8-279cd5930b5c act:0.948 src:10_DOCUMENTATION/resources/Obsidian/Artifacts/Promotion_and_Human_Review.md -->
# Promotion and Human Review

## Promotion gate

A `REVIEW` note may become `VERIFIED` and then `ACTIVE` only after provenance, schema, duplicate, contradiction, security, and relevant wikilink checks pass. Verification must match the evidence; it is not implied by promotion.

## Human review required

Require explicit human confirmation before activating material that affects identity facts, stable preferences, high-impact decisions, security/infrastructure procedures, unresolved contradictions, or any claim whose source cannot be independently interpreted.

## Automated or agent-assisted review

An agent may prepare, normalize, link, and flag a candidate. It must not silently promote weak imported or inferred content to `ACTIVE`. The review record belongs in the note changelog or a linked decision when material.

## Related

- [[Integrity Check]]
- [[Provenance and Redaction]]

### Import Pipeline
<!-- id:8f068f3d-cbc5-4912-b3ec-f15c3518d5ab act:0.903 src:10_DOCUMENTATION/resources/Obsidian/Artifacts/Import_Pipeline.md -->
# Import Pipeline

## Boundary

This is a documentation-only process. It does not implement parsing, indexing, embeddings, RAG, a graph runtime, or automatic promotion.

## Canonical pipeline

```text
External source
  -> RAW (preserved permanently in 06_INBOX/RAW_IMPORTS/)
  -> CLASSIFIED (derivative only)
  -> NORMALIZED
  -> REVIEW (security, provenance, duplicate and conflict checks)
  -> VERIFIED
  -> ACTIVE canonical memory
  -> SUPERSEDED/ARCHIVED when no longer current
```

## Raw evidence

`06_INBOX/RAW_IMPORTS/` is permanent evidence and provenance. Never delete, rewrite, rename, or index its contents as canonical memory. Every normalized or redacted derivative records `provenance.original_path` relative to that directory.

## Review and promotion

- Classify atomically and retain available platform, conversation, date, and source identifier.
- Do not merge merely because content is similar.
- Preserve unresolved contradictions and link them with `contradicts`.
- Require human review for the material defined in [[Promotion and Human Review]].
- Run [[Integrity Check]] before activating a candidate.

## Related

- [[Storage Conventions]]
- [[Provenance and Redaction]]
- [[Canonical Frontmatter]]

### Import Sanitization `[PARTIALLY_VERIFIED]`
<!-- id:0c4c8b76-85c4-4fde-a14a-4bde0b840010 act:0.877 src:10_DOCUMENTATION/procedures/Import_Sanitization.md -->
# Import Sanitization

## Purpose

Prepare an external-memory derivative without changing the original evidence.

## Procedure

1. Preserve the unmodified export permanently in `06_INBOX/RAW_IMPORTS/`.
2. Create a derivative outside `RAW_IMPORTS/`; record its source in `provenance.source_ref` and `provenance.original_path`.
3. Remove conversational noise, separate atomic concepts, classify the candidate, and assign provisional confidence and verification.
4. Redact credentials, secrets, and unnecessary personal data from the derivative only; record `provenance.redaction: applied` when used.
5. Check duplicates, contradictions, frontmatter, and links using [[Integrity_Check|Integrity Check]].
6. Move only the derivative through `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED`.
7. Require human review for the classes defined in [[Promotion_and_Human_Review|Promotion and Human Review]].

## Prohibitions

- Do not delete, rewrite, rename, or index `RAW_IMPORTS/` as canonical memory.
- Do not represent imported AI content as independently verified.
- Do not silently merge conflicting claims.

## Verification

- [ ] Original source remains present under `06_INBOX/RAW_IMPORTS/`.
- [ ] Derivative points to the original path.
- [ ] Integrity Check findings are resolved or documented.

## Related

- [[Storage_Conventions|Storage Conventions]]
- [[Memory_Lifecycle|Memory Lifecycle]]
- [[Provenance_and_Redaction|Provenance and Redaction]]

## FACTS

### Memory Lifecycle
<!-- id:89105d0b-9fd8-4037-906f-ed2325a9f1bc act:1.000 src:10_DOCUMENTATION/resources/Obsidian/Artifacts/Memory_Lifecycle.md -->
# Memory Lifecycle

The canonical lifecycle is:

`RAW → CLASSIFIED → NORMALIZED → REVIEW → VERIFIED → ACTIVE → SUPERSEDED/ARCHIVED`

| State | Meaning | Storage rule |
|---|---|---|
| `RAW` | unmodified external evidence | `06_INBOX/RAW_IMPORTS/`; never canonical or indexed as canonical |
| `CLASSIFIED` | candidate has a tentative type | derivative outside RAW_IMPORTS |
| `NORMALIZED` | candidate is atomic and uses schema | derivative outside RAW_IMPORTS |
| `REVIEW` | awaits deduplication, conflict, provenance, and security review | not active canonical guidance |
| `VERIFIED` | claims were checked to stated verification level | eligible for promotion |
| `ACTIVE` | approved canonical memory | canonical folders only |
| `SUPERSEDED` | retained history replaced by a newer note | keep link to replacement |
| `ARCHIVED` | retained but no longer current | never delete solely for age |

Only a derivative moves through the lifecycle. The raw original stays `RAW` permanently.

## Related

- [[Promotion and Human Review]]
- [[Import Pipeline]]

### Artifact: PERPLEXITY_TAKEOVER_01_DOCUMENTATION
<!-- id:1ed59fbc-9b1c-402f-a19a-e6bbb99cde46 act:0.906 src:10_DOCUMENTATION/resources/Obsidian/Artifacts/PERPLEXITY_TAKEOVER_01_DOCUMENTATION.md -->
# Artifact: PERPLEXITY_TAKEOVER_01_DOCUMENTATION

# PERPLEXITY TAKEOVER 01 DOCUMENTATION


============================================================
FILE: 02_PROJECTS/Continuity_Handoff.md
============================================================

---
id: "8c7d5c90-9c29-450b-b5a9-e2b2024db502"
type: project
lifecycle: ACTIVE
category: continuity
tags: [handoff, agent-continuity]
created: "2026-08-10"
updated: "2026-08-10"
provenance:
  source_type: user
  source_ref: handoff
confidence: very_high
verification: verified
relations: []
---

# Agent Transfer & Continuity Handoff Package
**Vault Path**: `02_PROJECTS/Continuity_Handoff.md`  
**Version**: `1.0.0`  
**Target Agent**: Perplexity Desktop

---

## 1. INSTRUCTIONS FOR PERPLEXITY ("START HERE")

Welcome, Successor Agent (Perplexity). Do not guess or assume what the previous agent knew. Follow these exact instructions to bootstrap your execution:

1. **Read this Document First**: This handoff details the entire system architecture, runtime call graphs, and historical context.
2. **Read the Core Operating Protocols**:
   - Inspect [00_CORE/Rules.md](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/00_CORE/Rules.md) (Core rules).
   - Inspect [00_CORE/Memory_Protocol.md](file:///C:/Users/user/Documents/Codex/AI_Memory_VAULT_CODEX_READY/00_CORE/Memory_Protocol.md) (Deduplication, versioning, and supersession enforcements).
3. **Verify the Environment State**:
   - Run the pytest suite immediately: `python -m pytest -q`
   - Run the multi-process restart verification: `python C:\Users\user\.gemini\antigravity\brain\aebf6032-0fa2-438b-bb11-3eda139a64e3\scratch\run_multi_process_test.py`
4. **Respect the Autonomy Gates**: Never attempt a `HIGH` risk action (e.g. modifying human-verified nodes or raw imports) without human approval.
...[truncated]
