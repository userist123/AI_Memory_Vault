"""Canonical memory data-route gate tests."""

import inspect
import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.append(str(PACKAGES))

import pytest

from memory.data_router import MemoryDataEgressGate, MemoryDataRouter, DataRouteViolation
from memory.controller import MemoryController
from retrieval.financial_search import MultiLayeredFinancialSearchEngine


def test_model_egress_route_is_canonical_and_auditable():
    router = MemoryDataRouter()
    pack = {
        "requestId": "route-test",
        "agentId": "AI_AGENT",
        "budget": {"hard_tokens": 240},
        "reduction": {"tokenizer_mode": "test", "net_tokens_saved": 10},
        "results": [
            {
                "id": "n1",
                "content": "trusted memory",
                "verification": {"status": "TRUSTED"},
                "provenance": {"source_type": "user", "source_ref": "test"},
            }
        ],
    }

    called = []
    router.register("search", lambda: called.append(True) or pack)
    routed = router.dispatch(
        source="search",
        principal="AI_AGENT",
    )

    assert called == [True]

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
    assert not hasattr(router, "route_to_model")


def test_model_egress_route_rejects_unverified_data():
    router = MemoryDataRouter()
    pack = {
        "requestId": "route-test",
        "agentId": "AI_AGENT",
        "budget": {"hard_tokens": 240},
        "reduction": {"tokenizer_mode": "test", "net_tokens_saved": 0},
        "results": [{"id": "n1", "content": "unverified"}],
    }

    router.register("search", lambda: pack)
    with pytest.raises(DataRouteViolation, match="verification"):
        router.dispatch(source="search", principal="AI_AGENT")


@pytest.mark.parametrize("method_name", ["search", "read", "cognitive_read"])
def test_controller_context_entrypoints_use_canonical_route(method_name):
    source = inspect.getsource(getattr(MemoryController, method_name))
    assert "data_router.dispatch" in source
    assert "route_to_model" not in source


def test_financial_engine_uses_canonical_route():
    source = inspect.getsource(MultiLayeredFinancialSearchEngine.execute_search)
    assert "data_router.dispatch" in source
