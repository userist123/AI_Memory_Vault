import pytest
import uuid
from cognitive_core.evaluation import RetrievalEvaluator
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.recall import RecallEngine
from cognitive_core.working_memory import WorkingMemory
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal

def test_trace_metrics_computation():
    semantic = DeterministicSemanticProvider()
    evaluator = RetrievalEvaluator(semantic)

    retrieved = [
        {"id": "doc-1", "content": "PostgreSQL relational database connection pool configuration guidelines"},
        {"id": "doc-2", "content": "FastAPI async endpoints best practices"}
    ]
    response = "You should configure the PostgreSQL relational database connection pool for optimal performance."

    # Utilization
    util = evaluator.utilization(retrieved, response)
    assert util > 0.0

    # Relevance
    rel = evaluator.relevance(retrieved, "PostgreSQL database")
    assert rel > 0.0

    # Adherence
    adh = evaluator.adherence(response, retrieved)
    assert adh > 0.0

    # Completeness
    comp = evaluator.completeness(retrieved, ["doc-1"])
    assert comp == 1.0
    comp_partial = evaluator.completeness(retrieved, ["doc-1", "doc-3"])
    assert comp_partial == 0.5

def test_ir_metrics_precision_recall_mrr_ndcg():
    evaluator = RetrievalEvaluator()

    retrieved = ["doc-1", "doc-2", "doc-3", "doc-4", "doc-5"]
    relevant = {"doc-1", "doc-3"}

    # Precision@3: doc-1 (rel), doc-2 (non-rel), doc-3 (rel) -> 2/3
    p3 = evaluator.precision_at_k(retrieved, relevant, k=3)
    assert abs(p3 - 2/3) < 1e-4

    # Recall@3: 2 out of 2 found -> 1.0
    r3 = evaluator.recall_at_k(retrieved, relevant, k=3)
    assert r3 == 1.0

    # Reciprocal rank: first relevant is at rank 1 -> 1.0
    rr = evaluator.reciprocal_rank(retrieved, relevant)
    assert rr == 1.0

    # Mean Reciprocal Rank
    rankings = [
        ["a", "b", "c"], # relevant at rank 1 -> 1.0
        ["x", "y", "z"]  # relevant 'y' at rank 2 -> 0.5
    ]
    rel_sets = [{"a"}, {"y"}]
    mrr = evaluator.mean_reciprocal_rank(rankings, rel_sets)
    assert mrr == 0.75

    # NDCG@3
    rel_scores = {"doc-1": 3.0, "doc-2": 0.0, "doc-3": 2.0, "doc-4": 1.0}
    ndcg3 = evaluator.ndcg_at_k(retrieved, rel_scores, k=3)
    assert 0.0 <= ndcg3 <= 1.0

def test_recall_inherits_score_from_superseded_node():
    storage = StorageEngine()
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall_engine = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    old_id = str(uuid.uuid4())
    active_id = str(uuid.uuid4())

    old_note = {
        "id": old_id,
        "type": "knowledge",
        "lifecycle": "SUPERSEDED",
        "superseded_by": active_id,
        "content": "Windows PowerShell Active Directory administrative commands",
        "confidence": "high"
    }
    active_note = {
        "id": active_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "supersedes": old_id,
        "content": "Windows PowerShell Active Directory administrative commands updated for 2026",
        "confidence": "high"
    }

    storage.set(old_id, old_note)
    storage.set(active_id, active_note)

    # Old note has high activation
    activated = [(old_note, 0.9)]
    results = recall_engine.recall(Principal.AI_AGENT, "PowerShell Active Directory", activated, wm)

    # Active note should be present in results and ranked high via inherited score with freshness boost
    result_ids = [n.get("id") for n, _ in results]
    assert active_id in result_ids
