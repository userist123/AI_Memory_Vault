import math
import uuid
import pytest
from unittest.mock import MagicMock

from cognitive_core.learning import ContinualLearningGuard, LearningEngine
from cognitive_core.evaluation import RetrievalEvaluator
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.tool_router import ToolRouter
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle


# ==============================================================================
# 1. ContinualLearningGuard Tests
# ==============================================================================

def test_guard_register_and_verify_clean_storage():
    """Verify that registering anchors and verifying against identical storage reports no violations."""
    guard = ContinualLearningGuard()
    anchor_a = {"id": "anchor-1", "content": "Critical kernel architecture", "verification": "verified", "type": "knowledge"}
    anchor_b = {"id": "anchor-2", "content": "Immutable provenance invariants", "verification": "verified", "type": "knowledge"}
    
    guard.register_anchor_node(anchor_a)
    guard.register_anchor_node(anchor_b)

    storage_notes = [dict(anchor_a), dict(anchor_b)]
    ok, violations = guard.verify_no_catastrophic_regression(storage_notes)

    assert ok is True
    assert len(violations) == 0


def test_guard_detects_removed_anchor_node():
    """Verify that removing an anchor node from storage is detected as a regression."""
    guard = ContinualLearningGuard()
    anchor_1 = {"id": "rule-101", "content": "Database WAL durability invariant", "verification": "verified"}
    anchor_2 = {"id": "rule-102", "content": "Audit log hash chain invariant", "verification": "verified"}
    
    guard.register_anchor_node(anchor_1)
    guard.register_anchor_node(anchor_2)

    # Storage only contains rule-101 (rule-102 missing/forgotten)
    storage_notes = [dict(anchor_1)]
    ok, violations = guard.verify_no_catastrophic_regression(storage_notes)

    assert ok is False
    assert len(violations) == 1
    assert "rule-102" in violations[0]
    assert "removed from active storage" in violations[0]


def test_guard_detects_verification_downgrade():
    """Verify that downgrading a verified anchor to unverified or partially_verified flags a violation."""
    guard = ContinualLearningGuard()
    anchor = {"id": "sec-core", "content": "AI self-verification strictly forbidden", "verification": "verified"}
    guard.register_anchor_node(anchor)

    # Case A: downgraded to unverified
    storage_notes_unverified = [{"id": "sec-core", "content": "AI self-verification strictly forbidden", "verification": "unverified"}]
    ok_unv, violations_unv = guard.verify_no_catastrophic_regression(storage_notes_unverified)
    assert ok_unv is False
    assert len(violations_unv) == 1
    assert "downgraded from verified to unverified" in violations_unv[0]

    # Case B: downgraded to partially_verified
    storage_notes_part = [{"id": "sec-core", "content": "AI self-verification strictly forbidden", "verification": "partially_verified"}]
    ok_part, violations_part = guard.verify_no_catastrophic_regression(storage_notes_part)
    assert ok_part is False
    assert len(violations_part) == 1
    assert "downgraded from verified to partially_verified" in violations_part[0]


def test_guard_detects_content_drift_and_corruption():
    """Verify that modifying or corrupting anchor content is flagged as a violation."""
    guard = ContinualLearningGuard()
    anchor = {"id": "spec-math", "content": "Euler identity e^(i*pi) + 1 = 0", "verification": "verified"}
    guard.register_anchor_node(anchor)

    # Content altered
    storage_tampered = [{"id": "spec-math", "content": "Euler identity modified falsely", "verification": "verified"}]
    ok, violations = guard.verify_no_catastrophic_regression(storage_tampered)
    assert ok is False
    assert len(violations) == 1
    assert "content drift/corruption detected" in violations[0]

    # Content emptied
    storage_empty_content = [{"id": "spec-math", "content": "", "verification": "verified"}]
    ok_empty, violations_empty = guard.verify_no_catastrophic_regression(storage_empty_content)
    assert ok_empty is False
    assert len(violations_empty) == 1
    assert "content drift/corruption detected" in violations_empty[0]


