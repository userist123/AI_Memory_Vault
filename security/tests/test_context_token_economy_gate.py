"""Executable token-economy gate for the production context-pack path."""

import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION" / "packages"
if str(PACKAGES) not in sys.path:
    sys.path.append(str(PACKAGES))

from retrieval.context.budget import ContextBudget
from retrieval.context.pack_builder import ContextPackBuilder


def test_production_context_pack_reduces_actual_model_input_tokens():
    tokenizer = lambda text: len(text.split())
    budget_config = {
        "max_notes": 1,
        "max_full_documents": 1,
        "soft": 65536,
        "hard": 131072,
        "soft_tokens": 120,
        "hard_tokens": 240,
        "estimated_overhead_tokens": 0,
        "tokenizer": tokenizer,
    }
    raw_content = (
        ("irrelevant history " * 160)
        + "
MUST NOT bypass verification.
"
        + ("token economy " * 160)
    )
    item = {
        "id": "token-gate-1",
        "content": raw_content,
        "relevance": 1,
        "verification": {"status": "TRUSTED", "scanner_verdict": "ALLOW"},
        "security": {"trust_state": "TRUSTED"},
        "provenance": {"verified": True},
        "requirements": ["keep boundary"],
        "forbidden": ["bypass"],
        "acceptance": ["preserve evidence"],
    }

    budget = ContextBudget(budget_config)
    raw_pack = {
        "requestId": "token-economy-gate",
        "agentId": "default",
        "results": [item],
    }
    raw_tokens = budget.estimate_tokens(raw_pack)

    result = ContextPackBuilder().build(
        request_id="token-economy-gate",
        agent_id="default",
        budget=budget_config,
        results=[item],
        disclosure_level="full_document",
        query="token economy",
    )
    final_tokens = budget.estimate_tokens(result)

    assert final_tokens < raw_tokens, (
        f"production context pack did not reduce model input tokens: "
        f"raw={raw_tokens}, final={final_tokens}"
    )
    assert raw_tokens - final_tokens > 0
    assert final_tokens <= budget.hard_token_budget

    entry = result["results"][0]
    assert "MUST NOT bypass verification." in entry["content"]
    assert entry["verification"]["status"] == "TRUSTED"
