import json
from pathlib import Path
import pytest

root = Path(r"C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
sem_dir = root / "07_EVALUATION" / "skills_semantic_v1"
ledger_f = sem_dir / "semantic_evaluation_ledger.jsonl"
cohorts_f = sem_dir / "cohorts.json"
tasks_f = sem_dir / "benchmark_tasks.json"
sec_f = sem_dir / "security_cohort_audit.json"
gold_f = sem_dir / "gold_standard_dual_evaluation.json"

@pytest.fixture(scope="module")
def ledger_records():
    assert ledger_f.is_file()
    return [json.loads(line) for line in open(ledger_f, "r", encoding="utf-8")]

@pytest.fixture(scope="module")
def cohorts_data():
    assert cohorts_f.is_file()
    return json.loads(cohorts_f.read_text("utf-8"))

def test_ledger_record_count(ledger_records):
    assert len(ledger_records) == 502

def test_unique_skill_ids(ledger_records):
    sids = [r["skill_id"] for r in ledger_records]
    assert len(set(sids)) == len(ledger_records)

def test_score_bounds(ledger_records):
    for r in ledger_records:
        scores = r["semantic_scores"]
        for dim, val in scores.items():
            if dim == "semantic_score":
                assert 0.0 <= val <= 100.0, f"Invalid semantic score {val}"
            else:
                assert 0.0 <= val <= 10.0, f"Invalid {dim} score {val}"

def test_allowed_enums(ledger_records):
    allowed_classes = {"CORE", "SPECIALIZED", "COMPLEMENTARY", "REDUNDANT", "GENERIC", 
                        "REFERENCE", "EXPERIMENTAL", "LOW_VALUE", "UNSAFE_TO_USE", "UNCLEAR"}
    allowed_conf = {"HIGH", "MEDIUM", "LOW"}
    allowed_red = {"IDENTICAL_FUNCTION", "SUBSTANTIAL_OVERLAP", "COMPLEMENTARY", 
                    "SPECIALIZED_VARIANT", "SAME_NAME_DIFFERENT_FUNCTION", "NO_MEANINGFUL_OVERLAP", "UNCLEAR"}
    for r in ledger_records:
        assert r["semantic_class"] in allowed_classes
        assert r["confidence"] in allowed_conf
        assert r["redundancy_relation"] in allowed_red

def test_all_p0_skills_included(ledger_records, cohorts_data):
    p0_cohort = [c for c in cohorts_data if c["cohort_id"] == "p0_security"][0]
    eval_sids = set(r["skill_id"] for r in ledger_records)
    for sid in p0_cohort["skill_ids"]:
        assert sid in eval_sids

def test_benchmark_tasks_match(ledger_records):
    tasks = json.loads(tasks_f.read_text("utf-8"))
    assert len(tasks) == len(ledger_records)
    task_sids = set(t["skill_id"] for t in tasks)
    eval_sids = set(r["skill_id"] for r in ledger_records)
    assert task_sids == eval_sids

def test_fixed_seed_reproducibility(cohorts_data):
    for c in cohorts_data:
        assert c["selection_seed"] == 20260903
        assert len(c["skill_ids"]) == len(set(c["skill_ids"]))
