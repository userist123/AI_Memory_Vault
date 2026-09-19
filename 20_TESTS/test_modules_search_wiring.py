"""test_modules_search_wiring.py — Tests for wiring of cognitive modules in MemoryController.search().

Validates that:
1. spreading_activation, working_memory, global_workspace, reasoning, and executive are OFF by default.
2. Enabling each module via constructor or search() argument activates it deterministically.
3. working_memory re-ranks/bounds active context via AttentionModel.
4. global_workspace submits proposals and broadcasts winning coalition.
5. reasoning runs Tree-of-Thought or direct synthesis grounded in retrieved context.
6. executive parses intent and traces execution loop.
7. All 5 modules leave clear candidate_trace signatures.
"""
import os
import pytest

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from memory_controller.authorizer import Principal
from memory_controller.controller import (
    MemoryController,
    StorageEngine,
    RANKING_ARM_BASELINE,
)


@pytest.fixture
def test_storage():
    storage = StorageEngine()
    for i in range(1, 10):
        storage.set(
            f"note_{i:02d}",
            {
                "id": f"note_{i:02d}",
                "lifecycle": "active",
                "type": "knowledge",
                "title": f"Cognitive architecture note {i}",
                "content": f"Detailed content of note {i} explaining memory, global workspace, and reasoning.",
                "verification": "verified",
            }
        )
    return storage


def test_all_modules_off_by_default(test_storage):
    controller = MemoryController(storage=test_storage, ranking_arm=RANKING_ARM_BASELINE)
    assert controller.enable_spreading_activation is False
    assert controller.enable_working_memory is False
    assert controller.enable_global_workspace is False
    assert controller.enable_reasoning is False
    assert controller.enable_executive is False

    pack = controller.search(Principal.HUMAN, "cognitive architecture", page_size=5)
    trace = pack.get("candidate_trace", {})

    assert trace.get("spreading_activation_enabled") is False
    assert trace.get("working_memory_enabled") is False
    assert trace.get("global_workspace_enabled") is False
    assert trace.get("reasoning_enabled") is False
    assert trace.get("executive_enabled") is False


def test_working_memory_wiring(test_storage):
    controller = MemoryController(storage=test_storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(
        Principal.HUMAN, "cognitive architecture", page_size=5, enable_working_memory=True
    )
    trace = pack.get("candidate_trace", {})
    assert trace.get("working_memory_enabled") is True
    assert "working_memory_active_count" in trace
    assert trace["working_memory_active_count"] <= 5
    assert len(pack["results"]) <= 5


def test_global_workspace_wiring(test_storage):
    controller = MemoryController(storage=test_storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(
        Principal.HUMAN, "cognitive architecture", page_size=5, enable_global_workspace=True
    )
    trace = pack.get("candidate_trace", {})
    assert trace.get("global_workspace_enabled") is True
    assert "global_workspace_broadcast" in trace
    broadcast = trace["global_workspace_broadcast"]
    assert broadcast is not None
    assert "winner_agent" in broadcast
    assert "coalition" in broadcast


def test_reasoning_wiring(test_storage):
    controller = MemoryController(storage=test_storage, ranking_arm=RANKING_ARM_BASELINE)
    # Query with 'why' to trigger Tree-of-Thought in reasoning engine
    pack = controller.search(
        Principal.HUMAN, "why does memory architecture matter", page_size=5, enable_reasoning=True
    )
    trace = pack.get("candidate_trace", {})
    assert trace.get("reasoning_enabled") is True
    assert "reasoning_synthesis" in trace
    assert trace.get("reasoning_mode") in ("tree_of_thought", "direct")
    assert isinstance(trace["reasoning_synthesis"], str)


def test_executive_wiring(test_storage):
    controller = MemoryController(storage=test_storage, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(
        Principal.HUMAN, "analyze system architecture", page_size=5, enable_executive=True
    )
    trace = pack.get("candidate_trace", {})
    assert trace.get("executive_enabled") is True
    assert "executive_intent" in trace
    assert trace["executive_intent"].get("type") == "task"


def test_constructor_flags_activation(test_storage):
    controller = MemoryController(
        storage=test_storage,
        enable_working_memory=True,
        enable_global_workspace=True,
        enable_reasoning=True,
        enable_executive=True,
        ranking_arm=RANKING_ARM_BASELINE,
    )
    pack = controller.search(Principal.HUMAN, "system architecture", page_size=5)
    trace = pack.get("candidate_trace", {})
    assert trace.get("working_memory_enabled") is True
    assert trace.get("global_workspace_enabled") is True
    assert trace.get("reasoning_enabled") is True
    assert trace.get("executive_enabled") is True
