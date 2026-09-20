"""20_TESTS/test_corpus_health_gate.py — Automated tests for CI-002 Corpus Health Gate.

Asserts the Phase 0 baseline invariants established in:
- 00_GOVERNANCE/phase_0/CORPUS_HEALTH_BASELINE.md
- 00_GOVERNANCE/VAULT_STATE.md
"""
import sys
from pathlib import Path
import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
VERIF_DIR = REPO_ROOT / "30_SCRIPTS" / "verification"
if str(VERIF_DIR) not in sys.path:
    sys.path.insert(0, str(VERIF_DIR))

from corpus_health_gate import check_corpus_health, MIN_EDGES_BASELINE, MAX_DUPLICATE_GROUPS_BASELINE



@pytest.fixture(scope="module")
def health_report():
    return check_corpus_health(REPO_ROOT)


def test_corpus_health_overall_pass(health_report):
    assert health_report["passed"] is True, f"Corpus health gate failed: {health_report['details']}"


def test_corpus_edge_count_invariant(health_report):
    metrics = health_report["metrics"]
    assert metrics["edge_count"] >= MIN_EDGES_BASELINE, (
        f"Graph edges dropped below baseline: {metrics['edge_count']} < {MIN_EDGES_BASELINE}"
    )


def test_corpus_zero_dangling_edges(health_report):
    metrics = health_report["metrics"]
    assert metrics["dangling_edge_count"] == 0, (
        f"Detected {metrics['dangling_edge_count']} dangling edges: {health_report['details']['dangling_edges']}"
    )


def test_corpus_zero_fixture_notes(health_report):
    metrics = health_report["metrics"]
    assert metrics["fixture_notes_count"] == 0, (
        f"Detected fixture notes inside the canonical index: {health_report['details']['fixture_notes']}"
    )


def test_corpus_duplicate_groups_within_baseline(health_report):
    metrics = health_report["metrics"]
    assert metrics["exact_duplicate_groups_count"] <= MAX_DUPLICATE_GROUPS_BASELINE, (
        f"Duplicate groups exceeded baseline: {metrics['exact_duplicate_groups_count']} > {MAX_DUPLICATE_GROUPS_BASELINE}"
    )


def test_corpus_active_notes_invariants(health_report):
    metrics = health_report["metrics"]
    assert metrics["active_notes_count"] >= 50, f"Active notes count unexpectedly low: {metrics['active_notes_count']}"
    assert metrics["active_missing_provenance_count"] == 0, (
        f"Active notes missing provenance: {health_report['details']['active_missing_provenance']}"
    )
    assert metrics["active_invalid_verification_count"] == 0, (
        f"Active notes with invalid verification: {health_report['details']['active_invalid_verification']}"
    )
