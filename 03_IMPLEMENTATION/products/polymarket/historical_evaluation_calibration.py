"""Leakage-safe calibration projection for the Phase 17 historical run."""
from __future__ import annotations

from .calibration import CalibrationObservation, CalibrationReport, score_calibration
from .historical_evaluation_runner import HistoricalEvaluationRun

SCHEMA_VERSION = "polymarket-historical-evaluation-calibration.v1"


def project_calibration_observations(
    run: HistoricalEvaluationRun,
) -> tuple[CalibrationObservation, ...]:
    """Project an accepted historical evaluation run into calibration observations."""
    run.validate()
    observations: list[CalibrationObservation] = []
    for row in run.rows:
        evaluation = next(
            item
            for item in run.evaluations
            if item.market_id == row.prediction.market_id
            and item.outcome_id == row.prediction.outcome_id
            and item.prediction_known_as_of == row.prediction.known_as_of
        )
        target = int(row.resolved_outcome_id == row.prediction.outcome_id)
        observations.append(
            CalibrationObservation(
                prediction_id=(
                    f"{row.prediction.market_id}:"
                    f"{row.prediction.outcome_id}:"
                    f"{row.prediction.known_as_of}"
                ),
                market_id=evaluation.market_id,
                outcome_id=evaluation.outcome_id,
                probability=evaluation.probability,
                target=target,
                prediction_known_as_of=evaluation.prediction_known_as_of,
                resolution_known_at=row.resolution_known_at,
            )
        )
    return tuple(observations)


def score_historical_evaluation_calibration(
    run: HistoricalEvaluationRun,
    *,
    bin_count: int = 10,
) -> CalibrationReport:
    """Score calibration only after the temporal historical run has validated."""
    observations = project_calibration_observations(run)
    return score_calibration(observations, bin_count=bin_count)


__all__ = [
    "SCHEMA_VERSION",
    "project_calibration_observations",
    "score_historical_evaluation_calibration",
]
