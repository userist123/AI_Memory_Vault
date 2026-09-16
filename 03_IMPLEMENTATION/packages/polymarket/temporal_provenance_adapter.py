"""Adapter that attaches Phase 14 provenance to Phase 11 replay points.

The Phase 11 tape remains unchanged. Retrospective API data is conservatively
classified as acquisition-only unless a historical knowledge timestamp is
explicitly supplied.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence

from .historical_paper_replay import HistoricalTapePoint
from .model_historical_evaluator_contract import TemporalTapePoint
from .temporal_provenance_contract import CaptureInput, ProvenanceRecord

SCHEMA_VERSION = "polymarket-temporal-provenance-adapter.v1"


@dataclass(frozen=True)
class ProvenancedTapePoint:
    tape_point: HistoricalTapePoint
    provenance: ProvenanceRecord
    source_timestamp_semantics: str

    def validate(self) -> None:
        self.tape_point.validate()
        self.provenance.validate()
        if not self.source_timestamp_semantics:
            raise ValueError("source_timestamp_semantics is required")
        if self.provenance.source_ref != self.tape_point.source_ref:
            raise ValueError("provenance source_ref must match tape point source_ref")
        if self.provenance.observed_at != self.tape_point.observed_at:
            raise ValueError("provenance observed_at must match tape point observed_at")

    def as_temporal_tape_point(self) -> TemporalTapePoint:
        self.validate()
        return TemporalTapePoint(
            market_id=self.tape_point.market_id,
            outcome_id=self.tape_point.outcome_id,
            observed_at=self.tape_point.observed_at,
            price=self.tape_point.price,
            known_as_of=self.provenance.known_as_of,
            acquired_at=self.provenance.acquired_at,
        )


def attach_provenance(
    point: HistoricalTapePoint,
    *,
    acquired_at: str,
    knowledge_basis: str,
    historical_knowledge_at: str | None = None,
    source_timestamp_semantics: str = "source_observed_at",
) -> ProvenancedTapePoint:
    provenance = CaptureInput(
        observed_at=point.observed_at,
        acquired_at=acquired_at,
        source_ref=point.source_ref,
        knowledge_basis=knowledge_basis,
        historical_knowledge_at=historical_knowledge_at,
    ).materialize()
    result = ProvenancedTapePoint(
        tape_point=point,
        provenance=provenance,
        source_timestamp_semantics=source_timestamp_semantics,
    )
    result.validate()
    return result


def attach_provenance_batch(
    points: Sequence[HistoricalTapePoint],
    *,
    acquired_at: str,
    knowledge_basis: str,
    historical_knowledge_at: str | None = None,
    source_timestamp_semantics: str = "source_observed_at",
) -> tuple[ProvenancedTapePoint, ...]:
    return tuple(
        attach_provenance(
            point,
            acquired_at=acquired_at,
            knowledge_basis=knowledge_basis,
            historical_knowledge_at=historical_knowledge_at,
            source_timestamp_semantics=source_timestamp_semantics,
        )
        for point in points
    )


__all__ = [
    "ProvenancedTapePoint",
    "attach_provenance",
    "attach_provenance_batch",
    "SCHEMA_VERSION",
]
