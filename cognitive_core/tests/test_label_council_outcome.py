from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

from scripts.label_council_outcome import append_outcome


def test_append_outcome_writes_structured_manual_record(tmp_path: Path) -> None:
    ledger = tmp_path / "outcomes.jsonl"

    record = append_outcome(
        "run_0042",
        "failure",
        reason="Verifier rejected the generated artifact.",
        evidence=["verifier:rejected", "human_review:confirmed"],
        source="human",
        confidence=1.0,
        ledger_path=ledger,
    )

    assert record["run_id"] == "run_0042"
    assert record["outcome"] == "failure"
    assert record["label_source"] == "human"
    assert record["confidence"] == 1.0
    assert record["evidence"] == ["verifier:rejected", "human_review:confirmed"]
    assert ledger.exists()

    stored = json.loads(ledger.read_text(encoding="utf-8").splitlines()[0])
    assert stored == record


def test_append_outcome_is_append_only(tmp_path: Path) -> None:
    ledger = tmp_path / "outcomes.jsonl"

    append_outcome("run_1", "success", source="automatic", ledger_path=ledger)
    append_outcome("run_1", "failure", source="human", reason="manual correction", ledger_path=ledger)

    lines = ledger.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2
    assert json.loads(lines[0])["outcome"] == "success"
    assert json.loads(lines[1])["outcome"] == "failure"


def test_all_supported_outcomes_are_accepted(tmp_path: Path) -> None:
    ledger = tmp_path / "outcomes.jsonl"

    for index, outcome in enumerate(("success", "partial", "failure"), start=1):
        append_outcome(f"run_{index}", outcome, ledger_path=ledger)

    records = [json.loads(line) for line in ledger.read_text(encoding="utf-8").splitlines()]
    assert [item["outcome"] for item in records] == ["success", "partial", "failure"]


def test_invalid_outcome_is_rejected(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="outcome must be one of"):
        append_outcome("run_1", "unknown", ledger_path=tmp_path / "outcomes.jsonl")


def test_invalid_source_is_rejected(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="source must be one of"):
        append_outcome("run_1", "success", source="llm", ledger_path=tmp_path / "outcomes.jsonl")


def test_invalid_confidence_is_rejected(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="confidence must be between 0 and 1"):
        append_outcome("run_1", "success", confidence=1.1, ledger_path=tmp_path / "outcomes.jsonl")


def test_cli_writes_manual_label_and_json_output(tmp_path: Path) -> None:
    ledger = tmp_path / "outcomes.jsonl"

    completed = subprocess.run(
        [
            sys.executable,
            "scripts/label_council_outcome.py",
            "--run-id",
            "run_cli_1",
            "--outcome",
            "partial",
            "--reason",
            "Human marked the result partial.",
            "--evidence",
            "human_review:confirmed",
            "--source",
            "human",
            "--confidence",
            "0.95",
            "--ledger",
            str(ledger),
        ],
        cwd=Path(__file__).resolve().parents[2],
        text=True,
        capture_output=True,
        check=False,
    )

    assert completed.returncode == 0, completed.stderr
    output = json.loads(completed.stdout)
    assert output["run_id"] == "run_cli_1"
    assert output["outcome"] == "partial"
    assert output["label_source"] == "human"
    assert output["confidence"] == 0.95
    assert json.loads(ledger.read_text(encoding="utf-8").splitlines()[0]) == output


def test_cli_invalid_outcome_fails(tmp_path: Path) -> None:
    ledger = tmp_path / "outcomes.jsonl"

    completed = subprocess.run(
        [
            sys.executable,
            "scripts/label_council_outcome.py",
            "--run-id",
            "run_cli_2",
            "--outcome",
            "unknown",
            "--ledger",
            str(ledger),
        ],
        cwd=Path(__file__).resolve().parents[2],
        text=True,
        capture_output=True,
        check=False,
    )

    assert completed.returncode != 0
    assert not ledger.exists()


def test_cli_does_not_touch_proposal_queue(tmp_path: Path) -> None:
    ledger = tmp_path / "outcomes.jsonl"

    append_outcome("run_queue_1", "success", ledger_path=ledger)

    # The outcome CLI has no proposal-queue output path and only creates its
    # explicitly supplied ledger file.
    created_paths = {path for path in tmp_path.rglob("*")}
    assert ledger in created_paths
    assert all("proposal" not in path.name.lower() for path in created_paths)
