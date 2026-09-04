# Score Distribution & Discrimination Report V1 (Antigravity Observability)

**Target Repository**: `userist123/AI_Memory_Vault_CODEX_READY`  
**Observability Agent**: Antigravity  
**Date**: 2026-09-04  
**Status**: `RUNTIME_VERIFIED` / `TEST_VERIFIED`  

---

## 1. Executive Summary & Separation Classification

We executed empirical retrieval trials across 7 canonical query archetypes against the active candidate set (10 book-derived synthesis review atoms + active, superseded, and archived nodes) using `RecallEngine` with `DeterministicSemanticProvider`.

| Query Archetype | Top-1 Target | Top-1 Score | Top-2 Score | Margin | Abstained | Separation Class |
|---|---|---|---|---|---|---|
| **1. Exact Relevant Query** | `M-ADAPT-001` | **0.3875** | 0.1850 | **+0.2025** | No | `WELL_SEPARATED` |
| **2. Paraphrase Query** | `M-ADAPT-001` | **0.2507** | 0.2037 | **+0.0470** | No | `MODERATE` |
| **3. Synonym Query** | `M-ARCH-001` | 0.1850 | 0.1841 | +0.0009 | **Yes** | `INDISTINGUISHABLE` |
| **4. Unrelated Query** | `M-RELIABILITY-001` | 0.2083 | 0.2024 | +0.0059 | No | `FLAT` |
| **5. Lexical Trap Query** | `M-ADAPT-001` | **0.2020** | 0.1730 | **+0.0290** | No | `MODERATE` (False Positive) |
| **6. Historical Query** | `M-ADAPT-001` | 0.1600 | 0.1600 | +0.0000 | **Yes** | `INDISTINGUISHABLE` |
| **7. Superseded Query** | `M-TOOLS-001` | 0.1746 | 0.1730 | +0.0016 | **Yes** | `INDISTINGUISHABLE` |

---

## 2. Archetype Distribution Walkthrough

### Archetype 1: Exact Relevant Query [`WELL_SEPARATED`]
- **Query**: `"Prompting, retrieval, fine-tuning, alignment, and inference-time methods are distinct adaptation levers"`
- **Target Memory**: `M-ADAPT-001`
- **Top-1 Raw Similarity**: $0.6500$
- **Top-1 Final Score**: $0.3875$ (Runner-up: $0.1850$, Margin: $+0.2025$)
- **Empirical Finding**: Lexical overlap between verbatim statements produces a sharp, unambiguous spike. The Top-1 score is $2.1\times$ the distractor runner-up score, demonstrating clean discrimination under exact phrasing.

### Archetype 2: Paraphrase Query [`MODERATE`]
- **Query**: `"What are the primary levers for adapting an AI application and their trade-offs?"`
- **Target Memory**: `M-ADAPT-001`
- **Top-1 Raw Similarity**: $0.2593$
- **Top-1 Final Score**: $0.2507$ (Runner-up `M-ARCH-001`: $0.2037$, Margin: $+0.0470$)
- **Empirical Finding**: While the target memory correctly wins Rank 1, the margin shrinks dramatically from $+0.2025$ down to $+0.0470$. Token-overlap heuristics capture keywords like "levers" and "adapting", but lose confidence quickly when phrasings diverge.

### Archetype 3: Synonym Query [`INDISTINGUISHABLE`]
- **Query**: `"Tweaking, modifying, adjusting, and context injection techniques for neural models"`
- **Target Memory**: Conceptually targets `M-ADAPT-001`
- **Top-1 Raw Similarity**: $0.0714$ (`M-ARCH-001`), $0.0000$ (`M-ADAPT-001`)
- **Top-1 Final Score**: $0.1850$ (Runner-up: $0.1841$, Margin: $+0.0009$)
- **Outcome**: **Abstained** (`best_pre = 0.1850 < 0.2000`).
- **Empirical Finding**: Pure lexical matching possesses zero semantic awareness for conceptual synonyms ("tweaking" $\to$ fine-tuning, "context injection" $\to$ retrieval). All scores collapse into the uniform baseline confidence floor ($0.16 - 0.18$). Abstention correctly prevents admitting irrelevant distractors, but genuine semantic recall fails.

### Archetype 4: Unrelated Query [`FLAT`]
- **Query**: `"What is the capital of France and how to make a croissant?"`
- **Target Memory**: None (Out of Domain)
- **Top-1 Score**: $0.2083$ (`M-RELIABILITY-001`), Top-2: $0.2024$ (Margin: $+0.0059$)
- **Empirical Finding**: In queries where stopwords or general English particles overlap with long technical statements, incidental token intersections (e.g. "of", "to") can push scores just above the $0.20$ threshold ($0.2083$), resulting in a flat distractor distribution where top candidates differ by less than $0.006$. This indicates the abstention threshold ($0.20$) is slightly too permissive for token-overlap scorers on lengthy notes.

### Archetype 5: Lexical Trap Query [`MODERATE` (False Positive)]
- **Query**: `"Prompting retrieval methods for cooking recipes from memory"`
- **Target Memory**: None (Adversarial Kitchen Query)
- **Top-1 Score**: $0.2020$ (`M-ADAPT-001`), Top-2: $0.1730$ (Margin: $+0.0290$)
- **Empirical Finding**: Lexical traps successfully deceive token-overlap scorers. Because "prompting", "retrieval", and "memory" appear in `M-ADAPT-001`, the system ranks it top-1 with a $+0.029$ margin over unrelated notes, completely unaware that the semantic intent is cooking recipes.

### Archetype 6 & 7: Historical and Superseded Queries [`INDISTINGUISHABLE`]
- **Historical Query**: `"legacy deprecated old synchronous socket server Python 2.7"`
- **Superseded Query**: `"synchronous socket server threading model Python 2.7"`
- **Outcome**: Both trigger abstention (`best_pre < 0.2000`) when testing against book synthesis atoms. When testing against actual superseded notes with active successors (`SUPERSEDED-NET-001` $\to$ `ACTIVE-NET-001`), lineage inheritance successfully boosts the active successor note while the superseded note receives the $0.30$ penalty [`TEST_VERIFIED`].

---

## 3. Separation Classification Summary

```text
Score Separation Metric:
  WELL_SEPARATED     : Margin >= 0.15, Top-1 / Top-2 ratio >= 1.5x
  MODERATE           : Margin in [0.02, 0.15), Top-1 is correct
  FLAT               : Margin in [0.005, 0.02), distinction indistinguishable from noise
  INDISTINGUISHABLE  : Margin < 0.005 or universal tie at baseline confidence floor
```

| Class | Percentage of Archetypes | Root Architectural Cause |
|---|---|---|
| `WELL_SEPARATED` | 14.3% (1/7) | Verbatim lexical presence in target note statement. |
| `MODERATE` | 28.6% (2/7) | Partial token intersection; vulnerable to lexical traps. |
| `FLAT` | 14.3% (1/7) | Stopword / particle accumulation across long note bodies. |
| `INDISTINGUISHABLE` | 42.8% (3/7) | Synonym vocabulary gap and baseline confidence floor saturation. |

---

## 4. Epistemic Assessment

- **Ranking Fidelity**: The current ranking function does NOT distort or invent scores [`CODE_VERIFIED`].
- **Deterministic Floor**: The deterministic baseline provides an honest floor: it proves where lexical matching succeeds (exact phrasing, partial keywords) and exposes precisely where dense semantic embeddings are required to handle synonyms and avoid keyword traps [`RUNTIME_VERIFIED`].
