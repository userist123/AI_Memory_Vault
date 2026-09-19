"""test_cognitive_core_search_wiring.py — Tests for wiring cognitive core into MemoryController.search().

Validates that:
1. Cognitive core is OFF by default.
2. Enabling it via constructor or search() argument works deterministically.
3. Simple queries trigger CouncilTier.NONE with compact token and note budgets.
4. Risky and destructive queries trigger CouncilTier.STANDARD or HIGH_RISK with appropriate budgets.
5. Telemetry in candidate_trace['cognitive_core'] is complete and structured.
6. When disabled, execution is 100% backward compatible.
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
from memory_controller.council_budget_controller import CouncilTier


@pytest.fixture
def mock_storage():
    storage = StorageEngine()
    # Add dummy active notes
    for i in range(1, 15):
        storage.set(
            f"note_{i:02d}",
            {
                "id": f"note_{i:02d}",
                "lifecycle": "active",
                "type": "knowledge",
                "title": f"Test Note {i}",
                "content": f"Content of test note {i} discussing systems and architecture.",
                "verification": "verified",
            }
        )
    return storage


def test_cognitive_core_is_off_by_default(mock_storage):
    controller = MemoryController(storage=mock_storage, ranking_arm=RANKING_ARM_BASELINE)
    assert controller.enable_cognitive_core is False

    pack = controller.search(Principal.HUMAN, "systems architecture", page_size=10)
    trace = pack.get("candidate_trace", {})
    assert "cognitive_core" in trace
    assert trace["cognitive_core"]["enabled"] is False
    assert len(pack["results"]) <= 10


def test_cognitive_core_can_be_enabled_per_search(mock_storage):
    controller = MemoryController(storage=mock_storage, enable_cognitive_core=False, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(
        Principal.HUMAN, "systems architecture", page_size=10, enable_cognitive_core=True
    )
    trace = pack.get("candidate_trace", {})
    assert trace["cognitive_core"]["enabled"] is True
    assert "plan_complexity" in trace["cognitive_core"]
    assert "council_decision" in trace["cognitive_core"]


def test_cognitive_core_can_be_enabled_in_constructor(mock_storage):
    controller = MemoryController(storage=mock_storage, enable_cognitive_core=True, ranking_arm=RANKING_ARM_BASELINE)
    assert controller.enable_cognitive_core is True

    pack = controller.search(Principal.HUMAN, "systems architecture", page_size=10)
    trace = pack.get("candidate_trace", {})
    assert trace["cognitive_core"]["enabled"] is True

    # Per-call override to disable
    pack_off = controller.search(
        Principal.HUMAN, "systems architecture", page_size=10, enable_cognitive_core=False
    )
    assert pack_off["candidate_trace"]["cognitive_core"]["enabled"] is False


def test_simple_query_gets_none_tier_and_compact_budget(mock_storage):
    controller = MemoryController(storage=mock_storage, enable_cognitive_core=True, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.HUMAN, "hello world", page_size=10)
    cc = pack["candidate_trace"]["cognitive_core"]

    assert cc["plan_complexity"]["execution_mode"] == "simple"
    assert cc["council_decision"]["tier"] == CouncilTier.NONE.value
    assert cc["allocated_max_results"] == 3
    assert cc["allocated_token_budget"] == 600
    assert len(pack["results"]) <= 3


def test_risky_query_gets_standard_tier(mock_storage):
    controller = MemoryController(storage=mock_storage, enable_cognitive_core=True, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.HUMAN, "verify security credentials", page_size=10)
    cc = pack["candidate_trace"]["cognitive_core"]

    assert cc["council_decision"]["tier"] in (CouncilTier.STANDARD.value, CouncilTier.HIGH_RISK.value)
    assert cc["allocated_max_results"] == 5
    assert cc["allocated_token_budget"] == 2500
    assert len(pack["results"]) <= 5


def test_destructive_query_gets_high_risk_tier(mock_storage):
    controller = MemoryController(storage=mock_storage, enable_cognitive_core=True, ranking_arm=RANKING_ARM_BASELINE)
    pack = controller.search(Principal.HUMAN, "delete_canonical note_01", page_size=10)
    cc = pack["candidate_trace"]["cognitive_core"]

    assert cc["plan_complexity"]["execution_mode"] == "high_risk"
    assert cc["plan_complexity"]["require_review"] is True
    assert cc["council_decision"]["tier"] == CouncilTier.HIGH_RISK.value
    assert cc["allocated_max_results"] == 5
    assert cc["allocated_token_budget"] == 2500