def test_guard_aggregates_multiple_regression_violations():
    """Verify that multiple distinct regression violations across multiple anchors are all collected."""
    guard = ContinualLearningGuard()
    a1 = {"id": "a-1", "content": "Rule 1", "verification": "verified"}
    a2 = {"id": "a-2", "content": "Rule 2", "verification": "verified"}
    a3 = {"id": "a-3", "content": "Rule 3", "verification": "verified"}
    
    guard.register_anchor_node(a1)
    guard.register_anchor_node(a2)
    guard.register_anchor_node(a3)

    # a1 is missing, a2 has downgraded verification, a3 has corrupted content
    current_storage = [
        {"id": "a-2", "content": "Rule 2", "verification": "unverified"},
        {"id": "a-3", "content": "Corrupted content for Rule 3", "verification": "verified"}
    ]
    ok, violations = guard.verify_no_catastrophic_regression(current_storage)
    assert ok is False
    assert len(violations) == 3
    assert any("a-1" in v and "removed" in v for v in violations)
    assert any("a-2" in v and "downgraded" in v for v in violations)
    assert any("a-3" in v and "drift" in v for v in violations)


def test_guard_empty_state_and_unanchored_storage():
    """Verify that an empty guard or unanchored notes do not trigger false regressions."""
    guard = ContinualLearningGuard()
    # Empty guard against empty storage
    ok, violations = guard.verify_no_catastrophic_regression([])
    assert ok is True
    assert violations == []

    # Empty guard against arbitrary storage
    storage = [{"id": "random-1", "content": "Arbitrary note", "verification": "unverified"}]
    ok, violations = guard.verify_no_catastrophic_regression(storage)
    assert ok is True
    assert violations == []


# ==============================================================================
# 2. LearningEngine & Confidence Promotion Gating Tests
# ==============================================================================

def test_promotion_requires_execution_provenance_for_very_high():
    """Verify that only notes with source_type == 'execution' can be promoted to very_high confidence."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    # Setup execution note with 9 relations
    exec_id = str(uuid.uuid4())
    exec_note = {
        "id": exec_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "testing",
        "tags": ["execution"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "execution", "source_ref": "test_runner"},
        "confidence": "high",
        "verification": "partially_verified",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(9)],
        "content": "Execution verified deterministic test assertion result"
    }
    storage.set(exec_id, exec_note)

    promoted = engine.promote_memories(Principal.AI_AGENT)
    assert exec_id in promoted
    updated = storage.get(exec_id)
    assert updated["confidence"] == "very_high"
    assert updated["verification"] == "partially_verified"  # Never AI self-verified


@pytest.mark.parametrize("disallowed_source_type", ["inference", "ai", "user", "unknown", "import"])
def test_promotion_rejects_non_execution_provenance_for_very_high(disallowed_source_type):
    """Verify that notes with non-execution provenance are strictly rejected from very_high promotion."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    note_id = str(uuid.uuid4())
    note = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "testing",
        "tags": ["test"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": disallowed_source_type, "source_ref": "external_source"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(12)],
        "content": f"Knowledge derived from {disallowed_source_type} provenance"
    }
    storage.set(note_id, note)

    promoted = engine.promote_memories(Principal.AI_AGENT)
    assert note_id not in promoted
    stored = storage.get(note_id)
    assert stored["confidence"] == "high"  # Unchanged
    assert stored["verification"] == "unverified"


def test_promotion_tiers_low_to_medium_and_medium_to_high():
    """Verify gradual promotion tiers: low -> medium (>=3 relations) and medium -> high (>=6 relations)."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    # Note 1: low confidence with 3 relations -> should become medium
    id_low = str(uuid.uuid4())
    note_low = {
        "id": id_low,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "core",
        "tags": ["core"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "inference", "source_ref": "model"},
        "confidence": "low",
        "verification": "unverified",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(3)],
        "content": "Emerging knowledge concept"
    }
    storage.set(id_low, note_low)

    # Note 2: medium confidence with 6 relations -> should become high and partially_verified
    id_med = str(uuid.uuid4())
    note_med = {
        "id": id_med,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "core",
        "tags": ["core"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "inference", "source_ref": "model"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(6)],
        "content": "Reinforced knowledge concept"
    }
    storage.set(id_med, note_med)

    promoted = engine.promote_memories(Principal.AI_AGENT)
    assert id_low in promoted
    assert id_med in promoted

    res_low = storage.get(id_low)
    assert res_low["confidence"] == "medium"

    res_med = storage.get(id_med)
    assert res_med["confidence"] == "high"
    assert res_med["verification"] == "partially_verified"


def test_promotion_skips_verified_canonical_notes():
    """Verify that human/admin-verified notes are never modified by the autonomous LearningEngine."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    ver_id = str(uuid.uuid4())
    verified_note = {
        "id": ver_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "axioms",
        "tags": ["axiom"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "official", "source_ref": "spec_doc"},
        "confidence": "medium",
        "verification": "verified",  # Human verified
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(10)],
        "content": "Axiom of memory integrity"
    }
    storage.set(ver_id, verified_note)

    promoted = engine.promote_memories(Principal.AI_AGENT)
    assert ver_id not in promoted
    stored = storage.get(ver_id)
    assert stored["confidence"] == "medium"
    assert stored["verification"] == "verified"


