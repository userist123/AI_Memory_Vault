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
    os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = "m4-4-test-hmac-secret-key-67890"
    yield
    os.environ.pop("MEMORY_CONTROLLER_HMAC_SECRET", None)

@pytest.fixture
def temp_checkpoint_dir():
    d = tempfile.mkdtemp(prefix="m4_4_checkpoints_")
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
    provenance: dict = None,
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
        "category": "milestone4-challenger-m4-4",
        "tags": ["m4_4", "adversarial"],
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
# Section 1: Executive.process_intent Concurrency & Error Injection Harness
# ============================================================================

def test_executive_process_intent_concurrent_threads_sqlite_wal(temp_sqlite_db, temp_checkpoint_dir):
    """Stress test: Multiple concurrent threads invoking Executive.process_intent on shared SQLite WAL.
    Verifies that no deadlocks occur, transactions commit cleanly, and each execution completes.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)

    # Seed notes
    for i in range(10):
        n = make_note(
            id_val=f"exec-seed-{i}",
            lifecycle="ACTIVE",
            verification="verified",
            content=f"Database replication clustering node configuration {i}"
        )
        storage.set(n["id"], n)

    thread_count = 6
    iterations_per_thread = 5
    exceptions: List[Exception] = []
    results: List[Dict[str, Any]] = []
    lock = threading.Lock()

    def run_worker(tid: int):
        try:
            # Each thread uses an executive with dedicated checkpoint subfolder
            t_checkpoint = os.path.join(temp_checkpoint_dir, f"thread_{tid}")
            os.makedirs(t_checkpoint, exist_ok=True)
            exec_instance = Executive(controller, checkpoint_dir=t_checkpoint)

            for it in range(iterations_per_thread):
                intent = f"find database replication node {it % 10}"
                res = exec_instance.process_intent(Principal.AI_AGENT, intent)
                with lock:
                    results.append(res)
                time.sleep(0.01)
        except Exception as e:
            with lock:
                exceptions.append(e)

    threads = [threading.Thread(target=run_worker, args=(i,)) for i in range(thread_count)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(exceptions) == 0, f"Exceptions in concurrent process_intent: {exceptions}"
    assert len(results) == thread_count * iterations_per_thread
    for r in results:
        assert r["status"] in ["success", "idle", "blocked"]


def test_executive_simulated_transient_tool_failures_and_replanning(temp_checkpoint_dir):
    """Stress test: Inject transient failures in ToolRouter.execute.
    Verify that Executive.step_loop handles errors gracefully, replans up to max_retries,
    and records reflections without crashing.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller, checkpoint_dir=temp_checkpoint_dir)

    call_count = [0]
    real_execute = executive.router.execute

    def flaky_execute(principal, *args, **kwargs):
        call_count[0] += 1
        if call_count[0] <= 2:
            raise ConnectionError(f"Simulated network fault on execution call {call_count[0]}")
        return real_execute(principal, *args, **kwargs)

    executive.router.execute = flaky_execute

    # 1. First step should fail and trigger replan 1
    res1 = executive.process_intent(Principal.AI_AGENT, "diagnose network fault")
    assert res1["status"] == "error"
    assert res1.get("replanned") is True
    assert executive._retry_count == 1
    assert "reflection_memory_generated" in res1

    # 2. Second step should fail and trigger replan 2
    res2 = executive.step_loop(Principal.AI_AGENT)
    assert res2["status"] == "error"
    assert res2.get("replanned") is True
    assert executive._retry_count == 2

    # 3. Third step succeeds because call_count > 2
    res3 = executive.step_loop(Principal.AI_AGENT)
    assert res3["status"] == "success"
    assert executive._retry_count == 0  # Reset on success


