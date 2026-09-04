"""Deterministic guards for malformed generated citation artifacts."""

from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[1]


def test_no_concatenated_filecite_turn_artifacts() -> None:
    """Reject the known malformed `fileciteturn` generator artifact."""
    result = subprocess.run(
        ["git", "grep", "-I", "-n", "-i", "-E", r"filecite\s*turn|fileciteturn", "--",
         ":!06_INBOX/RAW_IMPORTS/**"],
        cwd=ROOT, text=True, capture_output=True, check=False,
    )
    findings = result.stdout.splitlines()
    assert not findings, f"malformed filecite/turn artifact(s): {findings}"
