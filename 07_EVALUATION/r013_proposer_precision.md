# r013 — Edge proposer precision

r007 measured 18% precision on a hand-verified sample of 50, against a 70%
bar, and correctly stopped. The proposer was then hardened with a ~40-token
blacklist, but precision was never re-measured: `06_INBOX/edge_proposals.json`
still dates from before that change. This is the re-measurement, and the fixes
the numbers pointed at.

## Root causes, in order of how much they cost

**1. `entities()` applies no stopword filtering at all.** `tokenize()` filters
`STOP`; `entities()` regex-matches and lowercases. Everything downstream keys
on entities, so the filtering that existed never applied to the signal in use.

**2. Ephemeral notes were in the corpus (37% of proposals).** Session dumps
under `Artifacts/` share large volumes of internal system vocabulary with each
other, and `CURRENT.md` scratchpads change hourly. Every such pair a human
reviewed was judged wrong.

**3. Summed IDF with no rarity floor.** Six entities at DF=19 give
`6 x log(900/19) = 23.2` against a normalizer of `2 x log(900) = 13.6`, so the
score saturates at 1.0. Six mediocre matches scored exactly like one precise
one.

**4. No document-length normalisation.** The normalizer is a constant. Entity
counts run from a median of **3** to **1079** (`Master_Skills_Catalog_251`).
That one note appeared in 7 of 25 sampled proposals and was wrong in 6: a note
listing a thousand entities shares some with everything.

## Fixes

| Fix | Constant |
|---|---|
| Exclude session dumps and agent scratchpads | `EPHEMERAL_PATH_MARKERS` |
| Require one genuinely rare shared entity | `RARE_ENTITY_DF_MAX = 5` |
| Geometric-mean overlap coverage gate | `MIN_OVERLAP_COVERAGE = 0.10` |
| Generic tokens, legal furniture, UI frameworks | `SPURIOUS_ENTITIES` (+60) |
| Dates and CELEX citation numbers | `DATE_LIKE_RE` |
| Separator runs (`____`, `-----`) | `FILLER_RE` |

The coverage gate is the load-bearing one. Six shared entities give coverage
`sqrt((6/20)(6/1079)) = 0.04` against a thousand-entity catalogue and
`sqrt((6/20)(6/20)) = 0.30` between two comparable notes — a 7x separation the
previous scoring could not express.

## Measurements

| Stage | Proposals | Sampled precision |
|---|---:|---:|
| r007 baseline (pre-hardening) | 2000 | **18%** (n=50, Antigravity) |
| after blacklist hardening only | 1000 | not re-measured |
| + ephemeral exclusion, rarity floor | 377 | 68% (n=25, seed 42) |
| + generic/legal/UI tokens, dates | 347 | 56% (n=25, seed 7) |
| + length normalisation | 210 | 80% (n=25, seed 7) |
| + separator and furniture cleanup | **182** | **90%** (n=30, seed 2026) |

Volume was traded for precision deliberately: 1000 -> 182. At 90%, that is
~164 usable edges, which would roughly double the 127 declared edges now in
the runtime graph.

## What this measurement is not

The 90% figure was judged by the same author who tuned the thresholds. The
tuning samples used seeds 42 and 7; the final figure uses seed 2026, a sample
not looked at while tuning. That controls for overfitting to specific pairs,
**but not for judge bias**, and n=30 carries roughly +/-10pp.

Two of the three remaining failures at seed 2026 were EU-regulation pairs
whose shared entities are still legal furniture (`prezentul regulament`), and
one was a self-link between two files that share a name — a duplicate the r008
naming pass did not cover.

Before bulk promotion, r007's contract should be run again as written: a fresh
random sample of at least 50, hand-verified by someone who did not tune these
thresholds. This report lowers the risk of that check failing; it does not
substitute for it.
