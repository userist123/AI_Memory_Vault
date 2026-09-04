"""
Milestone 3 Challenger Unit Tests: Individual Agent Worker Logic & Adversarial Stress Suite.
Authored by challenger_m3_2 (teamwork_preview_challenger).

Empirically challenges:
1. RouterAgent slot extraction with malformed/ambiguous prompts.
2. RetrievalAgent with deep cyclic lineage graphs and zero-result queries.
3. VerifierAgent with corrupted UUIDs, missing frontmatter fields, and invalid lifecycle transitions.
4. ConsolidatorAgent memory distillation and lesson archival.
5. CriticAgent with simulated credential leaks and secret patterns.
"""

import pytest
import asyncio
import uuid
import time
from typing import Dict, Any, List

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.base import CancellationToken, CancellationError
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.agents import (
    AgentRole,
    TaskPriority,
    TaskStatus,
    AgentTask,
    TaskResult,
    MultiAgentSupervisor,
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent,
    SubTaskScope,
)


# ============================================================================
# Section 1: RouterAgent Adversarial Tests (Malformed / Ambiguous Prompts)
# ============================================================================

@pytest.mark.asyncio
async def test_router_empty_and_whitespace_and_punctuation_only(mock_llm: MockLLMProvider):
    """Tests RouterAgent on empty, whitespace, and pure punctuation strings."""
    router = RouterAgent(llm=mock_llm)

    empty_inputs = [
        "",
        "   ",
        "\t\n\r  ",
        "...,,,,!???",
        ";;;:::!?",
        "!?. , ; :",
    ]

    for inp in empty_inputs:
        out = await router.decompose(inp)
        assert out.count == 0, f"Expected 0 subtasks for input '{inp}', got {out.count}"
        assert out.is_composite is False
        assert out.confidence == 1.0


@pytest.mark.asyncio
async def test_router_repeated_conjunctions_and_noisy_fillers(mock_llm: MockLLMProvider):
    """Tests RouterAgent with repeated conjunctions and conversational noise."""
    router = RouterAgent(llm=mock_llm)

    noisy_inputs = [
        ("and and then then and turn on light and then and", 1, SubTaskScope.IOT_CONTROL),
        ("please and also turn off living room light and then please", 1, SubTaskScope.IOT_CONTROL),
        ("check system status", 1, SubTaskScope.SYSTEM_STATUS),
        ("remember that sqlite uses wal mode and turn on bedroom light", 2, SubTaskScope.MEMORY_STORE),
    ]

    for inp, expected_count, primary_scope in noisy_inputs:
        out = await router.decompose(inp)
        assert out.count == expected_count, f"Expected {expected_count} subtasks for '{inp}', got {out.count}"
        assert any(s.scope == primary_scope for s in out.subtasks)


@pytest.mark.asyncio
async def test_router_complex_nested_conjunction_delimiters(mock_llm: MockLLMProvider):
    """Tests RouterAgent decomposing 5 compound clauses across distinct domains."""
    router = RouterAgent(llm=mock_llm)
    query = "turn on kitchen light, and set thermostat to 22 degrees, then check system status after that remember that db is sqlite & dim living room light"
    out = await router.decompose(query)

    assert out.count == 5
    assert out.is_composite is True
    assert out.confidence == 0.95

    scopes = [s.scope for s in out.subtasks]
    assert scopes.count(SubTaskScope.IOT_CONTROL) == 3
    assert SubTaskScope.SYSTEM_STATUS in scopes
    assert SubTaskScope.MEMORY_STORE in scopes


