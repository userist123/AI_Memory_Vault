import pytest

from memory_controller.authorized_verdict import AuthorizedVerdictEngine, Verdict
from memory_controller.authorizer import Principal


def test_human_can_issue_verdict():
    verdict = AuthorizedVerdictEngine().issue(
        principal=Principal.HUMAN,
        reviewer="reviewer-1",
        verdict=Verdict.ACCEPT_A,
        memory_ids=["m1", "m2"],
        evidence_bundle_hash="a" * 64,
        evidence_valid=True,
        reason="Source A was verified against the evidence bundle.",
        as_of="2026-01-01",
        known_as_of="2026-02-01",
    )
    assert verdict.verdict == Verdict.ACCEPT_A
    assert verdict.reviewer_principal == "human"
    assert verdict.verdict_id.startswith("VR-")


def test_admin_can_issue_verdict():
    verdict = AuthorizedVerdictEngine().issue(
        principal=Principal.ADMIN,
        reviewer="admin-1",
        verdict="DEFER",
        memory_ids=["m1", "m2"],
        evidence_bundle_hash="b" * 64,
        evidence_valid=True,
        reason="Additional evidence is required.",
    )
    assert verdict.verdict == Verdict.DEFER


def test_ai_agent_cannot_issue_verdict():
    with pytest.raises(PermissionError):
        AuthorizedVerdictEngine().issue(
            principal=Principal.AI_AGENT,
            reviewer="jarvis",
            verdict="ACCEPT_A",
            memory_ids=["m1", "m2"],
            evidence_bundle_hash="c" * 64,
            evidence_valid=True,
            reason="AI should not decide canonically.",
        )


def test_invalid_evidence_cannot_receive_verdict():
    with pytest.raises(ValueError, match="invalid evidence"):
        AuthorizedVerdictEngine().issue(
            principal=Principal.HUMAN,
            reviewer="reviewer-2",
            verdict="ACCEPT_B",
            memory_ids=["m1", "m2"],
            evidence_bundle_hash="d" * 64,
            evidence_valid=False,
            reason="Evidence is stale.",
        )
