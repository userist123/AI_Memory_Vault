"""Research-only batch consumer for the Phase 16 historical evaluation dataset."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Iterable, Mapping

from .historical_evaluation_dataset import (
    HistoricalEvaluationDatasetRow,
    load_historical_evaluation_dataset,
)
from .model_historical_evaluator_contract import HistoricalEvaluation

SCHEMA_VERSION = "polymarket-historical-evaluation-run.v1"


@dataclass(frozen=True)
class HistoricalEvaluationRun:
    evaluations: tuple[HistoricalEvaluation, ...]
    rows: tuple[HistoricalEvaluationDatasetRow, ...]
    source_knowledge_bases: tuple[str, ...]

    @property
    def count(self) -> int:
        return len(self.evaluations)

    def validate(self) -> None:
        if not self.rows:
            raise ValueError("historical evaluation run is empty")
        if len(self.rows) != len(self.evaluations):
            raise ValueError("evaluation count does not match dataset row count")
        expected_keys: list[tuple[str, str, str]] = []
        for row in self.rows:
            row.validate()
            expected_keys.append(
                (
                    row.prediction.market_id,
                    row.prediction.outcome_id,
                    row.prediction.known_as_of,
                )
            )
        if len(set(expected_keys)) != len(expected_keys):
            raise ValueError("duplicate historical evaluation prediction key")
        expected_bases = tuple(sorted({row.provenance.knowledge_basis for row in self.rows}))
        if self.source_knowledge_bases != expected_bases:
            raise ValueError("source knowledge base summary mismatch")


def evaluate_historical_dataset(
    records: Iterable[Mapping[str, Any]],
) -> HistoricalEvaluationRun:
    """Load, validate, and evaluate a historical dataset as one deterministic batch."""
    rows = load_historical_evaluation_dataset(records)
    evaluations = tuple(row.evaluate() for row in rows)
    source_knowledge_bases = tuple(sorted({row.provenance.knowledge_basis for row in rows}))
    run = HistoricalEvaluationRun(
        evaluations=evaluations,
        rows=rows,
        source_knowledge_bases=source_knowledge_bases,
    )
    run.validate()
    return run


__all__ = [
    "HistoricalEvaluationRun",
    "SCHEMA_VERSION",
    "evaluate_historical_dataset",
]
