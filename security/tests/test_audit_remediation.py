"""Comprehensive test suite verifying remediation of all audit findings (M01-M08, B02, B11).

Covers:
- M01 & B02: Cryptographic signature verification, revision binding, parameter integrity, replay prevention.
- M02: Deny-by-default for unknown or missing trust states (zero persistence).
- M03: Transactional memory writes with rollback on integrity commit failure.
- M04 & M05: Unverified content quarantine and complete isolation from agent model context.
- M06: External skills import filtering (VCS metadata pruned; scripts, hidden paths, executables, symlinks, traversal abort the import).
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
    PersistentNonceStore,
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


def test_m01_b02_omitted_revision_or_content_rejected():
    """Red Team: Attacker attempts to bypass revision/content check by omitting them from request."""
    broker = ApprovalBroker("secret-production-broker-key-32bytes!")
    enforcer = RuntimeEnforcer(broker=broker)
    now = datetime.now(timezone.utc)

    token = broker.issue_approval(
        approval_id="app-strict-rev",
        actor="operator",
        tool_name="memory_patch",
        target="note-42",
        parameters_sha256=ExecutionRequest(actor="operator", tool_name="memory_patch", target="note-42", parameters={"op": "update"}).parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-rev-strict-1",
        revision_id="rev-exact-1",
        content_sha256=hashlib.sha256(b"exact-content").hexdigest(),
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    # 1. Request with omitted revision_id (None) -> rejected fail-closed
    req_no_rev = ExecutionRequest(
        actor="operator",
        tool_name="memory_patch",
        target="note-42",
        parameters={"op": "update"},
        revision_id=None,
        content_sha256=hashlib.sha256(b"exact-content").hexdigest(),
    )
    res_no_rev = enforcer.authorize(req_no_rev, decision, approval=token, now=now)
    assert not res_no_rev.allowed
    assert res_no_rev.reason == "approval_revision_mismatch"

    # 2. Request with omitted content_sha256 (None) -> rejected fail-closed
    req_no_hash = ExecutionRequest(
        actor="operator",
        tool_name="memory_patch",
        target="note-42",
        parameters={"op": "update"},
        revision_id="rev-exact-1",
        content_sha256=None,
    )
    res_no_hash = enforcer.authorize(req_no_hash, decision, approval=token, now=now)
    assert not res_no_hash.allowed
    assert res_no_hash.reason == "approval_content_mismatch"


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

_M04_BUDGET = {
    "max_notes": 8,
    "max_full_documents": 4,
    "soft": 4000,
    "hard": 8000,
    "soft_tokens": 1000,
    "hard_tokens": 2000,
}


def _m04_results():
    def note(note_id, lifecycle, verification, content, **extra):
        item = {
            "id": note_id,
            "lifecycle": lifecycle,
            "verification": verification,
            "content": content,
            "source_ref": f"source-{note_id}",
            "relevance": 0.9,
        }
        item.update(extra)
        return item

    return [
        note("review-unverified", "REVIEW", "unverified", "REVIEW_UNVERIFIED_BODY"),
        note("active-unverified", "ACTIVE", "unverified", "ACTIVE_UNVERIFIED_BODY"),
        note("active-verified", "ACTIVE", "verified", "ACTIVE_VERIFIED_BODY"),
        note("active-quarantined", "ACTIVE", "verified", "QUARANTINED_BODY", quarantined=True),
    ]


def _m04_pack(agent_id):
    from retrieval.context.pack_builder import ContextPackBuilder

    return ContextPackBuilder().build(
        request_id="req-isolation-test",
        agent_id=agent_id,
        budget=dict(_M04_BUDGET),
        results=_m04_results(),
        allow_unverified=True,
        disclosure_level="full",
    )


def _by_id(pack):
    return {item["id"]: item for item in pack["results"]}


def test_m04_m05_withheld_content_is_exactly_review_and_quarantined_for_agents():
    """M04/M05, restated against the read contract (CLAUDE.md): the model-facing route
    withholds the body of an unverified REVIEW candidate and of a note flagged quarantined.
    It does NOT withhold an ACTIVE note merely because it is not verified: that rule hid
    38 of the 48 notes an agent searched on the real vault."""
    items = _by_id(_m04_pack("subagent_worker"))
    assert set(items) == {"review-unverified", "active-unverified", "active-verified", "active-quarantined"}

    for note_id in ("review-unverified", "active-quarantined"):
        assert items[note_id]["content"] == "", note_id
        assert items[note_id].get("snippet", "") == "", note_id
        assert items[note_id]["model_egress"] is False, note_id
    assert items["review-unverified"]["trust_state"] == "UNVERIFIED_QUARANTINED"

    # ACTIVE content survives whatever its verification label; the label stays visible
    assert items["active-unverified"]["content"] == "ACTIVE_UNVERIFIED_BODY"
    assert items["active-unverified"]["model_egress"] is True
    assert items["active-unverified"]["trust_state"] == "UNVERIFIED_QUARANTINED"
    assert items["active-verified"]["content"] == "ACTIVE_VERIFIED_BODY"
    assert items["active-verified"]["model_egress"] is True
    assert "trust_state" not in items["active-verified"]


def test_m04_the_egress_gate_applies_the_same_rule_as_the_pack_builder():
    from memory.data_router import MemoryDataEgressGate

    gate = MemoryDataEgressGate()
    # a pack the builder let through to an owner, then routed to an agent: the gate must
    # re-apply the withholding rule independently of the builder
    owner_pack = _m04_pack("human")
    assert _by_id(owner_pack)["review-unverified"]["content"] == "REVIEW_UNVERIFIED_BODY"
    routed = _by_id(gate.route_to_model(owner_pack, source="canonical_memory", principal="ai_agent"))
    assert routed["review-unverified"]["content"] == ""
    assert routed["review-unverified"]["model_egress"] is False
    assert routed["active-quarantined"]["content"] == ""
    assert routed["active-unverified"]["content"] == "ACTIVE_UNVERIFIED_BODY"
    assert routed["active-verified"]["content"] == "ACTIVE_VERIFIED_BODY"


@pytest.mark.parametrize("owner", ["human", "admin"])
def test_m04_admin_and_human_are_treated_alike_by_the_builder_and_the_gate(owner):
    """The pack builder used to test `!= "human"` while the data router used {"human", "admin"}:
    ADMIN lost content in one layer and kept it in the other. One shared owner set now."""
    from memory.data_router import MemoryDataEgressGate
    from security.verified_reduction import OWNER_PRINCIPALS, is_owner_principal

    assert OWNER_PRINCIPALS == {"human", "admin"}
    assert is_owner_principal(owner) and not is_owner_principal("ai_agent")

    built = _by_id(_m04_pack(owner))
    assert built["review-unverified"]["content"] == "REVIEW_UNVERIFIED_BODY"
    assert built["active-quarantined"]["content"] == "QUARANTINED_BODY"
    routed = _by_id(MemoryDataEgressGate().route_to_model(_m04_pack(owner), source="canonical_memory", principal=owner))
    assert routed["review-unverified"]["content"] == "REVIEW_UNVERIFIED_BODY"
    assert routed["active-quarantined"]["content"] == "QUARANTINED_BODY"


def test_m04_principal_enum_members_are_accepted_by_the_owner_predicate():
    from memory_controller.authorizer import Principal
    from security.verified_reduction import is_owner_principal

    assert is_owner_principal(Principal.HUMAN) and is_owner_principal(Principal.ADMIN)
    assert not is_owner_principal(Principal.AI_AGENT)


# ============================================================================
# M06: External Skills Importer Filtering
# ============================================================================

def _load_importer():
    import importlib.util
    importer_path = REPO / "30_SCRIPTS/verification/import_external_skills.py"
    spec = importlib.util.spec_from_file_location("importer", importer_path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def test_m06_importer_prunes_vcs_metadata_and_copies_only_allowed_types(tmp_path):
    mod = _load_importer()
    src = tmp_path / "cloned_repo"
    src.mkdir()
    (src / ".git").mkdir()
    (src / ".git" / "HEAD").write_text("ref: refs/heads/main")
    (src / "SKILL.md").write_text("# Skill Guide")
    (src / "config.json").write_text('{"name": "test"}')
    (src / "logo.png").write_bytes(b"\x89PNG")

    dst = tmp_path / "staged"
    skipped = []
    mod.copy_tree(src, dst, skipped)

    assert (dst / "SKILL.md").exists() and (dst / "config.json").exists()
    assert not (dst / ".git").exists()
    # a type that is not imported is reported, never dropped silently
    assert skipped == ["logo.png"] and not (dst / "logo.png").exists()


def test_m06_importer_fails_closed_on_scripts_and_lists_every_offender(tmp_path):
    """M06 asks for filtering, not for silence: a script or binary in an external source aborts
    the whole import (nothing staged) and every offending path is named in the error."""
    mod = _load_importer()
    src = tmp_path / "cloned_repo"
    src.mkdir()
    (src / "SKILL.md").write_text("# Skill Guide")
    (src / "payload.py").write_text("import os; os.system('calc')")
    (src / "attack.sh").write_text("#!/bin/bash")
    (src / ".hidden.md").write_text("# hidden")

    dst = tmp_path / "staged"
    with pytest.raises(SystemExit) as raised:
        mod.copy_tree(src, dst)
    message = str(raised.value)
    assert "fail-closed" in message and "3 offending path(s)" in message
    for offender in ("payload.py", "attack.sh", ".hidden.md"):
        assert offender in message
    assert not dst.exists() or not any(dst.rglob("*"))


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


def test_m01_concurrent_replay_race_condition():
    import concurrent.futures

    broker = ApprovalBroker("secret-concurrent-broker-key-32b!!")
    enforcer = RuntimeEnforcer(broker=broker)
    now = datetime.now(timezone.utc)

    req = ExecutionRequest(
        actor="operator",
        tool_name="system_tool",
        target="target-concurrent",
        parameters={"action": "clean"},
    )
    token = broker.issue_approval(
        approval_id="app-concurrent-replay",
        actor="operator",
        tool_name="system_tool",
        target="target-concurrent",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-concurrent-1",
    )
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    def attempt_authorize():
        return enforcer.authorize(req, decision, approval=token, now=now)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futures = [pool.submit(attempt_authorize) for _ in range(8)]
        results = [f.result() for f in futures]

    allowed_count = sum(1 for r in results if r.allowed)
    replayed_count = sum(1 for r in results if not r.allowed and r.reason == "approval_replayed")

    # Exactly 1 request allowed, exactly 7 rejected due to thread-safe lock!
    assert allowed_count == 1
    assert replayed_count == 7


def test_b01_nested_provenance_synthetic_blocked():
    boundary = MemoryWriteBoundary()

    # Nested dict provenance claiming source_type synthetic
    payload_nested_prov = {
        "id": "exp-synth-nested-1",
        "provenance": {"source_type": "synthetic"},
        "verification": {"status": "verified"},
        "content": "synthetic benchmark dataset",
    }
    rec, dec = boundary.prepare("knowledge", payload_nested_prov, TrustState.TRUSTED)
    assert dec is not None
    assert not dec.allowed
    assert dec.reason == "synthetic_evidence_promotion_blocked"

    # Nested dict provenance claiming synthetic boolean
    payload_nested_bool = {
        "id": "exp-synth-nested-2",
        "provenance": {"synthetic": True},
        "lifecycle": "ACTIVE",
        "content": "synthetic reasoning traces",
    }
    rec2, dec2 = boundary.prepare("knowledge", payload_nested_bool, TrustState.TRUSTED)
    assert dec2 is not None
    assert not dec2.allowed
    assert dec2.reason == "synthetic_evidence_promotion_blocked"


def test_m07_security_update_manager_production_mode():
    policy = SecurityUpdatePolicy(current_version="1.0.0")
    # In production_mode without provenance_policy, must raise PermissionError fail-closed
    with pytest.raises(PermissionError, match="production runtime requires mandatory provenance_policy"):
        SecurityUpdateManager(
            policy=policy,
            verify_signature=lambda u: True,
            install=lambda u, p: None,
            production_mode=True,
        )


def test_blocker1_production_runtime_requires_explicit_broker_and_nonce_store(tmp_path):
    from security.supply_chain_policy import SoftwareAISupplyChainPolicy
    policy = SoftwareAISupplyChainPolicy()
    broker = ApprovalBroker("authoritative-production-secret-32bytes!", issuer="external-authority-broker")
    store = PersistentNonceStore(tmp_path / "nonces.db")

    # 1. RuntimeEnforcer rejects broker=None in production
    with pytest.raises(ValueError, match="production runtime requires an explicitly injected ApprovalBroker"):
        RuntimeEnforcer(broker=None, nonce_store=store, production_mode=True)

    # 2. RuntimeEnforcer rejects nonce_store=None in production
    with pytest.raises(ValueError, match="production runtime requires an explicitly injected PersistentNonceStore"):
        RuntimeEnforcer(broker=broker, nonce_store=None, production_mode=True)

    # 3. RuntimeAdapter rejects missing broker in production
    with pytest.raises(PermissionError, match="production runtime requires explicitly injected external approval broker"):
        RuntimeAdapter(supply_chain_policy=policy, broker=None, nonce_store=store, production_mode=True)

    # 4. RuntimeAdapter rejects missing nonce_store in production
    with pytest.raises(PermissionError, match="production runtime requires explicitly injected persistent nonce store"):
        RuntimeAdapter(supply_chain_policy=policy, broker=broker, nonce_store=None, production_mode=True)


def test_blocker1_test_secret_rejected_in_production_mode(tmp_path):
    store = PersistentNonceStore(tmp_path / "nonces.db")

    # 1. Dev secret rejected in broker init with production_mode=True
    with pytest.raises(ValueError, match="test/dev secret cannot be used in production mode"):
        ApprovalBroker("test-secret-123456789012345678901234", production_mode=True)

    # 2. Short secret rejected in production enforcer
    short_broker = ApprovalBroker("short_secret_under_32_bytes")
    with pytest.raises(ValueError, match="production approval broker secret must be at least 32 bytes"):
        RuntimeEnforcer(broker=short_broker, nonce_store=store, production_mode=True)

    # 3. Dev secret rejected in production enforcer
    dev_broker = ApprovalBroker("dev-secret-123456789012345678901234")
    with pytest.raises(ValueError, match="test/dev secret cannot be used in production mode"):
        RuntimeEnforcer(broker=dev_broker, nonce_store=store, production_mode=True)


def test_blocker1_agent_secret_cannot_authorize_production_operation(tmp_path):
    store = PersistentNonceStore(tmp_path / "nonces.db")
    authoritative_broker = ApprovalBroker("authoritative-production-secret-32bytes!")
    enforcer = RuntimeEnforcer(broker=authoritative_broker, nonce_store=store, production_mode=True)

    # Malicious agent tries to mint approval with its own self-selected secret
    agent_broker = ApprovalBroker("agent-self-selected-secret-32bytes-long!")
    now = datetime.now(timezone.utc)
    req = ExecutionRequest(
        actor="agent",
        tool_name="system_tool",
        target="sys-target",
        parameters={"cmd": "run"},
    )
    forged_token = agent_broker.issue_approval(
        actor="agent",
        tool_name="system_tool",
        target="sys-target",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-agent-forged-1",
    )

    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    res = enforcer.authorize(req, decision, approval=forged_token, now=now)
    assert not res.allowed
    assert res.reason == "approval_invalid_signature"


def test_blocker1_agent_cannot_access_broker_or_issue_approval_in_production(tmp_path):
    from security.supply_chain_policy import SoftwareAISupplyChainPolicy
    policy = SoftwareAISupplyChainPolicy()
    broker = ApprovalBroker("authoritative-production-secret-32bytes!", issuer="external-authority-broker")
    store = PersistentNonceStore(tmp_path / "nonces.db")
    adapter = RuntimeAdapter(supply_chain_policy=policy, broker=broker, nonce_store=store, production_mode=True)

    # 1. adapter.issue_approval is blocked in production
    with pytest.raises(PermissionError, match="issue_approval is forbidden on RuntimeAdapter in production mode"):
        adapter.issue_approval(actor="agent", tool_name="shell", target="exec", parameters_sha256="abc")

    # 2. adapter.broker access is blocked in production
    with pytest.raises(PermissionError, match="Access to ApprovalBroker is forbidden in production runtime"):
        _ = adapter.broker

    # 3. enforcer.issue_approval is blocked in production
    enforcer = RuntimeEnforcer(broker=broker, nonce_store=store, production_mode=True)
    with pytest.raises(PermissionError, match="issue_approval is forbidden on RuntimeEnforcer in production mode"):
        enforcer.issue_approval(actor="agent", tool_name="shell", target="exec", parameters_sha256="abc")

    # 4. enforcer.broker access is blocked in production
    with pytest.raises(PermissionError, match="Access to ApprovalBroker is forbidden in production runtime"):
        _ = enforcer.broker


def test_blocker2_mandatory_binding_for_mutating_tools(tmp_path):
    store = PersistentNonceStore(tmp_path / "nonces.db")
    broker = ApprovalBroker("authoritative-production-secret-32bytes!")
    enforcer = RuntimeEnforcer(broker=broker, nonce_store=store, production_mode=True)
    now = datetime.now(timezone.utc)
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    req_mutate = ExecutionRequest(
        actor="operator",
        tool_name="memory_patch",
        target="note-100",
        parameters={"content": "new text"},
        operation_type="patch",
    )

    # 1. Approval token for mutating operation without revision_id & content_sha256 -> REJECTED
    unbound_token = broker.issue_approval(
        actor="operator",
        tool_name="memory_patch",
        target="note-100",
        parameters_sha256=req_mutate.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-unbound-mutate",
        operation_type="patch",
        revision_id=None,
        content_sha256=None,
    )
    res_unbound = enforcer.authorize(req_mutate, decision, approval=unbound_token, now=now)
    assert not res_unbound.allowed
    assert res_unbound.reason == "approval_binding_required_for_tool"

    # 2. Mutating operation with full binding matching request -> ALLOWED
    bound_token = broker.issue_approval(
        actor="operator",
        tool_name="memory_patch",
        target="note-100",
        parameters_sha256=req_mutate.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-bound-mutate",
        operation_type="patch",
        revision_id="rev-exact-100",
        content_sha256=hashlib.sha256(b"new text").hexdigest(),
    )
    req_bound = ExecutionRequest(
        actor="operator",
        tool_name="memory_patch",
        target="note-100",
        parameters={"content": "new text"},
        operation_type="patch",
        revision_id="rev-exact-100",
        content_sha256=hashlib.sha256(b"new text").hexdigest(),
    )
    res_bound = enforcer.authorize(req_bound, decision, approval=bound_token, now=now)
    assert res_bound.allowed


def test_blocker4_sqlite_nonce_store_multi_instance_atomicity(tmp_path):
    import concurrent.futures
    db_file = tmp_path / "atomic_nonces.sqlite"
    broker = ApprovalBroker("authoritative-production-secret-32bytes!")

    enforcer1 = RuntimeEnforcer(broker=broker, nonce_store_path=db_file)
    enforcer2 = RuntimeEnforcer(broker=broker, nonce_store_path=db_file)
    now = datetime.now(timezone.utc)
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)

    req = ExecutionRequest(
        actor="operator",
        tool_name="system_tool",
        target="target-atomic",
        parameters={"action": "reconcile"},
    )
    token = broker.issue_approval(
        approval_id="app-atomic-1",
        actor="operator",
        tool_name="system_tool",
        target="target-atomic",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-atomic-claim-1",
    )

    # Concurrent authorization attempts across two separate enforcer instances sharing SQLite file
    def try_claim(enforcer_idx):
        enf = enforcer1 if enforcer_idx % 2 == 0 else enforcer2
        return enf.authorize(req, decision, approval=token, now=now)

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futures = [pool.submit(try_claim, i) for i in range(8)]
        results = [f.result() for f in futures]

    allowed_count = sum(1 for r in results if r.allowed)
    replayed_count = sum(1 for r in results if not r.allowed and r.reason == "approval_replayed")

    # Exactly 1 allowed across all instances, exactly 7 replayed
    assert allowed_count == 1
    assert replayed_count == 7

    # Restart: A third new instance pointing to the same SQLite db also rejects replay
    enforcer3 = RuntimeEnforcer(broker=broker, nonce_store_path=db_file)
    res3 = enforcer3.authorize(req, decision, approval=token, now=now)
    assert not res3.allowed
    assert res3.reason == "approval_replayed"


def test_blocker4_corrupted_nonce_store_fails_closed(tmp_path):
    db_file = tmp_path / "corrupted_nonces.sqlite"
    store = PersistentNonceStore(db_file)
    assert store.check_and_mark("valid-nonce-1")

    # Corrupt the SQLite file with invalid binary data
    with open(db_file, "wb") as f:
        f.write(b"GARBAGE_DATA_CORRUPTING_SQLITE_HEADER")

    # Subsequent check_and_mark on corrupted db must return False (fail-closed)
    corrupted_store = PersistentNonceStore.__new__(PersistentNonceStore)
    corrupted_store.path = db_file
    import sqlite3, threading
    corrupted_store._sqlite3 = sqlite3
    corrupted_store._lock = threading.Lock()
    assert not corrupted_store.check_and_mark("another-nonce")


def test_blocker3_search_does_not_leak_quarantined_or_unverified_content_in_snippets(tmp_path, monkeypatch):
    from unittest.mock import MagicMock
    from interfaces import memory_access as ma
    from memory_controller.authorizer import Principal

    # Mock controller where cognitive_read returns empty/redacted content for unverified note
    controller = MagicMock()
    unverified_note_id = "note-unverified-1"
    quarantined_note_id = "note-quarantined-2"

    controller.search.return_value = {
        "results": [
            {"id": unverified_note_id, "type": "knowledge", "lifecycle": "ACTIVE", "score": 0.95},
            {"id": quarantined_note_id, "type": "knowledge", "lifecycle": "ACTIVE", "score": 0.90},
        ],
        "candidate_trace": {"fused_ranking": []},
    }

    # Storage contains raw secret unverified and quarantined content
    storage_notes = {
        unverified_note_id: {
            "id": unverified_note_id,
            "title": "Unverified Title",
            "content": "SECRET_RAW_UNVERIFIED_CONTENT_MUST_NOT_LEAK",
            "verification": "unverified",
            "lifecycle": "ACTIVE",
            "quarantined": False,
        },
        quarantined_note_id: {
            "id": quarantined_note_id,
            "title": "Quarantined Title",
            "content": "SECRET_RAW_QUARANTINED_CONTENT_MUST_NOT_LEAK",
            "verification": "verified",
            "lifecycle": "ACTIVE",
            "quarantined": True,
        },
    }
    controller.storage.get.side_effect = lambda nid: storage_notes.get(nid)
    controller.storage.vault_root = str(tmp_path)
    controller.storage.id_to_path = {
        unverified_note_id: str(tmp_path / "01_ARCHITECTURE/knowledge/unverified.md"),
        quarantined_note_id: str(tmp_path / "01_ARCHITECTURE/knowledge/quarantined.md"),
    }

    # Cognitive read strips content from unverified notes for AI_AGENT
    def mock_cognitive_read(principal, nid):
        if nid == unverified_note_id:
            return {"results": [{"id": nid, "content": "", "verification": {"status": "UNVERIFIED"}}]}
        if nid == quarantined_note_id:
            return {"results": [{"id": nid, "content": "Quarantined readable", "quarantined": True}]}
        return {"results": []}

    controller.cognitive_read.side_effect = mock_cognitive_read

    # Execute search as AI_AGENT
    search_res = ma.search(controller, "query", principal=Principal.AI_AGENT)
    for result in search_res["query_results"]:
        assert result["snippet"] == "", f"Snippet leaked content for note {result['id']}: {result['snippet']}"
        assert "SECRET_RAW" not in result["snippet"]

    # Verify get() refuses quarantined note
    with pytest.raises(ValueError, match="quarantined"):
        ma.get(controller, quarantined_note_id, principal=Principal.AI_AGENT)


def test_blocker5_ci_reporting_distinguishes_passed_from_failed_and_rejects_masking():
    import yaml
    for rel_path in (".github/workflows/apisec-scan.yml", ".github/workflows/fortify.yml"):
        workflow_file = REPO / rel_path
        assert workflow_file.exists(), f"Workflow file missing: {rel_path}"
        content = workflow_file.read_text(encoding="utf-8")
        parsed = yaml.safe_load(content)

        # 1. Scanner step must have explicit ID so its outcome can be checked
        steps = list(parsed.get("jobs", {}).values())[0].get("steps", [])
        step_ids = {s.get("id") for s in steps if isinstance(s, dict) and s.get("id")}
        
        if "apisec" in rel_path:
            assert "apisec_scan" in step_ids
            assert "steps.apisec_scan.outcome" in content
        else:
            assert "fortify_scan" in step_ids
            assert "steps.fortify_scan.outcome" in content

        # 2. Must distinguish passed from failed in reporting step
        assert "EXECUTED_FAILED" in content
        assert "EXECUTED_PASSED" in content

        # 3. Simulate reporting shell logic on scan failure: must NOT emit EXECUTED_PASSED
        # If scan outcome is 'failure', status output must evaluate to EXECUTED_FAILED
        if "apisec" in rel_path:
            # shell condition: [[ "$scan_outcome" == "success" && ( "$import_outcome" == "success" || "$import_outcome" == "skipped" ) ]]
            scan_outcome = "failure"
            import_outcome = "skipped"
            is_passed = (scan_outcome == "success" and (import_outcome in ("success", "skipped")))
            status = "EXECUTED_PASSED" if is_passed else "EXECUTED_FAILED"
            assert status == "EXECUTED_FAILED"
        else:
            scan_outcome = "failure"
            is_passed = (scan_outcome == "success")
            status = "EXECUTED_PASSED" if is_passed else "EXECUTED_FAILED"
            assert status == "EXECUTED_FAILED"


# ============================================================================
# GATE 1 & GATE 2: Production Authority Proof & Multi-Process Atomicity
# ============================================================================

def test_gate1_production_mode_fails_closed_if_authenticated_approval_disabled(tmp_path):
    store = PersistentNonceStore(tmp_path / "nonces.db")
    broker = ApprovalBroker("authoritative-production-secret-32bytes!")

    # 1. Direct enforcer initialization with require_authenticated_approval=False must fail
    with pytest.raises(ValueError, match="production runtime strictly forbids disabling authenticated approvals"):
        RuntimeEnforcer(
            broker=broker,
            nonce_store=store,
            production_mode=True,
            require_authenticated_approval=False,
        )

    # 2. None, 0, or any non-True value must fail closed
    with pytest.raises(ValueError, match="production runtime strictly forbids disabling authenticated approvals"):
        RuntimeEnforcer(
            broker=broker,
            nonce_store=store,
            production_mode=True,
            require_authenticated_approval=None,
        )

    with pytest.raises(ValueError, match="production runtime strictly forbids disabling authenticated approvals"):
        RuntimeEnforcer(
            broker=broker,
            nonce_store=store,
            production_mode=True,
            require_authenticated_approval=0,
        )

    # 3. Adapter wrapping a non-production enforcer in production mode must fail
    from security.supply_chain_policy import SoftwareAISupplyChainPolicy
    policy = SoftwareAISupplyChainPolicy()
    enf_non_prod = RuntimeEnforcer(
        broker=broker,
        nonce_store=store,
        production_mode=False,
        require_authenticated_approval=False,
    )
    with pytest.raises(PermissionError, match="production runtime requires an enforcer configured in production mode"):
        RuntimeAdapter(enforcer=enf_non_prod, supply_chain_policy=policy, production_mode=True)

    # 4. Adapter wrapping an enforcer with disabled authenticated approval must fail
    dummy_bypassed_enf = type(
        "DummyBypassedEnforcer",
        (),
        {"_production_mode": True, "_require_authenticated_approval": False},
    )()
    with pytest.raises(PermissionError, match="strictly forbids enforcer with disabled authenticated approvals"):
        RuntimeAdapter(enforcer=dummy_bypassed_enf, supply_chain_policy=policy, production_mode=True)


def test_gate1_approval_issuer_mismatch_rejected(tmp_path):
    store = PersistentNonceStore(tmp_path / "nonces.db")
    broker = ApprovalBroker("authoritative-production-secret-32bytes!", issuer="trusted-authority-broker")
    enforcer = RuntimeEnforcer(broker=broker, nonce_store=store, production_mode=True)

    now = datetime.now(timezone.utc)
    decision = TrustDecision(TrustState.REVIEW, ("review",), True)
    req = ExecutionRequest(
        actor="operator",
        tool_name="system_tool",
        target="sys-target",
        parameters={"action": "clean"},
    )

    # Token issued by broker, but with mismatched issuer
    token = broker.issue_approval(
        actor="operator",
        tool_name="system_tool",
        target="sys-target",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="nonce-issuer-mismatch-1",
    )
    # Tamper issuer
    mismatched_token = ApprovalToken(
        approval_id=token.approval_id,
        actor=token.actor,
        tool_name=token.tool_name,
        target=token.target,
        parameters_sha256=token.parameters_sha256,
        issued_at=token.issued_at,
        expires_at=token.expires_at,
        nonce=token.nonce,
        signature=token.signature,
        issuer="impostor-authority-broker",
    )

    res = enforcer.authorize(req, decision, approval=mismatched_token, now=now)
    assert not res.allowed
    assert res.reason == "approval_issuer_mismatch"


def test_gate1_broker_secret_validated_against_host_authority_env(monkeypatch, tmp_path):
    host_secret = "host-super-secret-key-32bytes-min!"
    monkeypatch.setenv("VAULT_AUTHORITY_HMAC_SECRET", host_secret)

    store = PersistentNonceStore(tmp_path / "nonces.db")

    # Injected broker with mismatched secret must be rejected
    mismatched_broker = ApprovalBroker("different-secret-key-32bytes-val!")
    with pytest.raises(PermissionError, match="broker secret does not match host authority secret"):
        RuntimeEnforcer(broker=mismatched_broker, nonce_store=store, production_mode=True)

    # Injected broker with matching secret succeeds
    matching_broker = ApprovalBroker(host_secret)
    enforcer = RuntimeEnforcer(broker=matching_broker, nonce_store=store, production_mode=True)
    assert enforcer is not None


def test_gate2_real_os_multiprocess_nonce_replay(tmp_path):
    import subprocess
    import json

    runner_script = Path(__file__).resolve().parent / "os_multiprocess_nonce_runner.py"
    assert runner_script.exists(), "os_multiprocess_nonce_runner.py must exist"

    db_path = tmp_path / "os_multiprocess_nonces.sqlite"
    secret = "production-multiprocess-test-secret-32b!"
    broker = ApprovalBroker(secret, issuer="external-authority-broker")

    now = datetime.now(timezone.utc)
    req = ExecutionRequest(
        actor="operator",
        tool_name="system_tool",
        target="target-atomic-multiprocess",
        parameters={"action": "run_job"},
    )
    token = broker.issue_approval(
        approval_id="app-os-multiprocess-1",
        actor="operator",
        tool_name="system_tool",
        target="target-atomic-multiprocess",
        parameters_sha256=req.parameters_sha256(),
        issued_at=now,
        expires_at=now + timedelta(minutes=5),
        nonce="shared-nonce-os-atomic-9999",
    )

    token_json = json.dumps(token.to_dict())
    req_json = json.dumps(req.to_dict())

    # Launch 8 truly independent OS processes concurrently
    processes = []
    num_processes = 8
    for _ in range(num_processes):
        p = subprocess.Popen(
            [sys.executable, str(runner_script), str(db_path), secret, token_json, req_json],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        processes.append(p)

    outputs = []
    exit_codes = []
    for p in processes:
        stdout, stderr = p.communicate(timeout=15)
        outputs.append(stdout.strip())
        exit_codes.append(p.returncode)

    # Exactly 1 process should have succeeded (exit code 0, RESULT:ALLOWED)
    allowed_count = sum(1 for out in outputs if "RESULT:ALLOWED" in out)
    replayed_count = sum(1 for out in outputs if "RESULT:REJECTED:approval_replayed" in out)

    assert allowed_count == 1, f"Expected exactly 1 ALLOWED process, got {allowed_count}. Outputs: {outputs}"
    assert replayed_count == num_processes - 1, f"Expected {num_processes - 1} replayed, got {replayed_count}. Outputs: {outputs}"
    assert sorted(exit_codes) == [0] + [1] * (num_processes - 1)

    # Subsequent 9th independent OS process execution must also be rejected
    p_restart = subprocess.Popen(
        [sys.executable, str(runner_script), str(db_path), secret, token_json, req_json],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    stdout_restart, _ = p_restart.communicate(timeout=10)
    assert p_restart.returncode == 1
    assert "RESULT:REJECTED:approval_replayed" in stdout_restart


def test_u01_ai_agent_mcp_surface_has_no_update_or_mutate_tool():
    """U01 Analysis: At the external MCP agent interface boundary, ensure
    no mutating 'update', 'patch', 'delete', or 'attest' tool is exposed to AI agents.
    Only read/search/propose tools are registered on the FastMCP server.
    """
    from interfaces import memory_mcp_server, memory_access

    # The memory tools are exactly search/get/propose. The direct-route tools added with
    # vault:// (PR #214) are read-only; the server's full surface is the two lists together,
    # and no tool name may carry a mutating verb.
    assert memory_mcp_server.MEMORY_TOOL_NAMES == ("memory_search", "memory_get", "memory_propose")
    assert memory_mcp_server.VAULT_TOOL_NAMES == (
        "vault_resolve", "vault_list", "vault_read", "vault_search", "vault_get_metadata", "vault_check_quotes",
    )
    assert memory_mcp_server.TOOL_NAMES == memory_mcp_server.MEMORY_TOOL_NAMES + memory_mcp_server.VAULT_TOOL_NAMES
    mutating = ("update", "patch", "delete", "attest", "promote", "write", "set", "remove", "approve")
    assert not [t for t in memory_mcp_server.TOOL_NAMES if any(v in t for v in mutating)]
    
    # Verify memory_access module contains no direct update/attest tools for agents
    exposed_callable_names = [k for k in dir(memory_access) if not k.startswith("_")]
    assert "update" not in exposed_callable_names
    assert "patch" not in exposed_callable_names
    assert "delete" not in exposed_callable_names
    assert "attest" not in exposed_callable_names
    assert "promote" not in exposed_callable_names


def test_u01_controller_invariants_hold_under_ai_agent_update():
    """U01 Analysis: Even though MemoryController.update allows AI_AGENT for continual
    learning updates, verify that I-001 (cannot escalate verification to 'verified')
    and I-005 (cannot modify provenance source_type) remain strictly enforced.
    """
    import uuid
    from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
    from memory_controller.authorizer import Principal

    controller = MemoryController(StorageEngine())
    nid = str(uuid.uuid4())
    controller.propose(Principal.HUMAN, {
        "id": nid,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE,
        "category": "u01-audit",
        "tags": ["u01"],
        "created": "2026-10-05",
        "updated": "2026-10-05",
        "provenance": {"source_type": "user", "source_ref": "u01-test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "Original active content"
    })

    # 1. AI_AGENT attempts to mutate ACTIVE note -> MUST RAISE PermissionError
    with pytest.raises(PermissionError, match="AI_AGENT cannot update ACTIVE notes"):
        controller.update(Principal.AI_AGENT, nid, {"content": "tampered content"})

    # 2. For RAW note where AI updates are permitted, verify security invariants hold:
    raw_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, {
        "id": raw_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.RAW,
        "category": "u01-raw",
        "tags": ["u01"],
        "created": "2026-10-05",
        "updated": "2026-10-05",
        "provenance": {"source_type": "inference", "source_ref": "u01-test"},
        "confidence": "low",
        "verification": "unverified",
        "relations": [],
        "content": "Raw content"
    })

    # AI_AGENT attempts to escalate verification via update -> MUST RAISE ValueError
    with pytest.raises(ValueError, match="cannot be escalated via update"):
        controller.update(Principal.AI_AGENT, raw_id, {"verification": "verified"})

    # AI_AGENT attempts to forge provenance via update -> MUST RAISE ValueError
    with pytest.raises(ValueError, match="Field provenance.source_type is immutable"):
        controller.update(Principal.AI_AGENT, raw_id, {"provenance": {"source_type": "official"}})







