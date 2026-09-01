"""evaluation/tests/test_memory_trace_protocol.py — Test suite for Memory Trace Protocol.

Tests:
1. Trace schema validation (well-formed vs missing required fields)
2. Declared-only vs observed evidence reconciliation
3. Causal linkage between retrieved memory and decision influence
4. Skill lifecycle validation
5. Subagent dispatch verification
6. Retroactive WOB ART trace audit
7. Anti-fabrication verification
8. Trace completeness and first missing link detection
"""
from pathlib import Path
import pytest
import yaml

from evaluation.memory_trace.trace_validator import TraceValidator, ReconciliationReport


def load_examples() -> Dict[str, Any]:
    ex_path = Path(__file__).parents[1] / "memory_trace" / "trace_examples.yaml"
    with open(ex_path, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return {item["id"]: item["trace"] for item in data.get("examples", [])}


def test_trace_schema_validation():
    examples = load_examples()
    complete_trace = examples["complete_verified_trace"]

    is_valid, errors = TraceValidator.validate_schema(complete_trace)
    assert is_valid is True
    assert len(errors) == 0

    # Missing required field
    invalid_trace = dict(complete_trace)
    del invalid_trace["query"]
    is_valid, errors = TraceValidator.validate_schema(invalid_trace)
    assert is_valid is False
    assert any("query" in e for e in errors)


def test_complete_verified_trace():
    examples = load_examples()
    trace = examples["complete_verified_trace"]

    report = TraceValidator.reconcile(trace)
    assert report.memory_status == "VERIFIED"
    assert report.skill_status == "VERIFIED"
    assert report.decision_influence == "MEMORY_INFLUENCE_VERIFIED"
    assert report.verification_status == "VERIFIED"
    assert report.outcome_status == "VERIFIED"
    assert report.trust_level == "T3_OUTCOME_VERIFIED"
    assert report.completeness == "COMPLETE"
    assert report.first_missing_link is None


def test_wob_art_retroactive_trace():
    examples = load_examples()
    trace = examples["wob_art_retroactive_trace"]

    report = TraceValidator.reconcile(trace)
    # Verbal claims without evidence must evaluate to DECLARED_ONLY / MISSING
    assert report.memory_status == "DECLARED_ONLY"
    assert report.skill_status == "DECLARED_ONLY"
    assert report.decision_influence == "MEMORY_INFLUENCE_UNVERIFIED"
    assert report.verification_status == "DECLARED_ONLY"
    assert report.outcome_status == "DECLARED_ONLY"
    assert report.trust_level == "T0_DECLARED_ONLY"
    assert report.completeness == "BROKEN"
    assert report.first_missing_link == "RETRIEVE"
    assert len(report.declared_only_claims) >= 3


def test_broken_causal_link_detection():
    examples = load_examples()
    trace = examples["broken_causal_link_trace"]

    report = TraceValidator.reconcile(trace)
    assert report.memory_status == "VERIFIED"
    # Unlinked decision remains unverified
    assert report.decision_influence == "MEMORY_INFLUENCE_UNVERIFIED"
    assert report.completeness == "BROKEN"
    assert report.first_missing_link == "LOAD"


def test_anti_fabrication_assertions():
    trace = {
        "trace_id": "tr-fabrication-01",
        "task_id": "task-01",
        "session_id": "sess-01",
        "agent_id": "agent",
        "timestamp": "2026-09-02T00:00:00Z",
        "query": "Apply UI rules",
        "declared": {
            "retrieved_memories": ["AGENTS.md", "00_CORE/Memory_Protocol.md"],
            "activated_skills": ["ui-sensei", "tailwindcss"],
            "activated_subagents": ["UI Specialist"],
            "decisions_influenced": ["Followed all UI rules"],
            "verification_claims": ["100% verified with test pass"],
            "outcome_claims": ["success"]
        },
        "observed": {
            "retrieval_events": [],
            "memory_load_events": [],
            "skill_load_events": [],
            "subagent_events": [],
            "decision_events": [],
            "tool_events": [],
            "verification_events": [],
            "outcome_events": []
        }
    }

    report = TraceValidator.reconcile(trace)
    assert report.memory_status == "DECLARED_ONLY"
    assert report.skill_status == "DECLARED_ONLY"
    assert report.subagent_status == "DECLARED_ONLY"
    assert report.decision_influence == "MEMORY_INFLUENCE_UNVERIFIED"
    assert report.verification_status == "DECLARED_ONLY"
    assert report.outcome_status == "DECLARED_ONLY"
    assert report.trust_level == "T0_DECLARED_ONLY"
    assert report.completeness == "BROKEN"