def test_promotion_skips_inactive_lifecycles():
    """Verify that notes in REVIEW, SUPERSEDED, or ARCHIVED states are not promoted."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    # Note in REVIEW
    review_id = str(uuid.uuid4())
    review_note = {
        "id": review_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "staging",
        "tags": ["temp"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "execution", "source_ref": "runner"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(10)],
        "content": "Staging note under review"
    }
    storage.set(review_id, review_note)

    promoted = engine.promote_memories(Principal.AI_AGENT)
    assert review_id not in promoted
    stored = storage.get(review_id)
    assert stored["confidence"] == "high"


# ==============================================================================
# 3. TRACe Metrics Evaluation Tests
# ==============================================================================

def test_trace_utilization_metrics_and_edge_cases():
    """Verify TRACe Utilization metric under normal, edge, and empty conditions."""
    evaluator = RetrievalEvaluator()

    notes = [
        {"id": "doc-1", "content": "PostgreSQL database connection pooling with PgBouncer optimization"},
        {"id": "doc-2", "content": "FastAPI asynchronous background tasks using Celery and Redis"},
        {"id": "doc-3", "content": "Kubernetes pod horizontal autoscaling policies and metrics server"}
    ]
    
    # 1. High utilization response referencing doc-1 and doc-2 keywords
    resp_high = "We configure PostgreSQL database connection pooling and FastAPI asynchronous background tasks."
    util_high = evaluator.utilization(notes, resp_high)
    assert util_high >= 2 / 3

    # 2. Zero utilization response
    resp_zero = "The weather today is sunny and mild across the region."
    util_zero = evaluator.utilization(notes, resp_zero)
    assert util_zero == 0.0

    # 3. Edge cases: empty text, empty notes
    assert evaluator.utilization([], resp_high) == 0.0
    assert evaluator.utilization(notes, "") == 0.0
    assert evaluator.utilization([], "") == 0.0

    # 4. Note with short words only (< 5 characters)
    short_notes = [{"id": "s1", "content": "a bb ccc dddd"}]
    assert evaluator.utilization(short_notes, "a bb ccc dddd") == 0.0


def test_trace_relevance_semantic_and_edge_cases():
    """Verify TRACe Relevance metric calculation and edge cases."""
    semantic = DeterministicSemanticProvider()
    evaluator = RetrievalEvaluator(semantic)

    notes = [
        {"id": "doc-1", "content": "SQLite Write Ahead Logging WAL concurrency transaction isolation"},
        {"id": "doc-2", "content": "Python asyncio coroutines event loop concurrency execution"}
    ]

    rel = evaluator.relevance(notes, "SQLite WAL mode concurrency")
    assert 0.0 < rel <= 1.0

    # Edge cases
    assert evaluator.relevance([], "query") == 0.0
    assert evaluator.relevance(notes, "") == 0.0
    
    evaluator_no_sem = RetrievalEvaluator(None)
    assert evaluator_no_sem.relevance(notes, "query") == 0.0


def test_trace_adherence_semantic_and_edge_cases():
    """Verify TRACe Adherence metric fidelity check and fallback behaviors."""
    semantic = DeterministicSemanticProvider()
    evaluator = RetrievalEvaluator(semantic)

    notes = [
        {"id": "doc-1", "content": "SHA-256 cryptographic hash chaining provides tamper-evident audit logs"}
    ]
    
    # Response strictly aligned with source
    resp_adherent = "SHA-256 cryptographic hash chaining creates tamper-evident audit logs."
    adh = evaluator.adherence(resp_adherent, notes)
    assert 0.0 < adh <= 1.0

    # Edge cases
    assert evaluator.adherence("", notes) == 0.0
    assert evaluator.adherence(resp_adherent, []) == 0.0

    # Adherence with None semantic provider falls back to 1.0
    evaluator_no_sem = RetrievalEvaluator(None)
    assert evaluator_no_sem.adherence(resp_adherent, notes) == 1.0


def test_trace_completeness_and_edge_cases():
    """Verify TRACe Completeness metric calculation against gold reference IDs."""
    evaluator = RetrievalEvaluator()

    retrieved = [
        {"id": "g-1", "content": "Content 1"},
        {"id": "g-2", "content": "Content 2"},
        {"id": "other", "content": "Content other"}
    ]

    # Perfect completeness
    assert evaluator.completeness(retrieved, ["g-1", "g-2"]) == 1.0

    # Partial completeness (2 out of 4)
    assert evaluator.completeness(retrieved, ["g-1", "g-2", "g-3", "g-4"]) == 0.5

    # Zero completeness
    assert evaluator.completeness(retrieved, ["unretrieved-1", "unretrieved-2"]) == 0.0

    # Vacuous completeness with empty gold references
    assert evaluator.completeness(retrieved, []) == 1.0

    # Empty retrieved notes with non-empty gold references
    assert evaluator.completeness([], ["g-1"]) == 0.0


# ==============================================================================
# 4. Standard Information Retrieval (IR) Benchmarks Tests
# ==============================================================================

def test_ir_precision_at_k_thorough():
    """Verify Precision@K under normal rankings and extreme parameter boundary conditions."""
    evaluator = RetrievalEvaluator()

    retrieved = ["doc-1", "doc-2", "doc-3", "doc-4", "doc-5"]
    relevant = {"doc-1", "doc-3", "doc-5"}

    # Precision@1: doc-1 is relevant -> 1/1 = 1.0
    assert evaluator.precision_at_k(retrieved, relevant, k=1) == 1.0

    # Precision@2: doc-1 (rel), doc-2 (non-rel) -> 1/2 = 0.5
    assert evaluator.precision_at_k(retrieved, relevant, k=2) == 0.5

    # Precision@3: doc-1 (rel), doc-2 (non-rel), doc-3 (rel) -> 2/3
    assert abs(evaluator.precision_at_k(retrieved, relevant, k=3) - 2/3) < 1e-6

    # Precision@5: 3 out of 5 relevant -> 3/5 = 0.6
    assert evaluator.precision_at_k(retrieved, relevant, k=5) == 0.6

    # Boundary and edge conditions: k <= 0, empty lists, empty relevant sets
    assert evaluator.precision_at_k(retrieved, relevant, k=0) == 0.0
    assert evaluator.precision_at_k(retrieved, relevant, k=-3) == 0.0
    assert evaluator.precision_at_k([], relevant, k=5) == 0.0
    assert evaluator.precision_at_k(retrieved, set(), k=5) == 0.0

    # k exceeds retrieved list length
    assert evaluator.precision_at_k(["doc-1", "doc-2"], {"doc-1"}, k=10) == 0.5


def test_ir_recall_at_k_thorough():
    """Verify Recall@K under normal rankings and extreme parameter boundary conditions."""
    evaluator = RetrievalEvaluator()

    retrieved = ["doc-1", "doc-2", "doc-3", "doc-4", "doc-5"]
    relevant = {"doc-1", "doc-3", "doc-99"}  # 3 total relevant, 2 in retrieved list

    # Recall@1: 1 out of 3 found -> 1/3
    assert abs(evaluator.recall_at_k(retrieved, relevant, k=1) - 1/3) < 1e-6

    # Recall@2: 1 out of 3 found -> 1/3
    assert abs(evaluator.recall_at_k(retrieved, relevant, k=2) - 1/3) < 1e-6

    # Recall@3: 2 out of 3 found -> 2/3
    assert abs(evaluator.recall_at_k(retrieved, relevant, k=3) - 2/3) < 1e-6

    # Recall@5: 2 out of 3 found -> 2/3
    assert abs(evaluator.recall_at_k(retrieved, relevant, k=5) - 2/3) < 1e-6

    # Boundary conditions
    assert evaluator.recall_at_k(retrieved, set(), k=5) == 1.0  # Empty relevant set
    assert evaluator.recall_at_k(retrieved, relevant, k=0) == 0.0
    assert evaluator.recall_at_k(retrieved, relevant, k=-1) == 0.0
    assert evaluator.recall_at_k([], relevant, k=5) == 0.0


def test_ir_reciprocal_rank_and_mean_reciprocal_rank():
    """Verify Reciprocal Rank (RR) and Mean Reciprocal Rank (MRR)."""
    evaluator = RetrievalEvaluator()

    # RR tests
    assert evaluator.reciprocal_rank(["a", "b", "c"], {"a"}) == 1.0       # 1/1
    assert evaluator.reciprocal_rank(["x", "a", "b"], {"a"}) == 0.5       # 1/2
    assert evaluator.reciprocal_rank(["x", "y", "z", "a"], {"a"}) == 0.25 # 1/4
    assert evaluator.reciprocal_rank(["x", "y", "z"], {"a"}) == 0.0       # Not found
    assert evaluator.reciprocal_rank([], {"a"}) == 0.0
    assert evaluator.reciprocal_rank(["a"], set()) == 0.0

    # MRR tests
    rankings = [
        ["doc-a", "doc-b", "doc-c"],  # First rel at rank 1 -> 1.0
        ["doc-x", "doc-y", "doc-z"],  # First rel at rank 2 -> 0.5
        ["doc-m", "doc-n", "doc-o"],  # First rel at rank 3 -> 0.333333
        ["doc-1", "doc-2", "doc-3"]   # No rel found -> 0.0
    ]
    relevant_sets = [
        {"doc-a"},
        {"doc-y"},
        {"doc-o"},
        {"doc-99"}
    ]

    expected_mrr = (1.0 + 0.5 + (1/3) + 0.0) / 4
    calculated_mrr = evaluator.mean_reciprocal_rank(rankings, relevant_sets)
    assert abs(calculated_mrr - expected_mrr) < 1e-6

    # MRR edge cases
    assert evaluator.mean_reciprocal_rank([], []) == 0.0
    assert evaluator.mean_reciprocal_rank([["a"]], []) == 0.0


def test_ir_ndcg_at_k_exact_mathematical_validation():
    """Verify Normalized Discounted Cumulative Gain (NDCG@K) against explicit hand-calculated values."""
    evaluator = RetrievalEvaluator()

    # Ranking: [doc1, doc2, doc3, doc4]
    # Relevance scores: doc1: 3, doc2: 2, doc3: 0, doc4: 1
    # DCG@3:
    # rank 1 (doc1, rel=3): 3 / log2(2) = 3 / 1 = 3.0
    # rank 2 (doc2, rel=2): 2 / log2(3) = 2 / 1.5849625 = 1.2618595
    # rank 3 (doc3, rel=0): 0 / log2(4) = 0.0
    # Total DCG@3 = 3.0 + 1.2618595 = 4.2618595

    # Ideal ranking scores for top 3: [3, 2, 1]
    # rank 1 (rel=3): 3 / log2(2) = 3.0
    # rank 2 (rel=2): 2 / log2(3) = 1.2618595
    # rank 3 (rel=1): 1 / log2(4) = 1 / 2 = 0.5
    # Total IDCG@3 = 3.0 + 1.2618595 + 0.5 = 4.7618595

    # Expected NDCG@3 = 4.2618595 / 4.7618595 = 0.895000...
    retrieved = ["doc1", "doc2", "doc3", "doc4"]
    rel_scores = {"doc1": 3.0, "doc2": 2.0, "doc3": 0.0, "doc4": 1.0}

    ndcg3 = evaluator.ndcg_at_k(retrieved, rel_scores, k=3)
    
    # Hand calculation verification
    dcg3_expected = 3.0 / math.log2(2) + 2.0 / math.log2(3) + 0.0 / math.log2(4)
    idcg3_expected = 3.0 / math.log2(2) + 2.0 / math.log2(3) + 1.0 / math.log2(4)
    expected_ndcg = dcg3_expected / idcg3_expected

    assert abs(ndcg3 - expected_ndcg) < 1e-6

    # Perfect ranking NDCG = 1.0
    perfect_retrieved = ["doc1", "doc2", "doc4", "doc3"]
    assert abs(evaluator.ndcg_at_k(perfect_retrieved, rel_scores, k=3) - 1.0) < 1e-6

    # All zero scores -> returns 0.0 (prevents division by zero)
    zero_scores = {"doc1": 0.0, "doc2": 0.0}
    assert evaluator.ndcg_at_k(["doc1", "doc2"], zero_scores, k=2) == 0.0

    # Boundary edge conditions
    assert evaluator.ndcg_at_k(retrieved, rel_scores, k=0) == 0.0
    assert evaluator.ndcg_at_k(retrieved, rel_scores, k=-2) == 0.0
    assert evaluator.ndcg_at_k([], rel_scores, k=5) == 0.0