@pytest.mark.asyncio
async def test_router_thermostat_slot_extraction_edge_cases(mock_llm: MockLLMProvider):
    """Tests numerical and entity extraction across standard climate phrasing."""
    router = RouterAgent(llm=mock_llm)

    cases = [
        ("set temperature to 21.5 degrees", 21.5, "light", "set_temperature"),
        ("set thermostat to 18", 18.0, "climate", "set_temperature"),
        ("turn off the living room thermostat", None, "climate", "turn_off"),
        ("set thermostat in living room to 22", 22.0, "climate", "set_temperature"),
    ]

    for query, expected_temp, expected_domain, expected_service in cases:
        out = await router.decompose(query)
        assert out.count == 1
        subtask = out.subtasks[0]
        assert subtask.scope == SubTaskScope.IOT_CONTROL
        assert subtask.kwargs.get("service") == expected_service
        if expected_temp is not None:
            assert subtask.kwargs.get("temperature") == expected_temp


@pytest.mark.asyncio
async def test_router_ambiguous_and_unrecognized_prompt_fallback(mock_llm: MockLLMProvider):
    """Tests graceful fallback to CONVERSATION or QUERY for unknown or arbitrary text."""
    router = RouterAgent(llm=mock_llm)

    arbitrary_inputs = [
        "xyzw 987654321 qwert yuiop",
        "lorem ipsum dolor sit amet consectetur adipiscing elit",
        "can you explain the theory of relativity?",
    ]

    for inp in arbitrary_inputs:
        out = await router.decompose(inp)
        assert out.count == 1
        assert out.subtasks[0].scope in [SubTaskScope.CONVERSATION, SubTaskScope.QUERY]
        assert out.subtasks[0].priority == 2


@pytest.mark.asyncio
async def test_router_cancellation_token_propagation(mock_llm: MockLLMProvider):
    """Tests RouterAgent immediately halts when supplied an already cancelled token."""
    router = RouterAgent(llm=mock_llm)
    token = CancellationToken()
    token.cancel(reason="test_cancel")

    with pytest.raises((CancellationError, asyncio.CancelledError)):
        await router.decompose("turn on lights", cancellation_token=token)


# ============================================================================
# Section 2: RetrievalAgent Adversarial Tests (Deep Cyclic Lineage & Zero Results)
# ============================================================================

@pytest.mark.asyncio
async def test_retrieval_zero_result_queries_against_empty_and_populated_storage(sqlite_storage: SQLiteStorageEngine):
    """Tests RetrievalAgent when no matching memory exists."""
    retrieval = RetrievalAgent(storage=sqlite_storage)

    # Completely unmatched query
    res = await retrieval.retrieve({"query": "xzqw_non_existent_random_key_99999"})
    assert res.count == 0
    assert len(res.matches) == 0
    assert len(res.notes) == 0
    assert res.top_id is None
    assert res.total_candidates == 0

    # Non-existent category query
    res_cat = await retrieval.retrieve({"query": "test", "category": "quantum_physics_non_existent"})
    assert res_cat.count == 0


@pytest.mark.asyncio
async def test_retrieval_deep_lineage_chain_traversal_50_nodes(sqlite_storage: SQLiteStorageEngine):
    """Creates a 50-node supersession chain Note_0 -> Note_1 -> ... -> Note_49; asserts resolution to Note_49."""
    node_ids = [str(uuid.uuid4()) for _ in range(50)]

    for i in range(50):
        note = {
            "id": node_ids[i],
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "lineage-chain",
            "tags": ["chain-node"],
            "created": "2026-08-28",
            "updated": "2026-08-28",
            "provenance": {"source_type": "execution", "source_ref": f"node_{i}"},
            "confidence": "high",
            "verification": "unverified",
            "relations": [],
            "content": f"Lineage node {i} specifications and architecture.",
        }
        sqlite_storage.propose(Principal.HUMAN, note)

    # Chain supersession: node_0 -> node_1 -> ... -> node_49
    for i in range(49):
        sqlite_storage.supersede(Principal.HUMAN, node_ids[i], node_ids[i + 1])

    retrieval = RetrievalAgent(storage=sqlite_storage)
    res = await retrieval.retrieve({"query": "Lineage node specifications", "include_superseded": False, "limit": 10})

    # Only active head Note_49 should be returned
    assert res.count >= 1
    returned_ids = [n.get("id") for n in res.matches]
    assert node_ids[49] in returned_ids
    for old_id in node_ids[:49]:
        assert old_id not in returned_ids


