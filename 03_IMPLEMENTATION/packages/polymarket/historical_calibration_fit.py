"""Deterministic calibration fit/apply over a temporal calibration split.

Research-only: fit uses calibration observations only. Holdout observations are
never inspected during fitting. Empty calibration bins remain unavailable and
cause an explicit refusal when a holdout probability lands in one.
"""
from __future__ import annotations

from dataclasses import dataclass
import math

from .calibration import CalibrationObservation
from .historical_calibration_holdout import HistoricalCalibrationHoldout
from .historical_evaluation_calibration import project_calibration_observations

SCHEMA_VERSION = "polymarket-historical-calibration-fit.v1"


@dataclass(frozen=True)
class CalibrationMapping:
    lower: float
    upper: float
    count: int
    calibrated_probability: float

    def validate(self) -> None:
        if not 0.0 <= self.lower < self.upper <= 1.0:
            raise ValueError("calibration mapping bounds are invalid")
        if self.count <= 0:
            raise ValueError("calibration mapping count must be positive")
        if not math.isfinite(self.calibrated_probability) or not 0.0 <= self.calibrated_probability <= 1.0:
            raise ValueError("calibrated_probability must be finite and between 0 and 1")


@dataclass(frozen=True)
class HistoricalCalibrationFit:
    schema_version: str
    bin_count: int
    calibration_observation_count: int
    mappings: tuple[CalibrationMapping, ...]

    def validate(self) -> None:
        if self.schema_version != SCHEMA_VERSION:
            raise ValueError("unsupported historical calibration fit schema version")
        if self.bin_count < 2 or self.bin_count > 100:
            raise ValueError("bin_count must be between 2 and 100")
        if self.calibration_observation_count <= 0:
            raise ValueError("calibration fit requires at least one observation")
        for mapping in self.mappings:
            mapping.validate()

    def _mapping_for(self, probability: float) -> CalibrationMapping:
        if not math.isfinite(probability) or not 0.0 <= probability <= 1.0:
            raise ValueError("probability must be finite and between 0 and 1")
        index = self.bin_count - 1 if probability == 1.0 else min(int(probability * self.bin_count), self.bin_count - 1)
        lower = index / self.bin_count
        upper = (index + 1) / self.bin_count
        for mapping in self.mappings:
            if mapping.lower == lower and mapping.upper == upper:
                return mapping
        raise ValueError("holdout probability falls into an unobserved calibration bin")

    def transform(self, observation: CalibrationObservation) -> CalibrationObservation:
        observation.validate()
        mapping = self._mapping_for(observation.probability)
        return CalibrationObservation(
            prediction_id=observation.prediction_id,
            market_id=observation.market_id,
            outcome_id=observation.outcome_id,
            probability=mapping.calibrated_probability,
            target=observation.target,
            prediction_known_as_of=observation.prediction_known_as_of,
            resolution_known_at=observation.resolution_known_at,
        )


def fit_historical_calibration(split: HistoricalCalibrationHoldout, *, bin_count: int = 10) -> HistoricalCalibrationFit:
    """Fit empirical equal-width calibration bins using calibration rows only."""
    split.validate()
    if bin_count < 2 or bin_count > 100:
        raise ValueError("bin_count must be between 2 and 100")

    observations = project_calibration_observations(split.calibration)
    grouped: list[list[CalibrationObservation]] = [[] for _ in range(bin_count)]
    for observation in observations:
        index = bin_count - 1 if observation.probability == 1.0 else min(int(observation.probability * bin_count), bin_count - 1)
        grouped[index].append(observation)

    mappings = tuple(
        CalibrationMapping(
            lower=index / bin_count,
            upper=(index + 1) / bin_count,
            count=len(group),
            calibrated_probability=sum(item.target for item in group) / len(group),
        )
        for index, group in enumerate(grouped)
        if group
    )
    fit = HistoricalCalibrationFit(
        schema_version=SCHEMA_VERSION,
        bin_count=bin_count,
        calibration_observation_count=len(observations),
        mappings=mappings,
    )
    fit.validate()
    return fit


def transform_historical_holdout(
    split: HistoricalCalibrationHoldout,
    fit: HistoricalCalibrationFit,
) -> tuple[CalibrationObservation, ...]:
    """Apply an already fitted calibration transform to holdout only."""
    split.validate()
    fit.validate()
    if fit.bin_count <= 1:
        raise ValueError("invalid calibration fit bin_count")
    observations = project_calibration_observations(split.holdout)
    return tuple(fit.transform(observation) for observation in observations)


__all__ = [
    "CalibrationMapping",
    "HistoricalCalibrationFit",
    "SCHEMA_VERSION",
    "fit_historical_calibration",
    "transform_historical_holdout",
]