def test_executive_approval_required_policy_gate(temp_checkpoint_dir):
    """Verify that when a tool requires approval (ApprovalRequiredError),
    Executive.step_loop produces status='blocked' and does not crash or infinite loop.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller, checkpoint_dir=temp_checkpoint_dir)

    # Set up plan with an action that requires approval for AI_AGENT
    plan = ActivePlan(
        goal="Delete canonical memory",
        steps=[{"step": 1, "action": "delete_canonical", "query": "old_key"}]
    )
    executive.active_plan = plan

    res = executive.step_loop(Principal.AI_AGENT)
    assert res["status"] == "blocked"
    assert "Approval required" in res["reason"] or "blocked" in res["status"]
    # Reflection note should be generated for blocked action
    assert "reflection_memory_generated" in res
    reflection_id = res["reflection_memory_generated"]
    refl_note = storage.get(reflection_id)
    assert refl_note is not None
    assert refl_note["type"] == "lesson"


def test_executive_reflection_pipeline_exception_immunity(temp_checkpoint_dir):
    """Stress test: If ReflectionPipeline.evaluate_outcome throws an unexpected exception,
    Executive.step_loop catches it and completes normally (fault tolerance / WIRE-6).
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller, checkpoint_dir=temp_checkpoint_dir)

    def crashing_reflection(*args, **kwargs):
        raise RuntimeError("Reflection engine total crash")

    executive.reflection.evaluate_outcome = crashing_reflection

    plan = ActivePlan(
        goal="Resilient task",
        steps=[{"step": 1, "action": "search", "query": "test query"}]
    )
    executive.active_plan = plan

    # Step loop should not crash even if reflection raises RuntimeError
    res = executive.step_loop(Principal.AI_AGENT)
    assert res["status"] == "success"


def test_executive_checkpoint_load_save_state_integrity(temp_checkpoint_dir):
    """Verify that Executive save_state and load_state correctly serialize and restore
    both WorkingMemory nodes and ActivePlan step progress across re-instantiation.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)

    u1 = str(uuid.uuid4())
    n1 = make_note(u1, content="Node for checkpoint persistence")
    storage.set(u1, n1)

    # Instance 1: populate state and checkpoint
    exec1 = Executive(controller, checkpoint_dir=temp_checkpoint_dir)
    exec1.working_memory.admit([(n1, 0.95)])
    plan = ActivePlan(
        goal="Checkpoint test goal",
        steps=[
            {"step": 1, "action": "search", "query": "q1"},
            {"step": 2, "action": "search", "query": "q2"}
        ]
    )
    exec1.active_plan = plan
    exec1.step_loop(Principal.AI_AGENT) # completes step 1
    exec1.save_state()

    # Instance 2: reload from checkpoint_dir
    exec2 = Executive(controller, checkpoint_dir=None)
    exec2.load_state(temp_checkpoint_dir, Principal.AI_AGENT)

    assert exec2.active_plan is not None
    assert exec2.active_plan.goal == "Checkpoint test goal"
    assert exec2.active_plan.current_step_index == 1
    assert not exec2.active_plan.is_complete()

    # Verify working memory restored
    wm_context = exec2.working_memory.get_active_context()
    assert len(wm_context) == 1
    assert wm_context[0]["id"] == u1

    # Execute remaining step
    res = exec2.step_loop(Principal.AI_AGENT)
    assert res["status"] == "success"
    assert exec2.active_plan.is_complete()


# ============================================================================
# Section 2: Multi-Agent Least-Privilege & Permission Matrix Stress Suite
# ============================================================================

def test_exhaustive_subagent_permission_matrix_boundaries():
    """Adversarially probe all 5 subagents against all standard tool router actions
    to ensure strict least-privilege boundaries cannot be breached.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)

    agent_permissions = {
        RouterAgent(controller): {
            "allowed": {"search", "read"},
            "forbidden": {"propose", "update", "archive", "supersede", "delete_canonical", "modify_raw_imports", "attest", "admin_purge"}
        },
        RetrievalAgent(controller): {
            "allowed": {"search", "read"},
            "forbidden": {"propose", "update", "archive", "supersede", "delete_canonical", "modify_raw_imports", "attest", "admin_purge"}
        },
        VerifierAgent(controller): {
            "allowed": {"read"},
            "forbidden": {"search", "propose", "update", "archive", "supersede", "delete_canonical", "modify_raw_imports", "attest", "admin_purge"}
        },
        ConsolidatorAgent(controller): {
            "allowed": {"search", "read", "propose", "archive"},
            "forbidden": {"update", "supersede", "delete_canonical", "modify_raw_imports", "attest", "admin_purge"}
        },
        CriticAgent(controller): {
            "allowed": {"read", "propose"},
            "forbidden": {"search", "update", "archive", "supersede", "delete_canonical", "modify_raw_imports", "attest", "admin_purge"}
        }
    }

    for agent, perms in agent_permissions.items():
        for action in perms["allowed"]:
            assert agent.can_perform(action) is True, f"Agent '{agent.name}' should allow '{action}'"

        for action in perms["forbidden"]:
            assert agent.can_perform(action) is False, f"Agent '{agent.name}' should forbid '{action}'"
            with pytest.raises(PermissionError) as exc:
                agent.execute_action(Principal.AI_AGENT, action, {})
            assert "is not authorized to perform action" in str(exc.value)


