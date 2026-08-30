from memory_controller.authorizer import DefaultAuthorizer, Principal
from memory_controller.authorized_verdict import AuthorizedVerdictEngine, Verdict
from memory_controller.controller import StorageEngine, MemoryController
from memory_controller.mutation_gate import MutationGate


class RecordingController(MemoryController):
    def __init__(self):
        super().__init__(StorageEngine())
        self.calls = []

    def supersede(self, principal, old_id, new_id, evidence=""):
        self.calls.append(("supersede", principal.value, old_id, new_id, evidence))

    def attest(self, principal, note_id, verification_reason, evidence_reference, verification_state="verified"):
        self.calls.append(("attest", principal.value, note_id, verification_reason, evidence_reference))

    def archive(self, principal, note_id, reason):
        self.calls.append(("archive", principal.value, note_id, reason))


def _verdict(principal=Principal.HUMAN, verdict=Verdict.ACCEPT_A):
    engine = AuthorizedVerdictEngine(DefaultAuthorizer())
    return engine.issue(
        principal=principal,
        reviewer="reviewer-1",
        verdict=verdict,
        memory_ids=("A", "B"),
        evidence_bundle_hash="" * 64 or "a" * 64,
        evidence_valid=True,
        reason="verified by review",
        as_of="2026-01-01",
        known_as_of="2026-02-01",
    )


def test_ai_agent_cannot_apply_mutation():
    controller = RecordingController()
    gate = MutationGate(controller)
    verdict = _verdict()
    verification = {"valid": True, "bundle_hash_matches": True, "stale_memory_ids": [], "missing_memory_ids": []}
    try:
        gate.apply(
            principal=Principal.AI_AGENT,
            verdict=verdict,
            evidence_verification=verification,
            action="supersede",
            reason="not allowed",
        )
    except PermissionError:
        pass
    else:
        raise AssertionError("AI_AGENT mutation must be rejected")
    assert controller.calls == []


def test_invalid_evidence_blocks_mutation():
    controller = RecordingController()
    gate = MutationGate(controller)
    verdict = _verdict()
    verification = {"valid": False, "bundle_hash_matches": False, "stale_memory_ids": ["A"], "missing_memory_ids": []}
    try:
        gate.apply(
            principal=Principal.HUMAN,
            verdict=verdict,
            evidence_verification=verification,
            action="supersede",
            reason="should fail",
        )
    except ValueError:
        pass
    else:
        raise AssertionError("invalid evidence must block mutation")
    assert controller.calls == []


def test_defer_is_non_mutating():
    controller = RecordingController()
    gate = MutationGate(controller)
    verdict = _verdict(verdict=Verdict.DEFER)
    verification = {"valid": True, "bundle_hash_matches": True, "stale_memory_ids": [], "missing_memory_ids": [], "bundle_hash": "a" * 64}
    result = gate.apply(
        principal=Principal.HUMAN,
        verdict=verdict,
        evidence_verification=verification,
        action="none",
        reason="insufficient evidence",
    )
    assert result.status == "deferred"
    assert controller.calls == []


def test_accept_a_can_supersede_b_with_verified_evidence():
    controller = RecordingController()
    gate = MutationGate(controller)
    verdict = _verdict(verdict=Verdict.ACCEPT_A)
    verification = {"valid": True, "bundle_hash_matches": True, "stale_memory_ids": [], "missing_memory_ids": [], "bundle_hash": "a" * 64}
    result = gate.apply(
        principal=Principal.HUMAN,
        verdict=verdict,
        evidence_verification=verification,
        action="supersede",
        reason="A accepted from verified evidence",
    )
    assert result.changed is True
    assert controller.calls[0][:4] == ("supersede", "human", "B", "A")


def test_accept_b_can_attest_b():
    controller = RecordingController()
    gate = MutationGate(controller)
    verdict = _verdict(verdict=Verdict.ACCEPT_B)
    verification = {"valid": True, "bundle_hash_matches": True, "stale_memory_ids": [], "missing_memory_ids": [], "bundle_hash": "a" * 64}
    result = gate.apply(
        principal=Principal.HUMAN,
        verdict=verdict,
        evidence_verification=verification,
        action="attest",
        reason="B accepted from verified evidence",
    )
    assert result.changed is True
    assert controller.calls[0][0:3] == ("attest", "human", "B")