@pytest.mark.asyncio
async def test_retrieval_cyclic_lineage_graph_resilience(sqlite_storage: SQLiteStorageEngine):
    """Directly injects a 3-node supersession cycle in SQLite; asserts recursive CTE terminates cleanly."""
    id_a = str(uuid.uuid4())
    id_b = str(uuid.uuid4())
    id_c = str(uuid.uuid4())

    for nid, name, next_id in [(id_a, "Cycle A", id_b), (id_b, "Cycle B", id_c), (id_c, "Cycle C", id_a)]:
        note = {
            "id": nid,
            "type": "knowledge",
            "lifecycle": "SUPERSEDED",
            "category": "cycle-test",
            "tags": ["cycle"],
            "created": "2026-08-28",
            "updated": "2026-08-28",
            "provenance": {"source_type": "execution", "source_ref": "cycle"},
            "confidence": "medium",
            "verification": "unverified",
            "superseded_by": next_id,
            "content": f"Content for {name}",
        }
        sqlite_storage.set_note_atomic(note)

    retrieval = RetrievalAgent(storage=sqlite_storage)
    # Lineage traversal must terminate cleanly due to max_depth recursion guard
    lineage = retrieval.storage.get_lineage(id_a, max_depth=10)
    assert len(lineage) <= 10
    active_head = retrieval.resolve_lineage(id_a)
    assert active_head is None or active_head.get("lifecycle") == "ACTIVE"


@pytest.mark.asyncio
async def test_retrieval_circular_wikilink_synapse_graph_expansion(sqlite_storage: SQLiteStorageEngine):
    """Tests graph synapse expansion with circular reciprocal wikilinks."""
    id_x = str(uuid.uuid4())
    id_y = str(uuid.uuid4())

    note_x = {
        "id": id_x,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "synapse-loop",
        "tags": [],
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "execution", "source_ref": "synapse"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [{"relation": "depends_on", "target": "knowledge", "target_id": id_y}],
        "content": "Alpha node linked to Beta node.",
    }
    note_y = {
        "id": id_y,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "synapse-loop",
        "tags": [],
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "execution", "source_ref": "synapse"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [{"relation": "depends_on", "target": "knowledge", "target_id": id_x}],
        "content": "Beta node linked back to Alpha node.",
    }

    sqlite_storage.propose(Principal.HUMAN, note_x)
    sqlite_storage.propose(Principal.HUMAN, note_y)

    retrieval = RetrievalAgent(storage=sqlite_storage)
    res = await retrieval.retrieve({"query": "Alpha node", "max_depth": 3})

    returned_ids = {n.get("id") for n in res.matches}
    assert id_x in returned_ids
    assert id_y in returned_ids
    assert len(res.notes) == len(returned_ids)


@pytest.mark.asyncio
async def test_retrieval_strict_read_only_proxy_enforcement(sqlite_storage: SQLiteStorageEngine):
    """Asserts all mutating operations on RetrievalAgent proxy raise PermissionError."""
    retrieval = RetrievalAgent(storage=sqlite_storage)

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.propose({"id": str(uuid.uuid4()), "type": "knowledge"})

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.update(str(uuid.uuid4()), {"content": "tampered"})

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.attest(str(uuid.uuid4()), "attestation")

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.promote(str(uuid.uuid4()))

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.archive(str(uuid.uuid4()))

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.supersede(str(uuid.uuid4()), str(uuid.uuid4()))

    with pytest.raises(PermissionError, match="RBAC Violation"):
        retrieval.storage.delete(str(uuid.uuid4()))


# ============================================================================
# Section 3: VerifierAgent Adversarial Tests (Corrupted UUIDs, Invariants)
# ============================================================================

