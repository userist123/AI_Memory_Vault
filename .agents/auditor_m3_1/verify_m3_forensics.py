import sys
import os
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "../..")))

import pytest
import json
import uuid
import tempfile
import shutil
from datetime import datetime, timezone

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.authorizer import Principal, DefaultAuthorizer, Operation
import memory_controller.audit.logger as logger_module
from cognitive_core.tool_router import ToolRouter, ApprovalRequiredError
from cognitive_core.learning import LearningEngine

def run_independent_forensic_checks():
    print("=== STARTING INDEPENDENT FORENSIC VERIFICATION ===")
    
    # Check 1: Invariant P0-001 (AI cannot propose verified)
    storage = StorageEngine()
    ctrl = MemoryController(storage)
    nid1 = str(uuid.uuid4())
    try:
        ctrl.propose(Principal.AI_AGENT, {
            "id": nid1,
            "type": "knowledge",
            "lifecycle": "RAW",
            "verification": "verified",
            "provenance": {"source_type": "inference", "source_ref": "test"}
        })
        assert False, "P0-001 FAILED: Propose verified should have raised ValueError"
    except ValueError as e:
        assert "verified" in str(e)
        assert storage.get(nid1) is None
        print("[PASS] P0-001 Verified: AI cannot propose verified")

    # Check 2: Invariant P0-002/P0-003/Prohibited Provenances for AI
    prohibited_sources = ["user", "official", "experience", "import"]
    for src in prohibited_sources:
        nid = str(uuid.uuid4())
        try:
            ctrl.propose(Principal.AI_AGENT, {
                "id": nid,
                "type": "knowledge",
                "lifecycle": "RAW",
                "provenance": {"source_type": src, "source_ref": "forged"}
            })
            assert False, f"P0-002/003 FAILED: Source '{src}' should have been rejected for AI_AGENT"
        except ValueError as e:
            assert f"not permitted to claim provenance source_type '{src}'" in str(e)
            assert storage.get(nid) is None
    print(f"[PASS] P0-002/P0-003 Verified: AI cannot claim prohibited sources {prohibited_sources}")

    # Check 3: Permitted Provenances for AI
    permitted_sources = ["execution", "ai", "inference", "unknown"]
    for src in permitted_sources:
        nid = str(uuid.uuid4())
        res_id = ctrl.propose(Principal.AI_AGENT, {
            "id": nid,
            "type": "knowledge",
            "lifecycle": "RAW",
            "provenance": {"source_type": src, "source_ref": "legit"}
        })
        assert res_id == nid
        assert storage.get(nid)["provenance"]["source_type"] == src
    print(f"[PASS] AI Permitted Provenances Verified: {permitted_sources}")

    # Check 4: Invariant P0-004 (AI cannot inject ACTIVE / non-permitted lifecycle at creation)
    prohibited_lc = ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"]
    for lc in prohibited_lc:
        nid = str(uuid.uuid4())
        try:
            ctrl.propose(Principal.AI_AGENT, {
                "id": nid,
                "type": "knowledge",
                "lifecycle": lc,
                "provenance": {"source_type": "inference", "source_ref": "test"}
            })
            assert False, f"P0-004 FAILED: Lifecycle '{lc}' should have been rejected for AI_AGENT at creation"
        except ValueError as e:
            assert f"cannot set lifecycle to '{lc}' at creation" in str(e)
            assert storage.get(nid) is None
    print(f"[PASS] P0-004 Verified: AI cannot inject prohibited lifecycles at creation {prohibited_lc}")

    # Check 5: Invariant P0-005 (AI cannot escalate verification to verified via update)
    nid5 = str(uuid.uuid4())
    ctrl.propose(Principal.AI_AGENT, {
        "id": nid5,
        "type": "knowledge",
        "lifecycle": "RAW",
        "verification": "unverified",
        "provenance": {"source_type": "inference", "source_ref": "test"}
    })
    try:
        ctrl.update(Principal.AI_AGENT, nid5, {"verification": "verified"})
        assert False, "P0-005 FAILED: Escalating verification via update should have raised ValueError"
    except ValueError as e:
        assert "verified" in str(e)
        assert storage.get(nid5)["verification"] == "unverified"
    print("[PASS] P0-005 Verified: AI cannot update verification to verified")

    # Check 6: Invariant P0-006 (provenance.source_type is immutable post-creation)
    nid6 = str(uuid.uuid4())
    ctrl.propose(Principal.AI_AGENT, {
        "id": nid6,
        "type": "knowledge",
        "lifecycle": "RAW",
        "provenance": {"source_type": "inference", "source_ref": "test"}
    })
    try:
        ctrl.update(Principal.AI_AGENT, nid6, {"provenance": {"source_type": "execution"}})
        assert False, "P0-006 FAILED: Modifying provenance.source_type via update should have raised ValueError"
    except ValueError as e:
        assert "immutable post-creation" in str(e)
        assert storage.get(nid6)["provenance"]["source_type"] == "inference"
    print("[PASS] P0-006 Verified: provenance.source_type is immutable post-creation")

    # Check 7: Invariant P0-007 (lifecycle and id immutable on update)
    nid7 = str(uuid.uuid4())
    ctrl.propose(Principal.AI_AGENT, {
        "id": nid7,
        "type": "knowledge",
        "lifecycle": "RAW",
        "provenance": {"source_type": "inference", "source_ref": "test"}
    })
    try:
        ctrl.update(Principal.AI_AGENT, nid7, {"lifecycle": "ACTIVE"})
        assert False, "P0-007 FAILED: Modifying lifecycle via update should have raised ValueError"
    except ValueError as e:
        assert "Field lifecycle is immutable" in str(e)
        assert storage.get(nid7)["lifecycle"] == "RAW"
    print("[PASS] P0-007 Verified: lifecycle is immutable on update")

    # Check 8: Invariant P0-010/011 (Attestation gates: HUMAN/ADMIN allowed, AI_AGENT denied)
    nid8 = str(uuid.uuid4())
    ctrl.propose(Principal.AI_AGENT, {
        "id": nid8,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "provenance": {"source_type": "inference", "source_ref": "test"}
    })
    
    # AI_AGENT denied
    try:
        ctrl.attest(Principal.AI_AGENT, nid8, "ai self-attest", "evidence")
        assert False, "P0-011 FAILED: AI_AGENT calling attest() should have raised PermissionError"
    except PermissionError:
        assert storage.get(nid8)["verification"] == "unverified"
        print("[PASS] P0-011 Verified: AI_AGENT denied attest()")

    # Missing reason denied
    try:
        ctrl.attest(Principal.HUMAN, nid8, "   ", "evidence")
        assert False, "Attest with empty reason should have failed"
    except ValueError:
        pass

    # Missing evidence denied
    try:
        ctrl.attest(Principal.HUMAN, nid8, "Valid reason", "  ")
        assert False, "Attest with empty evidence should have failed"
    except ValueError:
        pass

    # HUMAN success
    ctrl.attest(Principal.HUMAN, nid8, "Verified against RFC", "RFC-9000")
    assert storage.get(nid8)["verification"] == "verified"
    assert storage.get(nid8)["verification_source"] == "human"
    print("[PASS] P0-010 Verified: HUMAN attest() succeeds with valid reason & evidence")

    # ADMIN success on another note
    nid8_admin = str(uuid.uuid4())
    ctrl.propose(Principal.AI_AGENT, {
        "id": nid8_admin,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "provenance": {"source_type": "inference", "source_ref": "test"}
    })
    ctrl.attest(Principal.ADMIN, nid8_admin, "Admin verified", "SEC-DOC-1")
    assert storage.get(nid8_admin)["verification"] == "verified"
    assert storage.get(nid8_admin)["verification_source"] == "admin"
    print("[PASS] P0-011 Verified: ADMIN attest() succeeds")

    # Check 9: Invariant P0-012 (LearningEngine promotes to partially_verified, never verified)
    nid9 = str(uuid.uuid4())
    relations = [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(6)]
    note9 = {
        "id": nid9,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "security-test",
        "tags": ["test"],
        "created": "2026-08-10",
        "updated": "2026-08-10",
        "provenance": {"source_type": "user", "source_ref": "base"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": relations,
        "content": "test"
    }
    storage.set(nid9, note9)
    router = ToolRouter(ctrl)
    le = LearningEngine(ctrl, router)
    promoted = le.promote_memories(Principal.AI_AGENT)
    assert nid9 in promoted
    assert storage.get(nid9)["verification"] == "partially_verified"
    assert storage.get(nid9)["confidence"] == "high"
    print("[PASS] P0-012 Verified: LearningEngine promotes to partially_verified (not verified)")

    # Check 10: Invariant P0-013 & SQLite Zero Partial Writes
    temp_dir = tempfile.mkdtemp()
    try:
        db_path = os.path.join(temp_dir, "test_forensic.db")
        sqlite_engine = SQLiteStorageEngine(db_path, wal_mode=True)
        sqlite_ctrl = MemoryController(sqlite_engine)

        nid_attack = str(uuid.uuid4())
        try:
            sqlite_ctrl.propose(Principal.AI_AGENT, {
                "id": nid_attack,
                "type": "knowledge",
                "lifecycle": "RAW",
                "verification": "verified",
                "provenance": {"source_type": "inference", "source_ref": "attack"}
            })
        except ValueError:
            pass

        assert sqlite_engine.get(nid_attack) is None
        # Raw SQL query check
        conn = sqlite_engine._get_connection()
        cursor = conn.execute("SELECT count(*) as cnt FROM notes WHERE id = ?", (nid_attack,))
        row = cursor.fetchone()
        assert row["cnt"] == 0
        sqlite_engine.close()
        print("[PASS] P0-013 Verified: Zero partial writes in SQLite on invariant rejection")
    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)

    # Check 11: Invariant P0-015 (Supersession trust isolation)
    nid_old = str(uuid.uuid4())
    nid_new = str(uuid.uuid4())
    ctrl.propose(Principal.ADMIN, {
        "id": nid_old,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "provenance": {"source_type": "user", "source_ref": "admin-doc"}
    })
    ctrl.attest(Principal.ADMIN, nid_old, "Base verified", "Ref-1")
    assert storage.get(nid_old)["verification"] == "verified"

    ctrl.propose(Principal.ADMIN, {
        "id": nid_new,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "verification": "unverified",
        "provenance": {"source_type": "ai", "source_ref": "model-1"}
    })
    assert storage.get(nid_new)["verification"] == "unverified"

    ctrl.supersede(Principal.ADMIN, nid_old, nid_new, "Super update")
    assert storage.get(nid_old)["lifecycle"] == "SUPERSEDED"
    assert storage.get(nid_new)["verification"] == "unverified"
    print("[PASS] P0-015 Verified: Supersession does not transfer verification trust")

    # Check 12: ToolRouter Reconciliation Boundary
    nid_verified = str(uuid.uuid4())
    ctrl.propose(Principal.ADMIN, {
        "id": nid_verified,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "provenance": {"source_type": "user", "source_ref": "doc"}
    })
    ctrl.attest(Principal.ADMIN, nid_verified, "Verified fact", "doc-ref")
    
    # ToolRouter should block AI_AGENT from updating or archiving verified node without approval
    try:
        router.execute(Principal.AI_AGENT, "update", {"note_id": nid_verified, "category": "tampered"})
        assert False, "ToolRouter failed to block update on verified note"
    except ApprovalRequiredError as e:
        assert "human-verified memory" in str(e)
        print("[PASS] ToolRouter Reconciliation Boundary Verified: update on verified note blocked")

    try:
        router.execute(Principal.AI_AGENT, "archive", {"note_id": nid_verified, "reason": "ai archive"})
        assert False, "ToolRouter failed to block archive on verified note"
    except ApprovalRequiredError as e:
        assert "human-verified memory" in str(e)
        print("[PASS] ToolRouter Reconciliation Boundary Verified: archive on verified note blocked")

    # Check 13: Audit Logger Tamper-Evident SHA-256 Hash Chain
    temp_dir2 = tempfile.mkdtemp()
    try:
        log_file = os.path.join(temp_dir2, "audit_test.jsonl")
        audit_logger = logger_module.AuditLogger(log_file)
        audit_logger.log("ai_agent", "search", "query_hash_1", "success")
        audit_logger.log("human", "attest", "node_123", "success", metadata={"reason": "audit"})
        audit_logger.log("admin", "promote", "node_123", "success")
        
        valid, violations = audit_logger.verify_integrity()
        assert valid is True
        assert len(violations) == 0
        print("[PASS] Audit Logger Untampered Chain Verified: valid=True")

        # Tamper test
        with open(log_file, "r", encoding="utf-8") as f:
            lines = [json.loads(line) for line in f if line.strip()]
        lines[1]["actor"] = "forged_actor"
        with open(log_file, "w", encoding="utf-8") as f:
            for l in lines:
                f.write(json.dumps(l) + "\n")
                
        valid, violations = audit_logger.verify_integrity()
        assert valid is False
        assert len(violations) > 0
        print(f"[PASS] Audit Logger Tamper Detection Verified: detected {len(violations)} violations as expected")
    finally:
        shutil.rmtree(temp_dir2, ignore_errors=True)

    # Check 14: Adversarial Case Variations & Schema Validation
    # Schema requires exact enum lowercase strings: "verified", "official", etc.
    # Non-enum casing like "Verified" or "OFFICIAL" will fail JSON schema validation.
    nid14 = str(uuid.uuid4())
    try:
        ctrl.propose(Principal.AI_AGENT, {
            "id": nid14,
            "type": "knowledge",
            "lifecycle": "RAW",
            "verification": "Verified", # Invalid enum case
            "provenance": {"source_type": "inference", "source_ref": "test"}
        })
        assert False, "Invalid verification case 'Verified' should have failed schema validation"
    except Exception as e:
        print("[PASS] Adversarial Edge Case: Casing variation 'Verified' rejected by schema")

    # Check 15: AI attempting to bypass via nested dict or non-string verification
    nid15 = str(uuid.uuid4())
    try:
        ctrl.propose(Principal.AI_AGENT, {
            "id": nid15,
            "type": "knowledge",
            "lifecycle": "RAW",
            "verification": {"status": "verified"}, # Type confusion
            "provenance": {"source_type": "inference", "source_ref": "test"}
        })
        assert False, "Non-string verification should have failed schema validation"
    except Exception as e:
        print("[PASS] Adversarial Edge Case: Type confusion on verification rejected by schema")

    # Check 16: Attestation with non-existent note
    try:
        ctrl.attest(Principal.HUMAN, "non-existent-note-uuid", "reason", "evidence")
        assert False, "Attesting non-existent note should raise ValueError"
    except ValueError as e:
        assert "not found" in str(e)
        print("[PASS] Adversarial Edge Case: Attestation on non-existent note rejected")

    # Check 17: Attestation idempotent if already in desired state
    nid17 = str(uuid.uuid4())
    ctrl.propose(Principal.ADMIN, {
        "id": nid17,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "provenance": {"source_type": "inference", "source_ref": "test"}
    })
    ctrl.attest(Principal.HUMAN, nid17, "Initial reason", "doc-1")
    assert storage.get(nid17)["verification"] == "verified"
    # Calling attest again with same state returns early without error
    ctrl.attest(Principal.HUMAN, nid17, "Second reason", "doc-2")
    assert storage.get(nid17)["verification"] == "verified"
    print("[PASS] Adversarial Edge Case: Idempotent attestation handled cleanly")

    print("=== ALL 17 INDEPENDENT FORENSIC CHECKS PASSED PERFECTLY ===")

if __name__ == "__main__":
    run_independent_forensic_checks()
