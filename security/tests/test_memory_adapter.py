from security.memory_adapter import MemoryAdapter
from security.trust_gate import TrustState


def test_memory_adapter_blocks_untrusted_before_backend_write():
    writes = []
    adapter = MemoryAdapter(lambda namespace, payload: writes.append((namespace, payload)))

    result = adapter.write("agent", {"fact": "untrusted"}, TrustState.UNTRUSTED)

    assert result.allowed is False
    assert result.reason == "memory_trust_denied"
    assert writes == []


def test_memory_adapter_commits_trusted_write_through_boundary():
    writes = []
    adapter = MemoryAdapter(lambda namespace, payload: writes.append((namespace, payload)))

    result = adapter.write("agent", {"fact": "verified"}, TrustState.TRUSTED)

    assert result.allowed is True
    assert writes == [("agent", {"fact": "verified"})]
    assert adapter.boundary.ledger.verify() is True


def test_memory_adapter_requires_approval_for_review():
    writes = []
    adapter = MemoryAdapter(lambda namespace, payload: writes.append((namespace, payload)))

    denied = adapter.write("agent", {"fact": "review"}, TrustState.REVIEW)
    allowed = adapter.write(
        "agent",
        {"fact": "review"},
        TrustState.REVIEW,
        human_approved=True,
    )

    assert denied.allowed is False
    assert denied.reason == "memory_approval_required"
    assert allowed.allowed is True
    assert len(writes) == 1
