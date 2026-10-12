import json
from pathlib import Path
import pytest

root = Path(r"C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
runtime_dir = root / "07_EVALUATION" / "runtime_v1"
quality_dir = root / "07_EVALUATION" / "skills_quality_v1"

@pytest.fixture(scope="module")
def ledger_records():
    ledger_f = runtime_dir / "runtime_evaluation_ledger.jsonl"
    assert ledger_f.is_file()
    return [json.loads(line) for line in open(ledger_f, "r", encoding="utf-8")]

@pytest.fixture(scope="module")
def cohort_data():
    cohort_f = runtime_dir / "runtime_cohort.json"
    assert cohort_f.is_file()
    return json.loads(cohort_f.read_text("utf-8"))

@pytest.fixture(scope="module")
def cases_data():
    cases_f = runtime_dir / "runtime_cases.json"
    assert cases_f.is_file()
    return json.loads(cases_f.read_text("utf-8"))

@pytest.fixture(scope="module")
def sec_audit():
    sec_f = runtime_dir / "runtime_security_audit.json"
    assert sec_f.is_file()
    return json.loads(sec_f.read_text("utf-8"))

def test_cohort_size(cohort_data):
    assert len(cohort_data) == 100
    sids = [c["skill_id"] for c in cohort_data]
    assert len(set(sids)) == 100

def test_ledger_record_count(ledger_records):
    assert len(ledger_records) == 100
    case_ids = [r["runtime_case_id"] for r in ledger_records]
    assert len(set(case_ids)) == 100

def test_no_p0_security_executed(cohort_data):
    # Load quality ledger to check security risk
    q_ledger = {json.loads(l)["skill_id"]: json.loads(l) for l in open(quality_dir / "skill_quality_ledger.jsonl", "r", encoding="utf-8")}
    for c in cohort_data:
        sid = c["skill_id"]
        assert q_ledger[sid]["security_risk"] not in ["HIGH", "CRITICAL"], f"P0 skill {sid} found in runtime cohort!"

def test_baseline_treatment_pairs(ledger_records):
    for r in ledger_records:
        assert "baseline" in r
        assert "treatment" in r
        assert r["baseline"]["target_skill_loaded"] is False
        assert r["treatment"]["target_skill_loaded"] is True
        assert 0 <= r["baseline"]["score"] <= 100
        assert 0 <= r["treatment"]["score"] <= 100
        assert r["delta_score"] == r["treatment"]["score"] - r["baseline"]["score"]

def test_effectiveness_rules(ledger_records):
    for r in ledger_records:
        eff = r["effective"]
        assert eff in ["TRUE", "FALSE", "UNKNOWN"]
        if eff == "TRUE":
            # EFFECTIVE cannot be TRUE unless USED is TRUE
            assert r["treatment"]["used"] is True, f"Case {r['runtime_case_id']}: EFFECTIVE TRUE without USED TRUE"
            # EFFECTIVE requires treatment success
            assert r["treatment"]["success"] is True
            # EFFECTIVE requires delta_score >= 10
            assert r["delta_score"] >= 10

def test_repeatability_runs(ledger_records):
    repeat_cases = [r for r in ledger_records if r["repeatability"]]
    assert len(repeat_cases) >= 30
    for r in repeat_cases:
        rep = r["repeatability"]
        assert "repeat_score" in rep
        assert "repeat_success" in rep
        assert "repeat_consistent" in rep
        score_diff = abs(r["treatment"]["score"] - rep["repeat_score"])
        outcome_match = (r["treatment"]["outcome_status"] == rep["repeat_outcome_status"])
        expected_consistent = (score_diff <= 10 and outcome_match)
        assert rep["repeat_consistent"] == expected_consistent

def test_secrets_redacted(ledger_records):
    for r in ledger_records:
        assert r["verification"]["secrets_redacted"] is True

def test_cases_mapping(cases_data, ledger_records):
    assert len(cases_data) == 100
    c_map = {c["runtime_case_id"]: c["skill_id"] for c in cases_data}
    l_map = {l["runtime_case_id"]: l["skill_id"] for l in ledger_records}
    assert c_map == l_map
