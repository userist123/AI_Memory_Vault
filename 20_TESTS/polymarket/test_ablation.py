"""Eleven comparisons at 95% produce a false finding 43% of the time.

    1 - 0.95 ** 11 = 0.43

That number is the reason this module exists, and these tests are about whether
the correction actually bites — including on the case where it must not, which
is a real effect that a too-conservative correction would hide.
"""
from __future__ import annotations

import pytest

from packages.polymarket.ablation import (
    ABLATION_SCHEMA_VERSION,
    AblationReport,
    run_ablations,
)
from packages.polymarket.benchmark import (
    VERDICT_NO_DIFFERENCE,
    VERDICT_UNDERPOWERED,
    VERDICT_WORSE,
    ArmScores,
)


def keys(n: int = 200) -> list[tuple[str, str]]:
    return [(f"m{i}", "2026-03-01T12:00:00Z") for i in range(n)]


def arm(name: str, fn, n: int = 200) -> ArmScores:
    if not callable(fn):
        constant = fn
        fn = lambda _i: constant  # noqa: E731
    return ArmScores(name, {k: fn(i) for i, k in enumerate(keys(n))})


def outcomes(n: int = 200) -> dict[tuple[str, str], float]:
    return {k: float(i % 2) for i, k in enumerate(keys(n))}


def informed(i: int) -> float:
    """A system that knows the answer."""
    return 0.85 if i % 2 else 0.15


# --- the arithmetic that motivates the module --------------------------------

def test_the_family_wise_error_the_correction_addresses():
    """Stated as a test because it is the entire justification.

    Eleven independent comparisons at 95% confidence, under a true null where
    nothing helps, produce at least one apparent finding 43% of the time.
    """
    assert 1 - 0.95 ** 11 == pytest.approx(0.43, abs=0.01)


# --- the correction bites ----------------------------------------------------

def test_a_ladder_of_useless_ablations_yields_no_components():
    """Every rung is the full system relabelled. Nothing should survive."""
    full = arm("full", informed)
    ladder = [(f"drop_{i}", arm(f"a{i}", informed)) for i in range(11)]
    report = run_ablations(full, ladder, outcomes())
    assert report.components_that_earn_their_place() == ()
    assert all(r.corrected_verdict == VERDICT_NO_DIFFERENCE for r in report.results)


def test_the_family_size_comes_from_the_whole_ladder():
    full = arm("full", informed)
    ladder = [(f"drop_{i}", arm(f"a{i}", informed)) for i in range(11)]
    report = run_ablations(full, ladder, outcomes())
    assert report.family_size == 11
    #: The most stringent threshold is alpha/family, the least is alpha.
    thresholds = sorted(r.holm_threshold for r in report.results)
    assert thresholds[0] == pytest.approx(0.05 / 11)
    assert thresholds[-1] == pytest.approx(0.05)


# --- the correction does not swallow a real effect ---------------------------

def test_a_component_that_genuinely_helps_survives_the_correction():
    """Dropping it makes the system much worse, and that must still register
    after correcting across a family of eleven. A method that can only produce
    nulls is no more honest than one that can only produce findings."""
    full = arm("full", informed)
    ladder = [("drop_memory", arm("no_memory", 0.5))]
    ladder += [(f"drop_{i}", arm(f"a{i}", informed)) for i in range(10)]
    report = run_ablations(full, ladder, outcomes())
    assert "drop_memory" in report.components_that_earn_their_place()


def test_removing_a_useful_component_reads_as_worse_not_better():
    """Direction matters: the full system is the baseline and the ablated
    variant is the challenger, so removing something useful looks bad."""
    report = run_ablations(
        arm("full", informed), [("drop_all", arm("blind", 0.5))], outcomes()
    )
    assert report.results[0].comparison.verdict == VERDICT_WORSE
    assert report.results[0].comparison.difference > 0


# --- the correction is visible, not silent -----------------------------------

def test_both_verdicts_are_reported_so_the_correction_can_be_seen():
    full = arm("full", informed)
    ladder = [("weak", arm("weak", lambda i: 0.84 if i % 2 else 0.16))]
    ladder += [(f"drop_{i}", arm(f"a{i}", informed)) for i in range(10)]
    report = run_ablations(full, ladder, outcomes())
    for result in report.results:
        assert result.raw_verdict
        assert result.corrected_verdict
        assert 0.0 <= result.crossing_fraction <= 1.0


def test_an_underpowered_rung_stays_underpowered_after_correction():
    """A correction cannot turn too little data into a conclusion."""
    full = arm("full", informed, n=20)
    report = run_ablations(
        full, [("drop_x", arm("x", 0.5, n=20))], outcomes(20)
    )
    assert report.results[0].corrected_verdict == VERDICT_UNDERPOWERED


def test_a_null_rung_cannot_become_a_finding():
    """An interval containing zero scores 1.0, above every threshold. No
    correction can promote a null, and one that could would be the bug."""
    full = arm("full", informed)
    report = run_ablations(full, [("identical", arm("same", informed))], outcomes())
    assert report.results[0].crossing_fraction == 1.0
    assert report.results[0].corrected_verdict == VERDICT_NO_DIFFERENCE


# --- shape -------------------------------------------------------------------

def test_an_empty_ladder_is_refused():
    with pytest.raises(ValueError, match="at least one rung"):
        run_ablations(arm("full", informed), [], outcomes())


@pytest.mark.parametrize("alpha", [0.0, 1.0, -0.1, 1.5])
def test_an_impossible_alpha_is_refused(alpha):
    with pytest.raises(ValueError, match="alpha must be"):
        run_ablations(
            arm("full", informed), [("x", arm("x", 0.5))], outcomes(), alpha=alpha
        )


def test_the_report_serialises():
    report = run_ablations(
        arm("full", informed), [("drop_all", arm("blind", 0.5))], outcomes()
    )
    payload = report.as_dict()
    assert payload["schema_version"] == ABLATION_SCHEMA_VERSION
    assert payload["family_size"] == 1
    assert payload["results"][0]["comparison"]["baseline"] == "full"
    assert "components_that_earn_their_place" in payload
