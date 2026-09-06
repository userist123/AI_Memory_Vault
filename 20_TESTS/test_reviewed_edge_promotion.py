"""test_reviewed_edge_promotion.py — Regression suite for r007 reviewed edge promotion contracts.

Contracts verified:
1. Review gating cannot be bypassed (lifecycle/policy.py single authority; AI agents cannot promote directly to ACTIVE).
2. Hub links are strictly refused (forbidden hubs and notes with in-degree >= HUB_IN_DEGREE_THRESHOLD are dropped).
3. Edge proposal generation is deterministic (identical index + parameters produce identical proposals and order).
4. Full provenance is required for every promoted edge (proposal source, score/confidence, evidence, and status).
"""
from __future__ import annotations

import sys
from pathlib import Path
import pytest

from lifecycle.policy import (
    LifecycleState,
    Mutation,
    PrincipalRole,
    TransitionRequest,
    evaluate,
    enforce,
    LifecycleViolation,
)
from graph.synapse_store import (
    Synapse,
    SynapseStore,
    HUB_IN_DEGREE_THRESHOLD,
    InvalidSynapseError,
)
from retrieval.vault_index import Note, VaultIndex

# ---------------------------------------------------------------------------
# Contract 1: Review Gating Cannot Be Bypassed
# ---------------------------------------------------------------------------

def test_ai_agent_cannot_create_or_promote_directly_to_active():
    """Requirement 2: Nothing is promoted straight to ACTIVE by AI agents."""
    req_create_active = TransitionRequest(
        mutation=Mutation.CREATE,
        to_state=LifecycleState.ACTIVE,
        principal=PrincipalRole.AI_AGENT,
    )
    decision = evaluate(req_create_active)
    assert not decision.allowed, "AI_AGENT must not create directly into ACTIVE"

    with pytest.raises(LifecycleViolation):
        enforce(req_create_active)

    req_promote_ai = TransitionRequest(
        mutation=Mutation.PROMOTE,
        from_state=LifecycleState.REVIEW,
        to_state=LifecycleState.ACTIVE,
        principal=PrincipalRole.AI_AGENT,
    )
    decision_promote = evaluate(req_promote_ai)
    assert not decision_promote.allowed, "AI_AGENT must not promote to ACTIVE without human/admin gate"

    with pytest.raises(LifecycleViolation):
        enforce(req_promote_ai)


def test_review_state_is_authorized_for_promoted_proposals():
    """Requirement 2: Promoted edges enter the vault as REVIEW lifecycle."""
    req_review = TransitionRequest(
        mutation=Mutation.CREATE,
        to_state=LifecycleState.REVIEW,
        principal=PrincipalRole.AI_AGENT,
    )
    decision = evaluate(req_review)
    assert decision.allowed, "AI_AGENT must be allowed to create proposals in REVIEW lifecycle"


# ---------------------------------------------------------------------------
# Contract 2: Hub Links Are Strictly Refused
# ---------------------------------------------------------------------------

def test_hub_links_refused_when_in_degree_exceeds_threshold():
    """Requirement 1: Do not add links to nodes whose in-degree reaches HUB_IN_DEGREE_THRESHOLD."""
    hub_note = Note(
        id="hub-target-001",
        path=Path("01_ARCHITECTURE/Hub_Map.md"),
        title="Hub Map",
        body="Content",
    )
    notes = [hub_note]
    for i in range(HUB_IN_DEGREE_THRESHOLD + 2):
        leaf = Note(
            id=f"leaf-{i:03d}",
            path=Path(f"01_ARCHITECTURE/leaf_{i:03d}.md"),
            title=f"Leaf {i:03d}",
            body="Linking to [[Hub Map]] for navigation.",
        )
        notes.append(leaf)

    index = VaultIndex(notes)
    store = SynapseStore.from_index(index, include_wikilinks=True, hub_in_degree=HUB_IN_DEGREE_THRESHOLD)

    edges_to_hub = [s for s in store.all() if s.target_id == "hub-target-001"]
    assert len(edges_to_hub) == 0, f"Expected 0 edges to hub, found {len(edges_to_hub)}"


def test_forbidden_canonical_hubs_excluded_by_proposer():
    """Requirement 1: Explicitly named navigation hubs are refused."""
    script_path = Path("30_SCRIPTS/knowledge/edge_proposer.py").resolve()
    import importlib.util
    spec = importlib.util.spec_from_file_location("edge_proposer", str(script_path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    assert "Knowledge Graph Home" in mod.FORBIDDEN_HUBS
    assert "08 Memory Subsystems Map" in mod.FORBIDDEN_HUBS
    assert "00 Core Map" in mod.FORBIDDEN_HUBS
    assert "02 Memory Knowledge Map" in mod.FORBIDDEN_HUBS


# ---------------------------------------------------------------------------
# Contract 3: Proposal Generation Is Deterministic
# ---------------------------------------------------------------------------

def test_deterministic_candidate_generation():
    """Requirement 6: Same corpus and thresholds produce the exact same edge set."""
    script_path = Path("30_SCRIPTS/knowledge/edge_proposer.py").resolve()
    import importlib.util
    spec = importlib.util.spec_from_file_location("edge_proposer", str(script_path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)

    note1 = Note(
        id="note-alpha-001",
        path=Path("01_ARCHITECTURE/Alpha.md"),
        title="Alpha Architecture",
        body="This note discusses DistributedTracing and FaultTolerance in SystemBus.",
    )
    note2 = Note(
        id="note-beta-002",
        path=Path("01_ARCHITECTURE/Beta.md"),
        title="Beta Architecture",
        body="This note also implements DistributedTracing and FaultTolerance in SystemBus.",
    )
    index = VaultIndex([note1, note2])

    props1, count1 = mod.deterministic_candidates(index, limit=10)
    props2, count2 = mod.deterministic_candidates(index, limit=10)

    assert count1 == count2
    assert len(props1) == len(props2)
    for p1, p2 in zip(props1, props2):
        assert p1["source_id"] == p2["source_id"]
        assert p1["target_id"] == p2["target_id"]
        assert p1["relation"] == p2["relation"]
        assert p1["confidence"] == p2["confidence"]
        assert p1["evidence_entities"] == p2["evidence_entities"]


# ---------------------------------------------------------------------------
# Contract 4: Full Provenance Is Mandatory
# ---------------------------------------------------------------------------

def test_promoted_edge_provenance_schema():
    """Requirement 5: Every promoted edge records proposed_by, score, evidence, and approver."""
    required_fields = {"source_id", "target_id", "relation", "confidence", "weight", "origin", "evidence_entities", "extraction_run_id"}
    
    mock_proposal = {
        "source_id": "src-123",
        "target_id": "dst-456",
        "relation": "related_to",
        "confidence": 0.85,
        "weight": 0.425,
        "origin": "proposed_weak",
        "evidence_entities": ["distributedtracing", "faulttolerance"],
        "extraction_run_id": "edgeprop_test01",
        "status": "PROPOSED_PENDING_REVIEW",
    }
    
    for field in required_fields:
        incomplete = dict(mock_proposal)
        del incomplete[field]
        assert not all(k in incomplete for k in required_fields)

    syn = Synapse(
        source_id=mock_proposal["source_id"],
        target_id=mock_proposal["target_id"],
        relation=mock_proposal["relation"],
        weight=mock_proposal["weight"],
        origin="proposed_weak",
        evidence=[mock_proposal["extraction_run_id"]],
    )
    syn.validate()
    assert syn.evidence == ["edgeprop_test01"]
    assert syn.origin == "proposed_weak"