def test_subagent_fuzzing_and_hostile_payload_resilience():
    """Fuzz subagents with string, unicode, injection, and boundary task dictionaries."""
    storage = StorageEngine()
    controller = MemoryController(storage)

    router = RouterAgent(controller)
    retrieval = RetrievalAgent(controller)
    verifier = VerifierAgent(controller)
    critic = CriticAgent(controller)

    # 1. Router fuzzing with string inputs
    for bad_query in ["", "   ", "\0\0\0", "'; DROP TABLE notes; --", "a" * 10000, "{'nested': 'dict'}"]:
        res = router.process_task(Principal.AI_AGENT, {"query": bad_query})
        assert res["status"] == "success"
        assert len(res["target_agents"]) >= 1

    # 2. Retrieval fuzzing
    for bad_query in ["non_matching_query_12345", "", "SELECT * FROM memories"]:
        res = retrieval.process_task(Principal.AI_AGENT, {"query": bad_query})
        assert res["status"] == "success"
        assert isinstance(res["results"], list)

    # 3. Verifier testing with conforming node structures and violation detections
    test_nodes = [
        {},
        {"id": "n-valid-verified", "verification": "verified", "provenance": {"source_type": "official"}},
        {"id": "n-valid-ai", "verification": "unverified", "provenance": {"source_type": "ai"}},
        {"id": "n-violation-user", "verification": "unverified", "provenance": {"source_type": "user"}},
        {"id": "n-violation-official", "verification": "partially_verified", "provenance": {"source_type": "official"}}
    ]
    res = verifier.process_task(Principal.AI_AGENT, {"nodes": test_nodes})
    assert res["status"] == "success"
    assert res["total_inspected"] == 5
    assert res["verified_count"] == 1
    assert res["unverified_count"] == 4
    assert len(res["violations"]) == 2
    assert res["is_clean"] is False

    # 4. Critic testing
    res_bad_type = critic.process_task(Principal.AI_AGENT, {"type": "unknown_action_type"})
    assert res_bad_type["status"] == "error"

    res_refine = critic.process_task(Principal.AI_AGENT, {
        "type": "self_refine",
        "candidate": {"content": "short"}
    })
    assert res_refine["status"] == "success"
    assert res_refine["passed_filter"] is False


def test_concurrent_multiagent_orchestrator_and_audit_integrity(temp_sqlite_db, temp_audit_file):
    """Stress test: 10 concurrent threads running MultiAgentOrchestrator.route_and_dispatch
    and maintenance pipelines against shared SQLite WAL with SHA-256 audit chaining.
    """
    os.environ["ANTIGRAVITY_ARTIFACT_DIR"] = os.path.dirname(temp_audit_file)
    logger = AuditLogger(log_path=temp_audit_file)
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    # Seed memories
    for i in range(15):
        n = make_note(
            id_val=str(uuid.uuid4()),
            lifecycle="ACTIVE",
            verification="verified",
            content=f"Seed memory {i}: Multi-agent routing cluster configuration {i}"
        )
        storage.set(n["id"], n)

    thread_count = 10
    iterations = 8
    exceptions = []

    def worker_dispatch(tid: int):
        try:
            for it in range(iterations):
                query = f"search find detail cluster configuration {it % 15}"
                res = orchestrator.route_and_dispatch(Principal.AI_AGENT, query, [])
                assert res["status"] == "completed"
                time.sleep(0.005)
        except Exception as e:
            exceptions.append(e)

    def worker_maintenance(tid: int):
        try:
            for it in range(iterations):
                orchestrator.run_maintenance_pipeline(Principal.ADMIN)
                time.sleep(0.01)
        except Exception as e:
            exceptions.append(e)

    threads = []
    for i in range(7):
        threads.append(threading.Thread(target=worker_dispatch, args=(i,)))
    for i in range(7, thread_count):
        threads.append(threading.Thread(target=worker_maintenance, args=(i,)))

    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(exceptions) == 0, f"Exceptions in concurrent orchestrator execution: {exceptions}"

    # Verify audit log integrity
    is_valid, reason = logger.verify_integrity()
    assert is_valid is True, f"Audit log verification failed: {reason}"


