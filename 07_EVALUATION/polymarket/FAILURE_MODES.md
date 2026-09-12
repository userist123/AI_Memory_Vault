# Failure Modes

Not hypothetical. Every entry below either happened during construction or is a
hole that is still open and named.

## Found while building

### The abstention gate failed open at exactly its threshold

`edge <= min_positive_edge` is correct arithmetic and wrong in binary floating
point. `0.55 - 0.50` is `0.050000000000000044`, larger than `0.05` by 4.2e-17,
so a prediction sitting precisely on a five-point threshold **took the bet**.

The direction is what makes it serious. This is the one component whose purpose
is to decline, and it failed open — on the exact boundary a policy author would
choose deliberately and would most expect to be honoured.

Fixed with `math.isclose`, absolute tolerance alongside relative because the
quantity is a probability difference and relative alone is meaningless as the
threshold approaches zero.

### Sizing allocated a position too small to mean anything

A book with a cap nearly consumed handed back whatever slice remained —
**0.001% of bankroll** in the measured case. That position pays fees and
occupies an exposure slot to express no view.

Found by writing the test, not by reading the code. `min_position_fraction`
now refuses below a floor, and the refusal still names the binding constraint
so thin liquidity stays distinguishable from a full book.

### A benchmark called a worthless improvement a win

200 markets each improving by exactly 0.001 produce a bootstrap interval with
**no width at all** — every resample sees the same difference — and the
comparison returned `CHALLENGER_BETTER`.

Statistically correct. Practically noise: 0.001 of Brier does not survive fees.
An interval excluding zero answers *is there a difference*; `min_effect_size`
answers *is it worth anything*, and both now have to hold.

### Calibration was order-independent by luck

`score_calibration` summed observations in arrival order. Floating-point
addition is not commutative, so the same observations reversed produced a report
differing in the last bits. The determinism test passed on Python 3.14 and
failed on CI's interpreter.

**A property that holds by luck reads exactly like one that holds by
construction, until an environment changes.** Observations are now sorted before
any arithmetic.

### Six test modules were never collected

`pytest.ini` listed one sys.path root. Six polymarket modules import
`from packages.polymarket.x`, which needs another, and it was supplied by
`PYTHONPATH` inside the phase 3–7 CI workflow files.

The tests were green in CI and, run by hand, failed collection — **six of eight
modules, zero tests executed**, on code already merged through five phases.

A gap that only appears outside CI is the worst kind: the report is green and
correct, and the coverage is something else.

### An end-to-end assertion proved the wrong thing

The pipeline test asserted the final market price was `0.55` rather than the
`0.80` point past the cutoff. But `select_latest_eligible_price` filters on its
own and returns `0.55` even when handed unfiltered input.

So the assertion proved the selector works — already covered elsewhere — and
said nothing about whether the replay stage filtered at all. Two layers of
defence meant a bug in either was invisible while the other held.

### A merged PR reverted four phases of work

A branch cut before two fixes landed was merged after them, removing constants
that the surviving code still referenced. `main` raised `NameError` on every
grounding check for hours, and the gate that refuses fabricated evidence could
not run at all.

Branch age is not visible in a diff. **Merging an old branch is a revert of
everything that landed after it was cut**, and only the tests said so.

## Open, and named

### Model-side leakage is not detected

If a prediction's evidence bundle contains a document written after the cutoff,
nothing in this package notices. The provenance chain records which snapshots
were read; it does not verify the model read only those.

This is the largest hole. It is not closeable from here — it needs the
retrieval layer to enforce temporal filtering at the point of retrieval.

### Correlated markets are counted as independent

Two markets asking nearly the same question are two observations in every count
in the benchmark, and the bootstrap resamples them as if they were independent.
If one resolves and informs the other, they are not.

### Parameter leakage across runs is a discipline, not a guarantee

Nothing prevents tuning a threshold, re-running the held-out set, and keeping
the better number. Nothing prevents passing three ablation rungs instead of
eleven and correcting across three.

Both are forbidden by protocol and unenforced by code. Said plainly rather than
implied to be covered.

### Every threshold is a guess

The eleven risk limits, the abstention threshold, the minimum effect size, the
100-observation floor — none is calibrated against realised performance,
because there is none. `min_calibration_samples` exists precisely because the
system knows it cannot trust itself yet.

### No real data has ever entered the system

Every number in every test comes from a fixture. The ingestion path is written
and tested offline; it has not been run. The resolution adapter does not exist,
because writing it requires inspecting a real response shape and fabricating
API fields is forbidden.

**Nothing in this repository is evidence that an edge exists.** The apparatus
can measure one. It has not measured anything.

## The pattern

Six of the seven found failures were caught by writing a test, not by reading
code — and three of those were caught by a test that was itself wrong in an
instructive way.

The two that reached `main` (the collection gap, the reverted phases) were both
invisible to CI while CI reported success. Both were found by running the suite
by hand on a clean checkout.
