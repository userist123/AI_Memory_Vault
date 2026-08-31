"""
Milestone 3 Unit Tests: Comprehensive Multi-Agent Worker Orchestration.
Covers MultiAgentSupervisor, RouterAgent, RetrievalAgent, VerifierAgent,
ConsolidatorAgent, and CriticAgent across 31 targeted test scenarios.
"""

import pytest
import asyncio
import uuid
import time
from typing import Dict, Any, List

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.agents import (
    AgentRole,
    TaskPriority,
    TaskStatus,
    AgentTask,
    TaskResult,
    MultiAgentSupervisor,
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent,
    SubTaskScope,
)


# ============================================================================
# Group 1: MultiAgentSupervisor Priority Queue & Worker Pool
# ============================================================================

@pytest.mark.asyncio
async def test_supervisor_priority_queue_strict_ordering(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits 5 tasks with mixed priorities [P5, P1, P4, P2, P3]; asserts execution in P1..P5 order."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    executed_order: List[int] = []

    # Mock dispatch or submit tasks
    tasks = [
        AgentTask(task_id="t-p5", priority=5, role=AgentRole.VERIFIER, payload={"note": {"id": str(uuid.uuid4()), "type": "knowledge", "lifecycle": "REVIEW", "category": "c", "provenance": {"source_type": "ai", "source_ref": "r"}}}),
        AgentTask(task_id="t-p1", priority=1, role=AgentRole.ROUTER, payload={"query": "turn on lights"}),
        AgentTask(task_id="t-p4", priority=4, role=AgentRole.VERIFIER, payload={"note": {"id": str(uuid.uuid4()), "type": "knowledge", "lifecycle": "REVIEW", "category": "c", "provenance": {"source_type": "ai", "source_ref": "r"}}}),
        AgentTask(task_id="t-p2", priority=2, role=AgentRole.CRITIC, payload={"draft": "hello world"}),
        AgentTask(task_id="t-p3", priority=3, role=AgentRole.RETRIEVAL, payload={"query": "test query"}),
    ]

    for t in tasks:
        supervisor.submit_task(t)

    # Pop one by one via run_next_task
    while supervisor.queue:
        t_id = supervisor.queue[0].task_id
        p = supervisor.queue[0].priority
        executed_order.append(p)
        await supervisor.run_next_task()

    assert executed_order == [1, 2, 3, 4, 5]


@pytest.mark.asyncio
async def test_supervisor_fifo_ordering_within_same_priority(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits 10 tasks all at Priority 2; asserts FIFO execution."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm)

    task_ids = [f"t-seq-{i}" for i in range(10)]
    for tid in task_ids:
        supervisor.submit_task(AgentTask(task_id=tid, priority=2, role=AgentRole.CRITIC, payload={"draft": f"draft {tid}"}))

    executed_ids: List[str] = []
    while supervisor.queue:
        tid = supervisor.queue[0].task_id
        executed_ids.append(tid)
        await supervisor.run_next_task()

    assert executed_ids == task_ids


@pytest.mark.asyncio
async def test_supervisor_async_worker_pool_concurrency_limit(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Configures supervisor with max_workers=3; asserts active worker count never exceeds 3."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=3)
    await supervisor.start()

    max_observed_concurrency = 0

    async def monitor():
        nonlocal max_observed_concurrency
        for _ in range(25):
            curr = supervisor.active_worker_count
            if curr > max_observed_concurrency:
                max_observed_concurrency = curr
            await asyncio.sleep(0.01)

    # Submit 8 tasks that take 50ms each
    for i in range(8):
        supervisor.submit_task(AgentTask(task_id=f"w-{i}", priority=2, role=AgentRole.CRITIC, payload={"draft": f"sample draft {i}"}))

    monitor_task = asyncio.create_task(monitor())
    await monitor_task

    await supervisor.shutdown(wait=True, timeout=2.0)
    assert max_observed_concurrency <= 3


@pytest.mark.asyncio
async def test_supervisor_non_blocking_voice_loop_execution(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Simulates high-frequency voice loop concurrently with supervisor background processing; asserts low jitter."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=4)
    await supervisor.start()

    # Submit 20 background tasks
    for i in range(20):
        supervisor.submit_task(AgentTask(task_id=f"bg-{i}", priority=5, role=AgentRole.CRITIC, payload={"draft": "test"}))

    # Voice loop simulating 50 audio frames @ 5ms interval
    loop_intervals: List[float] = []
    t_start = time.perf_counter()
    for _ in range(50):
        t0 = time.perf_counter()
        await asyncio.sleep(0.005)
        dt = (time.perf_counter() - t0) * 1000.0
        loop_intervals.append(dt)

    total_time = (time.perf_counter() - t_start) * 1000.0
    await supervisor.shutdown(wait=True, timeout=2.0)

    # Max loop jitter should remain small (<25ms on busy event loop)
    max_interval = max(loop_intervals)
    assert max_interval < 30.0


@pytest.mark.asyncio
async def test_supervisor_task_status_lifecycle_tracking(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits a task; asserts completion recording and callback firing."""
    callback_fired: List[TaskResult] = []

    def on_complete(res: TaskResult):
        callback_fired.append(res)

    supervisor = MultiAgentSupervisor(
        storage=sqlite_storage,
        llm=mock_llm,
        on_task_complete=on_complete,
    )

    task = AgentTask(
        task_id="t-lifecycle-1",
        priority=1,
        role=AgentRole.ROUTER,
        payload={"query": "turn on porch light"},
    )
    supervisor.submit_task(task)

    res = await supervisor.run_next_task()
    assert res is not None
    assert len(callback_fired) == 1
    assert callback_fired[0].task_id == "t-lifecycle-1"
    assert callback_fired[0].status == TaskStatus.COMPLETED


@pytest.mark.asyncio
async def test_supervisor_graceful_shutdown_drains_active_tasks(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Submits in-flight tasks and calls shutdown(wait=True); asserts clean completion."""
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)
    await supervisor.start()

    for i in range(4):
        supervisor.submit_task(AgentTask(task_id=f"drain-{i}", priority=2, role=AgentRole.CRITIC, payload={"draft": "drain test"}))

    await supervisor.shutdown(wait=True, timeout=2.0)
    assert len(supervisor.completed_tasks) == 4
    assert supervisor._running is False


# ============================================================================
# Group 2: Router Agent Intent Decomposition & Slot Parsing
# ============================================================================

@pytest.mark.asyncio
async def test_router_single_atomic_query(mock_llm: MockLLMProvider):
    """Verifies router produces 1 atomic task for a single knowledge query."""
    router = RouterAgent(llm=mock_llm)
    out = await router.decompose("What is the memory retrieval architecture?")
    assert out.count == 1
    assert out.is_composite is False
    assert out.subtasks[0].scope in [SubTaskScope.QUERY, SubTaskScope.CONVERSATION]


@pytest.mark.asyncio
async def test_router_composite_intent_decomposition(mock_llm: MockLLMProvider):
    """Verifies router decomposes multi-intent sentence into 3 atomic subtasks."""
    router = RouterAgent(llm=mock_llm)
    query = "Turn off the kitchen lights and set the living room thermostat to 21 degrees and check system status"
    out = await router.decompose(query)

    assert out.count == 3
    assert out.is_composite is True

    scopes = [s.scope for s in out.subtasks]
    assert SubTaskScope.IOT_CONTROL in scopes
    assert SubTaskScope.SYSTEM_STATUS in scopes


@pytest.mark.asyncio
async def test_router_composite_query_and_memory_store(mock_llm: MockLLMProvider):
    """Verifies decomposition into memory store + IoT control tasks."""
    router = RouterAgent(llm=mock_llm)
    query = "Remember that our SQLite database uses WAL mode and turn on the bedroom light"
    out = await router.decompose(query)

    assert out.count == 2
    scopes = [s.scope for s in out.subtasks]
    assert SubTaskScope.MEMORY_STORE in scopes
    assert SubTaskScope.IOT_CONTROL in scopes


@pytest.mark.asyncio
async def test_router_empty_and_whitespace_input_handling(mock_llm: MockLLMProvider):
    """Verifies router safely handles empty and whitespace strings."""
    router = RouterAgent(llm=mock_llm)
    out1 = await router.decompose("")
    assert out1.count == 0

    out2 = await router.decompose("    \n\t  ")
    assert out2.count == 0


@pytest.mark.asyncio
async def test_router_ambiguous_and_malformed_syntax(mock_llm: MockLLMProvider):
    """Verifies router strips repeated conjunctions and extracts valid clause."""
    router = RouterAgent(llm=mock_llm)
    out = await router.decompose("and and turn on light and")
    assert out.count == 1
    assert "turn on light" in out.subtasks[0].raw_query


# ============================================================================
# Group 3: Retrieval Agent Lineage & Multi-Signal Recall
# ============================================================================

@pytest.mark.asyncio
async def test_retrieval_agent_bm25_and_tag_filtering(sqlite_storage: SQLiteStorageEngine):
    """Seeds notes across categories; queries with keyword matching."""
    note_sec = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "security",
        "tags": ["crypto", "encryption"],
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "confidence": "very_high",
        "verification": "unverified",
        "relations": [],
        "content": "Cryptographic encryption standards require AES-GCM-256.",
    }
    sqlite_storage.propose(Principal.HUMAN, note_sec)

    retrieval = RetrievalAgent(storage=sqlite_storage)
    res = await retrieval.retrieve({"query": "cryptographic encryption standards"})
    assert res.count >= 1
    assert any("AES-GCM-256" in n.get("content", "") for n in res.matches)


@pytest.mark.asyncio
async def test_retrieval_agent_supersession_lineage_exclusion(sqlite_storage: SQLiteStorageEngine):
    """Creates chain Note A -> superseded by Note B -> superseded by Note C; asserts canonical resolution."""
    id_a, id_b, id_c = str(uuid.uuid4()), str(uuid.uuid4()), str(uuid.uuid4())

    note_a = {
        "id": id_a, "type": "knowledge", "lifecycle": "ACTIVE", "category": "arch", "tags": [],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "medium", "verification": "unverified", "relations": [],
        "content": "Protocol Version 1.0 architecture specifications.",
    }
    note_b = {
        "id": id_b, "type": "knowledge", "lifecycle": "ACTIVE", "category": "arch", "tags": [],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "medium", "verification": "unverified", "relations": [],
        "content": "Protocol Version 2.0 architecture specifications.",
    }
    note_c = {
        "id": id_c, "type": "knowledge", "lifecycle": "ACTIVE", "category": "arch", "tags": [],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "high", "verification": "unverified", "relations": [],
        "content": "Protocol Version 3.0 architecture specifications.",
    }

    sqlite_storage.propose(Principal.HUMAN, note_a)
    sqlite_storage.propose(Principal.HUMAN, note_b)
    sqlite_storage.propose(Principal.HUMAN, note_c)

    sqlite_storage.supersede(Principal.HUMAN, id_a, id_b)
    sqlite_storage.supersede(Principal.HUMAN, id_b, id_c)

    retrieval = RetrievalAgent(storage=sqlite_storage)
    res = await retrieval.retrieve({"query": "Protocol Version architecture", "include_superseded": False})

    # Only active head Note C should be returned
    assert any(n.get("id") == id_c for n in res.matches)
    assert not any(n.get("id") == id_a for n in res.matches)


@pytest.mark.asyncio
async def test_retrieval_agent_wikilink_synapse_graph_traversal(sqlite_storage: SQLiteStorageEngine):
    """Creates linked cluster Note A -> Note B; asserts traversal with max_depth=2."""
    id_a = str(uuid.uuid4())
    id_b = str(uuid.uuid4())

    note_b = {
        "id": id_b, "type": "knowledge", "lifecycle": "ACTIVE", "category": "core", "tags": [],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "high", "verification": "unverified", "relations": [],
        "content": "Deep dependency module details for system.",
    }
    note_a = {
        "id": id_a, "type": "knowledge", "lifecycle": "ACTIVE", "category": "core", "tags": [],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "high", "verification": "unverified",
        "relations": [{"relation": "depends_on", "target": "knowledge", "target_id": id_b}],
        "content": "Entry point module referencing subcomponent.",
    }

    sqlite_storage.propose(Principal.HUMAN, note_b)
    sqlite_storage.propose(Principal.HUMAN, note_a)

    retrieval = RetrievalAgent(storage=sqlite_storage)
    res = await retrieval.retrieve({"query": "Entry point module", "max_depth": 2})

    returned_ids = {n.get("id") for n in res.matches}
    assert id_a in returned_ids
    assert id_b in returned_ids


@pytest.mark.asyncio
async def test_retrieval_agent_confidence_and_recency_scoring(sqlite_storage: SQLiteStorageEngine):
    """Asserts composite scoring ranks high-confidence notes above low-confidence notes."""
    id_high = str(uuid.uuid4())
    id_low = str(uuid.uuid4())

    note_high = {
        "id": id_high, "type": "knowledge", "lifecycle": "ACTIVE", "category": "bench", "tags": ["perf"],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "very_high", "verification": "unverified", "relations": [],
        "content": "Benchmark test results for network throughput.",
    }
    note_low = {
        "id": id_low, "type": "knowledge", "lifecycle": "ACTIVE", "category": "bench", "tags": ["perf"],
        "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "low", "verification": "unverified", "relations": [],
        "content": "Benchmark test results for network throughput.",
    }

    sqlite_storage.propose(Principal.HUMAN, note_high)
    sqlite_storage.attest(Principal.HUMAN, id_high, "Attested performance benchmark result")
    sqlite_storage.propose(Principal.HUMAN, note_low)

    retrieval = RetrievalAgent(storage=sqlite_storage)
    res = await retrieval.retrieve({"query": "Benchmark test results"})

    assert res.count >= 2
    # First returned note should be the higher composite scored note
    assert res.notes[0].note.get("id") == id_high


@pytest.mark.asyncio
async def test_retrieval_agent_read_only_isolation(sqlite_storage: SQLiteStorageEngine):
    """Asserts RetrievalAgent storage proxy rejects propose or archive attempts with PermissionError."""
    retrieval = RetrievalAgent(storage=sqlite_storage)
    with pytest.raises(PermissionError):
        retrieval.storage.propose({"id": str(uuid.uuid4()), "type": "knowledge"})
    with pytest.raises(PermissionError):
        retrieval.storage.archive(str(uuid.uuid4()))


# ============================================================================
# Group 4: Verifier Agent Frontmatter & Invariant Auditing
# ============================================================================

def test_verifier_validates_canonical_frontmatter_schema():
    """Supplies complete NoteFrontmatter dictionary; asserts validation passes with 0 violations."""
    verifier = VerifierAgent()
    valid_note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "tags": ["governance"],
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "execution", "source_ref": "unit_test"},
        "confidence": "high",
        "verification": "unverified",
    }
    report = verifier.verify_note(valid_note)
    assert report.is_valid is True
    assert len(report.violations) == 0
    assert len(report.missing) == 0


def test_verifier_detects_missing_mandatory_fields():
    """Supplies note missing mandatory fields; asserts audit fails and lists missing fields."""
    verifier = VerifierAgent()
    invalid_note = {"content": "Missing all frontmatter fields"}
    report = verifier.verify_note(invalid_note)
    assert report.is_valid is False
    assert "id" in report.missing
    assert "type" in report.missing
    assert "lifecycle" in report.missing
    assert "category" in report.missing
    assert "provenance" in report.missing


def test_verifier_detects_invalid_uuid_and_enum_violations():
    """Supplies invalid UUID and non-existent enums; asserts violations flagged."""
    verifier = VerifierAgent()
    bad_note = {
        "id": "not-a-valid-uuid",
        "type": "invalid_note_type_xyz",
        "lifecycle": "UNKNOWN_LIFECYCLE",
        "category": "test",
        "provenance": {"source_type": "execution", "source_ref": "t"},
    }
    report = verifier.verify_note(bad_note)
    assert report.is_valid is False
    rules = [v.rule for v in report.violations]
    assert "ERR_P0_001_INVALID_UUID" in rules
    assert "ERR_INVALID_NOTE_TYPE" in rules
    assert "ERR_INVALID_LIFECYCLE" in rules


def test_verifier_flags_ai_agent_self_verification_attempt():
    """Supplies note with verification='verified' from AI agent; asserts ERR_P0_001_AI_VERIFIED_GATE."""
    verifier = VerifierAgent()
    self_verified_note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "provenance": {"source_type": "ai", "source_ref": "llm_output"},
        "verification": "verified",
    }
    report = verifier.verify_note(self_verified_note, principal=Principal.AI_AGENT)
    assert report.is_valid is False
    rules = [v.rule for v in report.violations]
    assert "ERR_P0_001_AI_VERIFIED_GATE" in rules


