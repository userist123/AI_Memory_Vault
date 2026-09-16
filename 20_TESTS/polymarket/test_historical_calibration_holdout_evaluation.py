from __future__ import annotations

import pytest

from polymarket.historical_calibration_fit import fit_historical_calibration
from polymarket.historical_calibration_holdout import split_historical_calibration_holdout
from polymarket.historical_calibration_holdout_evaluation import evaluate_historical_calibration_holdout
from polymarket.historical_evaluation_runner import evaluate_historical_dataset


def _row(day: int, probability: float, outcome: str) -> dict[str, object]:
    stamp = f"2026-01-{day:02d}T10:00:00Z"
    return {
        "market_id": f"m{day}",
        "outcome_id": "YES",
        "probability": probability,
        "prediction_known_as_of": stamp,
        "observed_at": f"2026-01-{day:02d}T09:00:00Z",
        "known_as_of": f"2026-01-{day:02d}T09:30:00Z",
        "acquired_at": stamp,
        "source_ref": f"fixture-{day}",
        "knowledge_basis": "source_native",
        "source_timestamp_semantics": "source publication time",
        "resolved_outcome_id": outcome,
        "resolution_known_at": "2026-02-01T10:00:00Z",
        "price": 0.5,
    }


def _split(rows):
    run = evaluate_historical_dataset(rows)
    return split_historical_calibration_holdout(run, boundary_exclusive="2026-01-03T00:00:00Z")


def test_holdout_evaluation_compares_raw_and_calibrated_on_same_rows() -> None:
    split = _split([
        _row(1, 0.20, "YES"),
        _row(2, 0.25, "NO"),
        _row(3, 0.20, "NO"),
    ])
    fit = fit_historical_calibration(split, bin_count=10)
    result = evaluate_historical_calibration_holdout(split, fit, bin_count=10)
    assert result.evaluated_count == 1
    assert result.raw.observation_count == 1
    assert result.calibrated.observation_count == 1
    assert result.calibrated.brier_score >= 0.0
    assert result.brier_delta == result.calibrated.brier_score - result.raw.brier_score


def test_holdout_targets_do_not_change_fitted_mapping() -> None:
    split_a = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "YES")])
    split_b = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "NO")])
    fit_a = fit_historical_calibration(split_a)
    fit_b = fit_historical_calibration(split_b)
    assert fit_a == fit_b


def test_unobserved_holdout_bin_is_refused() -> None:
    split = _split([_row(1, 0.20, "YES"), _row(2, 0.80, "YES"), _row(3, 0.50, "NO")])
    fit = fit_historical_calibration(split, bin_count=10)
    with pytest.raises(ValueError, match="unobserved calibration bin"):
        evaluate_historical_calibration_holdout(split, fit)


def test_invalid_evaluation_bin_count_is_rejected() -> None:
    split = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "YES")])
    fit = fit_historical_calibration(split)
    with pytest.raises(ValueError, match="bin_count must be between 2 and 100"):
        evaluate_historical_calibration_holdout(split, fit, bin_count=1)
