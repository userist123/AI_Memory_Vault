"""Deterministic temporal holdout boundary for historical calibration.

Research-only: this module partitions an already accepted historical evaluation
run into a calibration segment and a strictly later holdout segment. It does
not fit calibration parameters, alter predictions, or make profitability
claims.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Iterable

from .historical_evaluation_runner import HistoricalEvaluationRun

SCHEMA_VERSION = "polymarket-historical-calibration-holdout.v1"


def _parse(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return parsed.astimezone(timezone.utc)


def _prediction_key(row) -> tuple[str, str, str]:
    return (
        row.prediction.market_id,
        row.prediction.outcome_id,
        row.prediction.known_as_of,
    )


@dataclass(frozen=True)
class HistoricalCalibrationHoldout:
    calibration: HistoricalEvaluationRun
    holdout: HistoricalEvaluationRun
    boundary_exclusive: str

    @property
    def calibration_count(self) -> int:
        return self.calibration.count

    @property
    def holdout_count(self) -> int:
        return self.holdout.count

    def validate(self) -> None:
        boundary = _parse(self.boundary_exclusive)
        self.calibration.validate()
        self.holdout.validate()

        calibration_keys = {_prediction_key(row) for row in self.calibration.rows}
        holdout_keys = {_prediction_key(row) for row in self.holdout.rows}
        if calibration_keys & holdout_keys:
            raise ValueError("calibration and holdout prediction keys must be disjoint")

        for row in self.calibration.rows:
            if _parse(row.prediction.known_as_of) >= boundary:
                raise ValueError("calibration row is not strictly before holdout boundary")
        for row in self.holdout.rows:
            if _parse(row.prediction.known_as_of) < boundary:
                raise ValueError("holdout row precedes holdout boundary")

        if self.calibration_count == 0 or self.holdout_count == 0:
            raise ValueError("calibration and holdout segments must both be non-empty")


def split_historical_calibration_holdout(
    run: HistoricalEvaluationRun,
    *,
    boundary_exclusive: str,
) -> HistoricalCalibrationHoldout:
    """Split an accepted run into pre-boundary calibration and post-boundary holdout."""
    run.validate()
    boundary = _parse(boundary_exclusive)

    calibration_rows = []
    holdout_rows = []
    for row, evaluation in zip(run.rows, run.evaluations):
        cutoff = _parse(row.prediction.known_as_of)
        if cutoff < boundary:
            calibration_rows.append((row, evaluation))
        else:
            holdout_rows.append((row, evaluation))

    if not calibration_rows or not holdout_rows:
        raise ValueError("boundary must produce non-empty calibration and holdout segments")

    calibration = HistoricalEvaluationRun(
        evaluations=tuple(item[1] for item in calibration_rows),
        rows=tuple(item[0] for item in calibration_rows),
        source_knowledge_bases=tuple(sorted({item[0].provenance.knowledge_basis for item in calibration_rows})),
    )
    holdout = HistoricalEvaluationRun(
        evaluations=tuple(item[1] for item in holdout_rows),
        rows=tuple(item[0] for item in holdout_rows),
        source_knowledge_bases=tuple(sorted({item[0].provenance.knowledge_basis for item in holdout_rows})),
    )
    result = HistoricalCalibrationHoldout(
        calibration=calibration,
        holdout=holdout,
        boundary_exclusive=boundary_exclusive,
    )
    result.validate()
    return result


__all__ = [
    "HistoricalCalibrationHoldout",
    "SCHEMA_VERSION",
    "split_historical_calibration_holdout",
]
