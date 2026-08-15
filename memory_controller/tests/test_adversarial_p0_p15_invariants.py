import pytest
import os
import json
import uuid
import tempfile
import shutil
import threading
import sqlite3
from concurrent.futures import ThreadPoolExecutor, as_completed

from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.authorizer import Principal, DefaultAuthorizer, Operation
import memory_controller.audit.logger as logger_module
from cognitive_core.tool_router import ToolRouter, ApprovalRequiredError
from cognitive_core.learning import LearningEngine

@pytest.fixture
def temp_vault():
    temp_dir = tempfile.mkdtemp()
    for folder in ["00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES", "04_MEMORY", "05_RESOURCES", "99_SYSTEM"]:
        os.makedirs(os.path.join(temp_dir, folder), exist_ok=True)
    yield temp_dir
    shutil.rmtree(temp_dir, ignore_errors=True)

@pytest.fixture
def temp_db_path():
    fd, path = tempfile.mkstemp(suffix=".sqlite3")
    os.close(fd)
    if os.path.exists(path):
        os.remove(path)
    yield path
    for ext in ["", "-wal", "-shm"]:
        target = path + ext
        if os.path.exists(target):
            try:
                os.remove(target)
            except Exception:
                pass

@pytest.fixture
def test_audit_log():
    fd, path = tempfile.mkstemp(suffix=".jsonl")
    os.close(fd)
    orig_logger = logger_module._logger_instance
    logger_instance = logger_module.AuditLogger(path)
    logger_module._logger_instance = logger_instance
    yield path, logger_instance
    logger_module._logger_instance = orig_logger
    if os.path.exists(path):
        try:
            os.remove(path)
        except Exception:
            pass

