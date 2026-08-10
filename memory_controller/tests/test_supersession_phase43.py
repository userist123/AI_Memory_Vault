import pytest
import os
import json
import uuid
import tempfile
import shutil
from datetime import datetime, timezone
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.authorizer import Principal
import memory_controller.audit.logger as logger_module
from cognitive_core.recall import RecallEngine
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory

@pytest.fixture
def temp_vault():
    # Setup temporary directory for vault root
    temp_dir = tempfile.mkdtemp()
    
    # Create required canonical directories
    for folder in ["00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES", "04_MEMORY", "05_RESOURCES", "99_SYSTEM"]:
        os.makedirs(os.path.join(temp_dir, folder), exist_ok=True)
        
    yield temp_dir
    
    # Teardown
    shutil.rmtree(temp_dir)

@pytest.fixture
def test_audit_log():
    fd, path = tempfile.mkstemp(suffix=".jsonl")
    os.close(fd)
    
    # Keep reference to original logger
    orig_logger = logger_module._logger_instance
    logger_module._logger_instance = logger_module.AuditLogger(path)
    
    yield path
    
    # Restore original logger
    logger_module._logger_instance = orig_logger
    if os.path.exists(path):
        os.remove(path)

def make_note(id_val, lifecycle="ACTIVE", verification="unverified", provenance=None, version_range=None, content="some content"):
    if provenance is None:
        provenance = {"source_type": "user", "source_ref": "test"}
    note = {
        "id": id_val,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content
    }
    if version_range:
        note["version_range"] = version_range
    return note

