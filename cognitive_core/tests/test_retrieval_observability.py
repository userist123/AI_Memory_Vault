import os
from pathlib import Path
import pytest
from memory_controller.controller import MemoryController
from memory_controller.storage import FileStorageEngine
from memory_controller.authorizer import Principal
from cognitive_core.observability.retrieval_tracer import RetrievalTracer, RetrievalTrace
from cognitive_core.observability.ab_comparison_engine import ABComparisonEngine
from cognitive_core.observability.memory_outcome_tracer import MemoryOutcomeTracer, MemoryUtilityTier

BASE_DIR = Path(__file__).resolve().parent.parent.parent

def test_retrieval_tracer_14_stages():
    storage = FileStorageEngine(vault_root=str(BASE_DIR))
    controller = MemoryController(storage=storage)
    tracer = RetrievalTracer()
    
    trace = tracer.trace(
        query="Prompting retrieval and fine-tuning adaptation choices",
        controller=controller,
        principal=Principal.AI_AGENT,
        abstention_threshold=0.20
    )
    
    assert isinstance(trace, RetrievalTrace)
    assert len(trace.stages) == 8 # 8 major stage groupings covering the 14 micro-steps
    stage_names = [s.stage_name for s in trace.stages]
    assert "QUERY" in stage_names
    assert "SANITIZE" in stage_names
    assert "CLASSIFY" in stage_names
    assert "CANDIDATES" in stage_names
    assert "FINAL_RANK" in stage_names
    assert "ABSTENTION" in stage_names
    assert "FINAL_CONTEXT" in stage_names
    assert trace.context_sha256 != ""
    assert trace.total_latency_ms > 0

def test_ab_comparison_base_vs_activation():
    engine = ABComparisonEngine()
    notes = [
        {"id": "A", "content": "optimization of hyperparameters"},
        {"id": "B", "content": "neural architecture search"},
        {"id": "C", "content": "data cleaning and ingestion"}
    ]
    # Prime note C with heavy access history
    histories = {"C": 1.0, "A": 0.1, "B": 0.1}
    res = engine.compare_base_vs_activation("data and neural optimization", notes, histories)
    
    assert res.sample_size == 3
    assert -1.0 <= res.kendall_tau <= 1.0
    assert -1.0 <= res.spearman_rho <= 1.0
    # Note C should experience a positive rank delta (moved up)
    c_item = next(it for it in res.items if it.note_id == "C")
    assert c_item.activation_boost == 1.0

def test_lifecycle_degradation_evaluation():
    engine = ABComparisonEngine()
    scores = engine.evaluate_lifecycle_degradation("query", "some detailed engineering note content")
    assert scores["ACTIVE"] == pytest.approx(scores["REVIEW"])
    assert scores["SUPERSEDED"] < scores["ACTIVE"]
    assert scores["ARCHIVED"] < scores["SUPERSEDED"]
    assert scores["ARCHIVED"] == pytest.approx(scores["ACTIVE"] * 0.1, rel=1e-2)

def test_memory_outcome_tracer_structure():
    mock_trace = {
        "trace_id": "test_123",
        "task_id": "task_abc",
        "memory": {
            "retrieved_memory_ids": ["M-ADAPT-001"],
            "bounded_context_text": "Prompting fine-tuning levers"
        },
        "model": {
            "response_text": "We will use prompting and fine-tuning levers."
        },
        "actions": {
            "parsed_actions": [
                {"action": "write_file", "args": {"content": "# Using prompting levers"}}
            ]
        },
        "verification": {
            "outcome": "PASSED",
            "returncode": 0
        }
    }
    tracer = MemoryOutcomeTracer()
    linkages = tracer.analyze_trace(mock_trace)
    assert len(linkages) == 1
    assert linkages[0].utility_tier == MemoryUtilityTier.RETRIEVED_AND_FUNCTIONAL
    assert linkages[0].task_success is True

def test_retrieval_benchmark_evaluator():
    from cognitive_core.observability.benchmark_evaluator import RetrievalBenchmarkEvaluator, QueryBenchmarkCase
    storage = FileStorageEngine(vault_root=str(BASE_DIR))
    controller = MemoryController(storage=storage)
    evaluator = RetrievalBenchmarkEvaluator()

    test_cases = [
        QueryBenchmarkCase("test_1", "Architecture decision record", "exact", ["spec-mcp-server-0001"], should_abstain=False),
        QueryBenchmarkCase("test_2", "Random unrelated query 12345", "unrelated", [], should_abstain=True)
    ]
    summary = evaluator.evaluate_suite(test_cases, controller, principal=Principal.AI_AGENT)
    assert summary.total_queries == 2
    assert "exact" in summary.archetype_breakdown
    assert "unrelated" in summary.archetype_breakdown
    assert len(summary.results) == 2