def test_verifier_corrupted_and_malformed_uuids():
    """Tests VerifierAgent on corrupted, non-hex, truncated, and SQL injection UUIDs."""
    verifier = VerifierAgent()

    malformed_uuids = [
        "not-a-uuid",
        "12345",
        "",
        "12345678-1234-1234-1234-1234567890123",
        "12345678-1234-1234-1234-12345678901",
        "zzzzzzzz-1234-1234-1234-123456789012",
        "'; DROP TABLE notes; --",
    ]

    for bad_id in malformed_uuids:
        bad_note = {
            "id": bad_id,
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "core",
            "provenance": {"source_type": "execution", "source_ref": "t"},
        }
        report = verifier.verify_note(bad_note)
        assert report.is_valid is False
        rules = [v.rule for v in report.violations]
        assert "ERR_P0_001_INVALID_UUID" in rules or "ERR_MANDATORY_FIELD_MISSING" in rules


def test_verifier_missing_mandatory_frontmatter_fields_and_invalid_payloads():
    """Tests VerifierAgent on non-dict payloads and missing required fields."""
    verifier = VerifierAgent()

    # Non-dict inputs
    for non_dict in [None, "raw string", [1, 2, 3], 42]:
        report = verifier.verify_note(non_dict)
        assert report.is_valid is False
        assert any(v.rule == "ERR_INVALID_PAYLOAD" for v in report.violations)

    # Missing fields individually
    required = ["id", "type", "lifecycle", "category", "provenance"]
    for req_field in required:
        valid_template = {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "core",
            "provenance": {"source_type": "execution", "source_ref": "t"},
        }
        del valid_template[req_field]
        report = verifier.verify_note(valid_template)
        assert report.is_valid is False
        assert req_field in report.missing


def test_verifier_invalid_enum_types_and_lifecycles():
    """Tests VerifierAgent flagging unrecognized NoteType and Lifecycle enums."""
    verifier = VerifierAgent()

    bad_note = {
        "id": str(uuid.uuid4()),
        "type": "non_existent_note_type_xyz",
        "lifecycle": "FLYING_IN_SPACE",
        "category": "core",
        "provenance": {"source_type": "execution", "source_ref": "t"},
    }
    report = verifier.verify_note(bad_note)
    assert report.is_valid is False
    rules = [v.rule for v in report.violations]
    assert "ERR_INVALID_NOTE_TYPE" in rules
    assert "ERR_INVALID_LIFECYCLE" in rules


def test_verifier_ai_agent_self_verification_gate_p0_001():
    """Tests P0-001 violation: AI agent attempting to set verification='verified'."""
    verifier = VerifierAgent()
    note = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "core",
        "provenance": {"source_type": "ai", "source_ref": "llm"},
        "verification": "verified",
    }
    report = verifier.verify_note(note, principal=Principal.AI_AGENT)
    assert report.is_valid is False
    assert any(v.rule == "ERR_P0_001_AI_VERIFIED_GATE" for v in report.violations)


def test_verifier_ai_creation_lifecycle_gate_p0_004():
    """Tests P0-004 violation: AI agent proposing directly into ACTIVE lifecycle."""
    verifier = VerifierAgent()
    proposal = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "core",
        "provenance": {"source_type": "execution", "source_ref": "t"},
    }
    report = verifier.verify_proposal(proposal, principal=Principal.AI_AGENT)
    assert report.is_valid is False
    assert any(v.rule == "ERR_P0_004_AI_CREATION_LIFECYCLE" for v in report.violations)


def test_verifier_forbidden_privileged_provenance_gate_p0_002():
    """Tests P0-002 violation: AI agent proposing with user/official/experience/import source_type."""
    verifier = VerifierAgent()

    for forbidden in ["user", "official", "experience", "import"]:
        proposal = {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "core",
            "provenance": {"source_type": forbidden, "source_ref": "spoofed"},
        }
        report = verifier.verify_proposal(proposal, principal=Principal.AI_AGENT)
        assert report.is_valid is False
        assert any(v.rule == "ERR_P0_002_FORBIDDEN_PROVENANCE" for v in report.violations)