def read_audit_entries(audit_path):
    entries = []
    with open(audit_path, "r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                entries.append(json.loads(line))
    return entries

def test_supersession_happy_path(temp_vault, test_audit_log):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_old = str(uuid.uuid4())
    id_new = str(uuid.uuid4())
    
    note_old = make_note(id_old, content="Legacy guidelines for Python 3.11")
    note_new = make_note(id_new, content="Updated guidelines for Python 3.12")
    
    controller.propose(Principal.ADMIN, note_old)
    controller.propose(Principal.ADMIN, note_new)
    
    # Perform explicit supersede
    controller.supersede(Principal.ADMIN, id_old, id_new, evidence="Python 3.12 update")
    
    # Read back and verify
    old_updated = storage.get(id_old)
    new_updated = storage.get(id_new)
    
    assert old_updated["lifecycle"] == "SUPERSEDED"
    assert old_updated["superseded_by"] == id_new
    assert new_updated["supersedes"] == id_old
    
    # Reciprocal relations
    replaces_rel = [r for r in new_updated["relations"] if r["relation"] == "replaces"]
    replaced_by_rel = [r for r in old_updated["relations"] if r["relation"] == "replaced_by"]
    
    assert len(replaces_rel) == 1
    assert replaces_rel[0]["target_id"] == id_old
    assert len(replaced_by_rel) == 1
    assert replaced_by_rel[0]["target_id"] == id_new
    
    # Audit log check
    entries = read_audit_entries(test_audit_log)
    supersede_entries = [e for e in entries if e["operation"] == "supersede"]
    archive_entries = [e for e in entries if e["operation"] == "archive_superseded"]
    
    assert len(supersede_entries) == 1
    assert supersede_entries[0]["outcome"] == "success"
    assert supersede_entries[0]["target_id"] == id_new
    assert supersede_entries[0]["metadata"]["old_id"] == id_old
    assert supersede_entries[0]["metadata"]["evidence"] == "Python 3.12 update"
    
    assert len(archive_entries) == 1
    assert archive_entries[0]["outcome"] == "success"
    assert archive_entries[0]["target_id"] == id_old
    assert archive_entries[0]["metadata"]["new_id"] == id_new

def test_supersession_self_and_cycles_rejected(temp_vault):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_a = str(uuid.uuid4())
    id_b = str(uuid.uuid4())
    id_c = str(uuid.uuid4())
    
    controller.propose(Principal.ADMIN, make_note(id_a))
    controller.propose(Principal.ADMIN, make_note(id_b))
    controller.propose(Principal.ADMIN, make_note(id_c))
    
    # Self-supersession rejection
    with pytest.raises(ValueError, match="Self-supersession is not allowed"):
        controller.supersede(Principal.ADMIN, id_a, id_a)
        
    # Missing predecessor rejection
    id_missing = str(uuid.uuid4())
    with pytest.raises(ValueError, match="Predecessor note .* does not exist"):
        controller.supersede(Principal.ADMIN, id_missing, id_a)
        
    # Missing successor rejection
    with pytest.raises(ValueError, match="Successor note .* does not exist"):
        controller.supersede(Principal.ADMIN, id_a, id_missing)
        
    # Create chain A -> B
    controller.supersede(Principal.ADMIN, id_b, id_a, "A replaces B")
    
    # Try B -> A (immediate cycle)
    with pytest.raises(ValueError, match="cycle"):
        controller.supersede(Principal.ADMIN, id_a, id_b, "B replaces A")
        
    # Create chain C -> B (so A -> B -> C)
    controller.supersede(Principal.ADMIN, id_c, id_b, "B replaces C")
    
    # Try C -> A (transitive cycle A -> B -> C -> A)
    with pytest.raises(ValueError, match="cycle"):
        controller.supersede(Principal.ADMIN, id_a, id_c, "C replaces A")

def test_supersession_human_verified_protection(temp_vault):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_old_verified = str(uuid.uuid4())
    id_new = str(uuid.uuid4())
    
    # Provenance source_type user is human-verified
    controller.propose(Principal.ADMIN, make_note(id_old_verified, verification="verified", provenance={"source_type": "user", "source_ref": "user manual"}))
    controller.propose(Principal.ADMIN, make_note(id_new))
    
    # AI_AGENT tries to supersede human-verified note -> PermissionError
    with pytest.raises(PermissionError, match="Human-verified memory cannot be automatically superseded"):
        controller.supersede(Principal.AI_AGENT, id_old_verified, id_new, "AI updates human knowledge")
        
    # Admin or Human CAN supersede it
    controller.supersede(Principal.ADMIN, id_old_verified, id_new, "Admin updates human knowledge")
    
    # Verify it worked
    assert storage.get(id_old_verified)["lifecycle"] == "SUPERSEDED"

def test_supersession_atomicity_and_persistence(temp_vault):
    # Setup storage
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_old = str(uuid.uuid4())
    id_new = str(uuid.uuid4())
    
    controller.propose(Principal.ADMIN, make_note(id_old))
    controller.propose(Principal.ADMIN, make_note(id_new))
    
    # Simulate a write error during the second write in the transaction
    # We subclass the storage set or mock it
    original_set = storage.set
    fail_on_new = False
    
    def mock_set(note_id, data):
        if fail_on_new and note_id == id_new:
            raise IOError("Disk Full simulation")
        original_set(note_id, data)
        
    storage.set = mock_set
    fail_on_new = True
    
    with pytest.raises(ValueError, match="Atomic supersession write failed"):
        controller.supersede(Principal.ADMIN, id_old, id_new)
        
    # Verify rollback: old note remains ACTIVE, new note does not have supersedes
    assert storage.get(id_old)["lifecycle"] == "ACTIVE"
    assert "superseded_by" not in storage.get(id_old)
    assert "supersedes" not in storage.get(id_new)
    
    # Turn off error and complete
    fail_on_new = False
    controller.supersede(Principal.ADMIN, id_old, id_new)
    
    # Re-initialize controller (restart verification)
    storage2 = FileStorageEngine(temp_vault)
    controller2 = MemoryController(storage2)
    
    assert storage2.get(id_old)["lifecycle"] == "SUPERSEDED"
    assert storage2.get(id_old)["superseded_by"] == id_new
    assert storage2.get(id_new)["supersedes"] == id_old

def test_recall_version_aware_boosting(temp_vault):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_311 = str(uuid.uuid4())
    id_312 = str(uuid.uuid4())
    id_no_version = str(uuid.uuid4())
    
    # Save three notes with similar contents but different versions
    controller.propose(Principal.ADMIN, make_note(id_311, version_range="Python 3.11", content="Python rules and code formatting guidelines"))
    controller.propose(Principal.ADMIN, make_note(id_312, version_range="Python 3.12", content="Python rules and code formatting guidelines"))
    controller.propose(Principal.ADMIN, make_note(id_no_version, content="Python rules and code formatting guidelines"))
    
    engine = RecallEngine(controller, DeterministicSemanticProvider())
    wm = WorkingMemory(capacity=5)
    
    activated_nodes = [
        (storage.get(id_311), 1.0),
        (storage.get(id_312), 1.0),
        (storage.get(id_no_version), 1.0)
    ]
    
    # Query with Python 3.12 -> Python 3.12 note should be boosted to first place
    results = engine.recall(Principal.AI_AGENT, "Python 3.12 formatting rules", activated_nodes, wm)
    
    assert results[0][0]["id"] == id_312
    # Verify 3.11 is down-ranked because of version mismatch penalty
    # 3.12 is at top, no-version is middle (neutral), 3.11 is at bottom (mismatch)
    assert results[1][0]["id"] == id_no_version
    assert results[2][0]["id"] == id_311

def test_recall_historical_queries(temp_vault):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_active = str(uuid.uuid4())
    id_superseded = str(uuid.uuid4())
    
    controller.propose(Principal.ADMIN, make_note(id_active, content="Modern standard styling"))
    controller.propose(Principal.ADMIN, make_note(id_superseded, content="Old deprecated styling guide"))
    
    # Manually supersede
    controller.supersede(Principal.ADMIN, id_superseded, id_active, "New style replaces old style")
    
    engine = RecallEngine(controller, DeterministicSemanticProvider())
    wm = WorkingMemory(capacity=5)
    
    activated_nodes = [
        (storage.get(id_active), 1.0),
        (storage.get(id_superseded), 1.0)
    ]
    
    # Query historical -> Superseded note should be returned and not heavily penalized
    results = engine.recall(Principal.AI_AGENT, "legacy deprecated guide", activated_nodes, wm)
    
    # The superseded note has "deprecated" which matches query semantically, and legacy query reduces penalty,
    # so it should score highly or at least exist.
    note_ids = [n[0]["id"] for n in results]
    assert id_superseded in note_ids

def test_valid_until_update_logs_audit_event(temp_vault, test_audit_log):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_note = str(uuid.uuid4())
    controller.propose(Principal.ADMIN, make_note(id_note))
    
    # Clear logs and update valid_until
    entries_before = len(read_audit_entries(test_audit_log))
    
    controller.update(Principal.ADMIN, id_note, {"valid_until": "2026-12-31"})
    
    entries = read_audit_entries(test_audit_log)
    valid_until_updates = [e for e in entries if e["operation"] == "valid_until_update"]
    
    assert len(valid_until_updates) == 1
    assert valid_until_updates[0]["target_id"] == id_note
    assert valid_until_updates[0]["metadata"]["new_valid_until"] == "2026-12-31"

def test_recall_valid_from_filtering(temp_vault):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_not_yet_valid = str(uuid.uuid4())
    id_valid = str(uuid.uuid4())
    
    # Save a note with valid_from in the future (e.g. 2030)
    controller.propose(Principal.ADMIN, make_note(id_not_yet_valid, content="Style guide for next decade", lifecycle="ACTIVE"))
    controller.update(Principal.ADMIN, id_not_yet_valid, {"valid_from": "2030-01-01"})
    
    # Save another note that is valid today
    controller.propose(Principal.ADMIN, make_note(id_valid, content="Style guide for current decade", lifecycle="ACTIVE"))
    controller.update(Principal.ADMIN, id_valid, {"valid_from": "2020-01-01"})
    
    engine = RecallEngine(controller, DeterministicSemanticProvider())
    wm = WorkingMemory(capacity=5)
    
    activated_nodes = [
        (storage.get(id_not_yet_valid), 1.0),
        (storage.get(id_valid), 1.0)
    ]
    
    results = engine.recall(Principal.AI_AGENT, "decade style guide", activated_nodes, wm)
    
    # The note starting in 2030 should be penalized (lower score)
    # So the currently valid one should rank higher
    assert results[0][0]["id"] == id_valid
    assert results[1][0]["id"] == id_not_yet_valid
    assert results[0][1] > results[1][1]

def test_supersession_audit_failure(temp_vault, test_audit_log):
    storage = FileStorageEngine(temp_vault)
    controller = MemoryController(storage)
    
    id_old = str(uuid.uuid4())
    id_new = str(uuid.uuid4())
    
    with pytest.raises(ValueError):
        controller.supersede(Principal.ADMIN, id_old, id_new, "evidence info")
        
    entries = read_audit_entries(test_audit_log)
    failure_entries = [e for e in entries if e["operation"] == "supersede" and e["outcome"] == "error"]
    
    assert len(failure_entries) == 1
    assert failure_entries[0]["target_id"] == id_new
    assert failure_entries[0]["metadata"]["old_id"] == id_old
    assert "error" in failure_entries[0]["metadata"]
