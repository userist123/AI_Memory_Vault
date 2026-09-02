"""memory_controller/tests/test_effectiveness_stats.py — Test suite for Wilson bounds and statistical estimators.

Tests cover:
1. zero trials
2. zero successes
3. 100% success
4. partial success
5. invalid successes
6. invalid trials
7. invalid confidence
8. minimum sample size
9. 1/1 -> INSUFFICIENT_DATA
10. 10/12 -> Wilson approx 0.553
11. Laplace smoothing
12. determinism
"""
import pytest
from memory_controller.effectiveness_stats import (
    MIN_SAMPLE_SIZE,
    evaluate_proportion,
    laplace_smoothed_rate,
    wilson_lower_bound,
)


def test_1_zero_trials():
    """Zero trials must return 0.0 without ZeroDivisionError."""
    assert wilson_lower_bound(0, 0) == 0.0
    res = evaluate_proportion(0, 0)
    assert res["wilson_lower_bound"] == 0.0
    assert res["observed_rate"] == 0.0
    assert res["smoothed_rate"] == 0.5  # (0+1)/(0+2)
    assert res["status"] == "INSUFFICIENT_DATA"


def test_2_zero_successes():
    """Zero successes with positive trials returns 0.0 for Wilson lower bound."""
    w = wilson_lower_bound(0, 5, confidence=0.95)
    assert w == 0.0
    res = evaluate_proportion(0, 5)
    assert res["observed_rate"] == 0.0
    assert res["smoothed_rate"] == round(1.0 / 7.0, 4)
    assert res["wilson_lower_bound"] == 0.0
    assert res["status"] == "VALID"


def test_3_one_hundred_percent_success():
    """100% success rate with n >= MIN_SAMPLE_SIZE produces valid Wilson lower bound."""
    w = wilson_lower_bound(5, 5, confidence=0.95)
    assert 0.55 < w < 0.60
    res = evaluate_proportion(5, 5)
    assert res["observed_rate"] == 1.0
    assert res["smoothed_rate"] == round(6.0 / 7.0, 4)
    assert res["status"] == "VALID"


def test_4_partial_success():
    """Partial success rates compute correctly."""
    w = wilson_lower_bound(3, 5, confidence=0.95)
    assert 0.0 < w < 0.6
    res = evaluate_proportion(3, 5)
    assert res["observed_rate"] == 0.6
    assert res["status"] == "VALID"


def test_5_invalid_successes():
    """Negative successes or successes > trials must be rejected with ValueError/TypeError."""
    with pytest.raises(ValueError, match="successes cannot be negative"):
        wilson_lower_bound(-1, 5)

    with pytest.raises(ValueError, match="cannot exceed trials"):
        wilson_lower_bound(6, 5)

    with pytest.raises(TypeError):
        wilson_lower_bound("5", 5)  # type: ignore

    with pytest.raises(ValueError):
        laplace_smoothed_rate(-1, 5)

    with pytest.raises(ValueError):
        laplace_smoothed_rate(6, 5)


def test_6_invalid_trials():
    """Negative trials must be rejected."""
    with pytest.raises(ValueError, match="trials cannot be negative"):
        wilson_lower_bound(0, -1)

    with pytest.raises(TypeError):
        wilson_lower_bound(0, 5.5)  # type: ignore

    with pytest.raises(ValueError):
        laplace_smoothed_rate(0, -1)


def test_7_invalid_confidence():
    """Confidence outside (0, 1) must be rejected."""
    for bad_conf in [-0.5, 0.0, 1.0, 1.5]:
        with pytest.raises(ValueError, match="confidence must be strictly between 0.0 and 1.0"):
            wilson_lower_bound(5, 10, confidence=bad_conf)


def test_8_minimum_sample_size_boundary():
    """Sample sizes below MIN_SAMPLE_SIZE (5) are flagged INSUFFICIENT_DATA."""
    assert MIN_SAMPLE_SIZE == 5

    res_4 = evaluate_proportion(4, 4)
    assert res_4["status"] == "INSUFFICIENT_DATA"

    res_5 = evaluate_proportion(5, 5)
    assert res_5["status"] == "VALID"


def test_9_one_out_of_one_is_insufficient_data():
    """Obligatory requirement: 1/1 MUST be INSUFFICIENT_DATA."""
    res = evaluate_proportion(1, 1)
    assert res["observed_rate"] == 1.0
    assert res["status"] == "INSUFFICIENT_DATA"
    # Wilson lower bound for 1/1 at 95% is ~0.2065, reflecting high uncertainty
    assert res["wilson_lower_bound"] < 0.25


def test_10_ten_out_of_twelve_wilson_approx_0_553():
    """Obligatory validation case: 10/12 @ 0.95 confidence yields Wilson lower bound approx 0.553."""
    w = wilson_lower_bound(10, 12, confidence=0.95)
    # Expected analytical value is ~0.552 - 0.553
    assert abs(w - 0.552) < 0.005, f"Expected approx 0.553, got {w}"

    res = evaluate_proportion(10, 12, confidence=0.95)
    assert abs(res["wilson_lower_bound"] - 0.552) < 0.005
    assert res["status"] == "VALID"


def test_11_laplace_smoothing_distinct_from_wilson():
    """Laplace smoothing must follow (s + 1) / (n + 2) and remain distinct from Wilson bound."""
    assert laplace_smoothed_rate(0, 0) == 0.5
    assert laplace_smoothed_rate(1, 1) == 2.0 / 3.0
    assert laplace_smoothed_rate(10, 12) == 11.0 / 14.0

    res = evaluate_proportion(10, 12)
    assert res["smoothed_rate"] == round(11.0 / 14.0, 4)
    assert res["wilson_lower_bound"] != res["smoothed_rate"]


def test_12_determinism_across_multiple_runs():
    """Estimators must yield identical results across repeated executions."""
    val1 = wilson_lower_bound(7, 10, confidence=0.95)
    val2 = wilson_lower_bound(7, 10, confidence=0.95)
    assert val1 == val2

    res1 = evaluate_proportion(7, 10, confidence=0.95)
    res2 = evaluate_proportion(7, 10, confidence=0.95)
    assert res1 == res2
