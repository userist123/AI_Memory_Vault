import json
import subprocess
import hashlib
from pathlib import Path
import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
STARTING_COMMIT = "feeaee697994c1f9a9cbdd4e8143a94a204a8245"
CLEANUP_DIR = ROOT / "07_EVALUATION" / "security_cleanup_v1"


def test_audit_artifacts_exist():
    """Verify that all required audit artifacts for Defender cleanup exist."""
    detections_file = CLEANUP_DIR / "defender_detections.json"
    ledger_file = CLEANUP_DIR / "defender_removal_ledger.jsonl"
    report_file = ROOT / "07_EVALUATION" / "reports" / "defender_cleanup_v1_2026-09.md"

    assert detections_file.exists(), "defender_detections.json must exist"
    assert ledger_file.exists(), "defender_removal_ledger.jsonl must exist"
    assert report_file.exists(), "defender_cleanup_v1_2026-09.md report must exist"


def test_every_removed_path_has_sha256_and_defender_evidence():
    """Verify that every entry in defender_removal_ledger.jsonl has valid sha256 and defender evidence."""
    ledger_file = CLEANUP_DIR / "defender_removal_ledger.jsonl"
    records = [json.loads(line) for line in ledger_file.read_text("utf-8").splitlines() if line.strip()]

    assert len(records) > 0, "Removal ledger must contain removed records"

    for r in records:
        assert r.get("path"), "Path must be specified"
        assert len(r.get("sha256", "")) == 64, f"SHA-256 must be 64-char hex string: {r}"
        assert r.get("detection_name") in ["Trojan:Script/Wacatac.B!ml", "Trojan:Script/Wacatac.H!ml"], f"Invalid detection name: {r}"
        assert r.get("threat_id") in ["2147735503", "2147814524"], f"Invalid threat ID: {r}"
        assert r.get("reason") == "CONFIRMED_WINDOWS_DEFENDER_DETECTION"
        assert r.get("decision") == "REMOVE"


def test_every_removed_path_existed_before_deletion():
    """Verify that every removed path existed in the starting commit before deletion."""
    ledger_file = CLEANUP_DIR / "defender_removal_ledger.jsonl"
    records = [json.loads(line) for line in ledger_file.read_text("utf-8").splitlines() if line.strip()]

    for r in records:
        rel_path = r["path"]
        proc = subprocess.run(
            ["git", "cat-file", "-e", f"{STARTING_COMMIT}:{rel_path}"],
            cwd=str(ROOT),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        assert proc.returncode == 0, f"File {rel_path} did not exist in starting commit {STARTING_COMMIT}"


def test_every_removed_path_is_deleted_from_disk_and_git():
    """Verify that every confirmed removed path is absent on disk."""
    ledger_file = CLEANUP_DIR / "defender_removal_ledger.jsonl"
    records = [json.loads(line) for line in ledger_file.read_text("utf-8").splitlines() if line.strip()]

    for r in records:
        target_path = ROOT / r["path"]
        assert not target_path.exists(), f"Path {r['path']} still exists on disk!"


def test_no_unconfirmed_file_was_removed():
    """Verify that only the 6 confirmed files in 06_INBOX/RAW_IMPORTS/ were removed."""
    proc = subprocess.run(
        ["git", "-c", "core.quotepath=false", "diff", "--name-only", "--diff-filter=D", "HEAD", "--", "06_INBOX/RAW_IMPORTS/"],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    # Check staged or working tree deleted files
    proc_staged = subprocess.run(
        ["git", "-c", "core.quotepath=false", "diff", "--name-only", "--cached", "--diff-filter=D", "HEAD", "--", "06_INBOX/RAW_IMPORTS/"],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    deleted_files = set(proc.stdout.splitlines() + proc_staged.stdout.splitlines())
    ledger_file = CLEANUP_DIR / "defender_removal_ledger.jsonl"
    expected_paths = set(json.loads(line)["path"] for line in ledger_file.read_text("utf-8").splitlines() if line.strip())

    assert deleted_files == expected_paths, f"Mismatch in deleted files: {deleted_files ^ expected_paths}"


def test_critical_skills_remain_absent():
    """Verify sandbase-mcp and aspire remain absent from .agents/skills/."""
    sandbase = ROOT / ".agents" / "skills" / "sandbase-mcp"
    aspire = ROOT / ".agents" / "skills" / "aspire"

    assert not sandbase.exists(), "sandbase-mcp must not exist in active skills"
    assert not aspire.exists(), "aspire must not exist in active skills"


def test_no_defender_exclusion_created():
    """Verify realtime protection remains active and no exclusions created."""
    out = subprocess.check_output(
        ["powershell", "-Command", "(Get-MpPreference).DisableRealtimeMonitoring"],
        text=True,
    )
    assert out.strip() == "False", "Realtime monitoring must NOT be disabled"
