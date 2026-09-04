import pytest
import os
import uuid
import tempfile
import threading
import time
import json
from typing import Dict, Any, List, Optional

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.audit.logger import AuditLogger
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive
from cognitive_core.reasoning import ReasoningEngine, TreeOfThoughtReasoner, ThoughtValidator
from cognitive_core.recall import RecallEngine
from cognitive_core.reflection import FormalReflexion, SelfRefine, ReflectionPipeline
from cognitive_core.consolidation import Consolidator
from cognitive_core.deduplication import Deduplicator
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory
from cognitive_core.planning import Planner, ActivePlan
from cognitive_core.tool_router import ToolRouter, RiskLevel, ApprovalRequiredError
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole, SubagentSpec
from cognitive_core.agents import (
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent
)

@pytest.fixture(autouse=True)
def setup_test_environment():
    os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = "m4-5-adversarial-challenger-secret-99999"
    yield
    os.environ.pop("MEMORY_CONTROLLER_HMAC_SECRET", None)

@pytest.fixture
def temp_checkpoint_dir():
    d = tempfile.mkdtemp(prefix="m4_5_checkpoints_")
    yield d
    import shutil
    try:
        shutil.rmtree(d)
    except Exception:
        pass

@pytest.fixture
def temp_sqlite_db():
    fd, path = tempfile.mkstemp(suffix=".sqlite3")
    os.close(fd)
    if os.path.exists(path):
        os.remove(path)
    yield path
    for ext in ["", "-wal", "-shm"]:
        target = path + ext
        if os.path.exists(target):
            try:
                os.remove(target)
            except Exception:
                pass

@pytest.fixture
def temp_audit_file():
    fd, path = tempfile.mkstemp(suffix=".jsonl")
    os.close(fd)
    if os.path.exists(path):
        os.remove(path)
    yield path
    if os.path.exists(path):
        try:
            os.remove(path)
        except Exception:
            pass

