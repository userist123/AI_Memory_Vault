import json
from pathlib import Path

from importlib.util import spec_from_file_location, module_from_spec

BASE = Path(__file__).parents[1] / "07_EVALUATION" / "golden_memory_effectiveness_v1"
SPEC = spec_from_file_location("trace_validator", BASE / "trace_validator.py")
MODULE = module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def test_exactly_ten_machine_verifiable_tasks():
    tasks = json.loads((BASE / "golden_tasks.json").read_text())
    assert len(tasks) == 10
    required = {"task_id", "category", "prompt", "environment", "success_criteria", "verification_command", "verification_type", "expected_files", "expected_behavior", "timeout"}
    assert all(required <= set(task) for task in tasks)


def test_real_matrix_has_isolated_conditions_and_ninety_runs():
    rows = [json.loads(line) for line in (BASE / "execution_results.jsonl").read_text().splitlines()]
    assert len(rows) == 90
    assert {row["condition"] for row in rows} == {"CONTROL", "TREATMENT", "FULL_CONTEXT_ORACLE"}
    assert all(row["retrieval_count"] == 0 and not row["memory_ids"] for row in rows if row["condition"] == "CONTROL")
    assert any(row["retrieval_count"] > 1 for row in rows if row["condition"] == "TREATMENT")


def test_all_new_traces_pass_validator():
    trace_dir = BASE / "runs_v2" / "traces"
    traces = sorted(trace_dir.glob("*.benchmark.json"))
    assert len(traces) == 90
    assert all(not MODULE.validate(path) for path in traces)


def test_summary_has_observed_not_claimed_counts():
    summary = json.loads((BASE / "effectiveness_summary.json").read_text())
    assert summary["executions"] == 90
    assert summary["control_success"] == 19
    assert summary["treatment_success"] == 19
    assert summary["oracle_success"] == 18
    assert summary["invalid_runs"] == 0


def test_unrun_safety_suites_are_not_presented_as_passed():
    for name in ("memory_poisoning_results.json", "harmful_memory_results.json", "current_ablation_results.json"):
        result = json.loads((BASE / name).read_text())
        assert result["status"] == "NOT_RUN"
