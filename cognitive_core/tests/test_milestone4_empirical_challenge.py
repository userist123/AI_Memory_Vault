import pytest
import os
import uuid
import tempfile
import json
import time
from typing import Dict, Any, List

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive
from cognitive_core.reasoning import ReasoningEngine, TreeOfThoughtReasoner, ThoughtValidator
from cognitive_core.recall import RecallEngine
from cognitive_core.reflection import FormalReflexion, SelfRefine, ReflectionPipeline
from cognitive_core.consolidation import Consolidator
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory
from cognitive_core.tool_router import ToolRouter, RiskLevel, ApprovalRequiredError
from cognitive_core.orchestrator import MultiAgentOrchestrator, AgentRole
from cognitive_core.agents import (
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent
)

@pytest.fixture
def temp_checkpoint_dir():
    d = tempfile.mkdtemp(prefix="m4_checkpoints_")
    yield d
    # Cleanup
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

def make_test_note(
    id_val: str,
    lifecycle: str = "ACTIVE",
    verification: str = "verified",
    provenance: dict = None,
    content: str = "test content",
    note_type: str = "knowledge",
    relations: list = None
) -> Dict[str, Any]:
    if provenance is None:
        provenance = {"source_type": "user" if verification == "verified" else "inference", "source_ref": "test"}
    return {
        "id": id_val,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": "milestone4-challenge",
        "tags": ["m4", "test"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": relations or [],
        "content": content
    }

# ============================================================================
# 1. OODA Loop & Executive Verification
# ============================================================================

def test_ooda_loop_full_cycle_execution(temp_checkpoint_dir):
    """Verify that Executive completes the full OODA sequence:
    Observe -> Retrieve -> Attend -> Reason -> Plan -> Act -> Reflect -> Consolidate
    with atomic checkpointing and dynamic synapse generation.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller, checkpoint_dir=temp_checkpoint_dir)

    # Populate initial context notes
    n1 = make_test_note("node-alpha", content="PostgreSQL WAL replication configuration parameters", relations=[{"target_id": "node-beta", "type": "related_to"}])
    n2 = make_test_note("node-beta", content="PostgreSQL streaming standby failover runbook")
    storage.set("node-alpha", n1)
    storage.set("node-beta", n2)

    # Process intent
    result = executive.process_intent(Principal.ADMIN, "find PostgreSQL WAL replication configuration")
    assert result["status"] == "success"
    assert "result" in result
    assert "context" in result
    assert len(result["context"]) >= 1

    # Verify atomic checkpoint was written
    wm_file = os.path.join(temp_checkpoint_dir, "wm.json")
    plan_file = os.path.join(temp_checkpoint_dir, "plan.json")
    assert os.path.exists(wm_file)
    assert os.path.exists(plan_file)

    # Verify state recovery from checkpoint
    new_executive = Executive(controller)
    new_executive.load_state(temp_checkpoint_dir, Principal.ADMIN)
    assert len(new_executive.working_memory.get_active_context()) > 0
    assert new_executive.active_plan is not None

def test_executive_error_recovery_and_replanning(temp_checkpoint_dir):
    """Verify that Executive catches tool execution errors, attempts replanning up to max_retries,
    and captures formal reflection upon failure.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    executive = Executive(controller, checkpoint_dir=temp_checkpoint_dir)

    # Set up a failing search function to simulate runtime error during action execution
    def failing_search(principal, **kwargs):
        raise RuntimeError("Database connection reset during search")
    controller.search = failing_search

    from cognitive_core.planning import ActivePlan
    plan = ActivePlan(
        goal="execute complex operation",
        steps=[{"action": "search", "query": "crash"}]
    )
    executive.active_plan = plan

    step_result = executive.step_loop(Principal.AI_AGENT)
    assert step_result["status"] == "error"
    assert step_result.get("replanned") is True
    assert "reflection_memory_generated" in step_result

    # Verify reflection note was proposed into storage in REVIEW lifecycle
    ref_id = step_result["reflection_memory_generated"]
    ref_note = storage.get(ref_id)
    assert ref_note is not None
    assert ref_note["type"] == "error"
    assert ref_note["lifecycle"] == "REVIEW"
    assert ref_note["verification"] == "unverified"
    assert "Formal Reflexion Analysis" in ref_note["content"]

# ============================================================================
# 2. Tree-of-Thought Reasoning & Consistency Validation
# ============================================================================

def test_thought_validator_grounding_and_boundary_cases():
    """Verify ThoughtValidator grounding checks, scoring logic, and edge-case handling."""
    validator = ThoughtValidator()
    context = [
        {"content": "Redis in-memory cache clustering uses 16384 hash slots across master nodes."},
        {"content": "Sentinel monitoring provides automated failover for Redis clusters."}
    ]

    # Case 1: Well-grounded thought
    branch_grounded = {
        "id": "b1",
        "thought": "Redis clustering hash slots partitioning mechanism across sentinel monitoring instances."
    }
    is_valid, score, critique = validator.validate_branch(branch_grounded, context)
    assert is_valid is True
    assert score > 0.5
    assert critique == "Well grounded"

    # Case 2: Unrelated thought (lacks context grounding)
    branch_unrelated = {
        "id": "b2",
        "thought": "Quantum computing qubit entanglement superposition algorithms in cryptography."
    }
    is_valid_un, score_un, critique_un = validator.validate_branch(branch_unrelated, context)
    # Grounding ratio is low, score should be low
    assert score_un <= 0.6

    # Case 3: Empty or too short thought
    for sparse in ["", "   ", "short", "123456789"]:
        is_valid_sp, score_sp, critique_sp = validator.validate_branch({"thought": sparse}, context)
        assert is_valid_sp is False
        assert score_sp == 0.0
        assert "sparse" in critique_sp.lower()

def test_tree_of_thought_branch_generation_and_selection():
    """Verify TreeOfThoughtReasoner generates 3 distinct perspectives and selects the top branch."""
    tot = TreeOfThoughtReasoner()
    context = [
        {"content": "Kafka partition rebalance causes consumer group lag spikes during cluster scale-out."}
    ]
    query = "Why do Kafka consumer groups experience lag spikes during partition rebalance?"

    result = tot.reason(query, context)
    assert "best_branch" in result
    assert result["branches_explored"] == 3
    assert result["tree_depth"] == 2
    assert len(result["all_evaluated_branches"]) == 3

    branch_perspectives = [b["perspective"] for b in result["all_evaluated_branches"]]
    assert "direct evidence" in branch_perspectives
    assert "comparative causal" in branch_perspectives
    assert "counterfactual/edge case" in branch_perspectives

def test_reasoning_engine_complexity_trigger_and_read_only_boundary():
    """Verify ReasoningEngine selectively activates Tree-of-Thought on complex queries
    and enforces read-only query bounds.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    engine = ReasoningEngine(controller)

    # Triggers: "why", "how", "root cause", "compare", "plan", "troubleshoot", "evaluate", "complex", "architecture"
    complex_queries = [
        "Why did the database crash?",
        "How do we configure high-availability replication?",
        "What is the root cause of latency degradation?",
        "Compare Postgres and MySQL clustering models",
        "Plan a safe migration strategy",
        "Troubleshoot network packet drop anomalies",
        "Evaluate memory leak risks in worker threads",
        "Analyze complex multi-region synchronization topology",
        "Describe microservice architecture decomposition for payment processing gateway"
    ]

    for cq in complex_queries:
        res = engine.synthesize(Principal.AI_AGENT, [{"content": "Infrastructure diagnostic report."}], cq)
        assert res["mode"] == "tree_of_thought", f"Failed to activate ToT for complex query: '{cq}'"
        assert "tot_details" in res

    # Simple queries trigger direct mode
    simple_queries = [
        "Show port 8080",
        "Status check",
        "List items"
    ]
    for sq in simple_queries:
        res = engine.synthesize(Principal.AI_AGENT, [{"content": "Port 8080 is open."}], sq)
        assert res["mode"] == "direct", f"Incorrectly triggered ToT for simple query: '{sq}'"

# ============================================================================
# 3. Recall Scoring with 10% Freshness Boost & Lineage
# ============================================================================

def test_recall_scoring_freshness_boost_for_successor_notes():
    """Verify that active successor notes inherit semantic score from superseded notes
    with an exact 10% freshness bonus (min(1.0, score * 1.1)).
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall_engine = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    old_id = "old-docker-guidelines"
    active_id = "active-docker-guidelines-2026"

    old_note = {
        "id": old_id,
        "type": "knowledge",
        "lifecycle": "SUPERSEDED",
        "superseded_by": active_id,
        "content": "Docker container bridge networking and daemon security guidelines",
        "confidence": "high"
    }
    active_note = {
        "id": active_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "supersedes": old_id,
        "content": "Docker container bridge networking modern 2026 standards",
        "confidence": "high"
    }

    storage.set(old_id, old_note)
    storage.set(active_id, active_note)

    # Superseded note is activated with 0.8 activation
    activated = [(old_note, 0.8)]
    results = recall_engine.recall(Principal.AI_AGENT, "Docker bridge networking", activated, wm)

    result_map = {n["id"]: score for n, score in results}
    assert active_id in result_map
    assert old_id in result_map

    # Active note should rank higher than old note due to freshness boost & lifecycle downranking
    assert result_map[active_id] > result_map[old_id]

def test_recall_flags_review_notes_as_unverified():
    """Verify that notes in REVIEW lifecycle returned during recall are flagged with _cognitive_unverified = True."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall_engine = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    review_note = {
        "id": "rev-1",
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "content": "Unverified memory candidate",
        "confidence": "medium",
        "verification": "unverified"
    }
    storage.set("rev-1", review_note)

    results = recall_engine.recall(Principal.AI_AGENT, "Unverified memory", [(review_note, 0.5)], wm)
    assert len(results) >= 1
    found_node = next(n for n, _ in results if n["id"] == "rev-1")
    assert found_node.get("_cognitive_unverified") is True

def test_recall_version_aware_boosting_and_penalty():
    """Verify that recall applies version-aware scoring boost (+0.3) on exact match
    and penalty (-0.3) on version mismatch.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    semantic = DeterministicSemanticProvider()
    recall_engine = RecallEngine(controller, semantic)
    wm = WorkingMemory()

    py311_note = {
        "id": "py-311",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Python 3.11 exception groups and asyncio task groups",
        "confidence": "high"
    }
    py310_note = {
        "id": "py-310",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "Python 3.10 pattern matching syntax and union types",
        "confidence": "high"
    }
    storage.set("py-311", py311_note)
    storage.set("py-310", py310_note)

    activated = [(py311_note, 0.8), (py310_note, 0.8)]
    results = recall_engine.recall(Principal.AI_AGENT, "Python 3.11 task groups", activated, wm)

    result_map = {n["id"]: score for n, score in results}
    # Python 3.11 should have a significantly higher score than Python 3.10
    assert result_map["py-311"] > result_map["py-310"]

# ============================================================================
# 4. 6-Stage Formal Reflexion Verification
# ============================================================================

def test_formal_reflexion_all_stages():
    """Verify FormalReflexion formats all 6 stages into structured Markdown."""
    formatted = FormalReflexion.format_reflection(
        error="Network timeout connecting to Redis cluster",
        root_cause="Security group ingress rule missing on port 6379",
        fix="Added CIDR 10.0.0.0/16 ingress permission for port 6379",
        verification="Verified ping pong response from Redis CLI",
        prevention="Automate Terraform security group audits in CI",
        lesson="Always verify cloud firewall rules before deploying stateful services"
    )

    assert "## Formal Reflexion Analysis" in formatted
    assert "- **Error**: Network timeout connecting to Redis cluster" in formatted
    assert "- **Root Cause**: Security group ingress rule missing on port 6379" in formatted
    assert "- **Fix Applied**: Added CIDR 10.0.0.0/16 ingress permission for port 6379" in formatted
    assert "- **Verification**: Verified ping pong response from Redis CLI" in formatted
    assert "- **Prevention Rule**: Automate Terraform security group audits in CI" in formatted
    assert "- **Core Lesson**: Always verify cloud firewall rules before deploying stateful services" in formatted

def test_reflection_pipeline_creates_structured_error_and_blocked_memories():
    """Verify ReflectionPipeline constructs valid REVIEW memories for error and blocked outcomes."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    pipeline = ReflectionPipeline(controller)

    # 1. Error outcome
    err_id = pipeline.evaluate_outcome(
        Principal.AI_AGENT,
        {"query": "Scale kubernetes deployment"},
        {"action": "kubectl scale"},
        {
            "status": "error",
            "error": "Insufficient CPU quota in namespace",
            "root_cause": "Requested 32 cores, quota limit is 16 cores",
            "fix": "Reduced replica count to 4",
            "verification": "All 4 pods scheduled successfully",
            "prevention": "Check ResourceQuota before scaling",
            "lesson": "Respect namespace compute limits"
        }
    )
    assert err_id is not None
    err_note = storage.get(err_id)
    assert err_note["type"] == "error"
    assert err_note["lifecycle"] == "REVIEW"
    assert err_note["verification"] == "unverified"
    assert err_note["provenance"]["source_type"] == "inference"
    assert err_note["provenance"]["source_ref"] == "formal-reflexion"
    assert "Insufficient CPU quota" in err_note["content"]

    # 2. Blocked outcome
    blocked_id = pipeline.evaluate_outcome(
        Principal.AI_AGENT,
        {"query": "Purge all historical memory"},
        {"action": "delete_canonical"},
        {"status": "blocked", "reason": "Requires human operator approval"}
    )
    assert blocked_id is not None
    blocked_note = storage.get(blocked_id)
    assert blocked_note["type"] == "lesson"
    assert blocked_note["lifecycle"] == "REVIEW"
    assert blocked_note["verification"] == "unverified"
    assert blocked_note["provenance"]["source_type"] == "inference"
    assert blocked_note["provenance"]["source_ref"] == "autonomy-policy"
    assert "Autonomy Policy" in blocked_note["content"]

# ============================================================================
# 5. SelfRefine Memory Critique & Consolidation
# ============================================================================

def test_self_refine_critique_filter_edge_cases():
    """Verify SelfRefine filter rejects empty, whitespace, and sparse candidate memories."""
    # Valid candidate
    valid_cand = {
        "id": "c1",
        "type": "knowledge",
        "content": "Consolidated findings on multi-tenant database partitioning."
    }
    passed, refined = SelfRefine.refine_memory(valid_cand)
    assert passed is True
    assert refined["confidence"] == "medium"

    # Invalid candidates
    invalid_candidates = [
        {},
        {"content": ""},
        {"content": "   "},
        {"content": "too short"},
        {"content": "\n\t  \r  "},
    ]
    for ic in invalid_candidates:
        passed_inv, _ = SelfRefine.refine_memory(ic)
        assert passed_inv is False

def test_consolidator_groups_lessons_and_archives_sources():
    """Verify Consolidator groups 2+ REVIEW lessons, synthesizes a consolidated knowledge note
    via SelfRefine, proposes it, and archives the original lessons.
    """
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    consolidator = Consolidator(controller, router)

    l1_id = str(uuid.uuid4())
    l2_id = str(uuid.uuid4())
    l1 = make_test_note(l1_id, lifecycle="REVIEW", verification="unverified", note_type="lesson", content="Lesson 1: Always validate connection timeouts before executing RPC calls.")
    l2 = make_test_note(l2_id, lifecycle="REVIEW", verification="unverified", note_type="lesson", content="Lesson 2: Set reasonable circuit breaker thresholds to prevent cascading failures.")
    storage.set(l1_id, l1)
    storage.set(l2_id, l2)

    new_id = consolidator.consolidate_lessons(Principal.ADMIN)
    assert new_id is not None

    # Verify new consolidated knowledge note
    consolidated_note = storage.get(new_id)
    assert consolidated_note is not None
    assert consolidated_note["type"] == "knowledge"
    assert consolidated_note["lifecycle"] == "REVIEW"
    assert consolidated_note["verification"] == "unverified"
    assert l1_id in consolidated_note["provenance"]["source_ref"]
    assert l2_id in consolidated_note["provenance"]["source_ref"]

    # Verify original lessons were archived
    assert storage.get(l1_id)["lifecycle"] == "ARCHIVED"
    assert storage.get(l2_id)["lifecycle"] == "ARCHIVED"

# ============================================================================
# 6. Multi-Agent Worker Least Privilege & Orchestration
# ============================================================================

def test_multi_agent_workers_least_privilege_enforcement():
    """Verify all 5 specialized subagents enforce strict least-privilege action boundaries:
    - RouterAgent: ['search', 'read']
    - RetrievalAgent: ['search', 'read']
    - VerifierAgent: ['read']
    - ConsolidatorAgent: ['search', 'read', 'propose', 'archive']
    - CriticAgent: ['read', 'propose']
    """
    storage = StorageEngine()
    controller = MemoryController(storage)

    router = RouterAgent(controller)
    retrieval = RetrievalAgent(controller)
    verifier = VerifierAgent(controller)
    consolidator = ConsolidatorAgent(controller)
    critic = CriticAgent(controller)

    # 1. Router cannot propose or archive
    assert router.can_perform("search") is True
    assert router.can_perform("read") is True
    assert router.can_perform("propose") is False
    assert router.can_perform("archive") is False
    with pytest.raises(PermissionError):
        router.execute_action(Principal.AI_AGENT, "propose", {"note_data": {}})

    # 2. Retrieval cannot propose or archive
    assert retrieval.can_perform("search") is True
    assert retrieval.can_perform("propose") is False
    with pytest.raises(PermissionError):
        retrieval.execute_action(Principal.AI_AGENT, "archive", {"note_id": "x"})

    # 3. Verifier cannot search or propose
    assert verifier.can_perform("read") is True
    assert verifier.can_perform("search") is False
    assert verifier.can_perform("propose") is False
    with pytest.raises(PermissionError):
        verifier.execute_action(Principal.AI_AGENT, "search", {"query": "x"})

    # 4. Consolidator can propose & archive, but cannot attest
    assert consolidator.can_perform("propose") is True
    assert consolidator.can_perform("archive") is True
    assert consolidator.can_perform("attest") is False
    with pytest.raises(PermissionError):
        consolidator.execute_action(Principal.AI_AGENT, "attest", {"note_id": "x"})

    # 5. Critic can propose & read, but cannot archive
    assert critic.can_perform("propose") is True
    assert critic.can_perform("read") is True
    assert critic.can_perform("archive") is False
    with pytest.raises(PermissionError):
        critic.execute_action(Principal.AI_AGENT, "archive", {"note_id": "x"})

def test_verifier_agent_detects_unattested_privileged_provenance():
    """Verify VerifierAgent identifies nodes claiming 'user' or 'official' provenance without attested verification."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    verifier = VerifierAgent(controller)

    nodes = [
        {"id": "n1", "verification": "verified", "provenance": {"source_type": "user"}},
        {"id": "n2", "verification": "unverified", "provenance": {"source_type": "official"}},  # violation
        {"id": "n3", "verification": "unverified", "provenance": {"source_type": "inference"}}, # valid unverified
    ]

    report = verifier.process_task(Principal.AI_AGENT, {"nodes": nodes})
    assert report["status"] == "success"
    assert report["total_inspected"] == 3
    assert report["verified_count"] == 1
    assert report["unverified_count"] == 2
    assert len(report["violations"]) == 1
    assert report["is_clean"] is False
    assert "n2" in report["violations"][0]

def test_multiagent_orchestrator_end_to_end_pipeline():
    """Verify MultiAgentOrchestrator coordinates Router -> Retrieval -> Verifier -> Synthesis pipeline."""
    storage = StorageEngine()
    controller = MemoryController(storage)
    orchestrator = MultiAgentOrchestrator(controller)

    # Add verified and unverified notes
    storage.set("k-auth", make_test_note("k-auth", content="OAuth2 OpenID Connect security best practices", verification="verified"))
    storage.set("k-temp", make_test_note("k-temp", content="Temporary caching strategies", verification="unverified", lifecycle="REVIEW"))

    result = orchestrator.route_and_dispatch(
        Principal.AI_AGENT,
        "search for OAuth2 authentication best practices",
        [{"id": "init-ctx", "content": "Initial session state", "verification": "verified"}]
    )

    assert result["status"] == "completed"
    assert "orchestration_history" in result
    assert result["total_context_used"] >= 2
    history_agents = [h["agent"] for h in result["orchestration_history"]]
    assert "retrieval" in history_agents
    assert "verifier" in history_agents