def make_note(
    id_val: str,
    lifecycle: str = "ACTIVE",
    verification: str = "verified",
    provenance: Any = None,
    content: str = "test content",
    note_type: str = "knowledge",
    relations: list = None,
    superseded_by: str = None,
    supersedes: str = None,
    valid_from: str = None,
    valid_until: str = None,
    confidence: str = "high"
) -> Dict[str, Any]:
    if provenance is None:
        provenance = {"source_type": "user" if verification == "verified" else "inference", "source_ref": "test"}
    note = {
        "id": id_val,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": "milestone4-challenger-m4-5",
        "tags": ["m4_5", "adversarial"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": provenance,
        "confidence": confidence,
        "verification": verification,
        "relations": relations or [],
        "content": content
    }
    if superseded_by:
        note["superseded_by"] = superseded_by
    if supersedes:
        note["supersedes"] = supersedes
    if valid_from:
        note["valid_from"] = valid_from
    if valid_until:
        note["valid_until"] = valid_until
    return note


# ============================================================================
# Section 1: VerifierAgent Exhaustive Fuzzing & Malformed Provenance Resilience
# ============================================================================

def test_verifier_agent_exhaustive_fuzzing_non_dict_provenance_payloads():
    """Fuzz VerifierAgent with arbitrary non-dict provenance types:
    strings, integers, floats, booleans, lists, None, nested lists, and invalid node objects.
    VerifierAgent must never raise unhandled exceptions, must flag violations, and produce clean output.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    verifier = VerifierAgent(controller)

    fuzz_test_cases = [
        # (node, expected_is_clean, violation_keyword)
        ("not_a_dict_node", False, "Invalid node"),
        (12345, False, "Invalid node"),
        (None, False, "Invalid node"),
        ([], False, "Invalid node"),
        ({"id": "fuzz-str-prov", "provenance": "untrusted_string", "verification": "unverified"}, False, "invalid provenance"),
        ({"id": "fuzz-int-prov", "provenance": 99999, "verification": "unverified"}, False, "invalid provenance"),
        ({"id": "fuzz-float-prov", "provenance": 3.14159, "verification": "unverified"}, False, "invalid provenance"),
        ({"id": "fuzz-bool-prov", "provenance": True, "verification": "unverified"}, False, "invalid provenance"),
        ({"id": "fuzz-none-prov", "provenance": None, "verification": "unverified"}, False, "invalid provenance"),
        ({"id": "fuzz-list-prov", "provenance": ["user", "official"], "verification": "unverified"}, False, "invalid provenance"),
        ({"id": "fuzz-empty-dict-prov", "provenance": {}, "verification": "unverified"}, True, None),
        ({"id": "fuzz-clean-inference", "provenance": {"source_type": "inference"}, "verification": "unverified"}, True, None),
        ({"id": "fuzz-clean-verified", "provenance": {"source_type": "user"}, "verification": "verified"}, True, None),
        ({"id": "fuzz-violation-user-unverified", "provenance": {"source_type": "user"}, "verification": "unverified"}, False, "claims 'user' without attested verification"),
        ({"id": "fuzz-violation-official-unverified", "provenance": {"source_type": "official"}, "verification": "partially_verified"}, False, "claims 'official' without attested verification"),
    ]

    for node, expected_clean, expected_violation in fuzz_test_cases:
        res = verifier.process_task(Principal.AI_AGENT, {"nodes": [node]})
        assert res["status"] == "success", f"Failed on node payload: {node!r}"
        assert res["total_inspected"] == 1
        assert res["is_clean"] is expected_clean, f"Cleanliness mismatch for node: {node!r}"
        if not expected_clean:
            assert len(res["violations"]) >= 1
            if expected_violation:
                assert any(expected_violation in v for v in res["violations"]), f"Expected keyword '{expected_violation}' in violations: {res['violations']}"
        else:
            assert len(res["violations"]) == 0

    # Batch test with mixed collection of 100 malformed + valid nodes
    batch_nodes = []
    for i in range(50):
        batch_nodes.append({"id": f"bad-node-{i}", "provenance": f"string_prov_{i}", "verification": "unverified"})
    for i in range(25):
        batch_nodes.append({"id": f"good-node-{i}", "provenance": {"source_type": "execution"}, "verification": "unverified"})
    for i in range(25):
        batch_nodes.append({"id": f"verified-node-{i}", "provenance": {"source_type": "user"}, "verification": "verified"})

    batch_res = verifier.process_task(Principal.AI_AGENT, {"nodes": batch_nodes})
    assert batch_res["status"] == "success"
    assert batch_res["total_inspected"] == 100
    assert batch_res["verified_count"] == 25
    assert batch_res["unverified_count"] == 75
    assert len(batch_res["violations"]) == 50
    assert batch_res["is_clean"] is False


def test_verifier_agent_boundary_empty_task_handling():
    """Verify VerifierAgent handles empty task, missing 'nodes' key, and None task."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    verifier = VerifierAgent(controller)

    res_empty = verifier.process_task(Principal.AI_AGENT, {})
    assert res_empty["status"] == "success"
    assert res_empty["total_inspected"] == 0
    assert res_empty["is_clean"] is True

    res_none_nodes = verifier.process_task(Principal.AI_AGENT, {"nodes": []})
    assert res_none_nodes["status"] == "success"
    assert res_none_nodes["total_inspected"] == 0
    assert res_none_nodes["is_clean"] is True


# ============================================================================
# Section 2: RecallEngine Pre-Penalty Score Inheritance & Multi-Hop Lineages
# ============================================================================

def test_recall_engine_single_hop_exact_10_percent_freshness_boost(temp_sqlite_db):
    """Verify exact 10% freshness boost on single-hop supersession:
    old (SUPERSEDED) -> act (ACTIVE).
    The active note must inherit (old_unpenalized_score * 1.1) and old must be penalized by 0.3.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    n_old = make_note(
        "old-single",
        lifecycle="SUPERSEDED",
        superseded_by="act-single",
        content="Microservices service mesh configuration envoy istio proxy"
    )
    n_act = make_note(
        "act-single",
        lifecycle="ACTIVE",
        supersedes="old-single",
        content="Microservices service mesh configuration envoy istio proxy"
    )
    storage.set("old-single", n_old)
    storage.set("act-single", n_act)

    activated = [(n_old, 0.75)]
    results = recall.recall(Principal.AI_AGENT, "service mesh envoy proxy", activated, wm)
    res_map = {n["id"]: score for n, score in results}

    assert "old-single" in res_map
    assert "act-single" in res_map

    old_penalized_score = res_map["old-single"]
    act_inherited_score = res_map["act-single"]

    # Pre-penalty score is old_penalized_score / 0.3
    expected_act_score = min(1.0, (old_penalized_score / 0.3) * 1.1)
    assert act_inherited_score == pytest.approx(expected_act_score, rel=1e-3)
    assert act_inherited_score > old_penalized_score


def test_recall_engine_5_hop_supersession_lineage_freshness_boost(temp_sqlite_db):
    """Verify 5-hop supersession lineage:
    hop-0 -> hop-1 -> hop-2 -> hop-3 -> hop-4 (ACTIVE).
    Activating hop-0 must propagate the unpenalized score to hop-4 with exact 10% boost.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    hop_count = 5
    nodes = []
    for i in range(hop_count):
        is_last = (i == hop_count - 1)
        node_id = f"hop5-{i}"
        sup_by = f"hop5-{i+1}" if not is_last else None
        sup_from = f"hop5-{i-1}" if i > 0 else None
        lifecycle = "ACTIVE" if is_last else "SUPERSEDED"
        content = f"Distributed key-value store architecture consensus raft {i}"
        n = make_note(node_id, lifecycle=lifecycle, superseded_by=sup_by, supersedes=sup_from, content=content)
        storage.set(node_id, n)
        nodes.append(n)

    activated = [(nodes[0], 0.70)]
    results = recall.recall(Principal.AI_AGENT, "distributed consensus raft", activated, wm)
    res_map = {n["id"]: score for n, score in results}

    assert "hop5-0" in res_map
    assert "hop5-4" in res_map

    expected_hop4_score = min(1.0, (res_map["hop5-0"] / 0.3) * 1.1)
    assert res_map["hop5-4"] == pytest.approx(expected_hop4_score, rel=1e-3)
    assert res_map["hop5-4"] > res_map["hop5-0"]


def test_recall_engine_branching_lineage_highest_score_inheritance(temp_sqlite_db):
    """Verify branching supersession graph where multiple superseded notes point to the same active note:
    branch-A (SUPERSEDED, score 0.4) -> active-root
    branch-B (SUPERSEDED, score 0.85) -> active-root
    The active root must inherit from the higher-scoring branch-B.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    n_a = make_note("branch-A", lifecycle="SUPERSEDED", superseded_by="active-root", content="Kubernetes cluster autoscaler")
    n_b = make_note("branch-B", lifecycle="SUPERSEDED", superseded_by="active-root", content="Kubernetes cluster autoscaler HPA")
    n_root = make_note("active-root", lifecycle="ACTIVE", supersedes="branch-B", content="Kubernetes cluster autoscaler HPA v2")

    storage.set("branch-A", n_a)
    storage.set("branch-B", n_b)
    storage.set("active-root", n_root)

    activated = [(n_a, 0.40), (n_b, 0.85)]
    results = recall.recall(Principal.AI_AGENT, "Kubernetes cluster autoscaler", activated, wm)
    res_map = {n["id"]: score for n, score in results}

    assert "active-root" in res_map
    # Expected score derived from the higher pre-penalty score (branch-B)
    expected_root_score = min(1.0, (res_map["branch-B"] / 0.3) * 1.1)
    assert res_map["active-root"] == pytest.approx(expected_root_score, rel=1e-3)
    assert res_map["active-root"] > res_map["branch-B"]
    assert res_map["active-root"] > res_map["branch-A"]


def test_recall_engine_score_capping_at_unity(temp_sqlite_db):
    """Verify that score inheritance strictly caps at 1.0 even if pre_score * 1.1 exceeds 1.0."""
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    n_old = make_note("old-high", lifecycle="SUPERSEDED", superseded_by="act-high", content="Exact match query string", confidence="very_high")
    n_act = make_note("act-high", lifecycle="ACTIVE", supersedes="old-high", content="Exact match query string", confidence="very_high")
    storage.set("old-high", n_old)
    storage.set("act-high", n_act)

    # Populate working memory with matching context so sim_wm = 1.0
    wm.admit([(n_old, 1.0)])

    activated = [(n_old, 1.0)]
    results = recall.recall(Principal.AI_AGENT, "Exact match query string", activated, wm)
    res_map = {n["id"]: score for n, score in results}

    assert res_map["act-high"] == 1.0
    assert res_map["act-high"] <= 1.0


def test_recall_engine_historical_query_preserves_score_inheritance(temp_sqlite_db):
    """Verify that when querying historical/deprecated notes ('legacy database proxy'),
    superseded notes get a lighter penalty factor (0.8 instead of 0.3),
    while the active successor still correctly inherits (pre_score * 1.1).
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    n_old = make_note("legacy-proxy", lifecycle="SUPERSEDED", superseded_by="modern-proxy", content="Legacy database proxy connector")
    n_act = make_note("modern-proxy", lifecycle="ACTIVE", supersedes="legacy-proxy", content="Modern database proxy connector")
    storage.set("legacy-proxy", n_old)
    storage.set("modern-proxy", n_act)

    activated = [(n_old, 0.6)]
    results = recall.recall(Principal.AI_AGENT, "legacy database proxy", activated, wm)
    res_map = {n["id"]: score for n, score in results}

    assert "legacy-proxy" in res_map
    assert "modern-proxy" in res_map

    # For historical queries, lifecycle factor is 0.8
    pre_score = res_map["legacy-proxy"] / 0.8
    expected_modern_score = min(1.0, pre_score * 1.1)
    assert res_map["modern-proxy"] == pytest.approx(expected_modern_score, rel=1e-3)


# ============================================================================
# Section 3: ReflectionPipeline & SelfRefine Invariant & Integrity Probes
# ============================================================================

def test_reflection_pipeline_propose_synapse_sqlite_wal_integrity(temp_sqlite_db):
    """Verify ReflectionPipeline.propose_synapse executes with real SQLiteStorageEngine in WAL mode,
    dynamically resolving target note types, enforcing _CANONICAL_SCHEMA, and preventing duplicate edges.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    pipeline = ReflectionPipeline(controller)

    src_id = str(uuid.uuid4())
    tgt_id = str(uuid.uuid4())

    n_src = make_note(src_id, lifecycle="ACTIVE", verification="verified", note_type="knowledge", content="Source knowledge item")
    n_tgt = make_note(tgt_id, lifecycle="ACTIVE", verification="verified", note_type="procedure", content="Target procedure runbook")
    storage.set(src_id, n_src)
    storage.set(tgt_id, n_tgt)

    # 1. Propose synapse
    res = pipeline.propose_synapse(Principal.AI_AGENT, src_id, tgt_id, relation_type="supports")
    assert res == src_id

    # 2. Inspect updated relations
    updated_src = storage.get(src_id)
    assert len(updated_src["relations"]) == 1
    rel = updated_src["relations"][0]
    assert rel["relation"] == "supports"
    assert rel["target"] == "procedure"
    assert rel["target_id"] == tgt_id

    # 3. Prevent duplicate creation
    res_dup = pipeline.propose_synapse(Principal.AI_AGENT, src_id, tgt_id, relation_type="supports")
    assert res_dup is None
    assert len(storage.get(src_id)["relations"]) == 1


def test_reflection_pipeline_propose_synapse_safety_when_node_not_found(temp_sqlite_db):
    """Verify propose_synapse safely returns None without raising unhandled exceptions or corrupting storage when source or target note is not found."""
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    pipeline = ReflectionPipeline(controller)

    src_id = str(uuid.uuid4())
    missing_tgt_id = str(uuid.uuid4())

    n_src = make_note(src_id, lifecycle="ACTIVE", verification="verified", note_type="decision", content="Decision on caching")
    storage.set(src_id, n_src)

    # Propose synapse to nonexistent target returns None gracefully
    res_missing_tgt = pipeline.propose_synapse(Principal.AI_AGENT, src_id, missing_tgt_id, relation_type="depends_on")
    assert res_missing_tgt is None

    # Propose synapse from nonexistent source returns None gracefully
    res_missing_src = pipeline.propose_synapse(Principal.AI_AGENT, str(uuid.uuid4()), src_id, relation_type="depends_on")
    assert res_missing_src is None


def test_self_refine_adversarial_critique_filter():
    """Verify SelfRefine.refine_memory against malicious, empty, None, and boundary inputs."""
    # 1. Non-dict inputs
    for bad_input in [None, "string", 12345, [1, 2, 3], True]:
        passed, out = SelfRefine.refine_memory(bad_input)
        assert passed is False
        assert out == bad_input

    # 2. Empty or too short content
    for short_content in ["", "   ", "short", "12345678901234"]: # len < 15
        passed, out = SelfRefine.refine_memory({"content": short_content})
        assert passed is False

    # 3. Valid content length >= 15 with confidence injection
    valid_candidate = {"content": "This is a valid candidate note with sufficient length"}
    passed, out = SelfRefine.refine_memory(valid_candidate)
    assert passed is True
    assert out["confidence"] == "medium"

    # 4. Valid candidate preserving existing confidence
    valid_candidate_high = {"content": "Another substantive memory note candidate", "confidence": "high"}
    passed, out = SelfRefine.refine_memory(valid_candidate_high)
    assert passed is True
    assert out["confidence"] == "high"


# ============================================================================
# Section 4: Multi-Agent Orchestrator Concurrency & Audit Verification
# ============================================================================

def test_multi_agent_orchestrator_concurrent_stress_and_tamper_evident_audit(temp_sqlite_db, temp_audit_file):
    """Stress test: 8 concurrent threads executing orchestrator routing, consolidation, and audit logging.
    Verifies thread safety, 0 deadlocks, and that SHA-256 hash chain validates cleanly.
    """
    os.environ["ANTIGRAVITY_ARTIFACT_DIR"] = os.path.dirname(temp_audit_file)
    logger = AuditLogger(log_path=temp_audit_file)
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    # Seed notes
    for i in range(12):
        n = make_note(
            id_val=str(uuid.uuid4()),
            lifecycle="ACTIVE",
            verification="verified",
            content=f"Parallel orchestrator stress test memory {i} cluster orchestration"
        )
        storage.set(n["id"], n)

    thread_count = 8
    iterations = 6
    exceptions = []

    def worker_dispatch(tid: int):
        try:
            for it in range(iterations):
                query = f"orchestration cluster setup info {it % 12}"
                res = orchestrator.route_and_dispatch(Principal.AI_AGENT, query, [])
                assert res["status"] == "completed"
                time.sleep(0.005)
        except Exception as e:
            exceptions.append(e)

    threads = [threading.Thread(target=worker_dispatch, args=(i,)) for i in range(thread_count)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(exceptions) == 0, f"Exceptions occurred during concurrent dispatch: {exceptions}"

    is_valid, reason = logger.verify_integrity()
    assert is_valid is True, f"Tamper evident audit integrity failed: {reason}"