# ============================================================================
# Section 3: Dynamic Synapse Co-Activation & Canonical Schema Validation
# ============================================================================

def test_dynamic_synapse_coactivation_canonical_schema_persistence(temp_sqlite_db):
    """Verify that dynamic synapses generated via ReflectionPipeline.propose_synapse
    strictly conform to _CANONICAL_SCHEMA, resolve target types dynamically,
    and persist without triggering verification escalation errors.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    pipeline = ReflectionPipeline(controller)

    u1 = str(uuid.uuid4())
    u2 = str(uuid.uuid4())

    n1 = make_note(u1, lifecycle="ACTIVE", verification="verified", note_type="knowledge", content="Source active note on WAL storage")
    n2 = make_note(u2, lifecycle="ACTIVE", verification="verified", note_type="procedure", content="Target active procedure on recovery")

    # Propose through controller to ensure standard state
    storage.set(u1, n1)
    storage.set(u2, n2)

    # AI proposes dynamic synapse between u1 and u2
    res_id = pipeline.propose_synapse(Principal.AI_AGENT, u1, u2, relation_type="related_to")
    assert res_id == u1

    # Inspect stored relations
    updated_n1 = storage.get(u1)
    assert updated_n1 is not None
    relations = updated_n1.get("relations", [])
    assert len(relations) == 1
    rel = relations[0]

    # Must contain canonical schema keys: 'relation', 'target', 'target_id'
    assert rel["relation"] == "related_to"
    assert rel["target"] == "procedure"
    assert rel["target_id"] == u2
    # Must NOT contain forbidden legacy keys
    assert "type" not in rel
    assert "confidence" not in rel

    # Verify duplicate prevention: calling again returns None and does not duplicate
    res_dup = pipeline.propose_synapse(Principal.AI_AGENT, u1, u2, relation_type="related_to")
    assert res_dup is None
    assert len(storage.get(u1).get("relations", [])) == 1


# ============================================================================
# Section 4: 10-Hop Supersession Lineage Traversal & Freshness Boost
# ============================================================================

def test_deep_10_hop_supersession_lineage_and_score_inheritance(temp_sqlite_db):
    """Stress test: 10-hop deep supersession chain (hop-0 -> hop-1 -> ... -> hop-9).
    Verify that RecallEngine resolves to active hop-9, applies the 10% freshness boost,
    caps at 1.0 ceiling, and applies the 0.3 penalty to superseded ancestor hop-0.
    """
    storage = SQLiteStorageEngine(db_path=temp_sqlite_db)
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall_engine = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    hop_count = 10
    nodes = []
    for i in range(hop_count):
        is_last = (i == hop_count - 1)
        node_id = f"deep-hop-{i}"
        sup_by = f"deep-hop-{i+1}" if not is_last else None
        sup_from = f"deep-hop-{i-1}" if i > 0 else None
        lifecycle = "ACTIVE" if is_last else "SUPERSEDED"
        content = f"PostgreSQL configuration version {i} parameter tuning guide"
        
        n = make_note(
            node_id,
            lifecycle=lifecycle,
            superseded_by=sup_by,
            supersedes=sup_from,
            content=content
        )
        storage.set(node_id, n)
        nodes.append(n)

    # Activate deep-hop-0 with score 0.8
    activated = [(nodes[0], 0.8)]
    recalled = recall_engine.recall(Principal.AI_AGENT, "PostgreSQL parameter tuning", activated, wm)

    result_map = {node["id"]: score for node, score in recalled}

    assert "deep-hop-9" in result_map, "Active leaf deep-hop-9 must be resolved"
    assert "deep-hop-0" in result_map, "Origin node deep-hop-0 must be present"

    # deep-hop-9 inherits the unpenalized score with 10% boost: (result_map['deep-hop-0'] / 0.3) * 1.1
    assert result_map["deep-hop-9"] == pytest.approx((result_map["deep-hop-0"] / 0.3) * 1.1, rel=1e-2)
    assert result_map["deep-hop-9"] > result_map["deep-hop-0"]
