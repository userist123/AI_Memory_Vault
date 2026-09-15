"""Paired arm comparison that refuses to name a winner it cannot see.

This is the phase the whole project exists to reach. Everything before it builds
apparatus; this one asks the question the brief asks:

    does the Memory Vault produce better probabilities than the market price?

And it is built so the answer can be *no*. Section 42 of the brief: if the
evidence says the vault does not help, report that clearly — that result is a
successful research outcome.

## Why paired, and why bootstrap

Two arms scored on different markets are not comparable: one may have drawn the
easier questions. So arms are scored on the **same** market-and-cutoff pairs,
and an arm missing any pair is refused rather than averaged over what it has.

The quantity of interest is then the *difference* in Brier score on those pairs,
and its uncertainty comes from resampling the pairs. A bootstrap is used rather
than a t-interval because Brier differences on a few hundred markets are not
normal and are bounded below — the interval from a t-test would be symmetric
around a mean that is not the middle of anything.

Randomness is seeded and the seed is recorded in the result. This is the one
module in the pipeline allowed a random number generator, and only because a
resample is reproducible from its seed; `backtest.py` forbids randomness
entirely and that difference is deliberate.

## The refusal

`compare_arms` returns `VERDICT_NO_DIFFERENCE` whenever the confidence interval
on the difference contains zero, and `VERDICT_UNDERPOWERED` when there are fewer
paired observations than `min_paired_observations`. Neither is a failure of the
run. Both are the honest reading of the data, and a comparison that cannot
produce them is not a comparison — it is a ranking with a confidence interval
drawn around it afterwards.

A lower Brier is better, so a negative difference favours the challenger. The
sign convention is stated once here and asserted in the tests, because an
inverted sign is the cheapest way to report the opposite of what happened.
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass, field
from typing import Mapping, Optional, Sequence

BENCHMARK_SCHEMA_VERSION = "polymarket-benchmark.v1"

VERDICT_BETTER = "CHALLENGER_BETTER"
VERDICT_WORSE = "CHALLENGER_WORSE"
VERDICT_NO_DIFFERENCE = "NO_DETECTABLE_DIFFERENCE"
VERDICT_UNDERPOWERED = "UNDERPOWERED"

#: Below this many paired observations the interval is wide enough to contain
#: almost anything, and reporting a verdict from it invites the reader to
#: believe a number the data does not support.
MIN_PAIRED_OBSERVATIONS = 100

#: Resamples. Enough that the interval is stable to the third decimal; more buys
#: precision the underlying sample does not have.
BOOTSTRAP_RESAMPLES = 2000

#: A Brier difference smaller than this is not a finding, however tight the
#: interval around it. Added because the tests produced the case: 200 markets
#: each improving by exactly 0.001 give a bootstrap interval with no width at
#: all — every resample sees the same difference — and the comparison called it
#: CHALLENGER_BETTER. Statistically that is correct and practically it is
#: noise: 0.001 of Brier does not survive fees, and a benchmark that reports it
#: as a win teaches the reader to act on differences that cannot pay for
#: themselves.
#:
#: An interval excluding zero answers "is there a difference". This answers
#: "is it worth anything", and both have to hold.
MIN_MEANINGFUL_BRIER_DIFFERENCE = 0.005

DEFAULT_SEED = 20260912


@dataclass(frozen=True)
class ArmScores:
    """One arm's probabilities, keyed by the pair they were produced for.

    The key is `(market_id, as_of)` rather than `market_id` alone: the same
    market at two cutoffs is two observations, and collapsing them would let an
    arm that predicts more often look like one that predicts better.
    """

    name: str
    probabilities: Mapping[tuple[str, str], float]

    def validate(self) -> None:
        if not self.name:
            raise ValueError("an arm must be named")
        for key, probability in self.probabilities.items():
            if not isinstance(key, tuple) or len(key) != 2:
                raise ValueError("arm keys must be (market_id, as_of) pairs")
            if not math.isfinite(probability) or not 0.0 <= probability <= 1.0:
                raise ValueError(
                    f"{self.name}: probability for {key} must be between 0 and 1"
                )


@dataclass(frozen=True)
class Comparison:
    schema_version: str
    baseline: str
    challenger: str
    paired_observations: int
    baseline_brier: Optional[float]
    challenger_brier: Optional[float]
    #: challenger - baseline. Negative favours the challenger.
    difference: Optional[float]
    ci_low: Optional[float]
    ci_high: Optional[float]
    verdict: str
    seed: int
    resamples: int
    dropped_unpaired: int = 0

    def as_dict(self) -> dict[str, object]:
        return {
            "schema_version": self.schema_version,
            "baseline": self.baseline,
            "challenger": self.challenger,
            "paired_observations": self.paired_observations,
            "baseline_brier": self.baseline_brier,
            "challenger_brier": self.challenger_brier,
            "difference": self.difference,
            "ci_low": self.ci_low,
            "ci_high": self.ci_high,
            "verdict": self.verdict,
            "seed": self.seed,
            "resamples": self.resamples,
            "dropped_unpaired": self.dropped_unpaired,
        }


def _brier(probabilities: Sequence[float], targets: Sequence[float]) -> float:
    return sum((p - t) ** 2 for p, t in zip(probabilities, targets)) / len(targets)


def compare_arms(
    baseline: ArmScores,
    challenger: ArmScores,
    targets: Mapping[tuple[str, str], float],
    *,
    seed: int = DEFAULT_SEED,
    resamples: int = BOOTSTRAP_RESAMPLES,
    confidence: float = 0.95,
    min_paired_observations: int = MIN_PAIRED_OBSERVATIONS,
    min_effect_size: float = MIN_MEANINGFUL_BRIER_DIFFERENCE,
    require_full_pairing: bool = True,
) -> Comparison:
    """Compare two arms on the pairs they both scored.

    `require_full_pairing` is on by default: an arm that skipped observations
    the other answered is not comparable to it, and quietly comparing them on
    the intersection rewards abstaining from the hard ones. Turn it off only
    when the arms are *designed* to abstain differently, and then read the
    `dropped_unpaired` count as part of the result rather than a footnote.
    """
    baseline.validate()
    challenger.validate()
    if not 0.0 < confidence < 1.0:
        raise ValueError("confidence must be between 0 and 1")
    if resamples < 1:
        raise ValueError("resamples must be positive")

    scored = set(targets)
    shared = sorted(set(baseline.probabilities) & set(challenger.probabilities) & scored)
    union = (set(baseline.probabilities) | set(challenger.probabilities)) & scored
    dropped = len(union) - len(shared)

    if require_full_pairing and dropped:
        raise ValueError(
            f"{dropped} observation(s) scored by one arm and not the other; "
            "comparing on the intersection rewards abstaining from the hard "
            "ones. Pass require_full_pairing=False to accept that explicitly."
        )

    if not shared:
        return Comparison(
            BENCHMARK_SCHEMA_VERSION, baseline.name, challenger.name, 0,
            None, None, None, None, None, VERDICT_UNDERPOWERED, seed,
            resamples, dropped,
        )

    actual = [targets[key] for key in shared]
    base_p = [baseline.probabilities[key] for key in shared]
    chal_p = [challenger.probabilities[key] for key in shared]

    base_brier = _brier(base_p, actual)
    chal_brier = _brier(chal_p, actual)
    difference = chal_brier - base_brier

    if len(shared) < min_paired_observations:
        return Comparison(
            BENCHMARK_SCHEMA_VERSION, baseline.name, challenger.name, len(shared),
            base_brier, chal_brier, difference, None, None,
            VERDICT_UNDERPOWERED, seed, resamples, dropped,
        )

    #: Resample the pairs, not the arms independently — the whole point of
    #: pairing is that the same market's difficulty affects both arms, and
    #: resampling them apart throws that away.
    rng = random.Random(seed)
    per_pair = [
        (c - a) ** 2 - (b - a) ** 2
        for b, c, a in zip(base_p, chal_p, actual)
    ]
    count = len(per_pair)
    means: list[float] = []
    for _ in range(resamples):
        total = 0.0
        for _ in range(count):
            total += per_pair[rng.randrange(count)]
        means.append(total / count)
    means.sort()

    tail = (1.0 - confidence) / 2.0
    low = means[max(0, int(math.floor(tail * resamples)) - 1)]
    high = means[min(resamples - 1, int(math.ceil((1.0 - tail) * resamples)) - 1)]

    if low <= 0.0 <= high:
        verdict = VERDICT_NO_DIFFERENCE
    elif abs(difference) < min_effect_size:
        #: The interval excludes zero, so a difference is there. It is too
        #: small to act on, and saying so is the whole point of the threshold.
        verdict = VERDICT_NO_DIFFERENCE
    elif high < 0.0:
        verdict = VERDICT_BETTER
    else:
        verdict = VERDICT_WORSE

    return Comparison(
        BENCHMARK_SCHEMA_VERSION, baseline.name, challenger.name, len(shared),
        base_brier, chal_brier, difference, low, high, verdict, seed,
        resamples, dropped,
    )


@dataclass(frozen=True)
class BenchmarkReport:
    schema_version: str
    market_baseline: str
    comparisons: tuple[Comparison, ...]

    def beat_the_market(self) -> tuple[str, ...]:
        """Arms whose interval clears zero against the market baseline.

        Named this way on purpose. "Best arm" is answerable from any table of
        numbers; "beat the market" is the question, and the answer is allowed
        to be an empty tuple.
        """
        return tuple(
            c.challenger for c in self.comparisons
            if c.baseline == self.market_baseline and c.verdict == VERDICT_BETTER
        )

    def as_dict(self) -> dict[str, object]:
        return {
            "schema_version": self.schema_version,
            "market_baseline": self.market_baseline,
            "arms_that_beat_the_market": list(self.beat_the_market()),
            "comparisons": [c.as_dict() for c in self.comparisons],
        }


def run_benchmark(
    market_baseline: ArmScores,
    challengers: Sequence[ArmScores],
    targets: Mapping[tuple[str, str], float],
    **kwargs,
) -> BenchmarkReport:
    """Every challenger against the market, one comparison each.

    Against the market rather than against each other, because the market price
    is the thing that has to be beaten for any of this to be worth running. An
    arm that beats another arm and loses to the price has not found anything.
    """
    market_baseline.validate()
    comparisons = tuple(
        compare_arms(market_baseline, arm, targets, **kwargs) for arm in challengers
    )
    return BenchmarkReport(BENCHMARK_SCHEMA_VERSION, market_baseline.name, comparisons)


__all__ = [
    "BENCHMARK_SCHEMA_VERSION",
    "VERDICT_BETTER",
    "VERDICT_WORSE",
    "VERDICT_NO_DIFFERENCE",
    "VERDICT_UNDERPOWERED",
    "MIN_PAIRED_OBSERVATIONS",
    "MIN_MEANINGFUL_BRIER_DIFFERENCE",
    "BOOTSTRAP_RESAMPLES",
    "ArmScores",
    "Comparison",
    "BenchmarkReport",
    "compare_arms",
    "run_benchmark",
]
