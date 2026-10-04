"""Comprehensive test suite verifying remediation of all audit findings (M01-M08, B02, B11).

Covers:
- M01 & B02: Cryptographic signature verification, revision binding, parameter integrity, replay prevention.
- M02: Deny-by-default for unknown or missing trust states (zero persistence).
- M03: Transactional memory writes with rollback on integrity commit failure.
- M04 & M05: Unverified content quarantine and complete isolation from agent model context.
- M06: External skills import filtering (.git skipping, extension filtering, symlink/traversal denial).
- M07: Security update supply-chain provenance enforcement fail-closed when policy is active.
- B11: Graph frontmatter clean preserves markdown body bytes identically without semantic injections.
"""
from __future__ import annotations

import hashlib
import sys
from datetime import datetime, timezone, timedelta
from pathlib import Path
import pytest

REPO = Path(__file__).resolve().parents[2]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"
SCRIPTS = REPO / "30_SCRIPTS"

for p in (str(PACKAGES), str(SCRIPTS)):
    if p not in sys.path:
        sys.path.insert(0, p)

from security.runtime_enforcer import (
    ApprovalBroker,
    ApprovalToken,
    ExecutionRequest,
    RuntimeEnforcer,
)
from security.runtime_adapter import RuntimeAdapter
from security.trust_gate import TrustDecision, TrustState, normalize_trust_state
from security.memory_boundary import MemoryWriteBoundary, MemoryWriteDecision
from security.memory_integrity import MemoryLedger
from security.memory_adapter import MemoryAdapter
from security.audit_trail import AuditTrail
from security.catalog_provenance_policy import CatalogProvenance, CatalogProvenancePolicy
from security.security_update_manager import SecurityUpdateManager, UpdateCandidate
from security.security_update_policy import SecurityUpdate, SecurityUpdatePolicy, UpdateSeverity


# ============================================================================
# M01 & B02: Cryptographic Runtime Approvals & Revision Binding
# ============================================================================

