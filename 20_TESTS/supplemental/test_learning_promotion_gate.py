import pytest

from memory_controller.authorizer import Principal
from memory_controller.learning_promotion_gate import LearningPromotionGate


class RecordingController:
    class _Authorizer:
        @staticmethod
        def is_allowed(principal, operation):
            from memory_controller.authorizer import DefaultAuthorizer
            return DefaultAuthorizer().is_allowed(principal, operation)

    authorizer = _Authorizer()

    def __init__(self):
        self.calls = []

    def promote(self, principal, memory_id):
        self.calls.append((principal.value, memory_id))


def verified():
    return {
        "valid": True,
        "bundle_hash_matches": True,
        "stale_memory_ids": [],
        "missing_memory_ids": [],
        "bundle_hash": "a" * 64,
    }


def confidence(promotable=True, score=0.9):
    return {"promotable": promotable, "score": score}


def snapshot(promotable=True, score=0.9):
    return {
        "fingerprint": "snap-1",
        "confidence": {"promotable": promotable, "score": score},
        "decision_threshold": 0.7,
    }


def test_human_can_promote_verified_learning_candidate():
    controller = RecordingController()
    result = LearningPromotionGate(controller).apply(
        principal=Principal.HUMAN,
        reviewer="reviewer-1",
        memory_id="learn-1",
        evidence_verification=verified(),
        evidence_bundle_hash="a" * 64,
        confidence=confidence(),
        confidence_snapshot=snapshot(),
    )
    assert result.changed is True
    assert result.confidence_score == 0.9
    assert controller.calls == [("human", "learn-1")]


def test_ai_agent_cannot_promote():
    controller = RecordingController()
    with pytest.raises(PermissionError):
        LearningPromotionGate(controller).apply(
            principal=Principal.AI_AGENT,
            reviewer="agent",
            memory_id="learn-1",
            evidence_verification=verified(),
            evidence_bundle_hash="a" * 64,
            confidence=confidence(),
            confidence_snapshot=snapshot(),
        )
    assert controller.calls == []


def test_hash_mismatch_blocks_promotion():
    controller = RecordingController()
    evidence = verified()
    evidence["bundle_hash"] = "b" * 64
    with pytest.raises(ValueError):
        LearningPromotionGate(controller).apply(
            principal=Principal.HUMAN,
            reviewer="reviewer-1",
            memory_id="learn-1",
            evidence_verification=evidence,
            evidence_bundle_hash="a" * 64,
            confidence=confidence(),
            confidence_snapshot=snapshot(),
        )
    assert controller.calls == []


def test_non_promotable_confidence_blocks_promotion():
    controller = RecordingController()
    with pytest.raises(ValueError):
        LearningPromotionGate(controller).apply(
            principal=Principal.HUMAN,
            reviewer="reviewer-1",
            memory_id="learn-1",
            evidence_verification=verified(),
            evidence_bundle_hash="a" * 64,
            confidence=confidence(promotable=False, score=0.99),
            confidence_snapshot=snapshot(),
        )
    assert controller.calls == []
