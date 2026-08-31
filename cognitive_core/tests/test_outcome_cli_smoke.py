from scripts.label_council_outcome import append_outcome


def test_smoke_append_outcome(tmp_path):
    ledger = tmp_path / "outcomes.jsonl"
    record = append_outcome("smoke", "success", ledger_path=ledger)
    assert record["outcome"] == "success"
