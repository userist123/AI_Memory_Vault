"""
Unit Tests for Complete Stateful OODA Cognitive Loop, Executive Daemon, Reflexion, and Reconsolidation.
"""

import os
import uuid
import tempfile
import shutil
from pathlib import Path
from typing import Dict, Any
import pytest

from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.reflection import ReflexionEngine, FormalReflexion
from jarvis.memory.consolidation import ConsolidationEngine
from jarvis.core.models import (
    PerceptionEvent,
    IntentType,
    UserIntent,
    WorkingMemory,
    ActivePlan,
    PlanStep,
    StepStatus,
)
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.core.executive import CognitiveExecutive


@pytest.fixture
def temp_checkpoint_dir():
    temp_dir = Path(tempfile.mkdtemp())
    yield temp_dir
    shutil.rmtree(temp_dir, ignore_errors=True)


# ============================================================================
# 1. End-to-End OODA Cycle Tests
# ============================================================================

@pytest.mark.asyncio
async def test_e2e_ooda_query_cycle(sqlite_engine: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Verify complete OODA cycle: Observe -> Retrieve -> Plan -> Act -> Synthesize."""
    # Seed knowledge into memory
    seed_id = str(uuid.uuid4())
    sqlite_engine.set_note_atomic({
        "id": seed_id,
        "type": NoteType.KNOWLEDGE.value,
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "ai",
        "tags": ["ooda"],
        "created": "2026-08-27",
        "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high",
        "verification": "verified",
        "content": "Jarvis executes stateful OODA loops with 6 distinct phases.",
        "relations": [],
    })

    mock_llm.set_next_response("Based on memory, Jarvis runs a 6-phase stateful OODA loop.")

    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_engine)
    perception = PerceptionEvent(channel="voice", raw_data="What is the architecture of the OODA loop?")

    result = await engine.execute_cycle(perception)

    assert result.intent.intent_type == IntentType.QUERY
    assert len(result.context_used) >= 1
    assert result.active_plan.is_complete()
    assert len(result.step_results) == 1
    assert result.step_results[0].status == "success"
    assert "Based on memory" in result.step_results[0].result["answer"]


@pytest.mark.asyncio
async def test_e2e_ooda_iot_control_cycle(sqlite_engine: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Verify OODA loop parses IoT control commands and dispatches device actions."""
    mock_llm.set_next_response("Living room light turned on successfully.")

    executed_tools = []
    def mock_tool_dispatcher(action: str, kwargs: Dict[str, Any]):
        executed_tools.append((action, kwargs))
        return {"status": "ok", "state": "on", "entity_id": "light.living_room"}

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_engine,
        tool_executor=mock_tool_dispatcher,
    )

    perception = PerceptionEvent(channel="voice", raw_data="Turn on the living room light")
    result = await engine.execute_cycle(perception)

    assert result.intent.intent_type == IntentType.IOT_CONTROL
    assert len(result.step_results) == 2
    assert result.step_results[0].action == "iot_call"
    assert result.step_results[0].status == "success"
    assert len(executed_tools) == 1


# ============================================================================
# 2. Reflexion & Error Learning Tests
# ============================================================================

@pytest.mark.asyncio
async def test_ooda_reflect_on_step_failure(sqlite_engine: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """Verify that step execution failures trigger 6-stage formal Reflexion and store notes in REVIEW."""
    def failing_tool_dispatcher(action: str, kwargs: Dict[str, Any]):
        raise ConnectionResetError("Device connection timed out after 5000ms")

    engine = OODACognitiveEngine(
        llm_provider=mock_llm,
        storage_engine=sqlite_engine,
        tool_executor=failing_tool_dispatcher,
    )

    perception = PerceptionEvent(channel="voice", raw_data="Turn on the kitchen switch")
    result = await engine.execute_cycle(perception)

    assert not result.active_plan.is_complete()
    assert len(result.reflections) == 1

    reflection_note = sqlite_engine.get(result.reflections[0])
    assert reflection_note is not None
    assert reflection_note["type"] == NoteType.ERROR.value
    assert reflection_note["lifecycle"] == Lifecycle.REVIEW.value
    assert "Formal Reflexion Analysis" in reflection_note["content"]
    assert "Error" in reflection_note["content"]
    assert "Root Cause" in reflection_note["content"]
    assert "Fix Applied" in reflection_note["content"]


# ============================================================================
# 3. Cognitive Executive Daemon & Atomic Checkpoints
# ============================================================================

@pytest.mark.asyncio
async def test_cognitive_executive_atomic_checkpointing_and_recovery(
    sqlite_engine: SQLiteStorageEngine, mock_llm: MockLLMProvider, temp_checkpoint_dir: Path
):
    """Verify CognitiveExecutive saves atomic checkpoints and restores working memory on recovery."""
    exec_daemon = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_engine,
        checkpoint_dir=temp_checkpoint_dir,
    )

    # Seed note
    nid = str(uuid.uuid4())
    sqlite_engine.set_note_atomic({
        "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "sec",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "init"},
        "confidence": "high", "verification": "verified",
        "content": "Checkpointing guarantees crash resilience.", "relations": []
    })

    mock_llm.set_next_response("Checkpointing completed.")
    await exec_daemon.process_utterance("Explain checkpointing resilience")

    # Assert checkpoint files exist on disk
    wm_file = temp_checkpoint_dir / "wm.json"
    plan_file = temp_checkpoint_dir / "plan.json"
    assert wm_file.exists()
    assert plan_file.exists()

    # Simulate restart by creating a new daemon instance pointing to same directory
    new_daemon = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_engine,
        checkpoint_dir=temp_checkpoint_dir,
    )
    recovered = new_daemon.load_checkpoint()
    assert recovered is True
    assert len(new_daemon.working_memory.get_active_context()) >= 1
    assert new_daemon.active_plan is not None


