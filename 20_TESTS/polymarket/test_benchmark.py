"""The comparison has to be able to say "no".

A benchmark that always names a winner is a ranking, not a test. These cover
the four answers it must be able to give — better, worse, no detectable
difference, underpowered — and the three ways it could give a wrong one: an
inverted sign, an unpaired comparison, or an interval drawn around a sample too
small to constrain anything.
"""
from __future__ import annotations

import pytest

from packages.polymarket.benchmark import (
    BENCHMARK_SCHEMA_VERSION,
    VERDICT_BETTER,
    VERDICT_NO_DIFFERENCE,
    VERDICT_UNDERPOWERED,
    VERDICT_WORSE,
    ArmScores,
    compare_arms,
    run_benchmark,
)


def keys(n: int) -> list[tuple[str, str]]:
    return [(f"m{i}", "2026-03-01T12:00:00Z") for i in range(n)]


def arm(name: str, probability, n: int = 200) -> ArmScores:
    """`probability` is a callable of the index, or a constant."""
    if not callable(probability):
        constant = probability
        probability = lambda _i: constant  # noqa: E731
    return ArmScores(name, {k: probability(i) for i, k in enumerate(keys(n))})


def outcomes(n: int = 200) -> dict[tuple[str, str], float]:
    """Alternating, so a 0.5 forecaster is calibrated and uninformative."""
    return {k: float(i % 2) for i, k in enumerate(keys(n))}


# --- the four answers --------------------------------------------------------

def test_a_genuinely_better_arm_is_named():
    """The challenger knows the answer; the baseline does not."""
    result = compare_arms(
        arm("market", 0.5),
        arm("oracle", lambda i: 0.9 if i % 2 else 0.1),
        outcomes(),
    )
    assert result.verdict == VERDICT_BETTER
    assert result.difference < 0, "lower Brier is better, so the difference is negative"
    assert result.ci_high < 0


def test_a_genuinely_worse_arm_is_named():
    result = compare_arms(
        arm("market", lambda i: 0.9 if i % 2 else 0.1),
        arm("noise", lambda i: 0.1 if i % 2 else 0.9),
        outcomes(),
    )
    assert result.verdict == VERDICT_WORSE
    assert result.difference > 0
    assert result.ci_low > 0


def test_two_identical_arms_are_not_declared_different():
    """The interval must contain zero when nothing separates them."""
    result = compare_arms(arm("a", 0.5), arm("b", 0.5), outcomes())
    assert result.verdict == VERDICT_NO_DIFFERENCE
    assert result.difference == pytest.approx(0.0)
    assert result.ci_low <= 0.0 <= result.ci_high


def test_a_consistent_but_worthless_improvement_is_not_a_win():
    """Every one of 200 markets improves by the same hair, so the bootstrap
    interval has no width at all and excludes zero. Statistically there is a
    difference; practically 0.001 of Brier does not survive fees.

    This case is why min_effect_size exists — an interval excluding zero
    answers "is there a difference", not "is it worth anything".
    """
    result = compare_arms(
        arm("market", 0.5),
        arm("slightly", lambda i: 0.50 + (0.001 if i % 2 else -0.001)),
        outcomes(),
    )
    assert result.ci_high < 0.0, "the interval really does exclude zero"
    assert result.verdict == VERDICT_NO_DIFFERENCE


def test_an_effect_above_the_threshold_is_still_a_win():
    """The floor must not swallow real improvements."""
    result = compare_arms(
        arm("market", 0.5),
        arm("better", lambda i: 0.80 if i % 2 else 0.20),
        outcomes(),
    )
    assert abs(result.difference) > 0.005
    assert result.verdict == VERDICT_BETTER


def test_too_few_observations_is_underpowered_not_a_verdict():
    """Scores are still reported — they are just not a conclusion."""
    result = compare_arms(
        arm("market", 0.5, n=20),
        arm("oracle", lambda i: 0.9 if i % 2 else 0.1, n=20),
        outcomes(20),
    )
    assert result.verdict == VERDICT_UNDERPOWERED
    assert result.challenger_brier < result.baseline_brier, (
        "the numbers are computed; they are simply not called a result"
    )
    assert result.ci_low is None and result.ci_high is None


def test_no_shared_observations_is_underpowered():
    result = compare_arms(
        ArmScores("a", {("m1", "t"): 0.5}),
        ArmScores("b", {("m2", "t"): 0.5}),
        {("m1", "t"): 1.0, ("m2", "t"): 1.0},
        require_full_pairing=False,
    )
    assert result.verdict == VERDICT_UNDERPOWERED
    assert result.paired_observations == 0