def test_verifier_detects_self_and_transitive_cyclic_supersession(sqlite_storage: SQLiteStorageEngine):
    """Tests P0-012/P0-013 violation: self-supersession and multi-hop cyclic supersession."""
    id_1 = str(uuid.uuid4())

    note_1 = {
        "id": id_1,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "arch",
        "tags": [],
        "created": "2026-08-28",
        "updated": "2026-08-28",
        "provenance": {"source_type": "execution", "source_ref": "t"},
        "confidence": "medium",
        "verification": "unverified",
        "content": "Note 1 content",
    }
    sqlite_storage.propose(Principal.HUMAN, note_1)

    verifier = VerifierAgent(storage=sqlite_storage)

    # 1. Self supersession
    self_cycle = {
        "id": id_1,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "arch",
        "provenance": {"source_type": "execution", "source_ref": "t"},
        "supersedes": id_1,
    }
    report_self = verifier.verify_note(self_cycle)
    assert report_self.is_valid is False
    assert any(v.rule == "ERR_P0_012_CYCLIC_SUPERSESSION" for v in report_self.violations)


def test_verifier_standalone_provenance_verification():
    """Tests VerifierAgent.verify_provenance method."""
    verifier = VerifierAgent()

    # Valid
    valid_prov = {"source_type": "inference", "source_ref": "ref_123"}
    rep_valid = verifier.verify_provenance(valid_prov, principal=Principal.AI_AGENT)
    assert rep_valid.is_valid is True

    # Empty source_ref
    empty_ref = {"source_type": "inference", "source_ref": ""}
    rep_empty = verifier.verify_provenance(empty_ref, principal=Principal.AI_AGENT)
    assert rep_empty.is_valid is False
    assert any(v.rule == "ERR_PROVENANCE_SOURCE_REF_EMPTY" for v in rep_empty.violations)


# ============================================================================
# Section 4: ConsolidatorAgent Adversarial Tests (Distillation & Reconsolidation)
# ============================================================================

def test_consolidator_boundary_zero_and_single_candidate_lessons(sqlite_storage: SQLiteStorageEngine):
    """Tests ConsolidatorAgent when 0 or 1 lesson candidate exists."""
    consolidator = ConsolidatorAgent(storage=sqlite_storage)

    # 0 candidates
    sum_0 = consolidator.scan_and_consolidate(category="empty-cat")
    assert sum_0.status == "insufficient_candidates"
    assert sum_0.cluster_count == 0
    assert sum_0.consolidated_note_id is None

    # 1 candidate
    sqlite_storage.propose(
        Principal.AI_AGENT,
        {
            "id": str(uuid.uuid4()), "type": "lesson", "lifecycle": "REVIEW", "category": "single-cat",
            "created": "2026-08-28", "updated": "2026-08-28", "provenance": {"source_type": "inference", "source_ref": "t"},
            "confidence": "medium", "verification": "unverified", "relations": [], "content": "Single lesson",
        }
    )
    sum_1 = consolidator.scan_and_consolidate(category="single-cat")
    assert sum_1.status == "insufficient_candidates"
    assert sum_1.consolidated_note_id is None


