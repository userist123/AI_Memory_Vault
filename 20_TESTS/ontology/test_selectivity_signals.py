"""
test_selectivity_signals.py — Verification suite and negative controls for concept selectivity (Part A).

Verifies the evaluation artifact 07_EVALUATION/book_corpus_conversion/selectivity_evaluation_v1.json:
- 6 diverse books evaluated (Schacter, Squire, Ashby, Soar, Newell, Burniske)
- All 6 books carry independent author labels (in_index, in_headings, defined)
- All 6 signals evaluated per concept (occ, spread, span, early_def, heading_hit, co_deg)
- 100% rejection of experimental furniture list (validation set, ReLU units, etc.)
- Negative controls: planted furniture acceptance causes test failure
- Honest reporting of baseline comparison and negative result under Part A.4
"""
from __future__ import annotations

import copy
import json
import pathlib
import pytest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
EVAL_PATH = REPO_ROOT / "07_EVALUATION" / "book_corpus_conversion" / "selectivity_evaluation_v1.json"

FURNITURE_LIST = [
    "validation set",
    "ReLU units",
    "backbone",
    "hyperparameters",
    "number of training epochs",
    "Rot-MNIST",
    "S-TinyImageNet",
]


@pytest.fixture
def eval_data() -> dict:
    assert EVAL_PATH.exists(), f"Evaluation artifact missing: {EVAL_PATH}"
    data = json.loads(EVAL_PATH.read_text(encoding="utf-8"))
    return data


def test_selectivity_evaluation_artifact_structure(eval_data):
    """Artifact carries required metadata, criteria summaries, and conclusion."""
    assert eval_data["metadata"]["schema_version"] == "selectivity-evaluation.v1"
    assert eval_data["metadata"]["books_evaluated_count"] >= 6
    assert eval_data["metadata"]["all_books_carry_index"] is True
    assert eval_data["metadata"]["all_books_carry_headings"] is True

    # 6 books verified
    expected_books = {"schacter_1994", "squire_kandel", "ashby_design", "laird_soar", "newell_utc", "burniske_crypto"}
    actual_books = set(eval_data["books"].keys())
    assert expected_books.issubset(actual_books)


def test_independent_author_labels_present(eval_data):
    """Every book has author ground truth extracted from index and headings."""
    for b_id, b_info in eval_data["books"].items():
        counts = b_info["author_labels_count"]
        assert counts["in_index_entries"] > 50, f"Book {b_id} has insufficient index entries: {counts['in_index_entries']}"
        assert counts["headings"] > 0, f"Book {b_id} has 0 headings"


def test_six_signals_calculated_per_concept(eval_data):
    """Every candidate concept carries the 6 operational signals."""
    required_signals = {"occ", "spread", "span", "early_def", "heading_hit", "co_deg"}
    for b_id, b_info in eval_data["books"].items():
        for c, stats in b_info["concepts"].items():
            for sig in required_signals:
                assert sig in stats, f"Concept '{c}' in {b_id} missing signal '{sig}'"
            assert 0.0 <= stats["spread"] <= 1.0
            assert 0.0 <= stats["span"] <= 1.0
            assert isinstance(stats["early_def"], bool)
            assert isinstance(stats["heading_hit"], bool)
            assert isinstance(stats["co_deg"], int)


def test_furniture_negative_control_100_percent_rejected(eval_data):
    """100% of the known experimental furniture list must be rejected."""
    ctrl = eval_data["furniture_negative_control"]
    assert ctrl["all_rejected"] is True
    assert ctrl["total_items"] == len(FURNITURE_LIST)

    for term in FURNITURE_LIST:
        assert term in ctrl["items"]
        assert ctrl["items"][term]["rejected"] is True
        assert ctrl["items"][term]["status"] == "REJECTED_AS_EXPECTED"


def test_baseline_comparison_honest_negative_result(eval_data):
    """Honest reporting per A.4: no signal or composite beats occ >= 3 across all 6 books."""
    summary = eval_data["criteria_summary"]
    baseline = summary["baseline_occ3"]

    # Baseline occ >= 3 performance
    assert baseline["min_precision"] < 80.0, "Burniske outgroup brings min precision below 80%"
    assert baseline["per_book"]["burniske_crypto"]["precision"] < 60.0

    # No tested criterion resolves selectivity across all 6 books
    assert eval_data["conclusion"]["selectivity_resolved_by_new_signals"] is False
    assert eval_data["conclusion"]["best_existing_signal"] == "occ >= 3"


# --- Negative Controls (A.5) ---

def _verify_furniture_rejection(furniture_report: dict):
    """Validator function that must catch any accepted furniture."""
    if not furniture_report.get("all_rejected"):
        raise ValueError("Furniture leakage detected: not all items were rejected!")
    for term, details in furniture_report.get("items", {}).items():
        if not details.get("rejected"):
            raise ValueError(f"Furniture leakage detected for term: {term}")
    return True


def test_negative_control_planted_furniture_leakage(eval_data):
    """Negative control: If a furniture item is marked accepted, verification MUST raise."""
    tampered_ctrl = copy.deepcopy(eval_data["furniture_negative_control"])
    assert _verify_furniture_rejection(tampered_ctrl) is True

    # Plant violation: accept 'ReLU units'
    tampered_ctrl["all_rejected"] = False
    tampered_ctrl["items"]["ReLU units"]["rejected"] = False
    tampered_ctrl["items"]["ReLU units"]["status"] = "ACCEPTED_ERROR"

    with pytest.raises(ValueError, match=r"Furniture leakage detected"):
        _verify_furniture_rejection(tampered_ctrl)
