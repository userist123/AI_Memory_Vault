"""Canonical memory data-route gate tests."""

import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.append(str(PACKAGES))

import pytest

from memory.data_router import MemoryDataEgressGate, MemoryDataRouter, DataRouteViolation


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
    controller_source = (
        PACKAGES / "memory" / "controller.py"
    ).read_text(encoding="utf-8")
    marker = f"def {method_name}("
    start = controller_source.find(marker)
    assert start >= 0
    next_def = controller_source.find("\ndef ", start + len(marker))
    source = controller_source[start:] if next_def < 0 else controller_source[start:next_def]
    assert "data_router.dispatch" in source
    assert "route_to_model" not in source


def test_financial_engine_uses_canonical_route():
    financial_source = (
        PACKAGES / "retrieval" / "financial_search.py"
    ).read_text(encoding="utf-8")
    marker = "def execute_search("
    start = financial_source.find(marker)
    assert start >= 0
    next_def = financial_source.find("\ndef ", start + len(marker))
    source = financial_source[start:] if next_def < 0 else financial_source[start:next_def]
    assert "data_router.dispatch" in source


def test_egress_gate_does_not_count_observability_traces_as_model_input():
    router = MemoryDataRouter()
    pack = {
        "requestId": "route-observability",
        "agentId": "AI_AGENT",
        "budget": {"hard_tokens": 240},
        "reduction": {"tokenizer_mode": "test", "net_tokens_saved": 10},
        "results": [{
            "id": "n1",
            "content": "trusted memory",
            "verification": {"status": "TRUSTED"},
            "provenance": {"source_type": "user", "source_ref": "test"},
        }],
        "candidate_trace": {"events": ["trace"] * 1000},
        "retrieval_trace": {"events": ["trace"] * 1000},
    }

    router.register("search", lambda: pack)
    routed = router.dispatch(source="search", principal="AI_AGENT")

    assert routed["results"][0]["id"] == "n1"
    assert routed["data_route"]["final_model_input_tokens"] <= 240


@pytest.mark.parametrize("status", ["REVIEW", "UNTRUSTED", "BLOCKED", "DENIED", "UNKNOWN"])
def test_model_egress_route_rejects_non_trusted_verification_states(status):
    router = MemoryDataRouter()
    pack = {
        "requestId": "route-test-state",
        "agentId": "AI_AGENT",
        "budget": {"hard_tokens": 240},
        "reduction": {"tokenizer_mode": "test", "net_tokens_saved": 0},
        "results": [{
            "id": "n1",
            "content": "memory",
            "verification": {"status": status},
            "provenance": {"source_type": "user", "source_ref": "test"},
        }],
    }
    router.register("search", lambda: pack)
    with pytest.raises(DataRouteViolation, match="verification"):
        router.dispatch(source="search", principal="AI_AGENT")
