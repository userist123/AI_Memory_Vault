# Ranking arms on benchmark v3 — results

Preregistration: `07_EVALUATION/ranking_formula/PREREGISTRATION.md`, committed before this run.
Benchmark SHA-256 `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`, 160 cases, 1134 notes in the index.

The ranking arm only affects the graph-OFF branch of search() (controller.py:947), so graph expansion is off in every arm.

## Operating point `agent` — AI_AGENT, page_size=5

| arm | context_recall | 95% CI | candidate_recall | deterministic | sabotage Δ |
|---|---|---|---|---|---|
| `baseline` | 22/130 | 16.92% [11.45, 24.30] | 94/130 | True | 154 |
| `fused_score` | 24/130 | 18.46% [12.73, 26.00] | 94/130 | True | 0 |
| `no_confidence` | 18/130 | 13.85% [8.94, 20.83] | 94/130 | True | 133 |
| `confidence_tiebreak` | 19/130 | 14.62% [9.56, 21.70] | 94/130 | True | 134 |
| `fused_plus_tiebreak` | 24/130 | 18.46% [12.73, 26.00] | 94/130 | True | 0 |

**Sabotage control.** Each arm's sort key was replaced by a constant; the column above counts cases whose returned order changed. Arms whose output did not change, and whose result is therefore void: `fused_score`, `fused_plus_tiebreak`.

**Distinctness.**

| pair | cases with a different order | status |
|---|---|---|
| baseline vs fused_score | 154 | distinct |
| baseline vs no_confidence | 139 | distinct |
| baseline vs confidence_tiebreak | 114 | distinct |
| baseline vs fused_plus_tiebreak | 154 | distinct |
| fused_score vs no_confidence | 133 | distinct |
| fused_score vs confidence_tiebreak | 134 | distinct |
| fused_score vs fused_plus_tiebreak | 0 | indistinguishable_at_this_sample_size |
| no_confidence vs confidence_tiebreak | 79 | distinct |
| no_confidence vs fused_plus_tiebreak | 133 | distinct |
| confidence_tiebreak vs fused_plus_tiebreak | 134 | distinct |

**Paired against `fused_score`, context_recall.**

| arm | gains | losses | tied 1/1 | tied 0/0 | discordant | McNemar p | Holm p |
|---|---|---|---|---|---|---|---|
| `baseline` | 17 | 19 | 5 | 89 | 36 | 0.867939 | 1.0 |
| `no_confidence` | 11 | 17 | 7 | 95 | 28 | 0.344928 | 1.0 |
| `confidence_tiebreak` | 12 | 17 | 7 | 94 | 29 | 0.458258 | 1.0 |
| `fused_plus_tiebreak` | 0 | 0 | 24 | 106 | 0 | 1.0 | 1.0 |

## Operating point `human` — HUMAN, page_size=10

| arm | context_recall | 95% CI | candidate_recall | deterministic | sabotage Δ |
|---|---|---|---|---|---|
| `baseline` | 31/130 | 23.85% [17.34, 31.86] | 115/130 | True | 156 |
| `fused_score` | 32/130 | 24.62% [18.01, 32.68] | 115/130 | True | 0 |
| `no_confidence` | 27/130 | 20.77% [14.68, 28.53] | 115/130 | True | 158 |
| `confidence_tiebreak` | 29/130 | 22.31% [16.00, 30.20] | 115/130 | True | 157 |
| `fused_plus_tiebreak` | 32/130 | 24.62% [18.01, 32.68] | 115/130 | True | 0 |

**Sabotage control.** Each arm's sort key was replaced by a constant; the column above counts cases whose returned order changed. Arms whose output did not change, and whose result is therefore void: `fused_score`, `fused_plus_tiebreak`.

**Distinctness.**

| pair | cases with a different order | status |
|---|---|---|
| baseline vs fused_score | 156 | distinct |
| baseline vs no_confidence | 154 | distinct |
| baseline vs confidence_tiebreak | 129 | distinct |
| baseline vs fused_plus_tiebreak | 156 | distinct |
| fused_score vs no_confidence | 158 | distinct |
| fused_score vs confidence_tiebreak | 157 | distinct |
| fused_score vs fused_plus_tiebreak | 0 | indistinguishable_at_this_sample_size |
| no_confidence vs confidence_tiebreak | 120 | distinct |
| no_confidence vs fused_plus_tiebreak | 158 | distinct |
| confidence_tiebreak vs fused_plus_tiebreak | 157 | distinct |

**Paired against `fused_score`, context_recall.**

| arm | gains | losses | tied 1/1 | tied 0/0 | discordant | McNemar p | Holm p |
|---|---|---|---|---|---|---|---|
| `baseline` | 16 | 17 | 15 | 82 | 33 | 1.0 | 1.0 |
| `no_confidence` | 13 | 18 | 14 | 85 | 31 | 0.47313 | 1.0 |
| `confidence_tiebreak` | 13 | 16 | 16 | 85 | 29 | 0.711071 | 1.0 |
| `fused_plus_tiebreak` | 0 | 0 | 32 | 98 | 0 | 1.0 | 1.0 |

## Decision

> Change the production default to arm X only if, against fused_score at the agent operating point, X gains >= 8 context_recall cases, loses <= 2, and exact two-sided McNemar gives p < 0.05. Otherwise keep the default.

Reference arm `fused_score` at operating point `agent`.

| arm | gains ≥ 8 | losses ≤ 2 | p < 0.05 | meets the rule |
|---|---|---|---|---|
| `baseline` | 17 (True) | 19 (False) | 0.867939 (False) | **False** |
| `no_confidence` | 11 (True) | 17 (False) | 0.344928 (False) | **False** |
| `confidence_tiebreak` | 12 (True) | 17 (False) | 0.458258 (False) | **False** |
| `fused_plus_tiebreak` | 0 (False) | 0 (True) | 1.0 (False) | **False** |

**Verdict: keep the current default.**

**Void arms** (sabotage did not change their output): agent:fused_score, agent:fused_plus_tiebreak, human:fused_score, human:fused_plus_tiebreak.

## Reading the void label on the reference arm

`fused_score` is the production default, and its sort key is inert: `generate_candidates()` already returns notes in `(-fused_score, id)` order, so sorting by the same key again cannot move anything. Two consequences, kept apart on purpose:

- The **recall figures** for `fused_score` above are valid. They are what production returns, because production returns the fusion order unchanged.
- The **ranking step** under `fused_score` is redundant. Its gain over `baseline` is the gain of *not* applying `RelevanceScorer`'s key, not of applying a new one. There is no reranking step anywhere in the pipeline; the page is the fusion top-k minus whatever the context-pack builder cannot fit (`07_EVALUATION/reranker_envelope/DEVIATIONS.md`, D-1/D-2).

Pinned by `20_TESTS/test_fused_score_ranking_is_a_noop.py`, including the one known divergence (ties: generation ascends by id, the arm descends), which no benchmark page exercised.

