# R001 C10 — Hybrid retrieval forensics

Evidence level: `CODE_VERIFIED` + `RUNTIME_VERIFIED` for the deterministic
probe below. This lane is not independently verified by another agent.

## Baseline

```text
BASE_COMMIT=ea69ddb318a1c7feab98be2116e1a7d49b650375
```

## Observed architecture

- The default `MemoryController.search()` path uses
  `memory_controller.context.RelevanceScorer`, whose implementation is token
  overlap plus confidence.
- `BM25Ranker` exists in `memory_controller.financial_search`, but is not wired
  into the standard controller search path.
- `DeterministicSemanticProvider` is Jaccard token overlap, not an embedding
  model.
- Ollama/Qdrant semantic retrieval is optional and separate from default
  controller search.

Classification: default hybrid retrieval `PARTIAL`; real vector semantic
retrieval in the default path `UNVERIFIED`.

## Runtime probe

Command used a three-note corpus with one lexical match, one synonym-like
near match, and one unrelated high-confidence note:

```text
RELEVANCE [{'id': 'a', 'score': 0.5333333333333333}, {'id': 'b', 'score': 0.45}, {'id': 'c', 'score': 0.45}]
BM25 [0.8429001393069523, 0.0, 0.0]
```

The default scorer ties the unrelated note `c` with the near-match `b`
because confidence contributes even when lexical overlap is zero. BM25
discriminates the exact lexical match in this probe, but no production fusion
decision is made by the standard controller.

## Decision

No speculative hybrid implementation was added. Introducing BM25 or dense
embeddings into the default path would change ranking semantics and requires a
held-out benchmark, hard negatives, lifecycle filtering, provenance checks,
and abstention acceptance criteria first.

Remaining gap: implement or explicitly expose an opt-in hybrid adapter with
fixed candidate sets and traceable lexical/semantic component scores, then
validate it independently against paraphrases, synonyms, lexical traps, and
domain-near negatives.
