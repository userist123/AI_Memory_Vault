# WP-2 — independent precision re-sample

r007's contract requires a random sample of >=50, hand-verified, against a
70% bar, before any bulk promotion. r013 measured 90% (n=30, seed 2026) —
judged by the same author who tuned the thresholds; seeds 42 and 7 were used
*while tuning*. This is the independent discharge of that contract: a fresh
seed no prior pass used, judged without adjusting anything.

## Result

**45/55 correct = 81.8% precision. Bar: 70%. Decision: GO.**

| | value |
|---|---:|
| total proposals (fresh run, current hardened proposer) | 199 |
| sample size | 55 (>=50 required, +5 margin) |
| seed | **8675309** (not 42, 7, or 2026 — none used by r013's tuning or final pass) |
| correct | 45 |
| wrong | 10 |
| precision | 0.818 |

Full per-proposal judgements and reasoning: `review_worksheet.json`
(`judgement` + `judgement_reason` per item, `precision` block at the end).
Raw sampled content for audit: `review_readable.txt`.

## Method

1. Regenerated proposals fresh against the current vault with the
   already-hardened proposer (`edge_proposer.py`, unmodified) —
   `06_INBOX/edge_proposals.json` was stale (r013's own note), so this ran
   `--limit 2000` and took what the hardening actually accepts: **199**
   proposals (close to r013's 182; small drift is expected — r006-r008
   changed the corpus since r013 ran).
2. Sampled 55 with `random.Random(8675309)` — see
   `sample_for_review.py`. The seed is stated here, not searched for a
   favourable draw: it was picked once, before any proposal was read.
3. For each sampled proposal, read the actual content of BOTH notes (not
   just the `evidence_entities` list) and judged: does this specific pair
   show genuine shared meaning, or only coincidental/generic shared
   vocabulary? Every judgement's reasoning is recorded, not only the
   correct/wrong label.
4. **No threshold in `edge_proposer.py` was adjusted, in either direction,
   at any point in this package** — per requirement 1. Where a judgement
   below disagrees with what a threshold accepted, that is reported as a
   finding about the threshold, not corrected here.

## Failure patterns

Two of `r013_proposer_precision.md`'s documented patterns recurred exactly:

- **EU/RO regulatory "furniture"** (5 of 10 failures: #2, #6, #7, #31, #47):
  shared entities are generic institutional/procedural vocabulary — EU
  agency acronyms (ABE, BCE, DPO, EIOPA, ESMA) common to *every* EU
  financial regulation, or the vault's own standard invariant-code citations
  (I-001..I-012, P0-001..P0-015) that appear across many unrelated
  governance documents. r013 found this exact pattern in 2 of 3 residual
  failures at seed 2026; it is still the largest single category here.
- **Generic index/catalog documents** (2 of 10: #50, #52): a document whose
  entire purpose is to catalog or navigate many other documents (a design-
  skills index, the master vault navigational index) will superficially
  entity-match almost anything it catalogs or indexes. This is structurally
  the same problem `synapse_store.py`'s wikilink hub-cut solves for
  navigation hubs (r006) — but `edge_proposer.py` has no equivalent cut for
  a *declared* index/catalog document acting as a hub.

**One failure pattern not in r013's original four** (#54, #55, a single
proposal pair counted twice for direction): two *different, unrelated*
projects (Registru Transferuri and LogAnalyzer DFIR) linked only by generic
Windows security API names (DPAPI, SHA-256, WMI) that essentially any
Windows security application would share. This differs from r013's
"ephemeral notes" and "ubiquitous entity" patterns: both notes here are
substantive, non-ephemeral project documentation — the failure is that two
*substantive* documents about unrelated projects can still share enough
generic platform-API vocabulary to pass the coverage gate.

## What this measurement is, and is not

This controls for the same thing r013's seed-2026 pass controlled for
(overfitting to specific pairs seen while tuning) and additionally for judge
bias, since this reviewer did not tune `RARE_ENTITY_DF_MAX`,
`MIN_OVERLAP_COVERAGE`, `SPURIOUS_ENTITIES` or any other threshold in
`edge_proposer.py`. It does not control for a systematic blind spot shared
by both reviewers (e.g. both treating a specific relation type as
self-evidently correct); a second, independent reviewer disagreeing case-
by-case would be a stronger check than a second reviewer alone.

n=55 carries a wide interval (roughly the same order as r013's own
+/-10pp at n=30 — a rough binomial estimate at n=55, p=0.818 gives a 95% CI
of approximately 71%-89%). The result clears 70% with room, but the true
population figure could plausibly sit closer to 70% than 82% at the low end
of that interval.

## Decision

**GO.** Per the stop condition: at or above 70%, decision is GO and the
package ends here — promotion is WP-6's own separate gate with its own
acceptance criteria (provenance per edge, no hub linking, re-running R016
after), not decided or performed by this measurement.

## What remains open

- The two new-pattern failures (#54/#55) suggest `edge_proposer.py`'s
  coverage gate may need a "generic platform/security API vocabulary"
  exclusion list analogous to `SPURIOUS_ENTITIES`, similar to how legal
  furniture and generic tech terms were already excluded — not implemented
  here (WP-2's intent is verify, and "you did not tune these thresholds, do
  not adjust them" is explicit).
- The index/catalog-document pattern (#50, #52) suggests
  `SynapseStore.from_index()`'s wikilink hub-cut logic (r006,
  `HUB_IN_DEGREE_THRESHOLD`) has no equivalent in `edge_proposer.py` for a
  single declared index/catalog document acting as an entity hub. Recorded
  as a finding, not fixed here, per "findings first, repairs second, never
  in the branch that found them."
- Confidence interval at this n is wide; a larger sample would narrow it
  before any promotion decision that is sensitive to exactly where in that
  range the true figure sits.
