import pytest
import os
import json
import uuid
import tempfile
import shutil
from datetime import datetime, timezone
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.authorizer import Principal, DefaultAuthorizer
import memory_controller.audit.logger as logger_module
from cognitive_core.learning import LearningEngine
from cognitive_core.tool_router import ToolRouter

@pytest.fixture
def temp_vault():
    temp_dir = tempfile.mkdtemp()
    for folder in ["00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES", "04_MEMORY", "05_RESOURCES", "99_SYSTEM"]:
        os.makedirs(os.path.join(temp_dir, folder), exist_ok=True)
    yield temp_dir
    shutil.rmtree(temp_dir)

@pytest.fixture
def test_audit_log():
    fd, path = tempfile.mkstemp(suffix=".jsonl")
    os.close(fd)
    orig_logger = logger_module._logger_instance
    logger_module._logger_instance = logger_module.AuditLogger(path)
    yield path
    logger_module._logger_instance = orig_logger
    if os.path.exists(path):
        os.remove(path)

def read_audit_entries(audit_path):
    entries = []
    with open(audit_path, "r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                entries.append(json.loads(line))
    return entries

def make_test_note(id_val, lifecycle="RAW", verification="unverified", provenance=None, content="test content"):
    if provenance is None:
        provenance = {"source_type": "inference", "source_ref": "test-model"}
    return {
        "id": id_val,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "security-test",
        "tags": ["test"],
        "created": "2026-08-10",
        "updated": "2026-08-10",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content
    }

# P0-001: AI cannot propose verified notes
def test_p0_001_ai_cannot_propose_verified(test_audit_log):
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, verification="verified")
    
    with pytest.raises(ValueError, match="verified"):
        controller.propose(Principal.AI_AGENT, payload)
        
    assert storage.get(note_id) is None
    assert len(storage.store) == 0
    
    entries = read_audit_entries(test_audit_log)
    propose_errors = [e for e in entries if e["operation"] == "propose" and e["outcome"] == "error"]
    assert len(propose_errors) >= 1

# P0-002: AI cannot claim official provenance
def test_p0_002_ai_cannot_claim_official_provenance(test_audit_log):
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, provenance={"source_type": "official", "source_ref": "nist-doc"})
    
    with pytest.raises(ValueError, match="not permitted to claim provenance source_type 'official'"):
        controller.propose(Principal.AI_AGENT, payload)
        
    assert storage.get(note_id) is None
    assert len(storage.store) == 0

# P0-003: AI cannot claim user provenance
def test_p0_003_ai_cannot_claim_user_provenance(test_audit_log):
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, provenance={"source_type": "user", "source_ref": "user-command"})
    
    with pytest.raises(ValueError, match="not permitted to claim provenance source_type 'user'"):
        controller.propose(Principal.AI_AGENT, payload)
        
    assert storage.get(note_id) is None
    assert len(storage.store) == 0

# P0-004: AI cannot inject ACTIVE lifecycle at creation
def test_p0_004_ai_cannot_inject_active_lifecycle_at_creation():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, lifecycle="ACTIVE")
    
    with pytest.raises(ValueError, match="cannot set lifecycle to 'ACTIVE' at creation"):
        controller.propose(Principal.AI_AGENT, payload)
        
    assert storage.get(note_id) is None
    assert len(storage.store) == 0

# P0-005: AI cannot escalate verification to verified via update()
def test_p0_005_ai_cannot_update_verification_to_verified(test_audit_log):
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, lifecycle="RAW", verification="unverified")
    
    controller.propose(Principal.AI_AGENT, payload)
    assert storage.get(note_id)["verification"] == "unverified"
    
    with pytest.raises(ValueError, match="verified"):
        controller.update(Principal.AI_AGENT, note_id, {"verification": "verified"})
        
    assert storage.get(note_id)["verification"] == "unverified"

# P0-006: Provenance source_type is immutable post-creation
def test_p0_006_provenance_source_type_immutable(test_audit_log):
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, lifecycle="RAW", provenance={"source_type": "inference", "source_ref": "init"})
    
    controller.propose(Principal.AI_AGENT, payload)
    
    with pytest.raises(ValueError, match="Field provenance.source_type is immutable post-creation"):
        controller.update(Principal.AI_AGENT, note_id, {"provenance": {"source_type": "execution"}})
        
    assert storage.get(note_id)["provenance"]["source_type"] == "inference"