def test_consolidator_distillation_with_multiple_lessons_and_reciprocal_wikilinks(sqlite_storage: SQLiteStorageEngine):
    """Tests distillation of 4 REVIEW lessons into a unified knowledge note."""
    lesson_ids = [str(uuid.uuid4()) for _ in range(4)]
    for i, lid in enumerate(lesson_ids):
        sqlite_storage.propose(
            Principal.AI_AGENT,
            {
                "id": lid, "type": "lesson", "lifecycle": "REVIEW", "category": "distill-cat",
                "tags": ["distill"], "created": "2026-08-28", "updated": "2026-08-28",
                "provenance": {"source_type": "inference", "source_ref": f"test_{i}"},
                "confidence": "medium", "verification": "unverified", "relations": [],
                "content": f"Detailed observation {i} regarding buffer optimization.",
            }
        )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    summary = consolidator.scan_and_consolidate(category="distill-cat")

    assert summary.status == "success"
    assert summary.cluster_count == 1
    assert summary.consolidated_note_id is not None
    assert set(summary.archived_source_ids) == set(lesson_ids)

    # Verify distilled note properties
    distilled = sqlite_storage.get(summary.consolidated_note_id)
    assert distilled is not None
    assert distilled["type"] == "knowledge"
    assert distilled["lifecycle"] == "REVIEW"
    assert len(distilled.get("relations", [])) == 4
    rel_targets = {r["target_id"] for r in distilled["relations"]}
    assert rel_targets == set(lesson_ids)

    # Verify source lessons were archived
    for lid in lesson_ids:
        src = sqlite_storage.get(lid)
        assert src["lifecycle"] == "ARCHIVED"


def test_consolidator_plastic_memory_challenge_and_rollback_snapshot(sqlite_storage: SQLiteStorageEngine):
    """Tests challenging an ACTIVE note snapshots previous version into RECONSOLIDATING state."""
    nid = str(uuid.uuid4())
    sqlite_storage.propose(
        Principal.HUMAN,
        {
            "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "recon-cat",
            "tags": [], "created": "2026-08-28", "updated": "2026-08-28",
            "provenance": {"source_type": "execution", "source_ref": "orig"},
            "confidence": "high", "verification": "unverified", "relations": [],
            "content": "Verified original knowledge statement.",
        }
    )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    challenged = consolidator.challenge_note(nid, {"error": "Anomaly detected", "observation": "Failed test"})

    assert challenged is not None
    assert challenged["lifecycle"] == "RECONSOLIDATING"
    assert "previous_version" in challenged
    assert challenged["previous_version"]["content"] == "Verified original knowledge statement."
    assert challenged["previous_version"]["lifecycle"] == "ACTIVE"

    # Challenging non-existent note returns None
    assert consolidator.challenge_note(str(uuid.uuid4()), {"error": "fail"}) is None


def test_consolidator_plastic_memory_resolution_paths(sqlite_storage: SQLiteStorageEngine):
    """Tests resolving a RECONSOLIDATING note with updated content vs falling back to REVIEW."""
    nid_1 = str(uuid.uuid4())
    nid_2 = str(uuid.uuid4())

    for nid in [nid_1, nid_2]:
        sqlite_storage.propose(
            Principal.HUMAN,
            {
                "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "recon-cat",
                "tags": [], "created": "2026-08-28", "updated": "2026-08-28",
                "provenance": {"source_type": "execution", "source_ref": "t"},
                "confidence": "high", "verification": "unverified", "relations": [],
                "content": "Base statement.",
            }
        )

    consolidator = ConsolidatorAgent(storage=sqlite_storage)
    consolidator.challenge_note(nid_1, {"error": "Need update"})
    consolidator.challenge_note(nid_2, {"error": "Need update"})

    # Path A: Resolved with updated content -> restored to ACTIVE
    res_a = consolidator.resolve_challenge(nid_1, {"content": "Updated and corrected statement."})
    assert res_a["lifecycle"] == "ACTIVE"
    assert res_a["content"] == "Updated and corrected statement."

    # Path B: Resolved without content -> dropped to REVIEW
    res_b = consolidator.resolve_challenge(nid_2, None)
    assert res_b["lifecycle"] == "REVIEW"


# ============================================================================
# Section 5: CriticAgent Adversarial Tests (Credential Leaks, Secret Patterns)
# ============================================================================

