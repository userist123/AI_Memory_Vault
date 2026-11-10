import sys
from pathlib import Path

# Canonical location since WP-14b (the root `scripts/` copy was a migration leftover).
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "30_SCRIPTS" / "observability"))

from label_council_outcome import append_outcome  # noqa: E402


def test_smoke_append_outcome(tmp_path):
    ledger = tmp_path / "outcomes.jsonl"
    record = append_outcome("smoke", "success", ledger_path=ledger)
    assert record["outcome"] == "success"