def test_verifier_flags_unauthorized_active_lifecycle_at_creation():
    """Supplies proposed note with lifecycle='ACTIVE' by AI agent; asserts ERR_P0_004_AI_CREATION_LIFECYCLE."""
    verifier = VerifierAgent()
    active_proposal = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "core",
        "provenance": {"source_type": "execution", "source_ref": "test"},
    }
    report = verifier.verify_proposal(active_proposal, principal=Principal.AI_AGENT)
    assert report.is_valid is False
    rules = [v.rule for v in report.violations]
    assert "ERR_P0_004_AI_CREATION_LIFECYCLE" in rules


def test_verifier_flags_cyclic_and_self_supersession():
    """Supplies note where supersedes == id; asserts ERR_P0_012_CYCLIC_SUPERSESSION."""
    verifier = VerifierAgent()
    same_id = str(uuid.uuid4())
    cyclic_note = {
        "id": same_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "supersedes": same_id,
    }
    report = verifier.verify_note(cyclic_note)
    assert report.is_valid is False
    rules = [v.rule for v in report.violations]
    assert "ERR_P0_012_CYCLIC_SUPERSESSION" in rules


# ============================================================================
# Group 5: Consolidator Agent Lesson Synthesis & Reconsolidation
# ============================================================================

def test_consolidator_synthesizes_multiple_review_lessons(sqlite_storage: SQLiteStorageEngine):
    """Seeds 3 related lessons in REVIEW; asserts new unified knowledge note in REVIEW."""
    id1, id2, id3 = str(uuid.uuid4()), str(uuid.uuid4()), str(uuid.uuid4())
    for lid, topic in [(id1, "VAD silence threshold tuning"), (id2, "VAD speech frame sizing"), (id3, "VAD buffer latency optimization")]:
        sqlite_storage.propose(
            Principal.AI_AGENT,
            {
                "id": lid, "type": "lesson", "lifecycle": "REVIEW", "category": "audio-vad", "tags": ["vad"],
                "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "inference", "source_ref": "test"},
                "confidence": "medium", "verification": "unverified", "relations": [],
                "content": f"Detailed lesson regarding {topic} in audio engine.",
            }
        )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    summary = consolidator.scan_and_consolidate(category="audio-vad")

    assert summary.status == "success"
    assert summary.cluster_count == 1
    assert summary.consolidated_note_id is not None

    consolidated = sqlite_storage.get(summary.consolidated_note_id)
    assert consolidated is not None
    assert consolidated["type"] == "knowledge"
    assert consolidated["lifecycle"] == "REVIEW"
    assert len(consolidated.get("relations", [])) == 3


