# Leakage Defense

Every defence here exists because the alternative produces a number that looks
like a result. That is the whole problem with leakage: it does not crash, it
does not warn, it returns a Brier score with a plausible shape.

## The three timestamps

Most leakage in this system reduces to confusing one of these with another.

| field | means |
|---|---|
| `observed_at` | when the thing happened |
| `acquired_at` | when this system fetched it |
| `known_as_of` | when it became knowable to anyone |

`known_as_of` is the one that gates a decision. A price observed at noon and
fetched at midnight was knowable at noon; a resolution settled at noon and
fetched at midnight was knowable at noon. Fetch time says something about the
fetcher, not the world.

`gamma_ingest` sets all three to the caller's `acquired_at`, because the Gamma
endpoint returns *current* state and there is nothing older in the response. A
historical loader must not reuse that function.

## Two boundary rules that point opposite ways

They look like an inconsistency until you ask what each timestamp means. Both
are pinned by tests.

**An input observed at the cutoff instant is available.** It was readable at
that moment, and a decision made then could have read it.

**An outcome known at the cutoff instant is not scored.** A resolution and a
decision at the same instant cannot be ordered from the data, and ordering them
favourably is exactly the error.

So `build_leakage_proof` accepts `known_as_of == as_of`, and `score_decisions`
requires the resolution to be *strictly* after. Different directions, same
principle: when the data cannot settle the order, fail closed.

## The defences, and what each catches

### Filtering, in two independent layers

`HistoricalReplay.replay` drops every snapshot and price point whose
`observed_at`, `acquired_at` or `known_as_of` exceeds the cutoff.
`select_latest_eligible_price` filters again, independently.

This is defence in depth and deliberate. It is also a hazard: a bug in either
is invisible while the other holds, and the end-to-end test initially asserted
only the final price — which the selector produces correctly even when handed
unfiltered input, so the assertion proved nothing about replay.

Each filter now has a test proving it works alone. **A caller that uses one and
not the other must still be blind.**

### The leakage proof

Filtering is not enough, because a filter that silently returns nothing looks
identical to a market that had no data.

So every backtest `Decision` carries a `LeakageProof`: the cutoff, the latest
`known_as_of` of everything consumed, and how many inputs were rejected for
postdating it. Checked when the decision is built and again when it is
validated.

The rejected count matters on its own. **Zero rejections across a long run
usually means the fixture contained no future data**, and a leakage test that
never had anything to reject proves less than it appears to.

A decision that consumed nothing records `latest_input_known_as_of: null`
rather than a time. No data before the cutoff is a real state, distinguishable
from data that was filtered out, and both differ from never having looked.

### Resolution leakage

`score_decisions` refuses a resolution known at or before the decision's cutoff
and counts it as `unscored_resolution_not_after_decision` rather than dropping
it. A run that quietly skips such decisions reports a Brier score for a
population selected by which resolutions happened to be late.

Upstream, `resolutions.py` refuses a record whose source cannot supply a
settlement time. `SOURCE_DERIVED_CLOSE_TIME` does not exist and a test asserts
the name is absent — **using a market's close time as its resolution time is
the specific mistake most likely to be added back as a convenience.**

`gamma_ingest` records no resolution at all from a live listing, and never maps
`closed: true` to `RESOLVED`. Trading stopping is not the question being
answered.

### Calibration look-ahead

`Window` boundaries are half-open on the right, so a timestamp belongs to
exactly one split. Closed intervals would place a boundary instant in two
splits, which is the cheapest possible way to leak.

`score_decisions` scores only the test split by default. Scoring train or
calibration reports how well the system fitted what it was given.

`Window.validate` refuses equal or inverted boundaries.

### Survivorship and selection

Markets without a resolution are counted by reason, not dropped. **A run that
scores 40 of 100 markets and reports the 40 is reporting a hit rate for a
population nobody chose.**

`walk_forward` emits nothing when the period cannot hold one whole window. A
partial final window is the quiet way to report a shorter test period and then
compare it as though it were alike.

Duplicate resolutions are refused rather than reconciled. Two answers for one
question means one is wrong, and choosing silently is how a backtest gets the
answer it wanted.

### Determinism

`backtest.py` contains no clock and no randomness — a test greps for
`datetime.now`, `utcnow`, `time.time` and `random`. A backtest that reads the
wall clock is not replaying anything.

`benchmark.py` is the one module allowed a random number generator, because a
bootstrap resample is reproducible from its seed, and the seed is recorded in
the result.

`score_calibration` sorts its observations before any arithmetic. Floating-point
addition is not commutative, and the order-independence the contract claims held
by luck on one Python and failed on another.

## What is not defended

**Model-side leakage.** If a prediction's evidence bundle contains a document
written after the cutoff, nothing here detects it. The provenance chain records
which snapshots were read; it does not verify that the model read only those.
That is the largest open hole and it is not closeable from this side — it needs
the retrieval layer to enforce temporal filtering at the point of retrieval.

**Correlated markets.** Two markets asking nearly the same question are two
observations in every count here. If one resolves and informs the other, the
second decision is not independent, and the bootstrap treats them as if it were.

**Parameter leakage across runs.** Nothing stops someone tuning a threshold,
re-running the held-out set, and keeping the better number. Section 33 forbids
it; no code here enforces it, and the ablation protocol says the same about
family size. These are disciplines, not guarantees, and the distinction is
stated rather than blurred.

## Where the defences are tested

```
20_TESTS/polymarket/test_historical_replay.py      the primitive's filter
20_TESTS/polymarket/test_market_model_edge.py      the selector's filter
20_TESTS/polymarket/test_backtest.py               proofs, windows, scoring
20_TESTS/polymarket/test_resolutions.py            known_at and its sources
20_TESTS/polymarket/test_gamma_ingest.py           no resolution from a listing
20_TESTS/polymarket/test_pipeline_end_to_end.py    the seams between them
```
