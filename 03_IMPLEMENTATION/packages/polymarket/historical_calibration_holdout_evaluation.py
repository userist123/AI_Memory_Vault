"""Strict holdout-only evaluation of a historical calibration transform."""
from __future__ import annotations

from dataclasses import dataclass
from .calibration import CalibrationObservation, CalibrationReport, score_calibration
from .historical_calibration_holdout import HistoricalCalibrationHoldout
from .historical_calibration_fit import HistoricalCalibrationFit, transform_historical_holdout
from .historical_evaluation_calibration import project_calibration_observations

SCHEMA_VERSION = "polymarket-historical-calibration-holdout-eval.v1"

@dataclass(frozen=True)
class HistoricalCalibrationHoldoutEvaluation:
    schema_version: str
    raw: CalibrationReport
    calibrated: CalibrationReport
    evaluated_count: int

    @property
    def brier_delta(self) -> float:
        return self.calibrated.brier_score - self.raw.brier_score

    @property
    def log_loss_delta(self) -> float:
        return self.calibrated.log_loss - self.raw.log_loss

    def validate(self) -> None:
        if self.schema_version != SCHEMA_VERSION:
            raise ValueError("unsupported holdout evaluation schema version")
        self.raw.validate()
        self.calibrated.validate()
        if self.evaluated_count <= 0:
            raise ValueError("holdout evaluation requires at least one evaluated observation")
        if self.raw.observation_count != self.evaluated_count:
            raise ValueError("raw report count does not match evaluated holdout count")
        if self.calibrated.observation_count != self.evaluated_count:
            raise ValueError("calibrated report count does not match evaluated holdout count")


def evaluate_historical_calibration_holdout(
    split: HistoricalCalibrationHoldout,
    fit: HistoricalCalibrationFit,
    *,
    bin_count: int = 10,
) -> HistoricalCalibrationHoldoutEvaluation:
    """Compare raw and fitted probabilities using holdout outcomes only."""
    split.validate()
    fit.validate()
    if bin_count < 2 or bin_count > 100:
        raise ValueError("bin_count must be between 2 and 100")

    raw: tuple[CalibrationObservation, ...] = project_calibration_observations(split.holdout)
    calibrated = transform_historical_holdout(split, fit)
    if len(raw) != len(calibrated):
        raise ValueError("raw and calibrated holdout counts must match")

    raw_report = score_calibration(raw, bin_count=bin_count)
    calibrated_report = score_calibration(calibrated, bin_count=bin_count)
    result = HistoricalCalibrationHoldoutEvaluation(
        schema_version=SCHEMA_VERSION,
        raw=raw_report,
        calibrated=calibrated_report,
        evaluated_count=len(raw),
    )
    result.validate()
    return result

__all__ = [
    "HistoricalCalibrationHoldoutEvaluation",
    "SCHEMA_VERSION",
    "evaluate_historical_calibration_holdout",
]
