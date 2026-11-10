# Verified Reduction and Agent Knowledge Handoff

## Contract

The context pipeline is ordered:

1. Verification — establish provenance/trust before reduction.
2. Reduction — remove redundant, stale and repeated payload.
3. Budgeting — enforce byte/token limits on the reduced representation.
4. Progressive disclosure — expose deeper material only when required.
5. Minimal sufficient context — deliver only what the next agent needs.

Reduction is not a security mechanism. Unverified content is rejected before it can be shortened into something that looks trustworthy.

## What reduction preserves

The reducer treats these as structured knowledge rather than disposable prose:

- current state;
- decisions;
- discoveries;
- completed work;
- failed attempts / negative knowledge;
- constraints and requirements;
- dependencies;
- artifacts and recovery references;
- open questions;
- next actions;
- verification;
- provenance;
- integrity;
- security policy and acceptance criteria.

This allows Agent B to continue Agent A's work without replaying the investigation from the beginning.

## Knowledge capsule

VerifiedKnowledgeHandoff produces a compact, recoverable representation containing:

- reduced content;
- structured knowledge fields;
- security/evidence fields;
- original/final character counts;
- estimated tokens saved;
- an optional reference to the full source.

The full source remains recoverable; reduction is not destructive deletion.

## Re-learning avoidance

The reduction metrics expose:

- original_chars;
- final_chars;
- token_estimate_before;
- token_estimate_after;
- tokens_saved.

These metrics are evidence of compression, not a claim that every saved token would otherwise have been spent on re-learning. A future evaluation can add a measured relearning_avoided metric when a baseline/handoff experiment exists.

## Cache awareness

Reduction and prompt caching are separate optimizations. A compact prompt can save input tokens, while a stable prefix can avoid recomputing already-processed context. The implementation therefore must not assume that transport compression equals LLM token savings, and future cache-aware optimization must preserve stable prefixes where reuse is valuable.

## Security invariant

The implementation must never transform:

UNTRUSTED -> short -> trusted

The only permitted direction is:

input -> verification -> trust decision -> reduction -> budget -> disclosure -> agent

## Current implementation

- security/verified_reduction.py — verified-first semantic reducer.
- security/knowledge_handoff.py — recoverable agent-to-agent knowledge capsule.
- 03_IMPLEMENTATION/packages/retrieval/context/pack_builder.py — verifies/reduces context before budget degradation and records reduction metrics.
- security/tests/test_knowledge_handoff.py — reduction contract.
- security/tests/test_knowledge_handoff_capsule.py — capsule/recovery contract.
- security/tests/test_context_pack_reduction_order.py — structural ordering contract.

## Verification note

GitHub Actions is authoritative for runtime verification. Local repository execution was unavailable in the implementation environment, so a green local test result must not be claimed without evidence.