# ============================================================================
# 4. Memory Reconsolidation & Lesson Consolidation
# ============================================================================

def test_memory_reconsolidation_plasticity(sqlite_engine: SQLiteStorageEngine):
    """Verify Memory Reconsolidation challenge transitions note to RECONSOLIDATING and resolves back to ACTIVE."""
    consolidator = ConsolidationEngine(sqlite_engine)
    note_id = str(uuid.uuid4())

    orig_note = {
        "id": note_id,
        "type": NoteType.KNOWLEDGE.value,
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "networking",
        "tags": ["dns"],
        "created": "2026-08-27",
        "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "rfc"},
        "confidence": "high",
        "verification": "verified",
        "content": "DNS resolves via port 53 UDP.",
        "relations": [],
    }
    sqlite_engine.set_note_atomic(orig_note)

    # Challenge with conflicting evidence (DoT/DoH port 853/443)
    challenged = consolidator.challenge(
        note_id,
        conflicting_evidence={"observed_port": 853, "protocol": "DoT"},
        principal=Principal.AI_AGENT,
    )

    assert challenged is not None
    assert challenged["lifecycle"] == Lifecycle.RECONSOLIDATING.value
    assert challenged["previous_version"]["content"] == "DNS resolves via port 53 UDP."

    # Resolve challenge
    resolved = consolidator.resolve_challenge(
        note_id,
        resolved_node={"content": "DNS resolves via port 53 (plain) or port 853 (DoT).", "relations": []},
        principal=Principal.HUMAN,
    )

    assert resolved["lifecycle"] == Lifecycle.ACTIVE.value
    assert "port 853" in resolved["content"]


def test_lesson_consolidation_distillation(sqlite_engine: SQLiteStorageEngine):
    """Verify consolidating multiple REVIEW lesson notes into a unified knowledge note."""
    consolidator = ConsolidationEngine(sqlite_engine)

    l1_id = str(uuid.uuid4())
    l2_id = str(uuid.uuid4())

    l1 = {
        "id": l1_id, "type": NoteType.LESSON.value, "lifecycle": Lifecycle.REVIEW.value,
        "category": "lesson", "tags": ["db"], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "inference", "source_ref": "l1"},
        "confidence": "medium", "verification": "unverified",
        "content": "Lesson 1: Always specify busy_timeout=5000 in SQLite WAL mode to avoid database locks.",
        "relations": []
    }
    l2 = {
        "id": l2_id, "type": NoteType.LESSON.value, "lifecycle": Lifecycle.REVIEW.value,
        "category": "lesson", "tags": ["db"], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "inference", "source_ref": "l2"},
        "confidence": "medium", "verification": "unverified",
        "content": "Lesson 2: Always use BEGIN IMMEDIATE transactions for multi-threaded SQLite write paths.",
        "relations": []
    }

    sqlite_engine.set_note_atomic(l1)
    sqlite_engine.set_note_atomic(l2)

    new_knowledge_id = consolidator.consolidate_lessons(Principal.AI_AGENT)
    assert new_knowledge_id is not None

    consolidated_note = sqlite_engine.get(new_knowledge_id)
    assert consolidated_note is not None
    assert consolidated_note["type"] == NoteType.KNOWLEDGE.value
    assert consolidated_note["lifecycle"] == Lifecycle.REVIEW.value
    assert "Consolidated Domain Knowledge" in consolidated_note["content"]

    # Verify sources are archived
    assert sqlite_engine.get(l1_id)["lifecycle"] == Lifecycle.ARCHIVED.value
    assert sqlite_engine.get(l2_id)["lifecycle"] == Lifecycle.ARCHIVED.value
