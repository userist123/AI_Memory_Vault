import pytest

from packages.observability.graph_diagnostics import (
    GraphDiagnosticsProbe,
    GraphExecutionStatus,
)


class _Storage:
    def __init__(self, notes):
        self._notes = notes

    def all_notes(self):
        return list(self._notes)


class _Controller:
    def __init__(self, results, notes=None, fail=False):
        self.storage = _Storage(notes or [])
        self._results = results
        self._fail = fail

    def search(self, principal, query, page_size=10):
        if self._fail:
            raise RuntimeError("synthetic search failure")
        return {"results": list(self._results)}


def test_probe_reports_graph_execution_and_preserves_scores():
    results = [
        {"id": "a", "relevance_score": 0.9},
        {"id": "b", "relevance_score": 0.4},
    ]
    notes = [
        {"id": "a", "category": "memory", "tags": ["shared"]},
        {"id": "b", "category": "memory", "tags": ["shared"]},
    ]
    report = GraphDiagnosticsProbe.probe_ranked_search(
        _Controller(results, notes), "AI_AGENT", "memory"
    )
    assert report.status in {
        GraphExecutionStatus.APPLIED,
        GraphExecutionStatus.FALLBACK_NO_GRAPH_CHANGES,
    }
    assert report.base_count == 2
    assert report.ranked_count == 2
    assert report.relevance_scores_survived is True
    assert report.exception_type is None
    assert report.duration_ms >= 0


def test_probe_surfaces_base_search_failure():
    report = GraphDiagnosticsProbe.probe_ranked_search(
        _Controller([], fail=True), "AI_AGENT", "memory"
    )
    assert report.status == GraphExecutionStatus.FALLBACK_NO_RESULTS
    assert report.exception_type == "RuntimeError"


def test_candidate_rejection_attribution_is_structured():
    results = [{"id": "active", "relevance_score": 0.8}]
    notes = [
        {"id": "active", "lifecycle": "ACTIVE"},
        {"id": "raw", "lifecycle": "RAW", "source_type": "ingest"},
        {"id": "old", "lifecycle": "ARCHIVED", "source_type": "archive"},
    ]
    report = GraphDiagnosticsProbe.probe_candidate_rejection(
        _Controller(results), "AI_AGENT", "memory", notes
    )
    assert report.total_scanned == 3
    assert report.rejected_count == 2
    assert report.counts_by_reason["LIFECYCLE_RAW_EXCLUDED"] == 1
    assert report.counts_by_reason["SUPERSEDED_INACTIVE"] == 1
