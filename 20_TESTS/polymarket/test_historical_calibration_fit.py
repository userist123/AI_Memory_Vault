from __future__ import annotations

import pytest

from polymarket.historical_calibration_fit import fit_historical_calibration, transform_historical_holdout
from polymarket.historical_calibration_holdout import split_historical_calibration_holdout
from polymarket.historical_evaluation_runner import evaluate_historical_dataset


def _row(day: int, probability: float, outcome: str) -> dict[str, object]:
    stamp = f"2026-01-{day:02d}T10:00:00Z"
    return {
        "market_id": f"m{day}", "outcome_id": "YES", "probability": probability,
        "prediction_known_as_of": stamp, "observed_at": f"2026-01-{day:02d}T09:00:00Z",
        "known_as_of": f"2026-01-{day:02d}T09:30:00Z", "acquired_at": stamp,
        "source_ref": f"fixture-{day}", "knowledge_basis": "source_native",
        "source_timestamp_semantics": "source publication time", "resolved_outcome_id": outcome,
        "resolution_known_at": "2026-02-01T10:00:00Z", "price": 0.5,
    }


def _split(rows):
    run = evaluate_historical_dataset(rows)
    return split_historical_calibration_holdout(run, boundary_exclusive="2026-01-03T00:00:00Z")


def test_fit_uses_calibration_only_and_transforms_holdout() -> None:
    split = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "NO")])
    fit = fit_historical_calibration(split, bin_count=10)
    assert fit.calibration_observation_count == 2
    transformed = transform_historical_holdout(split, fit)
    assert transformed[0].probability == 0.5
    assert transformed[0].target == 0


def test_unobserved_calibration_bin_refuses_holdout() -> None:
    split = _split([_row(1, 0.20, "YES"), _row(2, 0.80, "YES"), _row(3, 0.50, "NO")])
    fit = fit_historical_calibration(split, bin_count=10)
    with pytest.raises(ValueError, match="unobserved calibration bin"):
        transform_historical_holdout(split, fit)


def test_fit_is_deterministic_and_rejects_invalid_bin_count() -> None:
    split = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "YES")])
    assert fit_historical_calibration(split) == fit_historical_calibration(split)
    with pytest.raises(ValueError, match="bin_count must be between 2 and 100"):
        fit_historical_calibration(split, bin_count=1)


def test_fit_does_not_depend_on_holdout_targets() -> None:
    split_a = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "YES")])
    split_b = _split([_row(1, 0.20, "YES"), _row(2, 0.25, "NO"), _row(3, 0.20, "NO")])
    assert fit_historical_calibration(split_a) == fit_historical_calibration(split_b)
