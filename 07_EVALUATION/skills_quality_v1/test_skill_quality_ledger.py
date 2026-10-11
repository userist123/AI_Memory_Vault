import json
from pathlib import Path
import pytest

root = Path(r"C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
ledger_file = root / "07_EVALUATION" / "skills_quality_v1" / "skill_quality_ledger.jsonl"

@pytest.fixture(scope="module")
def ledger_records():
    assert ledger_file.is_file(), "Ledger file does not exist"
    records = []
    with open(ledger_file, "r", encoding="utf-8") as f:
        for line in f:
            records.append(json.loads(line))
    return records

def test_record_count(ledger_records):
    assert len(ledger_records) == 3450, f"Expected 3450 records, got {len(ledger_records)}"

def test_unique_skill_ids(ledger_records):
    skill_ids = [r["skill_id"] for r in ledger_records]
    assert len(set(skill_ids)) == 3450, "Duplicate skill_id found in ledger"

def test_unique_paths(ledger_records):
    paths = [r["path"] for r in ledger_records]
    assert len(set(paths)) == 3450, "Duplicate path found in ledger"

def test_required_fields(ledger_records):
    required = ["skill_id", "skill_name", "path", "source_id", "content_hash",
                "structural_status", "documentation_score", "execution_readiness",
                "dependency_count", "security_flag_count", "security_risk",
                "duplication_signal", "static_utility_score", "maintainability_risk",
                "evaluation_priority", "evidence"]
    for r in ledger_records:
        for req in required:
            assert req in r, f"Missing required field '{req}' in record {r.get('skill_id')}"

def test_score_bounds(ledger_records):
    for r in ledger_records:
        assert 0 <= r["documentation_score"] <= 100, f"Invalid doc score {r['documentation_score']}"
        assert 0 <= r["static_utility_score"] <= 100, f"Invalid utility score {r['static_utility_score']}"

def test_enum_values(ledger_records):
    allowed_struct = {"VALID", "MINIMAL", "INCOMPLETE", "BROKEN"}
    allowed_readiness = {"DIRECT", "TOOL_DEPENDENT", "ENVIRONMENT_DEPENDENT", "REFERENCE_ONLY", "UNCLEAR"}
    allowed_sec = {"NONE", "LOW", "MEDIUM", "HIGH", "CRITICAL"}
    allowed_dup = {"NONE", "EXACT_DUPLICATE", "NEAR_DUPLICATE", "HIGH_OVERLAP", "SEMANTIC_OVERLAP"}
    allowed_maint = {"LOW", "MEDIUM", "HIGH", "UNKNOWN"}
    allowed_prio = {"P0_CRITICAL_REVIEW", "P1_HIGH_VALUE_REVIEW", "P2_STANDARD_REVIEW", "P3_LOW_PRIORITY"}
    
    for r in ledger_records:
        assert r["structural_status"] in allowed_struct
        assert r["execution_readiness"] in allowed_readiness
        assert r["security_risk"] in allowed_sec
        assert r["duplication_signal"] in allowed_dup
        assert r["maintainability_risk"] in allowed_maint
        assert r["evaluation_priority"] in allowed_prio
