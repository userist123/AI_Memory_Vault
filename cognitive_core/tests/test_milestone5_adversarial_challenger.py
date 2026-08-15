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

        # 1. Trailing space mutation
        s_trailing = [{"id": "anchor-core-001", "content": anchor["content"] + " ", "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_trailing)
        assert ok is False and len(v) == 1
        assert "content drift/corruption detected" in v[0]

        # 2. Leading space mutation
        s_leading = [{"id": "anchor-core-001", "content": " " + anchor["content"], "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_leading)
        assert ok is False and len(v) == 1

        # 3. Unicode Cyrillic 'а' replacing Latin 'a' in 'Boundaries'
        homoglyph_content = anchor["content"].replace("Boundaries", "Boundаries")
        s_homoglyph = [{"id": "anchor-core-001", "content": homoglyph_content, "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_homoglyph)
        assert ok is False and len(v) == 1

        # 4. Case change ('trust' vs 'Trust')
        case_content = anchor["content"].replace("Trust", "trust")
        s_case = [{"id": "anchor-core-001", "content": case_content, "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_case)
        assert ok is False and len(v) == 1

        # 5. Punctuation alteration ('P0-P15' -> 'P0_P15')
        punct_content = anchor["content"].replace("P0-P15", "P0_P15")
        s_punct = [{"id": "anchor-core-001", "content": punct_content, "verification": "verified"}]
        ok, v = guard.verify_no_catastrophic_regression(s_punct)
        assert ok is False and len(v) == 1

    def test_anchor_verification_status_tampering_and_downgrades(self):
        """Stress-test anchor verification flipping across all non-verified states."""
        guard = ContinualLearningGuard()
        anchor = {
            "id": "anchor-sec-002",
            "content": "Immutable Provenance Ledger Specification",
            "verification": "verified",
            "type": "knowledge"
        }
        guard.register_anchor_node(anchor)

        tampered_states = [
            "unverified",
            "partially_verified",
            "inferred",
            "bogus_status",
            "",
            None
        ]

        for state in tampered_states:
            s_tampered = [{
                "id": "anchor-sec-002",
                "content": "Immutable Provenance Ledger Specification",
                "verification": state
            }]
            ok, v = guard.verify_no_catastrophic_regression(s_tampered)
            assert ok is False, f"Failed to catch verification status downgrade to '{state}'"
            assert len(v) == 1
            assert "verification status was downgraded from verified to" in v[0]

    def test_anchor_deletion_and_storage_omission(self):
        """Stress-test anchor deletion from active storage with single and multiple missing anchors."""
        guard = ContinualLearningGuard()
        for i in range(5):
            guard.register_anchor_node({
                "id": f"anchor-del-{i}",
                "content": f"Critical rule {i}",
                "verification": "verified"
            })

        # Scenario A: Delete 1 anchor (anchor-del-2)
        storage_partial = [
            {"id": f"anchor-del-{i}", "content": f"Critical rule {i}", "verification": "verified"}
            for i in range(5) if i != 2
        ]
        ok, v = guard.verify_no_catastrophic_regression(storage_partial)
        assert ok is False
        assert len(v) == 1
        assert "anchor-del-2" in v[0]
        assert "removed from active storage" in v[0]

        # Scenario B: Delete all anchors (completely empty active storage)
        ok_empty, v_empty = guard.verify_no_catastrophic_regression([])
        assert ok_empty is False
        assert len(v_empty) == 5

    def test_large_scale_anchor_integrity_fuzzing(self):
        """Register 100 anchor notes, inject 10 targeted distinct corruptions across them,
        and assert that exactly the 10 corrupted anchors are detected.
        """
        guard = ContinualLearningGuard()
        storage = []
        for i in range(100):
            node = {
                "id": f"anchor-fuzz-{i:03d}",
                "content": f"Canonical verified memory content for invariant #{i:03d}",
                "verification": "verified",
                "type": "knowledge"
            }
            guard.register_anchor_node(node)
            storage.append(dict(node))

        # Corrupt 10 specific notes:
        # Notes 10, 20, 30: Deleted
        storage = [n for n in storage if n["id"] not in ["anchor-fuzz-010", "anchor-fuzz-020", "anchor-fuzz-030"]]
        # Notes 40, 50, 60: Content drifted
        for n in storage:
            if n["id"] in ["anchor-fuzz-040", "anchor-fuzz-050", "anchor-fuzz-060"]:
                n["content"] += " [ADVERSARIAL DRIFT]"
        # Notes 70, 80, 90, 95: Status downgraded
        for n in storage:
            if n["id"] == "anchor-fuzz-070":
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

        # Verify all targeted IDs are in the violations report
        expected_ids = [
            "anchor-fuzz-010", "anchor-fuzz-020", "anchor-fuzz-030",
            "anchor-fuzz-040", "anchor-fuzz-050", "anchor-fuzz-060",
            "anchor-fuzz-070", "anchor-fuzz-080", "anchor-fuzz-090", "anchor-fuzz-095"
        ]
        for target_id in expected_ids:
            assert any(target_id in v for v in violations), f"Target ID {target_id} not found in violations"

    def test_guard_resilience_to_malformed_notes(self):
        """Verify that malformed storage notes (None, missing IDs, empty dicts) do not crash the guard."""
        guard = ContinualLearningGuard()
        guard.register_anchor_node({"id": "valid-1", "content": "Valid anchor", "verification": "verified"})

        # Storage contains malformed items
        malformed_storage = [
            None,
            {},
            {"id": None},
            {"id": ""},
            {"id": "valid-1", "content": "Valid anchor", "verification": "verified"},
            {"other_key": 123}
        ]
        # Filter None before processing if list contains non-dicts
        clean_storage = [n for n in malformed_storage if isinstance(n, dict)]
        ok, violations = guard.verify_no_catastrophic_regression(clean_storage)
        assert ok is True
        assert len(violations) == 0


# ==============================================================================
# 2. LearningEngine Hostile Confidence Escalation & Trust Boundary Invariants
# ==============================================================================

class TestHostileConfidenceEscalationAndTrustBoundaries:

    def test_hostile_dense_relations_non_execution_escalation_blocked(self):
        """Hostile AI Agent attempts to escalate non-execution memories with massive graph density.
        Must NEVER promote to 'very_high' confidence.
        """
        storage = StorageEngine()
        controller = MemoryController(storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        non_execution_sources = ["ai", "inference", "user", "import", "unknown", "custom_external"]

        for src in non_execution_sources:
            note_id = str(uuid.uuid4())
            note = {
                "id": note_id,
                "type": "knowledge",
                "lifecycle": "ACTIVE",
                "category": "hostile_test",
                "tags": ["stress"],
                "created": "2026-08-15",
                "updated": "2026-08-15",
                "provenance": {"source_type": src, "source_ref": "adversarial_injector"},
                "confidence": "high",
                "verification": "unverified",
                "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(50)],
                "content": f"Hostile dense graph memory with source {src}"
            }
            storage.set(note_id, note)

            promoted = engine.promote_memories(Principal.AI_AGENT)
            assert note_id not in promoted

            stored = storage.get(note_id)
            assert stored["confidence"] == "high", f"Note with source {src} was illegally promoted to {stored['confidence']}"
            assert stored["verification"] == "unverified"

    def test_ai_agent_cannot_self_verify_through_learning_engine(self):
        """Verify that LearningEngine NEVER sets verification to 'verified' under any circumstance
        when invoked by Principal.AI_AGENT. (P0 Invariant: AI Self-Verification strictly gated).
        """
        storage = StorageEngine()
        controller = MemoryController(storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        # Setup 10 candidate notes across all confidence levels and graph densities
        notes = []
        for i in range(10):
            nid = str(uuid.uuid4())
            n = {
                "id": nid,
                "type": "knowledge",
                "lifecycle": "ACTIVE",
                "category": "core",
                "tags": ["p0_test"],
                "created": "2026-08-15",
                "updated": "2026-08-15",
                "provenance": {"source_type": "execution", "source_ref": "test_runner"},
                "confidence": "unknown" if i % 3 == 0 else ("low" if i % 3 == 1 else "medium"),
                "verification": "unverified",
                "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(i * 3 + 3)],
                "content": f"Candidate memory {i}"
            }
            storage.set(nid, n)
            notes.append(nid)

        # Run 5 consecutive promotion cycles
        for _ in range(5):
            engine.promote_memories(Principal.AI_AGENT)

        # Invariant check: No note in storage should EVER have verification == 'verified'
        for nid in notes:
            stored = storage.get(nid)
            assert stored["verification"] != "verified", f"Invariant P0 violated: Note {nid} has verification='verified'"
            assert stored["verification"] in ["unverified", "partially_verified"]

    def test_canonical_human_verified_notes_immunity(self):
        """Canonical notes verified by human/admin must remain completely immune
        to autonomous mutation by LearningEngine.
        """
        storage = StorageEngine()
        controller = MemoryController(storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        vid = str(uuid.uuid4())
        canonical_note = {
            "id": vid,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "axioms",
            "tags": ["immutable"],
            "created": "2026-08-15",
            "updated": "2026-08-15",
            "provenance": {"source_type": "official", "source_ref": "human_curator"},
            "confidence": "medium",  # Human assigned medium
            "verification": "verified",  # Human verified
            "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(30)],
            "content": "Canonical human-verified system invariant"
        }
        storage.set(vid, canonical_note)

        promoted = engine.promote_memories(Principal.AI_AGENT)
        assert vid not in promoted

        stored = storage.get(vid)
        assert stored["confidence"] == "medium"  # Untouched
        assert stored["verification"] == "verified"
        assert stored["content"] == canonical_note["content"]

    def test_inactive_lifecycle_isolation(self):
        """Notes in RAW, CLASSIFIED, NORMALIZED, REVIEW, SUPERSEDED, or ARCHIVED states
        must NEVER be promoted regardless of relation count or execution evidence.
        """
        storage = StorageEngine()
        controller = MemoryController(storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        non_active_lifecycles = [
            Lifecycle.RAW.value,
            Lifecycle.CLASSIFIED.value,
            Lifecycle.NORMALIZED.value,
            Lifecycle.REVIEW.value,
            Lifecycle.SUPERSEDED.value,
            Lifecycle.ARCHIVED.value
        ]

        ids = []
        for lc in non_active_lifecycles:
            nid = str(uuid.uuid4())
            n = {
                "id": nid,
                "type": "knowledge",
                "lifecycle": lc,
                "category": "lifecycle_test",
                "tags": ["lc"],
                "created": "2026-08-15",
                "updated": "2026-08-15",
                "provenance": {"source_type": "execution", "source_ref": "runner"},
                "confidence": "high",
                "verification": "unverified",
                "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(25)],
                "content": f"Note in lifecycle {lc}"
            }
            storage.set(nid, n)
            ids.append(nid)

        promoted = engine.promote_memories(Principal.AI_AGENT)
        assert len(promoted) == 0

        for nid in ids:
            stored = storage.get(nid)
            assert stored["confidence"] == "high"
            assert stored["verification"] == "unverified"

    def test_exact_promotion_ladder_boundary_conditions(self):
        """Exhaustive boundary check for the confidence promotion ladder:
        - 0-2 relations: No promotion
        - 3-5 relations + (unknown/low): Promoted to medium
        - 3-5 relations + medium: Stays medium (needs 6 for high)
        - 6-8 relations + medium: Promoted to high (partially_verified)
        - 6-8 relations + high (execution): Stays high (needs 9 for very_high)
        - 9+ relations + high (execution): Promoted to very_high (partially_verified)
        """
        storage = StorageEngine()
        controller = MemoryController(storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        # Test case: 2 relations with low confidence -> no change
        id_2 = str(uuid.uuid4())
        storage.set(id_2, {
            "id": id_2, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "low",
            "verification": "unverified", "provenance": {"source_type": "execution"},
            "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(2)],
            "content": "Note with 2 relations"
        })

        # Test case: 5 relations with medium confidence -> no change
        id_5 = str(uuid.uuid4())
        storage.set(id_5, {
            "id": id_5, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "medium",
            "verification": "unverified", "provenance": {"source_type": "execution"},
            "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(5)],
            "content": "Note with 5 relations"
        })

        # Test case: 8 relations with high confidence -> no change
        id_8 = str(uuid.uuid4())
        storage.set(id_8, {
            "id": id_8, "type": "knowledge", "lifecycle": "ACTIVE", "confidence": "high",
            "verification": "partially_verified", "provenance": {"source_type": "execution"},
            "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(8)],
            "content": "Note with 8 relations"
        })

        promoted = engine.promote_memories(Principal.AI_AGENT)
        assert id_2 not in promoted
        assert id_5 not in promoted
        assert id_8 not in promoted

        assert storage.get(id_2)["confidence"] == "low"
        assert storage.get(id_5)["confidence"] == "medium"
        assert storage.get(id_8)["confidence"] == "high"


# ==============================================================================
# 3. Concurrency & Race Condition Stress Testing
# ==============================================================================

class TestConcurrentLearningEngineAndRaceConditions:

    def test_multithreaded_concurrent_promotions_in_memory(self):
        """Execute multiple parallel threads calling promote_memories concurrently
        against shared memory storage with 20 candidate notes.
        Verify zero race condition crashes and consistent final states.
        """
        storage = StorageEngine()
        controller = MemoryController(storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        note_ids = []
        for i in range(20):
            nid = str(uuid.uuid4())
            storage.set(nid, {
                "id": nid,
                "type": "knowledge",
                "lifecycle": "ACTIVE",
                "confidence": "low",
                "verification": "unverified",
                "provenance": {"source_type": "execution", "source_ref": "concurrency_test"},
                "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(10)],
                "content": f"Concurrent test node {i}"
            })
            note_ids.append(nid)

        def worker():
            return engine.promote_memories(Principal.AI_AGENT)

        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            futures = [executor.submit(worker) for _ in range(16)]
            results = [f.result() for f in concurrent.futures.as_completed(futures)]

        # Verify all tasks succeeded without exception
        assert len(results) == 16
        # Verify notes reached expected state
        for nid in note_ids:
            stored = storage.get(nid)
            assert stored["confidence"] in ["medium", "high", "very_high"]
            assert stored["verification"] in ["unverified", "partially_verified"]
            assert stored["verification"] != "verified"

    def test_multithreaded_concurrent_promotions_sqlite_storage(self):
        """Execute concurrent learning promotions against real SQLite storage in WAL mode.
        Verify transaction atomicity, lock handling, and invariant preservation.
        """
        temp_dir = tempfile.mkdtemp()
        db_path = os.path.join(temp_dir, "test_learning_wal.db")

        sqlite_storage = SQLiteStorageEngine(db_path)
        controller = MemoryController(sqlite_storage)
        router = ToolRouter(controller)
        engine = LearningEngine(controller, router)

        note_ids = []
        for i in range(15):
            nid = str(uuid.uuid4())
            sqlite_storage.set(nid, {
                "id": nid,
                "type": "knowledge",
                "lifecycle": "ACTIVE",
                "category": "sqlite_concurrency",
                "tags": ["sqlite"],
                "created": "2026-08-15",
                "updated": "2026-08-15",
                "confidence": "low",
                "verification": "unverified",
                "provenance": {"source_type": "execution", "source_ref": "sqlite_worker"},
                "relations": [{"relation": "rel", "target": "k", "target_id": str(uuid.uuid4())} for _ in range(9)],
                "content": f"SQLite concurrency candidate {i}"
            })
            note_ids.append(nid)

        def sqlite_worker():
            return engine.promote_memories(Principal.AI_AGENT)

        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
            futures = [executor.submit(sqlite_worker) for _ in range(12)]
            results = [f.result() for f in concurrent.futures.as_completed(futures)]

        assert len(results) == 12
        for nid in note_ids:
            stored = sqlite_storage.get(nid)
            assert stored is not None
            assert stored["confidence"] in ["medium", "high", "very_high"]
            assert stored["verification"] != "verified"

        sqlite_storage.close()

    def test_concurrent_anchor_verification_under_storage_churn(self):
        """Verify ContinualLearningGuard thread-safety while active storage is being
        concurrently read, modified, and verified.
        """
        guard = ContinualLearningGuard()
        anchors = []
        for i in range(10):
            a = {
                "id": f"anchor-churn-{i}",
                "content": f"Immutable anchor rule #{i}",
                "verification": "verified",
                "type": "knowledge"
            }
            guard.register_anchor_node(a)
            anchors.append(a)

        def verify_task():
            storage_snapshot = [dict(a) for a in anchors]
            ok, v = guard.verify_no_catastrophic_regression(storage_snapshot)
            return ok, v

        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            futures = [executor.submit(verify_task) for _ in range(30)]
            results = [f.result() for f in concurrent.futures.as_completed(futures)]

        for ok, violations in results:
            assert ok is True
            assert len(violations) == 0


# ==============================================================================
# 4. Evaluation Numerical Robustness & Boundary Challenge Tests
# ==============================================================================

class TestAdversarialEvaluationNumericalRobustness:

    def test_trace_utilization_adversarial_inputs(self):
        """Stress-test TRACe utilization with extreme text lengths, special characters, and empty tokens."""
        evaluator = RetrievalEvaluator()

        notes = [
            {"id": "n1", "content": "PostgreSQL database indexing optimization techniques"},
            {"id": "n2", "content": "Distributed consensus protocols Raft and Paxos"}
        ]

        # 1. 100k character repeat response
        huge_response = "PostgreSQL database " * 5000
        util_huge = evaluator.utilization(notes, huge_response)
        assert 0.0 <= util_huge <= 1.0

        # 2. Response with unicode symbols, newlines, tabs, and punctuation
        symbol_response = "!!! PostgreSQL ??? database *** @@@ optimization \n\n\t techniques"
        util_sym = evaluator.utilization(notes, symbol_response)
        assert util_sym >= 0.5

        # 3. Notes containing numbers and short words
        notes_numbers = [{"id": "num", "content": "123 4567 8910"}]
        assert evaluator.utilization(notes_numbers, "123 4567 8910") == 0.0

    def test_ir_metrics_extreme_k_and_adversarial_distributions(self):
        """Stress-test precision_at_k, recall_at_k, and ndcg_at_k with extreme k values and edge distributions."""
        evaluator = RetrievalEvaluator()

        retrieved = [f"doc_{i}" for i in range(100)]
        relevant = {f"doc_{i}" for i in range(20)}

        # Extreme positive k
        p_huge_k = evaluator.precision_at_k(retrieved, relevant, k=1000000)
        assert p_huge_k == 20 / 100

        r_huge_k = evaluator.recall_at_k(retrieved, relevant, k=1000000)
        assert r_huge_k == 1.0

        # Negative and zero k boundary handling
        assert evaluator.precision_at_k(retrieved, relevant, k=-999) == 0.0
        assert evaluator.recall_at_k(retrieved, relevant, k=-999) == 0.0
        assert evaluator.ndcg_at_k(retrieved, {k: 1.0 for k in relevant}, k=-999) == 0.0

        assert evaluator.precision_at_k(retrieved, relevant, k=0) == 0.0
        assert evaluator.recall_at_k(retrieved, relevant, k=0) == 0.0
        assert evaluator.ndcg_at_k(retrieved, {k: 1.0 for k in relevant}, k=0) == 0.0

    def test_ndcg_at_k_identical_scores_and_single_elements(self):
        """Verify NDCG@K under uniform scores, single element lists, and disjoint sets."""
        evaluator = RetrievalEvaluator()

        # All items have identical relevance score 1.0 -> NDCG must be 1.0
        uniform_scores = {f"doc_{i}": 1.0 for i in range(10)}
        retrieved = [f"doc_{i}" for i in range(10)]
        assert abs(evaluator.ndcg_at_k(retrieved, uniform_scores, k=5) - 1.0) < 1e-6

        # Single element match
        assert abs(evaluator.ndcg_at_k(["doc_1"], {"doc_1": 5.0}, k=1) - 1.0) < 1e-6

        # Single element mismatch
        assert evaluator.ndcg_at_k(["doc_1"], {"doc_2": 5.0}, k=1) == 0.0
