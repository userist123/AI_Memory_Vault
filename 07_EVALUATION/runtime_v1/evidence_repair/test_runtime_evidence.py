import json
from pathlib import Path
import pytest

root = Path(r"C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
repair_dir = root / "07_EVALUATION" / "runtime_v1" / "evidence_repair"
traces_dir = repair_dir / "traces"

@pytest.fixture(scope="module")
def cohort():
    f = repair_dir / "runtime_evidence_repair_cohort.json"
    assert f.is_file()
    return json.loads(f.read_text("utf-8"))

@pytest.fixture(scope="module")
def cases():
    f = repair_dir / "runtime_evidence_repair_cases.json"
    assert f.is_file()
    return json.loads(f.read_text("utf-8"))

@pytest.fixture(scope="module")
def ledger():
    f = repair_dir / "runtime_evidence_ledger.jsonl"
    assert f.is_file()
    return [json.loads(line) for line in open(f, "r", encoding="utf-8")]

def test_cohort_and_cases_cardinality(cohort, cases, ledger):
    assert len(cohort) == 30
    assert len(cases) == 30
    assert len(ledger) == 30
    c_sids = set(c["skill_id"] for c in cohort)
    assert len(c_sids) == 30
    assert set(c["skill_id"] for c in cases) == c_sids
    assert set(l["skill_id"] for l in ledger) == c_sids

def test_real_traces_exist(ledger):
    for r in ledger:
        b_path = root / r["baseline"]["evidence_path"]
        t_path = root / r["treatment"]["evidence_path"]
        assert b_path.is_file(), f"Missing trace {b_path}"
        assert t_path.is_file(), f"Missing trace {t_path}"
        
        b_trace = json.loads(b_path.read_text("utf-8"))
        t_trace = json.loads(t_path.read_text("utf-8"))
        
        assert b_trace["target_skill_loaded"] is False
        assert t_trace["target_skill_loaded"] is True
        assert b_trace["exit_code"] == 0
        assert t_trace["exit_code"] == 0

def test_effectiveness_logic(ledger):
    for r in ledger:
        eff = r["effective"]
        assert eff in ["TRUE", "FALSE", "UNKNOWN"]
        if eff == "TRUE":
            assert r["treatment"]["used"] is True
            assert r["delta_score"] >= 10
            assert r["treatment"]["score"] >= r["baseline"]["score"]

def test_repeatability(ledger):
    repeats = [r for r in ledger if r["repeatability"]]
    assert len(repeats) >= 10
    for r in repeats:
        rep = r["repeatability"]
        rep_path = root / rep["evidence_path"]
        assert rep_path.is_file()
        rep_trace = json.loads(rep_path.read_text("utf-8"))
        assert rep_trace["mode"] == "REPEAT"
        assert rep_trace["target_skill_loaded"] is True
        assert rep["consistent"] is True

def test_no_secrets_in_traces(ledger):
    forbidden_terms = ["bearer ", "secret_key", "password=", "BEGIN PRIVATE KEY"]
    for trace_file in traces_dir.glob("*.json"):
        text = trace_file.read_text("utf-8", errors="ignore")
        for term in forbidden_terms:
            assert term not in text, f"Forbidden secret term {term} in {trace_file.name}"
