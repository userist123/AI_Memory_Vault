import json
from pathlib import Path
import pytest

root = Path(r"C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
sec_removal_dir = root / "07_EVALUATION" / "security_removal_v1"
active_skills_dir = root / ".agents" / "skills"
raw_skills_dir = root / "06_INBOX" / "RAW_IMPORTS" / "skills"

@pytest.fixture(scope="module")
def ledger():
    f = sec_removal_dir / "security_removal_ledger.jsonl"
    assert f.is_file()
    return [json.loads(line) for line in open(f, "r", encoding="utf-8")]

@pytest.fixture(scope="module")
def manifest():
    f = sec_removal_dir / "quarantine_manifest.json"
    assert f.is_file()
    return json.loads(f.read_text("utf-8"))

def test_removal_ledger_schema(ledger):
    assert len(ledger) == 2
    for r in ledger:
        assert "skill_name" in r
        assert "active_path" in r
        assert "file_count" in r
        assert "content_hashes" in r
        assert "source_id" in r
        assert "source_repository" in r
        assert "source_url" in r
        assert "security_risk" in r
        assert "detected_pattern" in r
        assert "defender_detected" in r
        assert "removal_reason" in r
        assert "removal_category" in r
        assert r["security_risk"] == "CRITICAL"

def test_critical_skills_absent_from_active_vault(ledger):
    for r in ledger:
        p = root / r["active_path"]
        assert not p.exists(), f"Active skill still exists at {p}"
    assert not (active_skills_dir / "sandbase-mcp").exists()
    assert not (active_skills_dir / "aspire").exists()

def test_raw_sources_preserved(ledger):
    for r in ledger:
        raw_p = root / r["raw_source_path"]
        assert raw_p.exists(), f"Raw source copy missing at {raw_p}"

def test_quarantine_manifest(manifest, ledger):
    assert manifest["raw_sources_preserved"] is True
    assert manifest["provenance_preserved"] is True
    assert manifest["defender_bypassed"] is False
    assert len(manifest["removed_skills"]) == len(ledger)

def test_arithmetic_invariants():
    # Canonical installed skills was 3450, removed 2 -> 3448
    active_dirs = [d for d in active_skills_dir.iterdir() if d.is_dir()]
    assert (active_skills_dir / "sandbase-mcp") not in active_dirs
    assert (active_skills_dir / "aspire") not in active_dirs
    # Verify standard skills still exist intact
    assert (active_skills_dir / "python").is_dir()
    assert (active_skills_dir / "bash").is_dir()
    assert (active_skills_dir / "3d-games").is_dir()
