"""20_TESTS/regression/test_p4_runtime_wiring_harness.py — P4-E Runtime Wiring Harness Test Suite.

Validates the prospective wiring harness and translation shims between:
MemoryController.search() parameters
  → request_from_controller()
  → RetrievalIntegrationAdapter.search()
  → response_to_controller()
  → Standard Controller Pack Dictionary
"""
from __future__ import annotations

from enum import Enum
from pathlib import Path
from typing import Any, Dict

import pytest

from cognitive_core.integration_adapter import IntegrationSearchRequest
from cognitive_core.p4_runtime_wiring_harness import (
    P4RuntimeWiringHarness,
    request_from_controller,
    response_to_controller,
)
from cognitive_core.vault_index import VaultIndex


class MockControllerPrincipal(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"
    INTRUDER = "untrusted_intruder"


class MockControllerLifecycle(Enum):
    RAW = "RAW"
    REVIEW = "REVIEW"
    ACTIVE = "ACTIVE"
    ARCHIVED = "ARCHIVED"


@pytest.fixture
def harness_vault(tmp_path: Path) -> VaultIndex:
    notes_data = [
        (
            "01_ARCHITECTURE/knw-harness-01.md",
            "id: knw-harness-01\ntitle: Architecture Core Engine\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Core engine architecture and consensus specification.",
        ),
        (
            "01_ARCHITECTURE/knw-harness-02.md",
            "id: knw-harness-02\ntitle: Architecture Secondary Node\ntype: knowledge\nlifecycle: ACTIVE\nverification: verified\nprovenance:\n  source_type: official\n",
            "Secondary worker nodes and task replication channels.",
        ),
        (
            "01_ARCHITECTURE/knw-harness-unverified.md",
            "id: knw-harness-unverified\ntitle: Architecture Unverified Draft\ntype: knowledge\nlifecycle: ACTIVE\nverification: unverified\n",
            "Poison unverified note that must not be exposed.",
        ),
        (
            "01_ARCHITECTURE/knw-harness-review.md",
            "id: knw-harness-review\ntitle: Architecture Review Pending\ntype: knowledge\nlifecycle: REVIEW\nverification: unverified\n",
            "Review candidate note that must not be exposed.",
        ),
    ]
    for rel, fm, b in notes_data:
        p = tmp_path / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(f"---\n{fm}\n---\n{b}", encoding="utf-8")

    return VaultIndex.load(tmp_path)


@pytest.fixture
def wiring_harness(harness_vault: VaultIndex) -> P4RuntimeWiringHarness:
    return P4RuntimeWiringHarness.from_vault(harness_vault)


# ===========================================================================
# 1. SHIM UNIT TESTS
# ===========================================================================

class TestWiringShims:
    def test_request_from_controller_converts_enums_and_disclosure(self):
        req = request_from_controller(
            principal=MockControllerPrincipal.AI_AGENT,
            query="Architecture",
            page_size=5,
            lifecycles=[MockControllerLifecycle.ACTIVE],
            types=["knowledge"],
            disclosure_level="metadata",
            request_id="custom-req-01",
        )
        assert isinstance(req, IntegrationSearchRequest)
        assert req.principal == MockControllerPrincipal.AI_AGENT
        assert req.query == "Architecture"
        assert req.page_size == 5
        assert req.lifecycles == ["ACTIVE"]
        assert req.types == ["knowledge"]
        assert req.disclosure_level == "summary"  # 'metadata' mapped to 'summary'
        assert req.request_id == "custom-req-01"

    @pytest.mark.parametrize("controller_level, expected_adapter_level", [
        ("metadata", "summary"),
        ("snippet", "summary"),
        ("sections", "standard"),
        ("full", "full"),
        ("unknown", "standard"),
    ])
    def test_disclosure_mapping(self, controller_level: str, expected_adapter_level: str):
        req = request_from_controller(
            principal=MockControllerPrincipal.AI_AGENT,
            query="Architecture",
            disclosure_level=controller_level,
        )
        assert req.disclosure_level == expected_adapter_level


# ===========================================================================
# 2. HARNESS EXECUTION TESTS
# ===========================================================================

class TestHarnessExecution:
    def test_execute_controller_search_returns_expected_pack_dict(
        self, wiring_harness: P4RuntimeWiringHarness
    ):
        pack = wiring_harness.execute_controller_search(
            principal=MockControllerPrincipal.AI_AGENT,
            query="Architecture",
            page_size=10,
            disclosure_level="metadata",
        )

        assert isinstance(pack, dict)
        assert pack["requestId"] == "search"
        assert pack["agentId"] == "ai_agent"
        assert pack["disclosureLevel"] == "metadata"
        assert "budget" in pack
        assert isinstance(pack["results"], list)
        assert len(pack["results"]) == 2  # exactly the 2 ACTIVE+verified notes

        returned_ids = {r["id"] for r in pack["results"]}
        assert returned_ids == {"knw-harness-01", "knw-harness-02"}

        for r in pack["results"]:
            assert r["lifecycle"] == "ACTIVE"
            assert r["verification"] == "verified"
            assert "summary" in r
            assert "citation" in r

    def test_pagination_via_harness(self, wiring_harness: P4RuntimeWiringHarness):
        # Page 1 (page_size 1)
        pack1 = wiring_harness.execute_controller_search(
            principal=MockControllerPrincipal.AI_AGENT,
            query="Architecture",
            page_size=1,
        )
        assert len(pack1["results"]) == 1
        cursor = pack1["next_page_token"]
        assert cursor is not None

        # Page 2
        pack2 = wiring_harness.execute_controller_search(
            principal=MockControllerPrincipal.AI_AGENT,
            query="Architecture",
            page_size=1,
            page_token=cursor,
        )
        assert len(pack2["results"]) == 1
        assert pack2["next_page_token"] is None
        assert pack1["results"][0]["id"] != pack2["results"][0]["id"]

    def test_security_violation_raises_permission_error(
        self, wiring_harness: P4RuntimeWiringHarness
    ):
        with pytest.raises(PermissionError) as exc_info:
            wiring_harness.execute_controller_search(
                principal=MockControllerPrincipal.AI_AGENT,
                query="Architecture",
                lifecycles=[MockControllerLifecycle.REVIEW],
            )
        assert "Security Boundary Violation" in str(exc_info.value)

    def test_untrusted_principal_raises_permission_error(
        self, wiring_harness: P4RuntimeWiringHarness
    ):
        with pytest.raises(PermissionError) as exc_info:
            wiring_harness.execute_controller_search(
                principal=MockControllerPrincipal.INTRUDER,
                query="Architecture",
            )
        assert "Security Boundary Violation" in str(exc_info.value)

    def test_cursor_tampering_raises_value_error(
        self, wiring_harness: P4RuntimeWiringHarness
    ):
        with pytest.raises(ValueError) as exc_info:
            wiring_harness.execute_controller_search(
                principal=MockControllerPrincipal.AI_AGENT,
                query="Architecture",
                page_token="bad-forged-token",
            )
        assert "Invalid pagination cursor" in str(exc_info.value)

    def test_invalid_parameter_raises_value_error(
        self, wiring_harness: P4RuntimeWiringHarness
    ):
        with pytest.raises(ValueError) as exc_info:
            wiring_harness.execute_controller_search(
                principal=MockControllerPrincipal.AI_AGENT,
                query="Architecture",
                page_size=0,
            )
        assert "validation failed" in str(exc_info.value)
