# WP-5 — corpus dilution: measured, not fixed

Prediction committed in `07_EVALUATION/r024_hypotheses.md` before this ran:
excluding policy-lesson notes would not move context recall beyond a
±1-case noise band, because the given candidate-to-top-10 share drop
(42%→17%) already showed the ranking mechanism deprioritising them.

## Identification

394 notes, `category == 'policy-lesson'`, `.text` (title+body) length 644-648
characters — matches the brief's numbers exactly. Storage-visible population
(`FileStorageEngine`, RAW excluded, no lifecycle/type filter): **743 notes,
394 policy-lesson = 53.0%**. The brief's 46% figure is against the
`VaultIndex` population (850 notes) — index and storage differ by design
(`VAULT_STATE.md` §4: "do not treat 842 and 738 as the same population").
Both are reported here rather than silently picking one.

## Method

No production code changed — WP-5's intent is `measure`. All three arms call
the real, unmodified `generate_candidates()` directly over different note
populations, per case, using each case's own `QueryClassifier`-derived
lifecycle/type filters (4 of the 8 dev cases add one: D05 `ACTIVE`, D06
`target_type=experience`, D07 `VERIFIED`, D08 `CLASSIFIED`+`decision` —
applying a single filter set to every case regardless of its own query was
considered and rejected, since it would silently change which notes each
case is even allowed to see). Context recall is computed by the same
RelevanceScorer-based reproduction Phase A (WP-1) validated as an adequate
proxy for the real pipeline at this scale.

- **B1 baseline** — unmodified.
- **B2 excluded** — policy-lesson notes never enter the ranked pool.
- **B3 capped** — capped at 40 (20% of the 200-candidate limit) within the
  final candidate set; excess dropped from the bottom and backfilled with
  the next-best-ranked non-policy-lesson notes from the full corpus ranking
  (not just the original top 200), so the cap changes composition, not just
  count.

## Result

| arm | candidate recall | context recall | mean policy-lesson candidate share |
|---|---:|---:|---:|
| B1 baseline | 0.500 | 0.000 | 22.9% |
| B2 excluded | 0.500 | 0.000 | 0.0% |
| B3 capped | 0.500 | 0.000 | 10.0% |

**Both recall metrics are identical across all three arms**, on all 8 cases
individually (not just in aggregate — see `wp5_arms_report.json`'s `rows`).
Excluding or capping policy-lesson notes changed their own candidate-slot
share from 22.9% to 0% / 10.0% and changed nothing else measured.

This matches the registered prediction and the brief's own suggested honest
outcome: **the policy-lesson notes are harmless to retrieval at their
current share.** No arm moves context recall beyond the ±1-case noise band
— it does not move at all.

## Decision

Per the registered abort criterion: report harmless, stop. No corpus or
index change is proposed. Per requirement 2, this finding is explicitly
about **retrieval**, not about the corpus's right to exist — nothing here
recommends deleting or altering the policy-lesson notes as memory; excluding
them from the ranked candidate pool during retrieval (B2) and deleting them
from the vault are two different actions, and this report recommends
neither.

## A defect found outside this package's scope, recorded and not fixed here

D06, D07 and D08's classifier-narrowed candidate pool sizes were 1, 0 and 0
notes respectively (`gated_pool_size` in `wp5_arms_report.json`) — i.e. for
D07 and D08, `QueryClassifier`'s lifecycle/type inference from the query
text filters out the **entire storage-visible corpus** before candidate
generation ever runs, independent of policy-lesson dilution or ranking.
Their `candidate_recall=0` is fully explained by this, not by anything WP-1
or WP-5 measured. This looks like a real defect in `query_classifier.py`'s
keyword-triggered filter inference (a query merely containing a word like
"verified" or "decision" appears to impose a lifecycle/type filter narrow
enough to zero out the corpus) — recorded here per "findings first, repairs
second, never in the branch that found them"; not investigated or fixed in
this package.

## What remains open

- n=8 is small (as in WP-1); a genuinely small dilution cost could be
  invisible at this sample size. The flat result across all 8 individual
  cases (not just the aggregate) is stronger evidence than the aggregate
  alone, but `heldout.json`'s larger n was not used (WP-5, like WP-1, tunes
  on dev.json only).
- The classifier-filter defect above (D06-D08) is unexplored beyond noting
  its existence and its pool-size symptom.
- B3's cap value (40) and backfill rule were fixed before running, per the
  registry's discipline, but were not swept across multiple cap values;
  a different cap might behave differently on a corpus/query mix where the
  cap actually binds harder than it did here.