def test_consolidator_archives_consumed_review_lessons(sqlite_storage: SQLiteStorageEngine):
    """Asserts source lessons transition to ARCHIVED after consolidation."""
    id1, id2 = str(uuid.uuid4()), str(uuid.uuid4())
    for lid in [id1, id2]:
        sqlite_storage.propose(
            Principal.AI_AGENT,
            {
                "id": lid, "type": "lesson", "lifecycle": "REVIEW", "category": "c-arch", "tags": [],
                "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "inference", "source_ref": "test"},
                "confidence": "medium", "verification": "unverified", "relations": [],
                "content": f"Lesson content for {lid}.",
            }
        )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    summary = consolidator.scan_and_consolidate(category="c-arch")

    assert id1 in summary.archived_source_ids
    assert id2 in summary.archived_source_ids

    note1 = sqlite_storage.get(id1)
    note2 = sqlite_storage.get(id2)
    assert note1["lifecycle"] == "ARCHIVED"
    assert note2["lifecycle"] == "ARCHIVED"


def test_consolidator_handles_insufficient_candidates_gracefully(sqlite_storage: SQLiteStorageEngine):
    """Seeds only 1 lesson in REVIEW; asserts no consolidation occurs."""
    sqlite_storage.propose(
        Principal.AI_AGENT,
        {
            "id": str(uuid.uuid4()), "type": "lesson", "lifecycle": "REVIEW", "category": "single-test", "tags": [],
            "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "inference", "source_ref": "test"},
            "confidence": "medium", "verification": "unverified", "relations": [],
            "content": "Single lonely lesson in review state.",
        }
    )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    summary = consolidator.scan_and_consolidate(category="single-test")
    assert summary.status == "insufficient_candidates"
    assert summary.consolidated_note_id is None


