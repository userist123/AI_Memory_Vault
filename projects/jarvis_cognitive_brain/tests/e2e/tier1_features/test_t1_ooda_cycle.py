"""
Tier 1 Feature Coverage: Complete Stateful OODA Cognitive Loop (R1).
Covers Observe (Intent classification), Retrieve (Associative recall), Reason/Plan (ActivePlan),
Act (Tool routing), Reflect (6-stage Reflexion), Consolidate (Lesson synthesis), and Checkpointing.
"""

import pytest
import uuid
from pathlib import Path

from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal, NoteType, Lifecycle
from jarvis.core.models import (
    PerceptionEvent,
    UserIntent,
    IntentType,
    WorkingMemory,
    ActivePlan,
    PlanStep,
    StepStatus,
    OODACycleResult,
)
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.core.executive import CognitiveExecutive


@pytest.mark.asyncio
async def test_ooda_observe_intent_classification(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test Observe phase parsing sensory input into structured UserIntent."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    
    event_iot = PerceptionEvent(channel="voice", raw_data="Turn on the living room ceiling light")
    intent_iot = await engine.observe(event_iot)
    assert intent_iot.intent_type == IntentType.IOT_CONTROL
    assert intent_iot.requires_tool is True

    event_query = PerceptionEvent(channel="text", raw_data="What is the architectural rule for memory persistence?")
    intent_query = await engine.observe(event_query)
    assert intent_query.intent_type in [IntentType.QUERY, IntentType.TASK, IntentType.CONVERSATION]


@pytest.mark.asyncio
async def test_ooda_retrieve_associative_memory_admission(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test Retrieve phase populating working memory with top-ranked associative notes."""
    # Seed a note
    note_id = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "system",
            "tags": ["audio", "bargein"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "execution", "source_ref": "seed"},
            "confidence": "high",
            "verification": "partially_verified",
            "relations": [],
            "content": "Barge-In interruption latency must be under 50 milliseconds.",
        },
    )

    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    intent = UserIntent(raw_text="What is the barge-in latency requirement?", intent_type=IntentType.QUERY)
    
    retrieved = await engine.retrieve(intent)
    assert len(retrieved) >= 1
    assert any("50 milliseconds" in n.get("content", "") for n in retrieved)
    assert len(engine.working_memory.active_chunks) >= 1


@pytest.mark.asyncio
async def test_ooda_reason_and_plan_formulation(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test Reason/Plan phase formulating discrete multi-step ActivePlan."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    intent = UserIntent(
        raw_text="Turn on the kitchen strip and set brightness to 150",
        intent_type=IntentType.IOT_CONTROL,
        requires_tool=True,
    )

    plan = await engine.reason_and_plan(intent, context=[])
    assert isinstance(plan, ActivePlan)
    assert len(plan.steps) >= 1
    first_step = plan.steps[0]
    assert first_step.action in ["iot_call", "search", "read", "reason", "synthesize_response"]
    assert first_step.status == StepStatus.PENDING


@pytest.mark.asyncio
async def test_ooda_act_tool_execution(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test Act phase executing plan steps through tool router."""
    tool_calls = []

    def mock_tool_router(action: str, kwargs: dict):
        tool_calls.append({"action": action, "kwargs": kwargs})
        return {"status": "success", "result": "Light toggled on"}

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=mock_tool_router,
    )

    step = PlanStep(
        step_id=1,
        action="iot_call",
        kwargs={"entity_id": "light.kitchen_strip", "service": "turn_on"},
    )

    result = await engine.act_step(step)
    assert result.status == "success"
    assert len(tool_calls) == 1
    assert tool_calls[0]["action"] == "iot_call"


@pytest.mark.asyncio
async def test_ooda_reflect_formal_six_stage_reflexion(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test Reflect phase triggering 6-stage formal Reflexion on step failure."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)

    failed_step = PlanStep(
        step_id=1,
        action="iot_call",
        kwargs={"entity_id": "light.offline_node"},
        status=StepStatus.FAILED,
        error="ConnectionRefusedError: Host unreachable",
    )

    reflection_id = await engine.reflect(failed_step, error="ConnectionRefusedError: Host unreachable")
    assert reflection_id is not None

    stored_reflection = sqlite_storage.get(reflection_id)
    assert stored_reflection is not None
    assert stored_reflection["type"] in ["lesson", "error"]
    assert stored_reflection["lifecycle"] == "REVIEW"


@pytest.mark.asyncio
async def test_ooda_consolidate_lesson_synthesis(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test Consolidate phase proposing validated reflection notes into REVIEW storage."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)

    # Seed two lessons for consolidation
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.AI_AGENT,
        {
            "id": id1,
            "type": "lesson",
            "lifecycle": "REVIEW",
            "category": "resilience",
            "tags": ["network", "timeout"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "inference", "source_ref": "reflexion"},
            "confidence": "medium",
            "verification": "unverified",
            "relations": [],
            "content": "Lesson: Always verify network connectivity before sending IoT command.",
        },
    )
    sqlite_storage.propose(
        Principal.AI_AGENT,
        {
            "id": id2,
            "type": "lesson",
            "lifecycle": "REVIEW",
            "category": "resilience",
            "tags": ["network", "retry"],
            "created": "2026-08-27T12:00:00Z",
            "updated": "2026-08-27T12:00:00Z",
            "provenance": {"source_type": "inference", "source_ref": "reflexion"},
            "confidence": "medium",
            "verification": "unverified",
            "relations": [],
            "content": "Lesson: Use exponential backoff when retrying failed socket requests.",
        },
    )

    consolidated_id = await engine.consolidate()
    assert consolidated_id is not None
    stored_cons = sqlite_storage.get(consolidated_id)
    assert stored_cons is not None
    assert stored_cons["type"] == "knowledge"


@pytest.mark.asyncio
async def test_ooda_full_end_to_end_cycle(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test complete end-to-end OODA cycle execution."""
    mock_llm.set_next_response("The living room ceiling light has been turned on successfully.")
    
    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        tool_executor=lambda act, kw: {"status": "success"},
    )

    event = PerceptionEvent(channel="voice", raw_data="Turn on the living room light")
    cycle_result = await engine.execute_cycle(event)

    assert isinstance(cycle_result, OODACycleResult)
    assert cycle_result.intent is not None
    assert cycle_result.active_plan is not None
    assert len(cycle_result.step_results) >= 1


@pytest.mark.asyncio
async def test_ooda_atomic_checkpointing_state_persistence(
    mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine, tmp_path: Path
):
    """Test CognitiveExecutive atomic checkpointing to disk and state restoration."""
    checkpoint_dir = tmp_path / "checkpoints"
    exec_daemon = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        checkpoint_dir=checkpoint_dir,
    )

    # Populate working memory and active plan
    exec_daemon.working_memory.admit([{"id": "mem-1", "content": "Active context item"}])
    exec_daemon.active_plan = ActivePlan(
        goal="Perform multi-stage deployment",
        steps=[PlanStep(step_id=1, action="read", status=StepStatus.SUCCESS)],
        current_step_index=1,
    )

    # Save checkpoint
    exec_daemon.save_checkpoint()
    assert (checkpoint_dir / "wm.json").exists()
    assert (checkpoint_dir / "plan.json").exists()

    # Create new daemon instance and load checkpoint
    recovery_daemon = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        checkpoint_dir=checkpoint_dir,
    )
    recovered = recovery_daemon.load_checkpoint()

    assert recovered is True
    assert len(recovery_daemon.working_memory.active_chunks) == 1
    assert recovery_daemon.active_plan is not None
    assert recovery_daemon.active_plan.goal == "Perform multi-stage deployment"
