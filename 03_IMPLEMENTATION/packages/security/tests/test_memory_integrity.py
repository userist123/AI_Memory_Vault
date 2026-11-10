from security.memory_integrity import MemoryLedger, MemoryRecord


def test_memory_versions_are_append_only_and_hash_chained():
    ledger = MemoryLedger()
    first = ledger.append("project/Casa3D", {"decision": "AI proposes; geometry validates"})
    second = ledger.append("project/Casa3D", {"decision": "external content is data"})

    assert first.version == 1
    assert second.version == 2
    assert second.previous_sha256 == first.sha256
    assert ledger.verify()


def test_memory_mutation_does_not_allow_rewriting_history():
    ledger = MemoryLedger()
    ledger.append("project/Casa3D", {"state": "v1"})

    try:
        ledger.replace(1, {"state": "tampered"})
    except PermissionError:
        pass
    else:
        raise AssertionError("replace must never be allowed")


def test_untrusted_proposal_is_not_committed():
    ledger = MemoryLedger()
    record = MemoryRecord(version=0, namespace="project/Casa3D", payload={"x": 1})
    result = ledger.commit_proposal(record, trust_state="UNTRUSTED")
    assert result is False
    assert len(ledger.records) == 0
