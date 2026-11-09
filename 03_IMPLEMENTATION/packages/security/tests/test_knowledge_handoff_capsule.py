"""Tests for the verified knowledge handoff capsule."""

from security.knowledge_handoff import VerifiedKnowledgeHandoff


def test_compile_creates_recoverable_knowledge_capsule():
    item = {
        "content": "Decision A.\nDecision A.\nNext action B.\n",
        "verification": {"status": "TRUSTED", "artifact_sha256": "abc"},
        "security": {"trust_state": "TRUSTED"},
        "provenance": {"source_ref": "agent-a", "verified": True},
        "decisions": ["Decision A."],
        "next_actions": ["Next action B."],
        "failed_attempts": ["Approach C failed."],
    }

    capsule = VerifiedKnowledgeHandoff().compile(
        item,
        max_chars=200,
        recoverable_ref="memory://agent-a/session-1",
    )

    assert capsule.knowledge["decisions"] == ["Decision A."]
    assert capsule.knowledge["next_actions"] == ["Next action B."]
    assert capsule.knowledge["failed_attempts"] == ["Approach C failed."]
    assert capsule.security["verification"] == item["verification"]
    assert capsule.recoverable_ref == "memory://agent-a/session-1"
    assert capsule.tokens_saved >= 0


def test_compile_refuses_untrusted_memory():
    item = {
        "content": "untrusted content",
        "verification": {"status": "UNTRUSTED"},
    }

    try:
        VerifiedKnowledgeHandoff().compile(item, max_chars=100)
    except PermissionError as exc:
        assert str(exc) == "verification_required"
    else:
        raise AssertionError("untrusted content must never enter a handoff capsule")
