import concurrent.futures
import math
import os
import tempfile
import uuid
import pytest

from cognitive_core.learning import ContinualLearningGuard, LearningEngine
from cognitive_core.evaluation import RetrievalEvaluator
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.tool_router import ToolRouter
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle


# ==============================================================================
# 1. ContinualLearningGuard Adversarial Mutation, Drift, & Regression Tests
# ==============================================================================

class TestAdversarialContinualLearningGuard:

    def test_anchor_subtle_mutation_unicode_and_whitespace(self):
        """Stress-test anchor drift detection with subtle character mutations,
        unicode homoglyphs, whitespace differences, and case alterations.
        """
        guard = ContinualLearningGuard()
        anchor = {
            "id": "anchor-core-001",
            "content": "Operating System Kernel Trust Boundaries and Invariants P0-P15",
            "verification": "verified",
            "type": "knowledge"
        }
        guard.register_anchor_node(anchor)
        s_trailing = [{"id": "anchor-core-001", "content": anchor["content"] + " ", "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_trailing)
        assert ok is False and len(v) == 1
        assert "content drift/corruption detected" in v[0]
        s_leading = [{"id": "anchor-core-001", "content": " " + anchor["content"], "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_leading)
        assert ok is False and len(v) == 1
        homoglyph_content = anchor["content"].replace("Boundaries", "Boundаries")
        s_homoglyph = [{"id": "anchor-core-001", "content": homoglyph_content, "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_homoglyph)
        assert ok is False and len(v) == 1
        case_content = anchor["content"].replace("Trust", "trust")
        s_case = [{"id": "anchor-core-001", "content": case_content, "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_case)
        assert ok is False and len(v) == 1
        punct_content = anchor["content"].replace("P0-P15", "P0_P15")
        s_punct = [{"id": "anchor-core-001", "content": punct_content, "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_punct)
        assert ok is False and len(v) == 1

    def test_anchor_verification_status_tampering_and_downgrades(self):
        guard = ContinualLearningGuard()
        anchor = {"id": "anchor-sec-002", "content": "Immutable Provenance Ledger Specification", "verification": "verified", "type": "knowledge"}
        guard.register_anchor_node(anchor)
        for state in ["unverified", "partially_verified", "inferred", "bogus_status", "", None]:
            s_tampered = [{"id": "anchor-sec-002", "content": anchor["content"], "verification": state}]
            ok, v = guard.verify_no_catastrophic_regression(s_tampered)
            assert ok is False
            assert len(v) == 1
            assert "verification status was downgraded from verified to" in v[0]

    def test_anchor_deletion_and_storage_omission(self):
        guard = ContinualLearningGuard()
        for i in range(5):
            guard.register_anchor_node({"id": f"anchor-del-{i}", "content": f"Critical rule {i}", "verification": "verified"})
        storage_partial = [{"id": f"anchor-del-{i}", "content": f"Critical rule {i}", "verification": "verified"} for i in range(5) if i != 2]
        ok, v = guard.verify_no_catastrophic_regression(storage_partial)
        assert ok is False
        assert len(v) == 1
        assert "anchor-del-2" in v[0]
        assert "removed from active storage" in v[0]
        ok_empty, v_empty = guard.verify_no_catastrophic_regression([])
        assert ok_empty is False
        assert len(v_empty) == 5

    def test_large_scale_anchor_integrity_fuzzing(self):
        guard = ContinualLearningGuard()
        storage = []
        for i in range(100):
            node = {"id": f"anchor-fuzz-{i:03d}", "content": f"Canonical verified memory content for invariant #{i:03d}", "verification": "verified", "type": "knowledge"}
            guard.register_anchor_node(node)
            storage.append(dict(node))
        storage = [n for n in storage if n["id"] not in ["anchor-fuzz-010", "anchor-fuzz-020", "anchor-fuzz-030"]]
        for n in storage:
            if n["id"] in ["anchor-fuzz-040", "anchor-fuzz-050", "anchor-fuzz-060"]:
                n["content"] += " [ADVERSARIAL DRIFT]"
            elif n["id"] == "anchor-fuzz-070":
                n["verification"] = "unverified"
            elif n["id"] == "anchor-fuzz-080":
                n["verification"] = "partially_verified"
            elif n["id"] == "anchor-fuzz-090":
                n["verification"] = "inferred"
            elif n["id"] == "anchor-fuzz-095":
                n["verification"] = None
        ok, violations = guard.verify_no_catastrophic_regression(storage)
        assert ok is False
        assert len(violations) == 10
        expected_ids = ["anchor-fuzz-010", "anchor-fuzz-020", "anchor-fuzz-030", "anchor-fuzz-040", "anchor-fuzz-050", "anchor-fuzz-060", "anchor-fuzz-070", "anchor-fuzz-080", "anchor-fuzz-090", "anchor-fuzz-095"]
        for target_id in expected_ids:
            assert any(target_id in v for v in violations), f"Target ID {target_id} not found in violations"

    def test_guard_resilience_to_malformed_notes(self):
        guard = ContinualLearningGuard()
        guard.register_anchor_node({"id": "valid-1", "content": "Valid anchor", "verification": "verified"})
        malformed_storage = [None, {}, {"id": None}, {"id": ""}, {"id": "valid-1", "content": "Valid anchor", "verification": "verified"}, {"other_key": 123}]
        clean_storage = [n for n in malformed_storage if isinstance(n, dict)]
        ok, violations = guard.verify_no_catastrophic_regression(clean_storage)
        assert ok is True
        assert len(violations) == 0


# ==============================================================================
# 2. LearningEngine Hostile Confidence Escalation & Trust Boundary Invariants
# ==============================================================================

class TestHostileConfidenceEscalationAndTrustBoundaries:

    def test_hostile_dense_relations_non_execution_escalation_blocked(self):
        storage = StorageEngine(); controller = MemoryController(storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        for src in ["ai", "inference", "user", "import", "unknown", "custom_external"]:
            note_id = str(uuid.uuid4())
            note = {"id": note_id, "type": "knowledge", "lifecycle": "ACTIVE", "category": "hostile_test", "tags": ["stress"], "created": "2026-08-15", "updated": "2026-08-15", "provenance": {"source_type": src, "source_ref": "adversarial_injector"}, "confidence": "high", "verification": "unverified", "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(50)], "content": f"Hostile dense graph memory with source {src}"}
            storage.set(note_id, note)
            promoted = engine.promote_memories(Principal.AI_AGENT)
            assert note_id not in promoted
            stored = storage.get(note_id)
            assert stored["confidence"] == "high"
            assert stored["verification"] == "unverified"

    def test_ai_agent_cannot_self_verify_through_learning_engine(self):
        storage = StorageEngine(); controller = MemoryController(storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        notes = []
        for i in range(10):
            nid = str(uuid.uuid4()); n = {"id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "core", "tags": ["p0_test"], "created": "2026-08-15", "updated": "2026-08-15", "provenance": {"source_type": "execution", "source_ref": "test_runner"}, "confidence": "unknown" if i % 3 == 0 else ("low" if i % 3 == 1 else "medium"), "verification": "unverified", "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(i * 3 + 3)], "content": f"Candidate memory {i}"}
            storage.set(nid, n); notes.append(nid)
        for _ in range(5): engine.promote_memories(Principal.AI_AGENT)
        for nid in notes:
            stored = storage.get(nid)
            assert stored["verification"] != "verified"
            assert stored["verification"] in ["unverified", "partially_verified"]

    def test_canonical_human_verified_notes_immunity(self):
        storage = StorageEngine(); controller = MemoryController(storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        vid = str(uuid.uuid4()); canonical_note = {"id": vid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "axioms", "tags": ["immutable"], "created": "2026-08-15", "updated": "2026-08-15", "provenance": {"source_type": "official", "source_ref": "human_curator"}, "confidence": "medium", "verification": "verified", "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(30)], "content": "Canonical human-verified system invariant"}
        storage.set(vid, canonical_note); promoted = engine.promote_memories(Principal.AI_AGENT); assert vid not in promoted
        stored = storage.get(vid); assert stored["confidence"] == "medium"; assert stored["verification"] == "verified"; assert stored["content"] == canonical_note["content"]

    def test_inactive_lifecycle_isolation(self):
        storage = StorageEngine(); controller = MemoryController(storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        ids = []
        for lc in [Lifecycle.RAW.value, Lifecycle.CLASSIFIED.value, Lifecycle.NORMALIZED.value, Lifecycle.REVIEW.value, Lifecycle.SUPERSEDED.value, Lifecycle.ARCHIVED.value]:
            nid = str(uuid.uuid4()); n = {"id": nid, "type": "knowledge", "lifecycle": lc, "category": "lifecycle_test", "tags": ["lc"], "created": "2026-08-15", "updated": "2026-08-15", "provenance": {"source_type": "execution", "source_ref": "runner"}, "confidence": "high", "verification": "unverified", "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(25)], "content": f"Note in lifecycle {lc}"}; storage.set(nid, n); ids.append(nid)
        promoted = engine.promote_memories(Principal.AI_AGENT); assert len(promoted) == 0
        for nid in ids:
            stored = storage.get(nid); assert stored["confidence"] == "high"; assert stored["verification"] == "unverified"

    def test_exact_promotion_ladder_boundary_conditions(self):
        storage = StorageEngine(); controller = MemoryController(storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        id_2 = str(uuid.uuid4()); storage.set(id_2, {"id": id_2, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "low", "verification": "unverified", "provenance": {"source_type": "execution"}, "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(2)], "content": "Note with 2 relations"})
        id_5 = str(uuid.uuid4()); storage.set(id_5, {"id": id_5, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "medium", "verification": "unverified", "provenance": {"source_type": "execution"}, "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(5)], "content": "Note with 5 relations"})
        id_8 = str(uuid.uuid4()); storage.set(id_8, {"id": id_8, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "high", "verification": "partially_verified", "provenance": {"source_type": "execution"}, "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(8)], "content": "Note with 8 relations"})
        promoted = engine.promote_memories(Principal.AI_AGENT); assert id_2 not in promoted; assert id_5 not in promoted; assert id_8 not in promoted
        assert storage.get(id_2)["confidence"] == "low"; assert storage.get(id_5)["confidence"] == "medium"; assert storage.get(id_8)["confidence"] == "high"


# ==============================================================================
# 3. Concurrency & Race Condition Stress Testing
# ==============================================================================

class TestConcurrentLearningEngineAndRaceConditions:

    def test_multithreaded_concurrent_promotions_in_memory(self):
        storage = StorageEngine(); controller = MemoryController(storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        note_ids = []
        for i in range(20):
            nid = str(uuid.uuid4()); storage.set(nid, {"id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "low", "verification": "unverified", "provenance": {"source_type": "execution", "source_ref": "concurrency_test"}, "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(10)], "content": f"Concurrent test node {i}"}); note_ids.append(nid)
        def worker(): return engine.promote_memories(Principal.AI_AGENT)
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            futures = [executor.submit(worker) for _ in range(16)]; results = [f.result() for f in concurrent.futures.as_completed(futures)]
        assert len(results) == 16
        for nid in note_ids:
            stored = storage.get(nid)
            assert stored["confidence"] in ["medium", "high", "very_high"]
            assert stored["verification"] in ["unverified", "partially_verified"]
            assert stored["verification"] != "verified"

    def test_multithreaded_concurrent_promotions_sqlite_storage(self):
        temp_dir = tempfile.mkdtemp(); db_path = os.path.join(temp_dir, "test_learning_wal.db")
        sqlite_storage = SQLiteStorageEngine(db_path); controller = MemoryController(sqlite_storage); router = ToolRouter(controller); engine = LearningEngine(controller, router)
        note_ids = []
        for i in range(15):
            nid = str(uuid.uuid4()); sqlite_storage.set(nid, {"id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "sqlite_concurrency", "tags": ["sqlite"], "created": "2026-08-15", "updated": "2026-08-15", "confidence": "low", "verification": "unverified", "provenance": {"source_type": "execution", "source_ref": "sqlite_worker"}, "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(9)], "content": f"SQLite concurrency candidate {i}"}); note_ids.append(nid)
        def sqlite_worker(): return engine.promote_memories(Principal.AI_AGENT)
        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
            futures = [executor.submit(sqlite_worker) for _ in range(12)]; results = [f.result() for f in concurrent.futures.as_completed(futures)]
        assert len(results) == 12
        for nid in note_ids:
            stored = sqlite_storage.get(nid); assert stored is not None; assert stored["confidence"] in ["medium", "high", "very_high"]; assert stored["verification"] != "verified"
        sqlite_storage.close()

    def test_concurrent_anchor_verification_under_storage_churn(self):
        guard = ContinualLearningGuard(); anchors = []
        for i in range(10):
            a = {"id": f"anchor-churn-{i}", "content": f"Immutable anchor rule #{i}", "verification": "verified", "type": "knowledge"}; guard.register_anchor_node(a); anchors.append(a)
        def verify_task():
            storage_snapshot = [dict(a) for a in anchors]; return guard.verify_no_catastrophic_regression(storage_snapshot)
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            futures = [executor.submit(verify_task) for _ in range(30)]; results = [f.result() for f in concurrent.futures.as_completed(futures)]
        for ok, violations in results: assert ok is True; assert len(violations) == 0


# ==============================================================================
# 4. Evaluation Numerical Robustness & Boundary Challenge Tests
# ==============================================================================

class TestAdversarialEvaluationNumericalRobustness:
    def test_trace_utilization_adversarial_inputs(self):
        evaluator = RetrievalEvaluator(); notes = [{"id": "n1", "content": "PostgreSQL database indexing optimization techniques"}, {"id": "n2", "content": "Distributed consensus protocols Raft and Paxos"}]
        huge_response = "PostgreSQL database " * 5000; assert 0.0 <= evaluator.utilization(notes, huge_response) <= 1.0
        symbol_response = "!!! PostgreSQL ??? database *** @@@ optimization \n\n\t techniques"; assert evaluator.utilization(notes, symbol_response) >= 0.5
        notes_numbers = [{"id": "num", "content": "123 4567 8910"}]; assert evaluator.utilization(notes_numbers, "123 4567 8910") == 0.0

    def test_ir_metrics_extreme_k_and_adversarial_distributions(self):
        evaluator = RetrievalEvaluator(); retrieved = [f"doc_{i}" for i in range(100)]; relevant = {f"doc_{i}" for i in range(20)}
        assert evaluator.precision_at_k(retrieved, relevant, k=1000000) == 20 / 100; assert evaluator.recall_at_k(retrieved, relevant, k=1000000) == 1.0
        assert evaluator.precision_at_k(retrieved, relevant, k=-999) == 0.0; assert evaluator.recall_at_k(retrieved, relevant, k=-999) == 0.0; assert evaluator.ndcg_at_k(retrieved, {k: 1.0 for k in relevant}, k=-999) == 0.0
        assert evaluator.precision_at_k(retrieved, relevant, k=0) == 0.0; assert evaluator.recall_at_k(retrieved, relevant, k=0) == 0.0; assert evaluator.ndcg_at_k(retrieved, {k: 1.0 for k in relevant}, k=0) == 0.0

    def test_ndcg_at_k_identical_scores_and_single_elements(self):
        evaluator = RetrievalEvaluator(); uniform_scores = {f"doc_{i}": 1.0 for i in range(10)}; retrieved = [f"doc_{i}" for i in range(10)]
        assert abs(evaluator.ndcg_at_k(retrieved, uniform_scores, k=5) - 1.0) < 1e-6
        assert abs(evaluator.ndcg_at_k(["doc_1"], {"doc_1": 5.0}, k=1) - 1.0) < 1e-6
        assert evaluator.ndcg_at_k(["doc_1"], {"doc_2": 5.0}, k=1) == 0.0
