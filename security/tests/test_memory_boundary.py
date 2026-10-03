from security.memory_boundary import MemoryWriteBoundary
from security.trust_gate import TrustState


def test_untrusted_memory_write_is_denied():
    boundary = MemoryWriteBoundary()
    result = boundary.propose("agent/session", {"note": "data"}, TrustState.UNTRUSTED)
    assert not result.allowed
    assert result.reason == "memory_trust_denied"
    assert boundary.ledger.records == []


def test_review_memory_write_requires_approval():
    boundary = MemoryWriteBoundary()
    denied = boundary.propose("agent/session", {"note": "data"}, TrustState.REVIEW)
    assert not denied.allowed

    allowed = boundary.propose(
        "agent/session", {"note": "data"}, TrustState.REVIEW, human_approved=True
    )
    assert allowed.allowed
    assert allowed.record_version == 1


def test_trusted_memory_write_is_committed_through_ledger():
    boundary = MemoryWriteBoundary()
    result = boundary.propose("agent/session", {"decision": "validated"}, TrustState.TRUSTED)
    assert result.allowed
    assert boundary.ledger.verify()