def make_note(id_val=None, lifecycle="RAW", verification="unverified", provenance=None, content="test content", note_type="knowledge", category="security-adversarial"):
    if id_val is None:
        id_val = str(uuid.uuid4())
    if provenance is None:
        provenance = {"source_type": "inference", "source_ref": "test-inference-model"}
    return {
        "id": id_val,
        "type": note_type,
        "lifecycle": lifecycle,
        "category": category,
        "tags": ["adversarial", "security", "p0-p15"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content
    }

def read_audit_entries(audit_path):
    entries = []
    with open(audit_path, "r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                entries.append(json.loads(line))
    return entries

# ============================================================================
# 1. Attack Vector: AI Self-Verification Bypass Attempts (P0-001, P0-005, P0-011)
# ============================================================================

def test_attack_ai_propose_verified_strict_rejection_and_zero_writes(temp_db_path):
    """Attack 1.1: AI attempts to propose notes with verification='verified' across storage engines.
    Verify immediate exception and 0 database writes in SQLite WAL."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    note_id = str(uuid.uuid4())
    payload = make_note(id_val=note_id, verification="verified")
    
    with pytest.raises(ValueError, match=r"Verification status 'verified' cannot be set via propose\. Use attest\(\) instead\."):
        controller.propose(Principal.AI_AGENT, payload)
        
    # Assert 0 storage retrieval
    assert storage.get(note_id) is None
    
    # Assert 0 rows in raw SQLite table
    conn = sqlite3.connect(temp_db_path)
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    row_count = cursor.fetchone()[0]
    conn.close()
    assert row_count == 0


def test_attack_ai_update_escalate_verification_strict_rejection(temp_db_path):
    """Attack 1.2: AI proposes a valid draft in RAW lifecycle, then attempts to escalate
    verification to 'verified' via update().
    Verify rejection, zero escalation, and verified cannot be set via update for any principal."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(id_val=note_id, lifecycle="RAW", verification="unverified"))
    assert storage.get(note_id)["verification"] == "unverified"
    
    # AI_AGENT update attack on RAW draft
    with pytest.raises(ValueError, match="Verification status 'verified' cannot be escalated via update"):
        controller.update(Principal.AI_AGENT, note_id, {"verification": "verified"})
    assert storage.get(note_id)["verification"] == "unverified"
    
    # Test HUMAN update on an ACTIVE note
    active_id = str(uuid.uuid4())
    controller.propose(Principal.HUMAN, make_note(id_val=active_id, lifecycle="REVIEW", provenance={"source_type": "user", "source_ref": "u"}))
    controller.attest(Principal.HUMAN, active_id, "Attesting active note", "evidence")
    controller.promote(Principal.HUMAN, active_id)
    assert storage.get(active_id)["lifecycle"] == "ACTIVE"
    
    # HUMAN update attack on ACTIVE note attempting to escalate verification directly
    with pytest.raises(ValueError, match="Verification status 'verified' cannot be escalated via update"):
        controller.update(Principal.HUMAN, active_id, {"verification": "verified"})


def test_attack_ai_attest_unauthorized_permission_error(temp_db_path):
    """Attack 1.3: AI directly calls controller.attest().
    Verify PermissionError raised and verification remains unverified."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(id_val=note_id, lifecycle="REVIEW", verification="unverified"))
    
    with pytest.raises(PermissionError, match="ai_agent not allowed to perform attest"):
        controller.attest(Principal.AI_AGENT, note_id, "AI self-attestation", "evidence-ai")
        
    assert storage.get(note_id)["verification"] == "unverified"
    assert "verification_source" not in storage.get(note_id)


# ============================================================================
# 2. Attack Vector: Provenance Forgery & Immutability Bypass (P0-002, P0-003, P0-006)
# ============================================================================

def test_attack_ai_forge_privileged_provenance_types(temp_db_path):
    """Attack 2.1: AI attempts to propose notes with forbidden provenance source_types
    ('user', 'official', 'experience', 'import').
    Verify strict rejection and 0 database writes in SQLite WAL."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    forbidden_types = ["user", "official", "experience", "import"]
    
    for st in forbidden_types:
        note_id = str(uuid.uuid4())
        payload = make_note(id_val=note_id, provenance={"source_type": st, "source_ref": "attack-src"})
        
        with pytest.raises(ValueError, match=f"Principal 'ai_agent' is not permitted to claim provenance source_type '{st}'"):
            controller.propose(Principal.AI_AGENT, payload)
            
        assert storage.get(note_id) is None
        
    # Verify raw SQLite count is 0
    conn = sqlite3.connect(temp_db_path)
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    assert cursor.fetchone()[0] == 0
    conn.close()


def test_attack_provenance_source_type_post_creation_immutability(temp_db_path):
    """Attack 2.2: Note is created with legitimate provenance ('inference').
    AI_AGENT, HUMAN, and ADMIN attempt to mutate provenance.source_type via update().
    Verify immutability is strictly enforced across all principals."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(id_val=note_id, lifecycle="REVIEW", provenance={"source_type": "inference", "source_ref": "v1"}))
    
    # 1. AI attempts to change source_type on draft note in RAW/REVIEW
    # Note: AI can update RAW drafts, but on REVIEW it requires ACTIVE for normal update or draft update
    # Let's create raw draft for AI update
    raw_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(id_val=raw_id, lifecycle="RAW", provenance={"source_type": "inference", "source_ref": "v1"}))
    with pytest.raises(ValueError, match="Field provenance.source_type is immutable post-creation"):
        controller.update(Principal.AI_AGENT, raw_id, {"provenance": {"source_type": "execution"}})
    assert storage.get(raw_id)["provenance"]["source_type"] == "inference"
    
    # Promote note_id (in REVIEW) to ACTIVE for HUMAN/ADMIN update checks
    controller.attest(Principal.HUMAN, note_id, "Attested for testing", "rfc-ref")
    controller.promote(Principal.HUMAN, note_id)
    assert storage.get(note_id)["lifecycle"] == "ACTIVE"
    
    # 2. HUMAN attempts to change source_type
    with pytest.raises(ValueError, match="Field provenance.source_type is immutable post-creation"):
        controller.update(Principal.HUMAN, note_id, {"provenance": {"source_type": "user"}})
    assert storage.get(note_id)["provenance"]["source_type"] == "inference"
    
    # 3. ADMIN attempts to change source_type
    with pytest.raises(ValueError, match="Field provenance.source_type is immutable post-creation"):
        controller.update(Principal.ADMIN, note_id, {"provenance": {"source_type": "official"}})
    assert storage.get(note_id)["provenance"]["source_type"] == "inference"


# ============================================================================
# 3. Attack Vector: Lifecycle Escalation & State Tampering (P0-004, P0-007)
# ============================================================================

def test_attack_ai_propose_active_lifecycle_strict_rejection(temp_db_path):
    """Attack 3.1: AI attempts to propose notes directly into ACTIVE, VERIFIED, SUPERSEDED, or ARCHIVED states.
    Verify strict rejection and 0 database writes."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    prohibited_states = ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"]
    
    for state in prohibited_states:
        note_id = str(uuid.uuid4())
        payload = make_note(id_val=note_id, lifecycle=state)
        
        with pytest.raises(ValueError, match=f"Principal 'ai_agent' cannot set lifecycle to '{state}' at creation"):
            controller.propose(Principal.AI_AGENT, payload)
            
        assert storage.get(note_id) is None
        
    conn = sqlite3.connect(temp_db_path)
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    assert cursor.fetchone()[0] == 0
    conn.close()


def test_attack_lifecycle_field_immutability_on_update(temp_db_path):
    """Attack 3.2: AI attempts to change lifecycle field directly in update().
    Verify rejection and zero state change."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(id_val=note_id, lifecycle="RAW"))
    
    with pytest.raises(ValueError, match="Field lifecycle is immutable"):
        controller.update(Principal.AI_AGENT, note_id, {"lifecycle": "ACTIVE"})
        
    assert storage.get(note_id)["lifecycle"] == "RAW"


# ============================================================================
# 4. Attack Vector: ToolRouter Reconciliation Boundary & Risk Gating (P0-009, BRAIN-13)
# ============================================================================

def test_attack_tool_router_reconciliation_boundary_blocks_unauthorized_mutations(temp_db_path):
    """Attack 4.1: Human-verified note exists in ACTIVE lifecycle.
    AI_AGENT via ToolRouter attempts automated update, archive, and supersede.
    Verify ApprovalRequiredError is raised and note remains intact."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    
    note_id = str(uuid.uuid4())
    controller.propose(Principal.HUMAN, make_note(id_val=note_id, lifecycle="REVIEW", provenance={"source_type": "user", "source_ref": "user-input"}))
    controller.attest(Principal.HUMAN, note_id, "Human verified rule", "contract-v1")
    controller.promote(Principal.HUMAN, note_id)
    
    assert storage.get(note_id)["verification"] == "verified"
    assert storage.get(note_id)["lifecycle"] == "ACTIVE"
    
    # 1. Update on verified note via ToolRouter
    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.AI_AGENT, "update", {"note_id": note_id, "updates": {"category": "tampered"}})
        
    # 2. Archive on verified note via ToolRouter
    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.AI_AGENT, "archive", {"note_id": note_id, "reason": "malicious archive"})
        
    # 3. Supersede on verified note via ToolRouter
    new_note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(id_val=new_note_id, lifecycle="REVIEW"))
    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.AI_AGENT, "supersede", {"old_id": note_id, "new_id": new_note_id})
        
    # Verify verified note in SQLite remains 100% unaltered
    verified_note = storage.get(note_id)
    assert verified_note["verification"] == "verified"
    assert verified_note["category"] == "security-adversarial"
    assert verified_note["lifecycle"] == "ACTIVE"


