"""Regression tests for indicator-series length boundaries (WP-4).

Defect fixed: `compute_all_indicators._pct()` guarded its lookback with
`len(closes) > abs(idx)` instead of `>=`. `closes.iloc[-k]` is valid as soon as
the series holds exactly k bars (it then addresses the FIRST element), so the
old guard demanded one bar more than the arithmetic needs. The failure mode was
silent and worse than a crash: at exactly 6 bars `var_sapt_pct` and at exactly
21 bars `var_luna_pct` returned 0.0 -- a value indistinguishable from a genuine
flat market -- instead of the real percentage change.

These tests pin the exact boundary length for every lookback in the series, so
a future change to the guard cannot silently re-introduce the same class of
bug. Each lookback is asserted at three lengths: one below the boundary (must
be 0.0, nothing to look back to), exactly at the boundary (must compute), and
one above (must compute).
"""
from __future__ import annotations

import pandas as pd
import pytest

from xau_kinetic.financial_ingestion.indicators import compute_all_indicators

# (metric key, negative lookback index used by compute_all_indicators)
# _pct(-2) -> var_zi, _pct(-6) -> var_sapt, _pct(-21) -> var_luna
LOOKBACKS = [
    ("var_zi_pct", 2),
    ("var_sapt_pct", 6),
    ("var_luna_pct", 21),
]

# compute_all_indicators refuses to produce anything below this many bars.
MIN_BARS_FOR_ANY_OUTPUT = 5


def _monotonic_ohlcv(n: int) -> pd.DataFrame:
    """Strictly increasing closes, so ANY correctly-computed percentage change
    is non-zero. A 0.0 result therefore proves the value was skipped, not that
    the market was flat."""
    closes = [100.0 + i for i in range(n)]
    return pd.DataFrame(
        {
            "Open": closes,
            "High": [c + 1.0 for c in closes],
            "Low": [c - 1.0 for c in closes],
            "Close": closes,
            "Volume": [1_000] * n,
        }
    )


@pytest.mark.parametrize("metric,lookback", LOOKBACKS)
def test_series_of_exactly_lookback_length_is_computed_not_zeroed(metric, lookback):
    """THE regression: a series of exactly `lookback` bars must produce a real
    value. This is the case the old `>` guard silently zeroed."""
    n = max(lookback, MIN_BARS_FOR_ANY_OUTPUT)
    result = compute_all_indicators(_monotonic_ohlcv(n), ticker="TEST")
    assert result, f"no indicators produced for a {n}-bar series"
    assert result[metric] != 0.0, (
        f"{metric} was zeroed for a series of exactly {n} bars; "
        f"closes.iloc[-{lookback}] is a valid position at this length"
    )


@pytest.mark.parametrize("metric,lookback", LOOKBACKS)
def test_series_one_bar_above_lookback_is_computed(metric, lookback):
    n = max(lookback + 1, MIN_BARS_FOR_ANY_OUTPUT)
    result = compute_all_indicators(_monotonic_ohlcv(n), ticker="TEST")
    assert result[metric] != 0.0


@pytest.mark.parametrize("metric,lookback", LOOKBACKS)
def test_series_one_bar_below_lookback_returns_zero(metric, lookback):
    """The guard must still refuse a lookback the series genuinely cannot
    satisfy -- the fix widens the boundary by exactly one, it does not remove
    it."""
    n = lookback - 1
    if n < MIN_BARS_FOR_ANY_OUTPUT:
        pytest.skip(f"{n} bars is below the {MIN_BARS_FOR_ANY_OUTPUT}-bar floor of compute_all_indicators")
    result = compute_all_indicators(_monotonic_ohlcv(n), ticker="TEST")
    assert result[metric] == 0.0, (
        f"{metric} produced a value for a {n}-bar series, but a {lookback}-bar "
        f"lookback is not satisfiable at that length"
    )


def test_exact_boundary_values_are_arithmetically_correct():
    """Not just non-zero -- the right number. For a strictly +1/bar series of
    length k, looking back k bars compares last (100+k-1) against first (100)."""
    for metric, lookback in LOOKBACKS:
        n = max(lookback, MIN_BARS_FOR_ANY_OUTPUT)
        result = compute_all_indicators(_monotonic_ohlcv(n), ticker="TEST")
        prev = 100.0 + (n - lookback)
        last = 100.0 + (n - 1)
        expected = round((last - prev) / prev * 100, 4)
        assert result[metric] == pytest.approx(expected, abs=1e-4), (
            f"{metric} at exactly {n} bars: expected {expected}, got {result[metric]}"
        )


def test_indicator_record_length_is_stable_across_series_lengths():
    """The indicator record itself must not gain or lose keys as the input
    series grows -- callers persist it as a fixed-shape note."""
    shapes = {
        n: set(compute_all_indicators(_monotonic_ohlcv(n), ticker="TEST").keys())
        for n in (5, 6, 21, 22, 60)
    }
    reference = shapes[60]
    for n, keys in shapes.items():
        assert keys == reference, (
            f"indicator record shape changed at {n} bars: "
            f"missing={reference - keys}, extra={keys - reference}"
        )


def test_below_minimum_bars_returns_empty_record():
    """Documented floor: fewer than 5 bars yields {} rather than a partially
    populated record."""
    assert compute_all_indicators(_monotonic_ohlcv(4), ticker="TEST") == {}