# P0-007: Lifecycle update immutability regression check
def test_p0_007_lifecycle_immutable_on_update():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    payload = make_test_note(note_id, lifecycle="RAW")
    
    controller.propose(Principal.AI_AGENT, payload)
    
    with pytest.raises(ValueError, match="Field lifecycle is immutable"):
        controller.update(Principal.AI_AGENT, note_id, {"lifecycle": "ACTIVE"})
        
    assert storage.get(note_id)["lifecycle"] == "RAW"

# P0-008: Direct controller attack protection without ToolRouter
def test_p0_008_direct_controller_attack_blocked():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    
    # Direct raw attack
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, {
            "id": note_id,
            "verification": "verified",
            "lifecycle": "ACTIVE",
            "provenance": {"source_type": "user", "source_ref": "attack"}
        })
    assert storage.get(note_id) is None
    assert len(storage.store) == 0

# P0-010: HUMAN can attest notes
def test_p0_010_human_attestation(test_audit_log):
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    
    # Propose note in REVIEW
    controller.propose(Principal.AI_AGENT, make_test_note(note_id, lifecycle="REVIEW", verification="unverified"))
    assert storage.get(note_id)["verification"] == "unverified"
    
    # Human attests
    controller.attest(
        Principal.HUMAN,
        note_id,
        verification_reason="manual inspection against documentation",
        evidence_reference="rfc-9000-sec-4"
    )
    
    note = storage.get(note_id)
    assert note["verification"] == "verified"
    assert note["verification_source"] == "human"
    assert "last_verified" in note
    
    # Check audit log
    entries = read_audit_entries(test_audit_log)
    attest_entries = [e for e in entries if e["operation"] == "attest"]
    assert len(attest_entries) == 1
    assert attest_entries[0]["outcome"] == "success"
    assert attest_entries[0]["metadata"]["attested_by"] == "human"
    assert attest_entries[0]["metadata"]["reason"] == "manual inspection against documentation"
    assert attest_entries[0]["metadata"]["evidence_reference"] == "rfc-9000-sec-4"
    assert attest_entries[0]["metadata"]["previous_verification_state"] == "unverified"
    assert attest_entries[0]["metadata"]["new_verification_state"] == "verified"

# P0-011: ADMIN can attest notes; AI_AGENT cannot attest
def test_p0_011_admin_attestation_and_ai_agent_denied():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    
    controller.propose(Principal.AI_AGENT, make_test_note(note_id, lifecycle="REVIEW", verification="unverified"))
    
    # AI_AGENT cannot call attest()
    with pytest.raises(PermissionError):
        controller.attest(Principal.AI_AGENT, note_id, "ai self verify", "none")
        
    # ADMIN can call attest()
    controller.attest(
        Principal.ADMIN,
        note_id,
        verification_reason="admin override after full security review",
        evidence_reference="sec-audit-2026-08"
    )
    assert storage.get(note_id)["verification"] == "verified"
    assert storage.get(note_id)["verification_source"] == "admin"

# P0-013: Atomic non-persistence on rejection
def test_p0_013_atomic_non_persistence():
    storage = StorageEngine()
    controller = MemoryController(storage)
    
    assert len(storage.store) == 0
    
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, make_test_note(str(uuid.uuid4()), verification="verified"))
    assert len(storage.store) == 0
    
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, make_test_note(str(uuid.uuid4()), provenance={"source_type": "user", "source_ref": "x"}))
    assert len(storage.store) == 0

# P0-014: Restart simulation preserves attested verification
def test_p0_014_restart_preserves_attestation(temp_vault):
    storage1 = FileStorageEngine(temp_vault)
    controller1 = MemoryController(storage1)
    note_id = str(uuid.uuid4())
    
    controller1.propose(Principal.ADMIN, make_test_note(note_id, lifecycle="REVIEW", provenance={"source_type": "inference", "source_ref": "obs"}))
    controller1.attest(Principal.HUMAN, note_id, "ground truth verified", "lab-result-1")
    
    # Reload in a second instance pointing to the same storage path
    storage2 = FileStorageEngine(temp_vault)
    controller2 = MemoryController(storage2)
    
    reloaded_note = storage2.get(note_id)
    assert reloaded_note is not None
    assert reloaded_note["verification"] == "verified"
    assert reloaded_note["verification_source"] == "human"
    assert reloaded_note["provenance"]["source_type"] == "inference"