def test_attack_tool_router_high_risk_actions_gated(temp_db_path):
    """Attack 4.2: Destructive operations (delete_canonical, modify_raw_imports)
    must always be blocked by ToolRouter as HIGH RISK."""
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    
    note_id = str(uuid.uuid4())
    
    with pytest.raises(ApprovalRequiredError, match="Action 'delete_canonical' is HIGH RISK"):
        router.execute(Principal.AI_AGENT, "delete_canonical", {"note_id": note_id})
        
    with pytest.raises(ApprovalRequiredError, match="Action 'modify_raw_imports' is HIGH RISK"):
        router.execute(Principal.AI_AGENT, "modify_raw_imports", {"target": "all"})


# ============================================================================
# 5. Attack Vector: File Storage Isolation & 0 Disk Artifacts on Rejection
# ============================================================================

def test_attack_file_storage_zero_disk_artifacts_on_rejected_proposals(temp_vault):
    """Attack 5.1: Verify FileStorageEngine does not write partial .md files when
    proposal is rejected due to security invariants."""
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    # Count initial files in temp_vault
    initial_files = []
    for root, dirs, files in os.walk(temp_vault):
        for f in files:
            initial_files.append(os.path.join(root, f))
            
    # Attack 1: verified propose
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, make_note(verification="verified"))
        
    # Attack 2: user provenance propose
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, make_note(provenance={"source_type": "user", "source_ref": "u"}))
        
    # Attack 3: active lifecycle propose
    with pytest.raises(ValueError):
        controller.propose(Principal.AI_AGENT, make_note(lifecycle="ACTIVE"))
        
    # Count files after attacks
    current_files = []
    for root, dirs, files in os.walk(temp_vault):
        for f in files:
            current_files.append(os.path.join(root, f))
            
    assert len(current_files) == len(initial_files), f"Expected 0 new files on disk, found {len(current_files) - len(initial_files)} new files!"


