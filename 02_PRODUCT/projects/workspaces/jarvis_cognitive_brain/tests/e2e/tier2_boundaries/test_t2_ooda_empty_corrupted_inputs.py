"""
Tier 2 Boundary & Invariants: OODA Loop Edge Cases & Corrupted Inputs (R1).
Covers empty inputs, special character fuzzing, massive context handling,
corrupted checkpoint recovery, and cyclic lineage traversal protection.
"""

import pytest
import uuid
import json
from pathlib import Path

from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal
from jarvis.core.models import PerceptionEvent, UserIntent, IntentType
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.core.executive import CognitiveExecutive


@pytest.mark.asyncio
async def test_ooda_empty_string_perception_event(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test empty string sensory input defaults to CONVERSATION without crashing."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    event = PerceptionEvent(channel="voice", raw_data="")

    intent = await engine.observe(event)
    assert intent.intent_type == IntentType.CONVERSATION
    assert intent.requires_tool is False


@pytest.mark.asyncio
async def test_ooda_excessive_whitespace_and_special_chars(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test noisy, whitespace-heavy, and special character inputs are handled gracefully."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    noisy_text = "   \n\t  @#$%^&*()_+ \t\n  "
    event = PerceptionEvent(channel="text", raw_data=noisy_text)

    intent = await engine.observe(event)
    assert intent is not None
    assert isinstance(intent.raw_text, str)


@pytest.mark.asyncio
async def test_ooda_massive_prompt_token_truncation(mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine):
    """Test massive token input (100k chars) is accepted and processed without buffer overflow."""
    engine = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=sqlite_storage)
    giant_text = "Explain the nature of cognitive memory. " * 2500
    event = PerceptionEvent(channel="text", raw_data=giant_text)

    intent = await engine.observe(event)
    assert intent is not None
    plan = await engine.reason_and_plan(intent, context=[])
    assert len(plan.steps) >= 1


def test_ooda_corrupted_checkpoint_json_recovery(
    mock_llm: MockLLMProvider, sqlite_storage: SQLiteStorageEngine, tmp_path: Path
):
    """Test CognitiveExecutive recovers gracefully when checkpoint JSON files are corrupted."""
    checkpoint_dir = tmp_path / "checkpoints_corrupted"
    checkpoint_dir.mkdir(parents=True, exist_ok=True)

    # Write broken JSON files
    (checkpoint_dir / "wm.json").write_text("{ broken invalid json ...", encoding="utf-8")
    (checkpoint_dir / "plan.json").write_text("[ not a valid plan dict ]", encoding="utf-8")

    exec_daemon = CognitiveExecutive(
        llm_provider=mock_llm,
        storage_engine=sqlite_storage,
        checkpoint_dir=checkpoint_dir,
    )

    # Should safely return False and initialize fresh state
    loaded = exec_daemon.load_checkpoint()
    assert loaded is False
    assert len(exec_daemon.working_memory.active_chunks) == 0


def test_ooda_cyclic_lineage_protection(sqlite_storage: SQLiteStorageEngine):
    """Test recursive CTE lineage traversal does not infinite loop on cyclic references."""
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())

    # Create two nodes with mutual reference
    n1 = {
        "id": id1,
        "type": "decision",
        "lifecycle": "SUPERSEDED",
        "category": "core",
        "superseded_by": id2,
        "content": "Node 1",
    }
    n2 = {
        "id": id2,
        "type": "decision",
        "lifecycle": "ACTIVE",
        "category": "core",
        "supersedes": id1,
        "content": "Node 2",
    }
    sqlite_storage.propose(Principal.HUMAN, n1)
    sqlite_storage.propose(Principal.HUMAN, n2)

    # Traversal should terminate safely within max_depth limit
    lineage = sqlite_storage.get_lineage(id1, max_depth=10)
    assert len(lineage) >= 1