# P0-015: Supersession does not transfer verification trust
def test_p0_015_supersession_does_not_transfer_trust(temp_vault):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    old_id = str(uuid.uuid4())
    new_id = str(uuid.uuid4())
    
    # Create old note and attest it as verified
    controller.propose(Principal.ADMIN, make_test_note(old_id, lifecycle="ACTIVE", provenance={"source_type": "user", "source_ref": "manual"}))
    controller.attest(Principal.ADMIN, old_id, "verified base", "doc")
    assert storage.get(old_id)["verification"] == "verified"
    
    # Create new note which is unverified
    controller.propose(Principal.ADMIN, make_test_note(new_id, lifecycle="ACTIVE", verification="unverified", provenance={"source_type": "ai", "source_ref": "gen"}))
    assert storage.get(new_id)["verification"] == "unverified"
    
    # Supersede old with new
    controller.supersede(Principal.ADMIN, old_id, new_id, evidence="update")
    
    assert storage.get(old_id)["lifecycle"] == "SUPERSEDED"
    assert storage.get(new_id)["verification"] == "unverified"  # Explicit check: trust NOT inherited

# test_ai_cannot_self_verify explicit test
def test_ai_cannot_self_verify():
    storage = StorageEngine()
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    
    # 1. via propose
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, make_test_note(note_id, verification="verified"))
        
    # 2. via attest
    controller.propose(Principal.AI_AGENT, make_test_note(note_id, verification="unverified"))
    with pytest.raises(PermissionError):
        controller.attest(Principal.AI_AGENT, note_id, "self verification", "self")
        
    # 3. via update
    with pytest.raises(ValueError):
        controller.update(Principal.AI_AGENT, note_id, {"verification": "verified"})

def test_p0_additional_ai_prohibited_provenance_types():
    storage = StorageEngine()
    controller = MemoryController(storage)
    
    # Prohibited: experience
    id1 = str(uuid.uuid4())
    with pytest.raises(ValueError, match="not permitted to claim provenance source_type 'experience'"):
        controller.propose(Principal.AI_AGENT, make_test_note(id1, provenance={"source_type": "experience", "source_ref": "exp"}))
    assert storage.get(id1) is None
    
    # Prohibited: import
    id2 = str(uuid.uuid4())
    with pytest.raises(ValueError, match="not permitted to claim provenance source_type 'import'"):
        controller.propose(Principal.AI_AGENT, make_test_note(id2, provenance={"source_type": "import", "source_ref": "imp"}))
    assert storage.get(id2) is None

def test_p0_ai_permitted_provenance_types():
    storage = StorageEngine()
    controller = MemoryController(storage)
    
    for st in ["execution", "ai", "inference", "unknown"]:
        nid = str(uuid.uuid4())
        controller.propose(Principal.AI_AGENT, make_test_note(nid, lifecycle="REVIEW", provenance={"source_type": st, "source_ref": f"ref-{st}"}))
        assert storage.get(nid) is not None
        assert storage.get(nid)["provenance"]["source_type"] == st

def test_p0_ai_prohibited_creation_lifecycles():
    storage = StorageEngine()
    controller = MemoryController(storage)
    
    for prohibited_lc in ["VERIFIED", "SUPERSEDED", "ARCHIVED"]:
        nid = str(uuid.uuid4())
        with pytest.raises(ValueError, match=f"cannot set lifecycle to '{prohibited_lc}' at creation"):
            controller.propose(Principal.AI_AGENT, make_test_note(nid, lifecycle=prohibited_lc))
        assert storage.get(nid) is None

def test_p0_sqlite_storage_security_hardening(temp_vault):
    from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
    db_path = os.path.join(temp_vault, "test_sec.sqlite3")
    storage = SQLiteStorageEngine(db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    # 1. AI cannot propose verified
    id1 = str(uuid.uuid4())
    with pytest.raises(ValueError, match="verified"):
        controller.propose(Principal.AI_AGENT, make_test_note(id1, verification="verified"))
    assert storage.get(id1) is None
    
    # 2. AI cannot claim user provenance
    id2 = str(uuid.uuid4())
    with pytest.raises(ValueError, match="not permitted to claim provenance source_type 'user'"):
        controller.propose(Principal.AI_AGENT, make_test_note(id2, provenance={"source_type": "user", "source_ref": "u"}))
    assert storage.get(id2) is None
    
    # 3. Legitimate propose in REVIEW -> Human attest -> Promote to ACTIVE
    id3 = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_test_note(id3, lifecycle="REVIEW", provenance={"source_type": "inference", "source_ref": "inf"}))
    assert storage.get(id3)["verification"] == "unverified"
    
    controller.attest(Principal.HUMAN, id3, "Human verified against facts", "evidence-ref-1")
    assert storage.get(id3)["verification"] == "verified"
    assert storage.get(id3)["verification_source"] == "human"
    
    controller.promote(Principal.HUMAN, id3)
    assert storage.get(id3)["lifecycle"] == "ACTIVE"
    
    storage.close()