# ============================================================================
# 6. Attack Vector: Multi-Threaded Adversarial Barrage & 0 Partial Writes
# ============================================================================

def test_attack_multi_threaded_adversarial_barrage_zero_partial_writes(temp_db_path):
    """Attack 6.1: High-concurrency barrage:
    - 8 attacker threads bombard controller with rejected operations (verified, forged provenance, active lifecycle).
    - 4 legitimate threads propose valid draft notes in REVIEW.
    - 4 reader threads perform queries.
    Assert:
    1. Exactly the legitimate notes exist in SQLite.
    2. Zero partial or corrupted records exist in SQLite.
    3. SQLite PRAGMA integrity_check returns 'ok'.
    """
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True, timeout=10.0)
    controller = MemoryController(storage)
    
    num_attackers = 8
    attacks_per_thread = 25
    num_legit_writers = 4
    legit_per_thread = 25
    num_readers = 4
    
    security_violations = []
    legit_ids_created = []
    lock = threading.Lock()
    
    def attacker(idx):
        for j in range(attacks_per_thread):
            mode = j % 3
            nid = str(uuid.uuid4())
            if mode == 0:
                payload = make_note(id_val=nid, verification="verified")
            elif mode == 1:
                payload = make_note(id_val=nid, provenance={"source_type": "official", "source_ref": "forged"})
            else:
                payload = make_note(id_val=nid, lifecycle="ACTIVE")
                
            try:
                controller.propose(Principal.AI_AGENT, payload)
                with lock:
                    security_violations.append(f"Security breach: Attack {mode} succeeded for {nid}")
            except (ValueError, PermissionError):
                pass
            except Exception as e:
                with lock:
                    security_violations.append(f"Unexpected attacker exception: {type(e).__name__}: {str(e)}")

    def legit_writer(idx):
        for j in range(legit_per_thread):
            nid = str(uuid.uuid4())
            payload = make_note(id_val=nid, lifecycle="REVIEW", provenance={"source_type": "inference", "source_ref": f"worker-{idx}"})
            try:
                controller.propose(Principal.AI_AGENT, payload)
                with lock:
                    legit_ids_created.append(nid)
            except Exception as e:
                with lock:
                    security_violations.append(f"Legit writer error: {type(e).__name__}: {str(e)}")

    def reader(idx):
        for _ in range(30):
            try:
                res = storage.query(lifecycle=["REVIEW"])
                assert isinstance(res, list)
            except Exception as e:
                with lock:
                    security_violations.append(f"Reader error: {type(e).__name__}: {str(e)}")

    with ThreadPoolExecutor(max_workers=num_attackers + num_legit_writers + num_readers) as executor:
        futures = []
        for a in range(num_attackers):
            futures.append(executor.submit(attacker, a))
        for lw in range(num_legit_writers):
            futures.append(executor.submit(legit_writer, lw))
        for r in range(num_readers):
            futures.append(executor.submit(reader, r))
            
        for f in as_completed(futures):
            f.result()
            
    assert len(security_violations) == 0, f"Violations found: {security_violations}"
    
    expected_count = num_legit_writers * legit_per_thread
    assert len(legit_ids_created) == expected_count
    
    # Check raw SQLite row count
    conn = sqlite3.connect(temp_db_path)
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    total_db_rows = cursor.fetchone()[0]
    
    cursor.execute("PRAGMA integrity_check")
    integrity = cursor.fetchall()
    conn.close()
    
    assert total_db_rows == expected_count, f"Expected {expected_count} rows in SQLite, found {total_db_rows}!"
    assert integrity == [("ok",)], f"Integrity check failed: {integrity}"
