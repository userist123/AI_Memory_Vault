import json
from pathlib import Path
import pytest

root = Path(r"c:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY")
sem_dir = root / "07_EVALUATION" / "skills_semantic_v1"

@pytest.fixture(scope="module")
def rec():
    rec_f = sem_dir / "reconciliation.json"
    assert rec_f.is_file()
    return json.loads(rec_f.read_text("utf-8"))

@pytest.fixture(scope="module")
def gold():
    gold_f = sem_dir / "gold_standard_dual_evaluation.json"
    assert gold_f.is_file()
    return json.loads(gold_f.read_text("utf-8"))

@pytest.fixture(scope="module")
def cohorts():
    cohorts_f = sem_dir / "cohorts.json"
    assert cohorts_f.is_file()
    return json.loads(cohorts_f.read_text("utf-8"))

@pytest.fixture(scope="module")
def tasks():
    tasks_f = sem_dir / "benchmark_tasks.json"
    assert tasks_f.is_file()
    return json.loads(tasks_f.read_text("utf-8"))

@pytest.fixture(scope="module")
def ledger():
    ledger_f = sem_dir / "semantic_evaluation_ledger.jsonl"
    assert ledger_f.is_file()
    return [json.loads(l) for l in open(ledger_f, "r", encoding="utf-8")]

@pytest.fixture(scope="module")
def p0():
    p0_f = sem_dir / "security_cohort_audit.json"
    assert p0_f.is_file()
    return json.loads(p0_f.read_text("utf-8"))

def test_reconciliation_verdict(rec):
    assert rec["verdict"] == "PASS"

def test_cohort_cardinality(cohorts, rec):
    total_mems = sum(len(c["skill_ids"]) for c in cohorts)
    assert total_mems == 534
    assert rec["cohorts"]["membership_total"] == 534
    
    all_sids = []
    for c in cohorts:
        # Check no duplicates within cohort
        assert len(c["skill_ids"]) == len(set(c["skill_ids"]))
        all_sids.extend(c["skill_ids"])
        
    unique_sids = set(all_sids)
    assert len(unique_sids) == 502
    assert rec["cohorts"]["unique_skills"] == 502
    assert rec["cohorts"]["overlap_count"] == 32

def test_benchmark_task_cardinality(tasks, rec):
    assert len(tasks) == 502
    assert rec["benchmark_tasks"]["task_count"] == 502
    unique_task_sids = set(t["skill_id"] for t in tasks)
    assert len(unique_task_sids) == 502
    assert rec["benchmark_tasks"]["unique_skills"] == 502

def test_ledger_cardinality(ledger, rec):
    assert len(ledger) == 502
    assert rec["semantic_ledger"]["records"] == 502
    unique_ledger_sids = set(l["skill_id"] for l in ledger)
    assert len(unique_ledger_sids) == 502
    assert rec["semantic_ledger"]["unique_skill_ids"] == 502

def test_p0_completeness(p0, rec):
    assert len(p0) == 9
    assert rec["p0"]["required"] == 9
    assert rec["p0"]["audited"] == 9

def test_gold_standard_metrics(gold, rec):
    evals = gold["evaluations"]
    assert len(evals) == 30
    deltas = [e["score_delta"] for e in evals]
    avg_delta = round(sum(deltas) / len(deltas), 2)
    assert avg_delta == rec["gold_standard"]["average_score_delta"]
    
    agrees = sum(1 for e in evals if e["classification_agreement"])
    agree_pct = round((agrees / len(evals)) * 100.0, 1)
    assert agree_pct == rec["gold_standard"]["classification_agreement"]
    
    major_dis = sum(1 for e in evals if not e["classification_agreement"] or e["score_delta"] >= 10.0)
    assert major_dis == rec["gold_standard"]["major_disagreements"]
