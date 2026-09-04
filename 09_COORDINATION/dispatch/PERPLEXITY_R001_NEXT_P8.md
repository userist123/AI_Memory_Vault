# PERPLEXITY R001 — NEXT TASK P8

## Mission
Convert the existing external research into a **repository-independent decision/evidence matrix** that CODEX and LUNA can directly use for implementation and falsification. Do not repeat generic literature review.

## Start point
Use the existing `PERPLEXITY_MEMORY_ENGINE_RESEARCH_V2` as the research base. Preserve its explicit limitation: it does not prove repository behavior.

## Required outputs
Create:
- `07_EVALUATION/perplexity/P8_EVIDENCE_TO_TEST_MATRIX.md`
- `07_EVALUATION/perplexity/P8_SOURCE_REGISTER.md`

## Build the matrix
For each major area:
1. memory poisoning;
2. hybrid retrieval;
3. held-out evaluation;
4. calibration;
5. associative/graph memory;
6. outcome learning;
7. temporal validity;
8. provenance-aware ranking;

map:
`external evidence → claim supported by literature → limitation → repository question → exact test → metric → failure interpretation → implementation implication`.

## Hard requirements
- Keep source evidence separate from inference.
- Flag emerging/low-confidence literature.
- Do not claim the repository implements a cited technique.
- Do not prescribe architecture merely because a paper reports an improvement.
- For every proposed technique, state what would falsify its usefulness in this Vault.

## High-value decisions to sharpen
- When is hybrid lexical+dense retrieval preferable to pure dense?
- What held-out split prevents evidence leakage for multi-hop memory?
- How should source confidence, retrieval relevance, and answer correctness remain separate?
- What is the minimum evidence required to call a graph system genuinely associative?
- What must be independently verified before outcome-derived procedures can be reinforced?
- How should SUPERSEDED memories behave under historical vs current queries?
- How should provenance reliability interact with freshness and relevance?

## Acceptance criteria
P8 is complete when the two artifacts provide directly executable acceptance tests for the next implementation/review cycle and clearly identify which recommendations are evidence-backed versus design hypotheses.

No repository implementation. No canonical memory changes. No modification of other agents' artifacts.
