"""evaluation/tests/test_memory_usage_audit.py — Test suite for Memory Usage Audit Engine.

Tests:
1. Anti-fabrication tests: Unsubstantiated agent claims remain UNVERIFIED
2. Verified tool execution produces VERIFIED status
3. Multi-dimensional scorecard computation
4. Broken provenance detection
5. WOB ART conversation audit evaluation
"""
from pathlib import Path
import pytest

from evaluation.memory_usage_audit.conversation_auditor import ConversationAuditor
from evaluation.memory_usage_audit.scoring import AuditScorecard, StageEvaluation


def test_anti_fabrication_unverified_claims():
    """Prove that verbal claims without tool calls evaluate strictly as UNVERIFIED."""
    unverified_text = """
    I used the Vault to inspect the rules.
    I checked the skills for frontend development.
    I followed the architecture strictly.
    I made the GitHub changes to the repository.
    I verified it thoroughly with tests.
    """
    auditor = ConversationAuditor(unverified_text, case_id="test_unverified")
    scorecard = auditor.audit()

    # None of the execution/retrieval stages should be VERIFIED
    assert scorecard.stage_evaluations["B_MEMORY_RETRIEVAL"].level in ("UNVERIFIED", "MISSING")
    assert scorecard.stage_evaluations["E_SKILL_ACTIVATION"].level in ("UNVERIFIED", "MISSING")
    assert scorecard.stage_evaluations["G_DECISION_INFLUENCE"].level in ("UNVERIFIED", "MISSING")
    assert scorecard.stage_evaluations["I_VERIFICATION"].level in ("UNVERIFIED", "MISSING")

    # Scores must reflect zero verified utilization
    assert scorecard.memory_retrieval_score == 0.0
    assert scorecard.verification_score == 0.0
    assert scorecard.decision_influence_score == 0.0


def test_verified_tool_execution():
    """Prove that real tool calls result in VERIFIED status."""
    verified_transcript = """
    {"type": "USER_INPUT", "content": "Check storage rules"}
    {"type": "PLANNER_RESPONSE", "content": "Inspecting rules", "tool_calls": [{"name": "view_file", "args": {"AbsolutePath": "c:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/00_CORE/Memory_Protocol.md"}}]}
    {"type": "PLANNER_RESPONSE", "content": "Running tests", "tool_calls": [{"name": "run_command", "args": {"CommandLine": "python -m pytest evaluation/tests/ -q"}}]}
    """
    auditor = ConversationAuditor(verified_transcript, case_id="test_verified")
    scorecard = auditor.audit()

    assert scorecard.stage_evaluations["A_MEMORY_DISCOVERY"].level == "VERIFIED"
    assert scorecard.stage_evaluations["B_MEMORY_RETRIEVAL"].level == "VERIFIED"
    assert scorecard.stage_evaluations["I_VERIFICATION"].level == "VERIFIED"
    assert scorecard.memory_access_score > 0.0
    assert scorecard.verification_score == 100.0


def test_wob_art_audit_evaluation():
    """Audits the sample WOB ART transcript."""
    sample_path = Path(__file__).parents[1] / "memory_usage_audit" / "samples" / "wob_art_conversation.json"
    raw_content = sample_path.read_text(encoding="utf-8")

    auditor = ConversationAuditor(raw_content, case_id="wob_art")
    scorecard = auditor.audit()

    # Verify WOB ART audit findings
    assert scorecard.stage_evaluations["A_MEMORY_DISCOVERY"].level == "SUPPORTED"
    assert scorecard.stage_evaluations["B_MEMORY_RETRIEVAL"].level == "MISSING"
    assert scorecard.stage_evaluations["E_SKILL_ACTIVATION"].level in ("UNVERIFIED", "MISSING")
    assert scorecard.stage_evaluations["I_VERIFICATION"].level in ("UNVERIFIED", "MISSING")
    assert scorecard.overall_utilization_score < 15.0
