"""Tests for the paired statistics of the B03 harness, against published reference values."""
import math

import pytest

from lifecycle.validation.book_to_memory_paired_stats import (
    StatsError,
    betainc_reg,
    bootstrap_ci_mean,
    holm_adjust,
    mean,
    paired_differences,
    paired_t_test,
    sample_sd,
    sign_flip_permutation_test,
    t_cdf,
    t_ppf,
)


@pytest.mark.parametrize("df,crit", [(1, 12.7062), (4, 2.7764), (9, 2.2622), (30, 2.0423), (50, 2.0086), (120, 1.9799)])
def test_t_critical_values_match_the_published_table(df, crit):
    assert t_ppf(0.975, df) == pytest.approx(crit, abs=1e-4)


def test_t_cdf_known_points():
    assert t_cdf(0.0, 7) == pytest.approx(0.5)
    assert t_cdf(1.0, 1) == pytest.approx(0.75)           # Cauchy
    assert t_cdf(2.0, 10) == pytest.approx(0.96331, abs=1e-5)
    assert t_cdf(-2.0, 10) == pytest.approx(1 - 0.96331, abs=1e-5)


def test_t_ppf_inverts_t_cdf():
    for df in (3, 11, 50):
        for p in (0.01, 0.2, 0.5, 0.9, 0.995):
            assert t_cdf(t_ppf(p, df), df) == pytest.approx(p, abs=1e-9)


def test_betainc_edges_and_symmetry():
    assert betainc_reg(2, 3, 0.0) == 0.0 and betainc_reg(2, 3, 1.0) == 1.0
    assert betainc_reg(2, 3, 0.3) == pytest.approx(1 - betainc_reg(3, 2, 0.7))
    with pytest.raises(StatsError):
        t_cdf(1.0, 0)
    with pytest.raises(StatsError):
        t_ppf(1.0, 5)


def test_paired_t_worked_example():
    """d = 1..5: mean 3, sd sqrt(2.5), t = 4.2426 on 4 df, p = 0.0132, 95% CI [1.0368, 4.9632]."""
    r = paired_t_test([1, 2, 3, 4, 5])
    assert r["mean_diff"] == 3 and r["df"] == 4
    assert r["sd_diff"] == pytest.approx(math.sqrt(2.5))
    assert r["t"] == pytest.approx(4.2426, abs=1e-4)
    assert r["p_two_sided"] == pytest.approx(0.0132, abs=5e-5)
    assert r["ci_low"] == pytest.approx(1.0368, abs=1e-3)
    assert r["ci_high"] == pytest.approx(4.9632, abs=1e-3)
    assert r["dz"] == pytest.approx(3 / math.sqrt(2.5))


def test_paired_t_zero_variance_has_no_t_statistic():
    r = paired_t_test([2.0, 2.0, 2.0])
    assert r["t"] is None and r["p_two_sided"] is None and r["dz"] is None
    assert r["ci_low"] == r["ci_high"] == 2.0


def test_paired_t_needs_two_pairs():
    with pytest.raises(StatsError):
        paired_t_test([1.0])
    with pytest.raises(StatsError):
        paired_differences([1, 2], [1])


def test_symmetric_data_is_not_significant():
    r = paired_t_test([-2, -1, 0, 1, 2])
    assert r["mean_diff"] == 0 and r["p_two_sided"] == pytest.approx(1.0)


def test_permutation_exact_small_sample():
    """All five differences positive: only the all-plus and all-minus sign patterns are as extreme, 2/32."""
    r = sign_flip_permutation_test([1, 2, 3, 4, 5], seed=1)
    assert r["method"] == "exact" and r["p_two_sided"] == pytest.approx(2 / 32)
    assert sign_flip_permutation_test([-2, -1, 0, 1, 2], seed=1)["p_two_sided"] == pytest.approx(1.0)


def test_permutation_monte_carlo_is_seeded_and_close_to_exact():
    diffs = [0.5, 1.0, -0.2, 0.8, 1.2, 0.3, 0.9, -0.1, 0.7, 1.1, 0.6, 0.4, 0.2, 1.3, 0.8, 0.5, 0.9, 1.0, 0.1, 0.6, 0.7, 0.3]
    a = sign_flip_permutation_test(diffs, seed=7, n_draws=20000, exact_max_n=10)
    b = sign_flip_permutation_test(diffs, seed=7, n_draws=20000, exact_max_n=10)
    assert a == b and a["method"] == "monte_carlo"
    assert 0.0 < a["p_two_sided"] < 0.01


def test_bootstrap_ci_is_seeded_and_brackets_the_mean():
    diffs = [1.0, 2.0, 0.5, 1.5, 2.5, 1.0, 0.0, 2.0]
    lo, hi = bootstrap_ci_mean(diffs, seed=3, n_boot=2000)
    assert (lo, hi) == bootstrap_ci_mean(diffs, seed=3, n_boot=2000)
    assert lo < mean(diffs) < hi


def test_holm_adjustment():
    assert holm_adjust([0.01, 0.04, 0.03]) == pytest.approx([0.03, 0.06, 0.06])
    assert holm_adjust([0.5]) == [0.5]
    assert holm_adjust([0.6, 0.7]) == pytest.approx([1.0, 1.0])


def test_mean_and_sd_guard_empty_input():
    with pytest.raises(StatsError):
        mean([])
    with pytest.raises(StatsError):
        sample_sd([1.0])