# --- pairing -----------------------------------------------------------------

def test_an_unpaired_comparison_is_refused_by_default():
    """Comparing on the intersection rewards abstaining from the hard ones."""
    baseline = arm("market", 0.5, n=200)
    partial = ArmScores("selective", {k: 0.5 for k in keys(150)})
    with pytest.raises(ValueError, match="abstaining from the hard"):
        compare_arms(baseline, partial, outcomes())


def test_unpaired_can_be_accepted_explicitly_and_is_counted():
    baseline = arm("market", 0.5, n=200)
    partial = ArmScores("selective", {k: 0.5 for k in keys(150)})
    result = compare_arms(baseline, partial, outcomes(), require_full_pairing=False)
    assert result.paired_observations == 150
    assert result.dropped_unpaired == 50, (
        "the count belongs in the result, not in a footnote"
    )


def test_the_same_market_at_two_cutoffs_is_two_observations():
    """Collapsing them would let an arm that predicts more often look like one
    that predicts better."""
    targets = {("m1", "t1"): 1.0, ("m1", "t2"): 0.0}
    result = compare_arms(
        ArmScores("a", {("m1", "t1"): 0.5, ("m1", "t2"): 0.5}),
        ArmScores("b", {("m1", "t1"): 0.5, ("m1", "t2"): 0.5}),
        targets,
    )
    assert result.paired_observations == 2


# --- reproducibility ---------------------------------------------------------

def test_the_same_seed_gives_the_same_interval():
    args = (arm("market", 0.5), arm("x", lambda i: 0.6 if i % 2 else 0.4), outcomes())
    first = compare_arms(*args, seed=7)
    second = compare_arms(*args, seed=7)
    assert (first.ci_low, first.ci_high) == (second.ci_low, second.ci_high)
    assert first.seed == 7, "the seed belongs in the result"


def test_a_different_seed_moves_the_interval_but_not_the_point_estimate():
    #: Genuine spread across observations, otherwise every resample sees the
    #: same value and the interval collapses to a point — which is correct, and
    #: would make this test assert nothing.
    args = (
        arm("market", 0.5),
        arm("x", lambda i: (0.95, 0.55, 0.15, 0.45)[i % 4]),
        outcomes(),
    )
    first = compare_arms(*args, seed=1)
    second = compare_arms(*args, seed=2)
    assert first.difference == second.difference
    assert (first.ci_low, first.ci_high) != (second.ci_low, second.ci_high)


# --- the benchmark as a whole ------------------------------------------------

def test_the_report_can_say_nothing_beat_the_market():
    """The answer the brief says is a successful outcome."""
    report = run_benchmark(
        arm("market", lambda i: 0.9 if i % 2 else 0.1),
        [arm("memory", 0.5), arm("evidence", 0.5)],
        outcomes(),
    )
    assert report.beat_the_market() == ()
    assert all(c.verdict == VERDICT_WORSE for c in report.comparisons)


def test_the_report_names_only_arms_that_beat_the_market():
    report = run_benchmark(
        arm("market", 0.5),
        [arm("useless", 0.5), arm("oracle", lambda i: 0.9 if i % 2 else 0.1)],
        outcomes(),
    )
    assert report.beat_the_market() == ("oracle",)


def test_every_challenger_is_compared_against_the_market_not_each_other():
    """An arm that beats another arm and loses to the price has found nothing."""
    report = run_benchmark(
        arm("market", 0.5), [arm("a", 0.4), arm("b", 0.45)], outcomes()
    )
    assert {c.baseline for c in report.comparisons} == {"market"}


def test_the_report_serialises():
    payload = run_benchmark(
        arm("market", 0.5), [arm("x", 0.5)], outcomes()
    ).as_dict()
    assert payload["schema_version"] == BENCHMARK_SCHEMA_VERSION
    assert payload["arms_that_beat_the_market"] == []
    assert payload["comparisons"][0]["seed"]


# --- input validation --------------------------------------------------------

@pytest.mark.parametrize("probability", [-0.1, 1.1, float("nan")])
def test_an_impossible_probability_is_refused(probability):
    with pytest.raises(ValueError, match="between 0 and 1"):
        ArmScores("bad", {("m1", "t"): probability}).validate()


def test_an_unnamed_arm_is_refused():
    with pytest.raises(ValueError, match="must be named"):
        ArmScores("", {}).validate()