def test_consolidator_plastic_memory_reconsolidation_challenge(sqlite_storage: SQLiteStorageEngine):
    """Challenges an ACTIVE note with conflicting evidence; asserts transition to RECONSOLIDATING."""
    nid = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "core", "tags": [],
            "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "high", "verification": "unverified", "relations": [],
            "content": "Original active knowledge statement.",
        }
    )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    updated = consolidator.challenge_note(nid, {"error": "Empirical contradiction observed"})

    assert updated is not None
    assert updated["lifecycle"] == "RECONSOLIDATING"
    assert "previous_version" in updated
    assert updated["previous_version"]["content"] == "Original active knowledge statement."


def test_consolidator_plastic_memory_reconsolidation_resolution(sqlite_storage: SQLiteStorageEngine):
    """Resolves a RECONSOLIDATING note with updated content; asserts return to ACTIVE."""
    nid = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "core", "tags": [],
            "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "high", "verification": "unverified", "relations": [],
            "content": "Pre-challenge knowledge.",
        }
    )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    consolidator.challenge_note(nid, {"error": "Need update"})

    resolved = consolidator.resolve_challenge(nid, {"content": "Updated verified knowledge."})
    assert resolved["lifecycle"] == "ACTIVE"
    assert resolved["content"] == "Updated verified knowledge."
    assert resolved["conflicting_evidence"] is None


