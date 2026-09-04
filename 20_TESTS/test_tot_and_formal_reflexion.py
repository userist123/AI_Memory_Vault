import pytest
from cognitive_core.reasoning import ReasoningEngine, TreeOfThoughtReasoner, ThoughtValidator
from cognitive_core.reflection import FormalReflexion, SelfRefine, ReflectionPipeline
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal

def test_thought_validator():
    validator = ThoughtValidator()
    context = [{"content": "PostgreSQL replication lag occurs under heavy write throughput."}]

    # Good branch
    branch_good = {"thought": "Analyzing PostgreSQL replication write lag based on database throughput."}
    is_valid, score, critique = validator.validate_branch(branch_good, context)
    assert is_valid is True
    assert score >= 0.5

    # Sparse / empty branch
    branch_bad = {"thought": "ok"}
    is_valid_bad, score_bad, _ = validator.validate_branch(branch_bad, context)
    assert is_valid_bad is False
    assert score_bad == 0.0

def test_tree_of_thought_reasoner():
    tot = TreeOfThoughtReasoner()
    context = [
        {"content": "Microservice authentication token expiration causes HTTP 401 errors."},
        {"content": "Redis session cache stores active authentication tokens."}
    ]
    query = "Why are users getting HTTP 401 errors during session renewal?"
    result = tot.reason(query, context)

    assert "best_branch" in result
    assert result["branches_explored"] >= 3
    assert len(result["all_evaluated_branches"]) >= 1

def test_reasoning_engine_selective_tot():
    storage = StorageEngine()
    controller = MemoryController(storage)
    engine = ReasoningEngine(controller)

    # Simple query -> direct mode
    res_simple = engine.synthesize(Principal.AI_AGENT, [{"content": "Port 443 is HTTPS"}], "What is port 443?")
    assert res_simple["mode"] == "direct"

    # Complex query -> tree_of_thought mode
    res_complex = engine.synthesize(
        Principal.AI_AGENT,
        [{"content": "Distributed consensus deadlock observed across node quorum."}],
        "Explain root cause and evaluate how to troubleshoot distributed consensus deadlock"
    )
    assert res_complex["mode"] == "tree_of_thought"
    assert "tot_details" in res_complex

def test_formal_reflexion_structure():
    storage = StorageEngine()
    controller = MemoryController(storage)
    pipeline = ReflectionPipeline(controller)

    # Trigger error reflection
    intent = {"query": "Deploy service"}
    action = {"action": "kubectl apply"}
    result = {
        "status": "error",
        "error": "Pod CrashLoopBackOff",
        "root_cause": "OOM killed due to memory limit 256Mi",
        "fix": "Increased memory limit to 512Mi",
        "verification": "Pod successfully reached Running state",
        "prevention": "Define memory baseline sizing before deployment",
        "lesson": "JVM applications require higher memory headroom"
    }

    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is not None

    stored = storage.get(note_id)
    assert stored is not None
    assert stored["type"] == "error"
    assert stored["lifecycle"] == "REVIEW"
    assert "Formal Reflexion Analysis" in stored["content"]
    assert "OOM killed" in stored["content"]
    assert "JVM applications require higher memory headroom" in stored["content"]

def test_self_refine_pre_consolidation():
    valid_candidate = {
        "content": "Consolidated lessons regarding PostgreSQL connection pool tuning under high concurrent load."
    }
    passed, refined = SelfRefine.refine_memory(valid_candidate)
    assert passed is True
    assert refined["confidence"] == "medium"

    too_short = {"content": "short"}
    passed_short, _ = SelfRefine.refine_memory(too_short)
    assert passed_short is False