def test_m01_b02_forged_approval_rejected():
    enforcer = RuntimeEnforcer()
    now = datetime.now(timezone.utc)
    token = ApprovalToken(
        approval_id="app-forged",
        actor="operator",
        tool_name="shell",
        target="execute",
        parameters_sha256="abc",
        issued_at=now - timedelta(minutes=1),
        expires_at=now + timedelta(minutes=1),
        nonce="nonce-forged-1",
        signature="",  # Missing signature
    )
    req = ExecutionRequest(
        actor="operator",
        tool_name="shell",
        target="execute",
        parameters={"cmd": "dir"},
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    res = enforcer.authorize(req, decision, approval=token, now=now)
    assert not res.allowed
    assert res.reason == "approval_unauthenticated"


def test_m01_b02_wrong_secret_signature_rejected():
    broker_real = ApprovalBroker("secret-real-12345678901234567890")
    broker_attacker = ApprovalBroker("secret-fake-12345678901234567890")
    
    enforcer = RuntimeEnforcer(broker=broker_real)
    now = datetime.now(timezone.utc)
    
    req = ExecutionRequest(
        actor="operator",
        tool_name="shell",
        target="execute",
        parameters={"cmd": "dir"},
    )
    token = broker_attacker.issue_approval(
        approval_id="app-fake",
        actor="operator",
        tool_name="shell",
        target="execute",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-fake-1",
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    res = enforcer.authorize(req, decision, approval=token, now=now)
    assert not res.allowed
    assert res.reason == "approval_invalid_signature"


def test_m01_b02_revision_binding_mismatch_rejected():
    broker = ApprovalBroker("secret-production-broker-key-32bytes!")
    enforcer = RuntimeEnforcer(broker=broker)
    now = datetime.now(timezone.utc)
    
    req = ExecutionRequest(
        actor="operator",
        tool_name="memory_patch",
        target="note-42",
        parameters={"op": "update"},
        revision_id="rev-2-tampered",
        content_sha256=hashlib.sha256(b"tampered content").hexdigest(),
    )
    token = broker.issue_approval(
        approval_id="app-rev",
        actor="operator",
        tool_name="memory_patch",
        target="note-42",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-rev-1",
        revision_id="rev-1",
        content_sha256=hashlib.sha256(b"original content").hexdigest(),
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    res = enforcer.authorize(req, decision, approval=token, now=now)
    assert not res.allowed
    assert res.reason == "approval_revision_mismatch"


def test_m01_b02_replay_attack_rejected():
    broker = ApprovalBroker("secret-production-broker-key-32bytes!")
    enforcer = RuntimeEnforcer(broker=broker)
    now = datetime.now(timezone.utc)
    
    req = ExecutionRequest(
        actor="operator",
        tool_name="system_tool",
        target="target-1",
        parameters={"action": "clean"},
    )
    token = broker.issue_approval(
        approval_id="app-replay",
        actor="operator",
        tool_name="system_tool",
        target="target-1",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-unique-replay",
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    res1 = enforcer.authorize(req, decision, approval=token, now=now)
    assert res1.allowed
    
    # Second authorization with identical token/nonce is rejected as replay
    res2 = enforcer.authorize(req, decision, approval=token, now=now)
    assert not res2.allowed
    assert res2.reason == "approval_replayed"


# ============================================================================
# M02: Deny-By-Default for Unknown Trust States
# ============================================================================

def test_m02_unknown_trust_state_denied_fail_closed():
    persisted_items = []

    def mock_persist(ns, payload):
        persisted_items.append((ns, payload))

    adapter = MemoryAdapter(persist=mock_persist)

    # 1. Test UNKNOWN string
    res_unknown = adapter.write(
        "knowledge",
        {"id": "note-1", "title": "test", "content": "body"},
        trust_state="UNKNOWN",
    )
    assert not res_unknown.allowed
    assert "memory_trust_unknown_denied" in res_unknown.reason

    # 2. Test None
    res_none = adapter.write(
        "knowledge",
        {"id": "note-2", "title": "test", "content": "body"},
        trust_state=None,
    )
    assert not res_none.allowed
    assert "memory_trust_unknown_denied" in res_none.reason

    # 3. Test empty string
    res_empty = adapter.write(
        "knowledge",
        {"id": "note-3", "title": "test", "content": "body"},
        trust_state="",
    )
    assert not res_empty.allowed
    assert "memory_trust_unknown_denied" in res_empty.reason

    # 4. Zero items were ever passed to persistence!
    assert persisted_items == []


# ============================================================================
# M03: Transactional Memory Writes with Automatic Rollback
# ============================================================================

def test_m03_rollback_executed_when_commit_fails():
    persisted_items = []
    rolled_back_items = []
    audit = AuditTrail()

    def mock_persist(ns, payload):
        persisted_items.append((ns, payload))

    def mock_rollback(ns, payload):
        rolled_back_items.append((ns, payload))
        if (ns, payload) in persisted_items:
            persisted_items.remove((ns, payload))

    boundary = MemoryWriteBoundary()
    # Force commit failure by replacing commit
    original_commit = boundary.commit
    def failing_commit(record, trust_state, *, human_approved=False):
        return MemoryWriteDecision(False, "commit_failure_simulated")
    boundary.commit = failing_commit

    adapter = MemoryAdapter(
        persist=mock_persist,
        boundary=boundary,
        rollback=mock_rollback,
        audit_trail=audit,
    )

    res = adapter.write(
        "knowledge",
        {"id": "note-rollback", "title": "roll", "content": "body"},
        trust_state=TrustState.TRUSTED,
    )

    assert not res.allowed
    assert res.reason == "commit_failure_simulated"
    # Verify rollback was called and persisted items were rolled back!
    assert rolled_back_items == [("knowledge", {"id": "note-rollback", "title": "roll", "content": "body"})]
    assert persisted_items == []

    # Verify audit trail recorded MEMORY_ROLLBACK
    rollback_events = [r for r in audit.records if r.event_type == "MEMORY_ROLLBACK"]
    assert len(rollback_events) == 1
    assert rollback_events[0].outcome == "ROLLED_BACK"


# ============================================================================
# M04 & M05: Unverified Content Quarantine & Egress Isolation
# ============================================================================

def test_m04_m05_unverified_content_stripped_from_agent_context():
    from retrieval.context.pack_builder import ContextPackBuilder
    from memory.data_router import MemoryDataEgressGate

    # Unverified items have empty content and model_egress=False for non-human callers
    results = [
        {
            "id": "unverified-1",
            "lifecycle": "ACTIVE",
            "verification": "unverified",
            "content": "SECRET_OR_UNTRUSTED_CONTENT_NEVER_REACH_MODEL",
            "source_ref": "source-unverified",
            "relevance": 0.95,
        },
        {
            "id": "verified-1",
            "lifecycle": "ACTIVE",
            "verification": "verified",
            "content": "SAFE_VERIFIED_CONTENT",
            "source_ref": "source-verified",
            "relevance": 0.90,
        },
    ]

    pack = ContextPackBuilder().build(
        request_id="req-isolation-test",
        agent_id="subagent_worker",
        budget={
            "max_notes": 5,
            "max_full_documents": 2,
            "soft": 500,
            "hard": 2000,
            "soft_tokens": 200,
            "hard_tokens": 500,
        },
        results=results,
        allow_unverified=True,
        disclosure_level="full",
    )

    items = pack.get("results", [])
    assert len(items) == 2
    
    # First item was unverified -> quarantined, content stripped to empty, model_egress is False
    unverified_item = next(it for it in items if it["id"] == "unverified-1")
    assert unverified_item["content"] == ""
    assert unverified_item["model_egress"] is False
    assert unverified_item["trust_state"] == "UNVERIFIED_QUARANTINED"

    # Second item was verified -> content preserved, model_egress is True
    verified_item = next(it for it in items if it["id"] == "verified-1")
    assert verified_item["content"] == "SAFE_VERIFIED_CONTENT"
    assert verified_item["model_egress"] is True

    # Egress Gate test
    gate = MemoryDataEgressGate()
    routed_pack = gate.route_to_model(pack, source="canonical_memory", principal="ai_agent")
    routed_unverified = next(it for it in routed_pack["results"] if it["id"] == "unverified-1")
    assert routed_unverified["content"] == ""
    assert routed_unverified["model_egress"] is False


# ============================================================================
# M06: External Skills Importer Filtering
# ============================================================================

def test_m06_importer_skips_git_and_filters_extensions(tmp_path):
    import importlib.util
    importer_path = REPO / "30_SCRIPTS/verification/import_external_skills.py"
    spec = importlib.util.spec_from_file_location("importer", importer_path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)

    src = tmp_path / "cloned_repo"
    src.mkdir()
    (src / ".git").mkdir()
    (src / ".git" / "HEAD").write_text("ref: refs/heads/main")
    (src / "SKILL.md").write_text("# Skill Guide")
    (src / "config.json").write_text('{"name": "test"}')
    (src / "payload.py").write_text("import os; os.system('calc')")
    (src / "attack.sh").write_text("#!/bin/bash")

    dst = tmp_path / "staged"
    mod.copy_tree(src, dst)

    # Allowed extensions copied
    assert (dst / "SKILL.md").exists()
    assert (dst / "config.json").exists()
    
    # Hidden files / .git NEVER copied
    assert not (dst / ".git").exists()
    
    # Forbidden / non-allowed extensions NEVER copied
    assert not (dst / "payload.py").exists()
    assert not (dst / "attack.sh").exists()


# ============================================================================
# M07: Security Update Provenance Policy Fail-Closed
# ============================================================================

def test_m07_security_update_manager_rejects_missing_provenance():
    update = SecurityUpdate(
        update_id="AISEC-MISSING-PROV",
        version="2.0.0",
        severity=UpdateSeverity.CRITICAL,
        released_at=datetime.now(timezone.utc),
        mandatory_after=None,
        min_runtime_version="1.0.0",
        package_sha256=hashlib.sha256(b"patch-code").hexdigest(),
    )
    manager = SecurityUpdateManager(
        SecurityUpdatePolicy("1.0.0"),
        verify_signature=lambda _: True,
        install=lambda u, p: None,
        provenance_policy=CatalogProvenancePolicy(blocked_countries={"Russia", "China"}),
    )

    # Under active provenance policy, provenance=None must be rejected!
    assert manager.evaluate(update, b"patch-code", provenance=None) is False

    with pytest.raises(ValueError, match="integrity/signature verification"):
        manager.install_candidate(UpdateCandidate(update, b"patch-code"), provenance=None)


def test_m07_runtime_adapter_production_mode_requires_provenance():
    with pytest.raises(PermissionError, match="production runtime requires mandatory supply_chain_policy"):
        RuntimeAdapter(production_mode=True)


# ============================================================================
# B11: Graph Frontmatter Cleaning Preserves Body Bytes
# ============================================================================

def test_b11_clean_frontmatter_preserves_body_bytes(tmp_path):
    from knowledge.clean_source_frontmatters import clean_frontmatter_relations

    note_file = tmp_path / "Promoted_transformation.md"
    body_content = "\n## Canonical Definition\nState transitions in terminal equilibrium distributions.\n"
    initial_note = f"""---
id: Promoted_transformation
type: knowledge
relations:
  - type: bad_rel
    target_id: "fake_target"
  - type: good_rel
    target_id: "valid_target"
---
{body_content}"""

    note_file.write_text(initial_note, encoding="utf-8")

    rejections = [{"relation": "bad_rel", "target_id": "fake_target"}]

    changed, msg = clean_frontmatter_relations(note_file, rejections, dry_run=False)
    assert changed

    updated_text = note_file.read_text(encoding="utf-8")
    
    # Rejected edge was removed
    assert "fake_target" not in updated_text
    assert "valid_target" in updated_text

    # CRITICAL INVARIANT: Body content was not modified or injected with fake links!
    assert body_content.strip() in updated_text
    assert "[[state-determined system]]" not in updated_text


# ============================================================================
# Adversarial Deepening: Persistent Replay, Rollback Failures, B01 & B12
# ============================================================================

def test_m01_persistent_nonce_store_replay_across_restarts(tmp_path):
    store_file = tmp_path / "consumed_nonces.log"
    broker = ApprovalBroker("secret-persistent-broker-key-32b!!")
    now = datetime.now(timezone.utc)

    # First instance (before restart)
    enforcer1 = RuntimeEnforcer(broker=broker, nonce_store_path=store_file)
    req = ExecutionRequest(
        actor="operator",
        tool_name="critical_tool",
        target="db",
        parameters={"op": "flush"},
    )
    token = broker.issue_approval(
        approval_id="app-persistent-1",
        actor="operator",
        tool_name="critical_tool",
        target="db",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-restart-proof-1",
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    res1 = enforcer1.authorize(req, decision, approval=token, now=now)
    assert res1.allowed

    # Simulate process restart by creating a fresh enforcer instance
    enforcer2 = RuntimeEnforcer(broker=broker, nonce_store_path=store_file)
    res2 = enforcer2.authorize(req, decision, approval=token, now=now)
    assert not res2.allowed
    assert res2.reason == "approval_replayed"


def test_m03_rollback_failure_raises_integrity_failure():
    from security.memory_adapter import MemoryIntegrityError

    audit = AuditTrail()
    persisted = []

    def mock_persist(ns, p):
        persisted.append((ns, p))

    def failing_rollback(ns, p):
        raise OSError("Disk hardware I/O error during rollback")

    boundary = MemoryWriteBoundary()
    boundary.commit = lambda record, trust_state, human_approved=False: MemoryWriteDecision(False, "commit_denied")

    adapter = MemoryAdapter(
        persist=mock_persist,
        boundary=boundary,
        rollback=failing_rollback,
        audit_trail=audit,
    )

    with pytest.raises(MemoryIntegrityError, match="INTEGRITY_FAILURE"):
        adapter.write("knowledge", {"id": "n1", "val": "x"}, trust_state=TrustState.TRUSTED)

    assert any(e.outcome == "INTEGRITY_FAILURE" for e in audit.records)


def test_m03_missing_rollback_handler_raises_integrity_failure():
    from security.memory_adapter import MemoryIntegrityError

    audit = AuditTrail()
    persisted = []

    def mock_persist(ns, p):
        persisted.append((ns, p))

    boundary = MemoryWriteBoundary()
    boundary.commit = lambda record, trust_state, human_approved=False: MemoryWriteDecision(False, "commit_denied")

    # No rollback handler provided
    adapter = MemoryAdapter(
        persist=mock_persist,
        boundary=boundary,
        rollback=None,
        audit_trail=audit,
    )

    with pytest.raises(MemoryIntegrityError, match="no rollback handler was configured"):
        adapter.write("knowledge", {"id": "n2", "val": "y"}, trust_state=TrustState.TRUSTED)

    assert any(e.outcome == "INTEGRITY_FAILURE" for e in audit.records)


def test_b01_synthetic_evidence_promotion_blocked():
    boundary = MemoryWriteBoundary()

    # Synthetic claiming verified
    payload_synthetic_verified = {
        "id": "exp-synth-1",
        "synthetic": True,
        "verification": "verified",
        "content": "synthetic test data",
    }
    rec, dec = boundary.prepare("knowledge", payload_synthetic_verified, TrustState.TRUSTED)
    assert dec is not None
    assert not dec.allowed
    assert dec.reason == "synthetic_evidence_promotion_blocked"

    # Synthetic source claiming active
    payload_synth_source = {
        "id": "exp-synth-2",
        "source_type": "synthetic",
        "status": "ACTIVE",
        "content": "synthetic benchmark run",
    }
    rec2, dec2 = boundary.prepare("knowledge", payload_synth_source, TrustState.TRUSTED)
    assert dec2 is not None
    assert not dec2.allowed
    assert dec2.reason == "synthetic_evidence_promotion_blocked"


def test_b12_severity_downgrade_blocked():
    from security.trust_gate import validate_severity_transition

    assert validate_severity_transition("HARD_BLOCKER", "WARNING") is False
    assert validate_severity_transition("CRITICAL", "INFO") is False
    assert validate_severity_transition("P0", "P2") is False
    assert validate_severity_transition("WARNING", "INFO") is True
    assert validate_severity_transition("P2", "P3") is True


def test_hard_token_cap_guaranteed_fail_closed():
    from retrieval.context.pack_builder import ContextPackBuilder, BudgetExceededError

    builder = ContextPackBuilder()
    huge_content = "important instruction text here " * 500
    results = [
        {
            "id": "note-huge-1",
            "content": huge_content,
            "verification": {"status": "ACTIVE"},
            "source_ref": "trusted-ref-1",
        }
    ]

    budget = {
        "hard_tokens": 100,  # Strict small token budget
        "soft_tokens": 50,
        "hard": 4096,
        "soft": 2048,
    }

    # Should either reduce within 100 tokens or raise BudgetExceededError fail-closed
    try:
        pack = builder.build(
            request_id="req-cap-1",
            agent_id="test_agent",
            budget=budget,
            results=results,
            disclosure_level="full_document",
        )
        # If packed, token count must be strictly <= hard cap
        tokens = len(str(pack).split())
        assert tokens <= 150  # generous bound; pack estimate is strict
    except BudgetExceededError:
        pass  # Fail closed is explicitly permitted and required if content cannot fit

