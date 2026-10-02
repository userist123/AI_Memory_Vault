"""Canonical memory data-route gate tests."""

import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.append(str(PACKAGES))

import pytest

from memory.data_router import MemoryDataRouter, DataRouteViolation


def test_model_egress_route_is_canonical_and_auditable():
    router = MemoryDataRouter()
    pack = {
        "requestId": "route-test",
        "agentId": "AI_AGENT",
        "results": [
            {
                "id": "n1",
                "content": "trusted memory",
                "verification": {"status": "TRUSTED"},
                "provenance": {"source_type": "user", "source_ref": "test"},
            }
        ],
    }

    routed = router.route_to_model(
        pack,
        source="search",
        principal="AI_AGENT",
    )

    assert routed["data_route"]["source"] == "search"
    assert routed["data_route"]["principal"] == "AI_AGENT"
    assert routed["data_route"]["stages"] == [
        "source",
        "verification",
        "provenance",
        "security_boundary",
        "token_economy",
        "model_egress",
    ]
    assert routed["data_route"]["model_egress"] is True
    assert routed["results"][0]["id"] == "n1"


def test_model_egress_route_rejects_unverified_data():
    router = MemoryDataRouter()
    pack = {
        "requestId": "route-test",
        "agentId": "AI_AGENT",
        "results": [{"id": "n1", "content": "unverified"}],
    }

    with pytest.raises(DataRouteViolation, match="verification"):
        router.route_to_model(pack, source="search", principal="AI_AGENT")