# ============================================================================
# Group 6: Critic Agent 6-Stage Formal Reflexion & SelfRefine
# ============================================================================

def test_critic_evaluates_valid_draft_and_approves():
    """Submits clear, concise response draft; asserts approval."""
    critic = CriticAgent()
    res = critic.critique_draft("The kitchen light has been turned on successfully.", is_voice=True)
    assert res.approved is True
    assert res.score >= 0.85
    assert len(res.flags) == 0


def test_critic_detects_secret_or_credential_leak():
    """Submits draft containing simulated OpenAI API key; asserts rejection and redaction."""
    critic = CriticAgent()
    leaked_draft = "Your API key is sk-12345abcdef67890ghijkl for access."
    res = critic.critique_draft(leaked_draft)
    assert res.approved is False
    assert res.score == 0.0
    assert "SECRET_LEAK" in res.flags
    assert res.suggested_refinement is not None
    assert "sk-12345" not in res.suggested_refinement


def test_critic_flags_hallucinated_facts_or_contradictions():
    """Submits draft contradicting stored context; asserts score reduction."""
    critic = CriticAgent()
    context = [{"id": "c1", "content": "Database uses WAL mode", "conflicts_with": "journal_mode_delete"}]
    res = critic.critique_draft("We have switched to journal_mode_delete.", context=context)
    assert res.approved is False
    assert "CONTRADICTION" in res.flags


def test_critic_self_refine_enforces_atomicity_and_style():
    """Submits overly verbose multi-concept draft; asserts non-atomic flag."""
    critic = CriticAgent()
    draft = "Everything about our system architecture, storage, audio pipeline, and security all in one place."
    res = critic.critique_draft(draft)
    assert "NON_ATOMIC" in res.flags
