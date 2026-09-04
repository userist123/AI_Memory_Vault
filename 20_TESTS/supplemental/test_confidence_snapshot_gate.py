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


def confidence():
    return {"score": 0.9, "promotable": True}


def snapshot(score=0.9, promotable=True, fingerprint="fp-1"):
    return {
        "case_id": "LC-1",
        "as_of": "2026-08-01T00:00:00",
        "known_as_of": "2026-08-02T00:00:00",
        "evidence_ids": ["ev-1"],
        "confidence": {"score": score, "promotable": promotable},
        "fingerprint": fingerprint,
    }


def test_snapshot_is_required():
    with pytest.raises(TypeError):
        LearningPromotionGate(RecordingController()).apply(
            principal=Principal.HUMAN,
            reviewer="reviewer",
            memory_id="learn-1",
            evidence_verification=verified(),
            evidence_bundle_hash="a" * 64,
            confidence=confidence(),
        )


def test_snapshot_score_must_match_confidence():
    with pytest.raises(ValueError):
        LearningPromotionGate(RecordingController()).apply(
            principal=Principal.HUMAN,
            reviewer="reviewer",
            memory_id="learn-1",
            evidence_verification=verified(),
            evidence_bundle_hash="a" * 64,
            confidence=confidence(),
            confidence_snapshot=snapshot(score=0.8),
        )


def test_non_promotable_snapshot_blocks():
    with pytest.raises(ValueError):
        LearningPromotionGate(RecordingController()).apply(
            principal=Principal.HUMAN,
            reviewer="reviewer",
            memory_id="learn-1",
            evidence_verification=verified(),
            evidence_bundle_hash="a" * 64,
            confidence=confidence(),
            confidence_snapshot=snapshot(promotable=False),
        )
