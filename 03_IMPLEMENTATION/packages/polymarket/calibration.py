"""Leakage-safe retrospective calibration scoring for Polymarket predictions."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import math
from typing import Iterable, Mapping

from .market_snapshot import MarketSnapshot, _parse_datetime
from .prediction_ledger import PredictionRecord

CALIBRATION_SCHEMA_VERSION = "polymarket-calibration.v1"
LOG_EPSILON = 1e-15


@dataclass(frozen=True)
class CalibrationObservation:
    prediction_id: str
    market_id: str
    outcome_id: str
    probability: float
    target: int
    prediction_known_as_of: str
    resolution_known_at: str

    def validate(self) -> None:
        if not self.prediction_id:
            raise ValueError("prediction_id is required")
        if not self.market_id or not self.outcome_id:
            raise ValueError("market_id and outcome_id are required")
        if not math.isfinite(self.probability) or not 0.0 <= self.probability <= 1.0:
            raise ValueError("probability must be finite and between 0 and 1")
        if self.target not in (0, 1):
            raise ValueError("target must be 0 or 1")
        prediction_time = _parse_datetime(
            self.prediction_known_as_of, field_name="prediction_known_as_of"
        )
        resolution_time = _parse_datetime(
            self.resolution_known_at, field_name="resolution_known_at"
        )
        if prediction_time >= resolution_time:
            raise ValueError("prediction information cutoff must precede resolution knowledge")


@dataclass(frozen=True)
class CalibrationBin:
    lower: float
    upper: float
    count: int
    mean_predicted_probability: float
    empirical_rate: float
    absolute_gap: float

    def validate(self) -> None:
        if not 0.0 <= self.lower < self.upper <= 1.0:
            raise ValueError("calibration bin bounds are invalid")
        if self.count < 0:
            raise ValueError("calibration bin count cannot be negative")
        if self.count == 0:
            if self.mean_predicted_probability != 0.0 or self.empirical_rate != 0.0:
                raise ValueError("empty bins must have zero summary values")
        else:
            for name, value in (
                ("mean_predicted_probability", self.mean_predicted_probability),
                ("empirical_rate", self.empirical_rate),
                ("absolute_gap", self.absolute_gap),
            ):
                if not math.isfinite(value):
                    raise ValueError(f"{name} must be finite")
                if not 0.0 <= value <= 1.0:
                    raise ValueError(f"{name} must be between 0 and 1")


@dataclass(frozen=True)
class CalibrationReport:
    schema_version: str
    observation_count: int
    brier_score: float
    log_loss: float
    bins: tuple[CalibrationBin, ...]

    def validate(self) -> None:
        if self.schema_version != CALIBRATION_SCHEMA_VERSION:
            raise ValueError("unsupported calibration schema version")
        if self.observation_count <= 0:
            raise ValueError("at least one calibration observation is required")
        for name, value in (("brier_score", self.brier_score), ("log_loss", self.log_loss)):
            if not math.isfinite(value) or value < 0.0:
                raise ValueError(f"{name} must be finite and non-negative")
        if not self.bins:
            raise ValueError("at least one calibration bin is required")
        for calibration_bin in self.bins:
            calibration_bin.validate()

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": self.schema_version,
            "observation_count": self.observation_count,
            "brier_score": self.brier_score,
            "log_loss": self.log_loss,
            "bins": [
                {
                    "lower": item.lower,
                    "upper": item.upper,
                    "count": item.count,
                    "mean_predicted_probability": item.mean_predicted_probability,
                    "empirical_rate": item.empirical_rate,
                    "absolute_gap": item.absolute_gap,
                }
                for item in self.bins
            ],
        }


def _resolution_target(prediction: PredictionRecord, resolution_snapshot: MarketSnapshot) -> int:
    resolution = resolution_snapshot.resolution
    if resolution is None:
        raise ValueError("resolution metadata is required")
    if resolution.status != "resolved":
        raise ValueError("only resolved markets can be calibrated")
    if prediction.market_id != resolution_snapshot.market.market_id:
        raise ValueError("prediction and resolution market_id must match")
    if prediction.outcome_id not in resolution_snapshot.market.outcome_ids:
        raise ValueError("prediction outcome_id is not present in the resolved market")
    if not resolution.outcome_ids:
        raise ValueError("resolved outcome_ids are required")
    if not set(resolution.outcome_ids).issubset(set(resolution_snapshot.market.outcome_ids)):
        raise ValueError("resolution outcome_ids must belong to the market outcomes")
    resolution_known_at = _parse_datetime(
        resolution.known_at, field_name="resolution.known_at"
    )
    prediction_cutoff = _parse_datetime(
        prediction.known_as_of, field_name="prediction.known_as_of"
    )
    if prediction_cutoff >= resolution_known_at:
        raise ValueError("prediction information cutoff must precede resolution knowledge")
    return int(prediction.outcome_id in resolution.outcome_ids)


def observe_prediction(
    prediction: PredictionRecord,
    *,
    evidence_snapshots: Mapping[str, MarketSnapshot],
    resolution_snapshot: MarketSnapshot,
) -> CalibrationObservation:
    """Build one calibration observation while rejecting post-cutoff evidence."""
    prediction.validate()
    resolution_snapshot.verify()
    if resolution_snapshot.data_quality != "verified":
        raise ValueError("calibration requires a verified resolution snapshot")
    resolution = resolution_snapshot.resolution
    if resolution is None:
        raise ValueError("resolution metadata is required")

    for snapshot_id in prediction.provenance.snapshot_ids:
        snapshot = evidence_snapshots.get(snapshot_id)
        if snapshot is None:
            raise ValueError(f"missing evidence snapshot: {snapshot_id}")
        snapshot.verify()
        if snapshot.data_quality != "verified":
            raise ValueError("calibration evidence snapshots must be verified")
        if snapshot.market.market_id != prediction.market_id:
            raise ValueError("prediction evidence market_id mismatch")
        snapshot_known_as_of = _parse_datetime(
            snapshot.known_as_of, field_name="snapshot.known_as_of"
        )
        prediction_cutoff = _parse_datetime(
            prediction.known_as_of, field_name="prediction.known_as_of"
        )
        if snapshot_known_as_of > prediction_cutoff:
            raise ValueError("prediction evidence was known after the prediction cutoff")

    target = _resolution_target(prediction, resolution_snapshot)
    return CalibrationObservation(
        prediction_id=prediction.prediction_id,
        market_id=prediction.market_id,
        outcome_id=prediction.outcome_id,
        probability=prediction.probability,
        target=target,
        prediction_known_as_of=prediction.known_as_of,
        resolution_known_at=resolution.known_at,
    )


def _bin_index(probability: float, bin_count: int) -> int:
    if probability == 1.0:
        return bin_count - 1
    return min(int(probability * bin_count), bin_count - 1)


def score_calibration(
    observations: Iterable[CalibrationObservation], *, bin_count: int = 10
) -> CalibrationReport:
    """Compute Brier, clipped log-loss, and equal-width reliability bins."""
    items = tuple(observations)
    if not items:
        raise ValueError("at least one calibration observation is required")
    if bin_count < 2 or bin_count > 100:
        raise ValueError("bin_count must be between 2 and 100")
    for item in items:
        item.validate()

    brier = sum((item.probability - item.target) ** 2 for item in items) / len(items)
    log_loss = sum(
        -math.log(max(LOG_EPSILON, item.probability))
        if item.target
        else -math.log(max(LOG_EPSILON, 1.0 - item.probability))
        for item in items
    ) / len(items)

    grouped: list[list[CalibrationObservation]] = [[] for _ in range(bin_count)]
    for item in items:
        grouped[_bin_index(item.probability, bin_count)].append(item)

    bins: list[CalibrationBin] = []
    for index, group in enumerate(grouped):
        lower = index / bin_count
        upper = (index + 1) / bin_count
        if not group:
            bins.append(
                CalibrationBin(lower, upper, 0, 0.0, 0.0, 0.0)
            )
            continue
        mean_probability = sum(item.probability for item in group) / len(group)
        empirical_rate = sum(item.target for item in group) / len(group)
        bins.append(
            CalibrationBin(
                lower=lower,
                upper=upper,
                count=len(group),
                mean_predicted_probability=mean_probability,
                empirical_rate=empirical_rate,
                absolute_gap=abs(mean_probability - empirical_rate),
            )
        )

    report = CalibrationReport(
        schema_version=CALIBRATION_SCHEMA_VERSION,
        observation_count=len(items),
        brier_score=brier,
        log_loss=log_loss,
        bins=tuple(bins),
    )
    report.validate()
    return report


__all__ = [
    "CALIBRATION_SCHEMA_VERSION",
    "LOG_EPSILON",
    "CalibrationBin",
    "CalibrationObservation",
    "CalibrationReport",
    "observe_prediction",
    "score_calibration",
]