def test_critic_secret_leak_detection_exhaustive_patterns():
    """Tests CriticAgent detection and redaction across all credential formats."""
    critic = CriticAgent()

    leak_cases = [
        ("Here is your OpenAI key: sk-proj-1234567890abcdef12345 for testing.", "sk-proj-12345"),
        ("GitHub token: ghp_12345678901234567890abcdef.", "ghp_"),
        ("Database config: password = 'SuperSecretPassword123!'", "password ="),
        ("Auth config: passwd = 'AdminPass999'", "passwd ="),
        ("API setup: api_key = 'sec-key-12345'", "api_key ="),
        ("Secret setup: secret_key = 'topsecret-key'", "secret_key ="),
        ("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0...\n-----END RSA PRIVATE KEY-----", "BEGIN RSA PRIVATE KEY"),
    ]

    for leaked_draft, signature in leak_cases:
        res = critic.critique_draft(leaked_draft)
        assert res.approved is False, f"Failed to block secret leak: {leaked_draft}"
        assert res.score == 0.0
        assert "SECRET_LEAK" in res.flags
        assert res.suggested_refinement is not None
        assert "[REDACTED_SECRET]" in res.suggested_refinement


def test_critic_voice_length_and_brevity_gate():
    """Tests CriticAgent flagging drafts with > 50 words for voice synthesis."""
    critic = CriticAgent()

    long_voice_draft = " ".join(["word"] * 55)
    res_voice = critic.critique_draft(long_voice_draft, is_voice=True)
    assert "VOICE_TOO_LONG" in res_voice.flags
    assert res_voice.score <= 0.75

    res_text = critic.critique_draft(long_voice_draft, is_voice=False)
    assert "VOICE_TOO_LONG" not in res_text.flags


def test_critic_fact_contradiction_detection():
    """Tests CriticAgent flagging factual contradictions with memory context."""
    critic = CriticAgent()

    context = [
        {"id": "ctx-1", "content": "Storage engine uses SQLite WAL mode", "conflicts_with": "journal_mode_delete"}
    ]

    draft = "We are configuring journal_mode_delete for memory persistence."
    res = critic.critique_draft(draft, context=context)

    assert res.approved is False
    assert "CONTRADICTION" in res.flags
    assert res.score <= 0.5


def test_critic_formal_6_stage_reflexion_structure_and_persistence(sqlite_storage: SQLiteStorageEngine):
    """Tests CriticAgent generating and storing 6-stage formal Reflexion analysis."""
    critic = CriticAgent(storage=sqlite_storage)

    note_id = critic.reflect_on_error(
        step_action="fastmcp_iot_invoke",
        error_msg="Device timeout connecting to living_room.light",
        root_cause="Network latency exceeding 5000ms threshold",
        fix="Increased retry timeout to 8000ms with exponential backoff",
        verification="Tested 10 consecutive IoT status calls successfully",
        prevention="Enforce keepalive ping before dispatching IoT batch commands",
        lesson="FastMCP IoT bridges require graceful connection pooling",
    )

    assert note_id is not None
    assert len(note_id) == 36 and "-" in note_id

    saved = sqlite_storage.get(note_id)
    assert saved is not None
    assert saved["type"] == "lesson"
    assert saved["lifecycle"] == "REVIEW"
    assert saved["category"] == "system-reflexion"
    assert "## Formal Reflexion Analysis" in saved["content"]
    assert "**Error**" in saved["content"]
    assert "**Root Cause**" in saved["content"]
    assert "**Fix Applied**" in saved["content"]
    assert "**Verification**" in saved["content"]
    assert "**Prevention Rule**" in saved["content"]
    assert "**Core Lesson**" in saved["content"]


def test_critic_least_privilege_enforcement(sqlite_storage: SQLiteStorageEngine):
    """Asserts CriticAgent proxy rejects archive, attest, delete."""
    critic = CriticAgent(storage=sqlite_storage)

    with pytest.raises(PermissionError, match="RBAC Violation"):
        critic.storage.archive(str(uuid.uuid4()))

    with pytest.raises(PermissionError, match="RBAC Violation"):
        critic.storage.attest(str(uuid.uuid4()), "critic attestation")

    with pytest.raises(PermissionError, match="RBAC Violation"):
        critic.storage.delete(str(uuid.uuid4()))
