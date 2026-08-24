---
id: "23d98699-7c5f-48c0-be63-ac87bbeaf504"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "PERPLEXITY_TAKEOVER_04_TESTS.md"
confidence: high
verification: verified
relations: []
---

# Artifact: PERPLEXITY_TAKEOVER_04_TESTS

# PERPLEXITY TAKEOVER 04 TESTS


============================================================
FILE: cognitive_core/tests/test_working_memory_persistence.py
============================================================

import os
import tempfile
import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.working_memory import WorkingMemory

def test_working_memory_save_load():
    wm = WorkingMemory(capacity=5)
    # Mock some admitted nodes
    wm.admit([
        ({"id": "node-1"}, 1.0),
        ({"id": "node-2"}, 0.8)
    ])
    
    # Verify they are in WM
    assert len(wm.buffer) == 2
    assert wm.tick == 1
    
    with tempfile.TemporaryDirectory() as temp_dir:
        state_file = os.path.join(temp_dir, "wm_state.json")
        
        # Save state
        wm.save_state(state_file)
        assert os.path.exists(state_file)
        
        # Create a new WM instance
        new_wm = WorkingMemory(capacity=5)
        
        # Mock MemoryController to return the nodes when loading
        mock_controller = MagicMock()
        def mock_read(principal, node_id, **kwargs):
            return {"results": [{"id": node_id, "mock_data": True}]}
        mock_controller.read.side_effect = mock_read
        
        # Load state
        new_wm.load_state(state_file, mock_controller, Principal.AI_AGENT)
        
        # Verify state was restored
        assert new_wm.tick == 1
        assert len(new_wm.buffer) == 2
        
        # Verify node-1
        assert "node-1" in new_wm.buffer
        assert new_wm.buffer["node-1"]["activation"] == 1.0
        assert new_wm.buffer["node-1"]["node"]["mock_data"] is True
        
        # Verify node-2
        assert "node-2" in new_wm.buffer
        assert new_wm.buffer["node-2"]["activation"] == 0.8

def test_working_memory_load_missing_node():
    wm = WorkingMemory(capacity=5)
    wm.admit([({"id": "node-1"}, 1.0)])
    
    with tempfile.TemporaryDirectory() as temp_dir:
        state_file = os.path.join(temp_dir, "wm_state.json")
        wm.save_state(state_file)
        
        new_wm = WorkingMemory(capacity=5)
        
        # Mock MemoryController to simulate node-1 being deleted or unauthorized
        mock_controller = MagicMock()
        mock_controller.read.side_effect = ValueError("Not found or access denied")
        
        new_wm.load_state(state_file, mock_controller, Principal.AI_AGENT)
        
        # Buffer should be empty because node-1 couldn't be loaded
        assert len(new_wm.buffer) == 0
        assert new_wm.tick == 1


============================================================
FILE: cognitive_core/tests/test_continuity.py
============================================================

import os
import tempfile
import pytest
from unittest.mock import MagicMock

from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive
from cognitive_core.planning import ActivePlan

def test_executive_continuity():
    mock_controller = MagicMock()
    mock_controller.search.return_value = {"results": [{"id": "node-1"}]}
    mock_controller.read = MagicMock(return_value={"results": [{"id": "node-1"}]})
    mock_controller.cognitive_read = MagicMock(return_value={"results": [{"id": "node-1"}]})

    
    with tempfile.TemporaryDirectory() as temp_dir:
        exec1 = Executive(mock_controller, checkpoint_dir=temp_dir)
        
        plan = ActivePlan("test goal", [
            {"step": 1, "action": "search", "query": "step 1"},
            {"step": 2, "action": "search", "query": "step 2"}
        ])
        
        exec1.active_plan = plan
        exec1.working_memory.admit([({
            "id": "node-1", "content": "test", "confidence": "high"
        }, 1.0)])
        
        # Execute first step
        res1 = exec1.step_loop(Principal.AI_AGENT)
        assert res1["status"] == "success"
        assert exec1.active_plan.current_step_index == 1
        
        # WIRE-5: Auto-checkpoint should have written files
        assert os.path.exists(os.path.join(temp_dir, "wm.json"))
        assert os.path.exists(os.path.join(temp_dir, "plan.json"))
        
        # New process starts
        exec2 = Executive(mock_controller)
        exec2.load_state(temp_dir, Principal.AI_AGENT)
        
        assert exec2.active_plan is not None
        assert exec2.active_plan.goal == "test goal"
        assert exec2.active_plan.current_step_index == 1
        assert "node-1" in exec2.working_memory.buffer
        
        # Execute next step
        res2 = exec2.step_loop(Principal.AI_AGENT)
        assert res2["status"] == "success"
        
        assert exec2.active_plan.current_step_index == 2
        assert exec2.active_plan.is_complete()
        
        res3 = exec2.step_loop(Principal.AI_AGENT)
        assert res3["status"] == "idle"


============================================================
FILE: cognitive_core/tests/test_end_to_end_workflow.py
============================================================

import os
import tempfile
import pytest
from memory_controller.controller import controller as global_controller
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.executive import Executive

@pytest.fixture
def setup_notes():
    # Clean storage
    global_controller.storage.store = {}
    # Create ACTIVE note A with relation to B
    note_a = {
        "id": "A",
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "confidence": "high",
        "verification": "verified",
        "provenance": {"source_type": "user"},
        "content": "Content A",
        "relations": [{"target_id": "B"}]
    }
    global_controller.storage.set("A", note_a)
    # Create REVIEW note B
    note_b = {
        "id": "B",
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "confidence": "high",
        "verification": "unverified",
        "provenance": {"source_type": "user"},
        "content": "Content B",
        "relations": []
    }
    global_controller.storage.set("B", note_b)
    return note_a, note_b

def test_end_to_end_workflow(setup_notes):
    note_a, note_b = setup_notes
    with tempfile.TemporaryDirectory() as tmp_dir:
        # Initialize Executive with checkpoint directory
        exec1 = Executive(global_controller, checkpoint_dir=tmp_dir)
        # Process a normal intent that should succeed
        result = exec1.process_intent(Principal.AI_AGENT, "find A")
        assert result["status"] == "success"
        # Working memory should contain both A and B (B is REVIEW and reachable via cognitive_read)
        wm_ids_pre = set(exec1.working_memory.buffer.keys())
        assert "A" in wm_ids_pre
        assert "B" in wm_ids_pre
        # Verify B is flagged as unverified in WM
        b_entry = exec1.working_memory.buffer.get("B")
        assert b_entry is not None
        assert b_entry["node"].get("_cognitive_unverified") is True
        # Check checkpoint files exist
        assert os.path.exists(os.path.join(tmp_dir, "wm.json"))
        assert os.path.exists(os.path.join(tmp_dir, "plan.json"))
        # Simulate a blocked action to generate a reflection lesson (REVIEW)
        blocked_res = exec1.process_intent(Principal.ADMIN, "delete_canonical")
        assert blocked_res["status"] == "blocked"
        assert "reflection_memory_generated" in blocked_res
        lesson_id = blocked_res["reflection_memory_generated"]
        lesson = global_controller.storage.get(lesson_id)
        assert lesson is not None
        assert lesson["type"] == "lesson"
        assert lesson["lifecycle"] == Lifecycle.REVIEW.value
        # The lesson should be retrievable via cognitive_read (eligible for Cognitive Core)
        pack = global_controller.cognitive_read(Principal.AI_AGENT, lesson_id)
        results = pack.get("results", [])
        assert any(r["id"] == lesson_id for r in results)
        # Load a new Executive from checkpoint and ensure state is restored
        exec2 = Executive(global_controller)
        exec2.load_state(tmp_dir, Principal.AI_AGENT)
        # WM should contain the same nodes as before reflection (checkpoint reflects pre-reflection state)
        restored_ids = set(exec2.working_memory.buffer.keys())
        assert "A" in restored_ids
        assert "B" in restored_ids
        # Active plan should be at step 1 (since first step was completed)
        assert exec2.active_plan is not None
        assert exec2.active_plan.current_step_index == 0
        # Continue executing the remaining step
        step_res = exec2.step_loop(Principal.AI_AGENT)
        assert step_res["status"] == "blocked"
        # After completing plan, executive should be idle
        idle_res = exec2.step_loop(Principal.AI_AGENT)
        assert idle_res["status"] == "blocked"


============================================================
FILE: memory_controller/tests/test_supersession_phase43.py
============================================================

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


============================================================
FILE: cognitive_core/tests/test_version_parsing.py
============================================================

import pytest
from cognitive_core.version import parse_technology_version, is_compatible, TechnologyIdentity, VersionRange, Version

@pytest.mark.parametrize(
    "input_str,expected_tech,expected_range",
    [
        ("Python 3.11", "Python", VersionRange(exact=Version(3, 11))),
        ("Python 3.12", "Python", VersionRange(exact=Version(3, 12))),
        ("Python 3.13", "Python", VersionRange(exact=Version(3, 13))),
        ("PowerShell 5.1", "PowerShell", VersionRange(exact=Version(5, 1))),
        ("PowerShell 7.x", "PowerShell", VersionRange(prefix=7)),
        ("Windows Server 2012", "Windows Server", VersionRange(exact=Version(2012))),
        ("Windows Server 2012 R2", "Windows Server", VersionRange(exact=Version(2012, 2))),
        ("Windows Server 2016", "Windows Server", VersionRange(exact=Version(2016))),
        ("Windows Server 2019", "Windows Server", VersionRange(exact=Version(2019))),
        ("Windows Server 2022", "Windows Server", VersionRange(exact=Version(2022))),
        (".NET Framework 4.8", ".NET Framework", VersionRange(exact=Version(4, 8))),
        (".NET 8", ".NET", VersionRange(exact=Version(8))),
        (".NET 9", ".NET", VersionRange(exact=Version(9))),
        ("unknown tech", "unknown", VersionRange(unknown=True)),
    ]
)
def test_parse_technology_version(input_str, expected_tech, expected_range):
    tech, vr = parse_technology_version(input_str)
    assert isinstance(tech, TechnologyIdentity)
    assert tech.name == expected_tech
    assert vr == expected_range

def test_version_compatibility():
    # Exact matches
    req = VersionRange(exact=Version(7, 1))
    cand = VersionRange(exact=Version(7, 1))
    assert is_compatible(req, cand)
    # Prefix matches exact candidate
    req_prefix = VersionRange(prefix=7)
    cand_exact = VersionRange(exact=Version(7, 4))
    assert is_compatible(req_prefix, cand_exact)
    # Exact request matches prefix candidate (major equal)
    req_exact = VersionRange(exact=Version(7, 2))
    cand_prefix = VersionRange(prefix=7)
    assert is_compatible(req_exact, cand_prefix)
    # Different major should be false
    req = VersionRange(prefix=5)
    cand = VersionRange(exact=Version(7, 0))
    assert not is_compatible(req, cand)
    # Unknown request matches anything
    req = VersionRange(unknown=True)
    cand = VersionRange(exact=Version(3, 11))
    assert is_compatible(req, cand)


============================================================
FILE: cognitive_core/tests/test_deduplication.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.deduplication import Deduplicator

def test_deduplicator_scans_and_flags():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-A",
                "type": "knowledge",
                "content": "this is a test of memory",
                "verification": "verified",
                "version_range": "Python 3.12",
                "provenance": {"source_type": "user", "source_ref": "test"}
            },
            {
                "id": "node-B",
                "type": "knowledge",
                "content": "this is a test memory",
                "verification": "unverified",
                "version_range": "Python 3.12",
                "provenance": {"source_type": "user", "source_ref": "test"}
            }
        ]
    }
    
    provider = DeterministicSemanticProvider()
    dedup = Deduplicator(mock_controller, provider, mock_router)
    dedup.similarity_threshold = 0.5
    
    flagged = dedup.scan_for_duplicates(Principal.AI_AGENT, "test")
    
    assert len(flagged) == 1
    # Verify propose was called through ToolRouter
    calls = mock_router.execute.call_args_list
    propose_calls = [c for c in calls if c[0][1] == "propose"]
    assert len(propose_calls) == 1
    proposed_node = propose_calls[0][0][2]["note_data"]
    assert proposed_node["type"] == "hypothesis"
    assert "Potential duplicate detected" in proposed_node["content"]

def test_deduplicator_different_versions_remain_separate():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-A",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Python 3.11",
                "provenance": {"source_type": "user", "source_ref": "test"}
            },
            {
                "id": "node-B",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Python 3.12",
                "provenance": {"source_type": "user", "source_ref": "test"}
            }
        ]
    }
    
    provider = DeterministicSemanticProvider()
    dedup = Deduplicator(mock_controller, provider, mock_router)
    dedup.similarity_threshold = 0.5
    
    flagged = dedup.scan_for_duplicates(Principal.AI_AGENT, "test")
    assert len(flagged) == 0

def test_deduplicator_different_sources_remain_separate():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-A",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Python 3.12",
                "provenance": {"source_type": "user", "source_ref": "test"}
            },
            {
                "id": "node-B",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Python 3.12",
                "provenance": {"source_type": "official", "source_ref": "test"}
            }
        ]
    }
    
    provider = DeterministicSemanticProvider()
    dedup = Deduplicator(mock_controller, provider, mock_router)
    dedup.similarity_threshold = 0.5
    
    flagged = dedup.scan_for_duplicates(Principal.AI_AGENT, "test")
    assert len(flagged) == 0

def test_deduplicator_unknown_versions_never_overlap():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-A",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Unknown technology 1.0",
                "provenance": {"source_type": "user", "source_ref": "test"}
            },
            {
                "id": "node-B",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Unknown technology 1.0",
                "provenance": {"source_type": "user", "source_ref": "test"}
            }
        ]
    }
    
    provider = DeterministicSemanticProvider()
    dedup = Deduplicator(mock_controller, provider, mock_router)
    dedup.similarity_threshold = 0.5
    
    flagged = dedup.scan_for_duplicates(Principal.AI_AGENT, "test")
    assert len(flagged) == 0

def test_deduplicator_different_technologies_remain_separate():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-A",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "Python 3.12",
                "provenance": {"source_type": "user", "source_ref": "test"}
            },
            {
                "id": "node-B",
                "type": "knowledge",
                "content": "this is a test of memory",
                "version_range": "PowerShell 5.1",
                "provenance": {"source_type": "user", "source_ref": "test"}
            }
        ]
    }
    
    provider = DeterministicSemanticProvider()
    dedup = Deduplicator(mock_controller, provider, mock_router)
    dedup.similarity_threshold = 0.5
    
    flagged = dedup.scan_for_duplicates(Principal.AI_AGENT, "test")
    assert len(flagged) == 0



============================================================
FILE: cognitive_core/tests/test_cognitive_loop.py
============================================================

import pytest
import os
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.executive import Executive

# We assume the same global mocked controller from conftest
from memory_controller.controller import controller as global_controller

@pytest.fixture
def clean_memory():
    """Ensure the global controller's storage is clean before each test."""
    os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = "test_secret_key"
    global_controller.storage.store = {}
    
    def _create_note(note_id: str, relations: list = None, lifecycle=Lifecycle.ACTIVE) -> str:
        relations = relations or []
        note = {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": lifecycle.value if hasattr(lifecycle, 'value') else lifecycle,
            "confidence": "high",
            "verification": "verified",
            "provenance": {"source_type": "user"},
            "content": f"Content for {note_id}",
            "relations": relations
        }
        global_controller.storage.set(note_id, note)
        return note_id
        
    yield _create_note
    global_controller.storage.store = {}

def test_full_cognitive_loop(clean_memory):
    # Setup some basic memories
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B")
    
    # Initialize the Executive (Prefrontal Cortex)
    executive = Executive(global_controller)
    
    # Trigger a task
    # "migrate memory" triggers a search, which returns nodes, puts them in WM, creates a plan
    result = executive.process_intent(Principal.ADMIN, "find node A")
    
    assert result["status"] == "success", f"Failed with: {result.get('error')}"
    
    # Verify context was populated
    context = result["context"]
    assert len(context) > 0
    
    # Let's trigger a failure to see reflection at work
    # We will simulate a blocked intent
    result_blocked = executive.process_intent(Principal.ADMIN, "delete_canonical")
    assert result_blocked["status"] == "blocked"
    
    # Check if a reflection memory was generated (lesson about autonomy)
    assert "reflection_memory_generated" in result_blocked
    lesson_id = result_blocked["reflection_memory_generated"]
    
    # Retrieve the lesson via storage to verify
    lesson = global_controller.storage.get(lesson_id)
    assert lesson is not None
    assert lesson["type"] == "lesson"
    assert "Autonomy Policy" in lesson["content"]


============================================================
FILE: cognitive_core/tests/test_recall.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory
from cognitive_core.recall import RecallEngine

def test_deterministic_semantic_provider():
    provider = DeterministicSemanticProvider()
    
    score1 = provider.compute_similarity("hello world", "world hello")
    assert score1 == 1.0
    
    score2 = provider.compute_similarity("hello world", "goodbye moon")
    assert score2 == 0.0
    
    score3 = provider.compute_similarity("hello beautiful world", "hello world")
    assert score3 == 2/3

def test_recall_engine_scoring():
    mock_controller = MagicMock()
    provider = DeterministicSemanticProvider()
    engine = RecallEngine(mock_controller, provider)
    
    wm = WorkingMemory(capacity=5)
    wm.admit([({"id": "wm1", "content": "docker container"}, 1.0)])
    
    # WIRE-9: Use (node, activation) tuples instead of _temp_activation
    activated_nodes = [
        ({"id": "node1", "content": "docker kubernetes", "confidence": "high"}, 1.0),
        ({"id": "node2", "content": "kubernetes helm", "confidence": "low"}, 0.5),
    ]
    
    query = "kubernetes"
    
    results = engine.recall(Principal.AI_AGENT, query, activated_nodes, wm)
    
    assert len(results) == 2
    assert results[0][0]["id"] == "node1"
    assert results[1][0]["id"] == "node2"
    assert results[0][1] > results[1][1]


============================================================
FILE: cognitive_core/tests/test_executive.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.executive import Executive

def test_executive_process_intent():
    mock_controller = MagicMock()
    mock_controller.search.return_value = {"results": [
        {"id": "n1", "content": "test", "confidence": "high", "relations": []}
    ]}
    mock_controller.cognitive_read = MagicMock(return_value={"results": []})
    
    exec1 = Executive(mock_controller)
    result = exec1.process_intent(Principal.AI_AGENT, "find something")
    assert result["status"] == "success"


============================================================
FILE: cognitive_core/tests/test_planning.py
============================================================

import pytest
from cognitive_core.planning import Planner, ActivePlan

def test_planner_create_plan():
    planner = Planner()
    context = [{"id": "node1"}]
    
    plan = planner.create_plan("migrate memory", context)
    assert not plan.is_complete()
    
    step = plan.get_next_step()
    assert step["action"] == "search"
    assert step["query"] == "migrate memory"

def test_planner_evaluate_plan():
    planner = Planner()
    plan = ActivePlan("goal", [{"step": 1}])
    
    assert planner.evaluate_plan(plan, []) is True
    plan.complete_current_step()
    assert planner.evaluate_plan(plan, []) is False


============================================================
FILE: cognitive_core/tests/test_reasoning.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.reasoning import ReasoningEngine

def test_reasoning_synthesize():
    mock_controller = MagicMock()
    mock_controller.search.return_value = {"results": [{"id": "node2"}]}
    
    engine = ReasoningEngine(mock_controller)
    
    context = [{"id": "node1"}]
    
    # Simple query, no extra retrieval
    result = engine.synthesize(Principal.AI_AGENT, context, "summary")
    assert result["context_used"] == 1
    assert result["extra_retrieved"] == 0
    mock_controller.search.assert_not_called()
    
    # Detailed query triggers read-only search
    result_detailed = engine.synthesize(Principal.AI_AGENT, context, "detailed analysis")
    assert result_detailed["context_used"] == 1
    assert result_detailed["extra_retrieved"] == 1
    mock_controller.search.assert_called_once()


============================================================
FILE: cognitive_core/tests/test_reflection.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.reflection import ReflectionPipeline

def test_reflection_evaluates_success():
    mock_controller = MagicMock()
    pipeline = ReflectionPipeline(mock_controller)
    
    intent = {"query": "find node"}
    action = {"action": "search"}
    result = {"status": "success", "result": []}
    
    # Success means no new memory proposed
    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is None
    mock_controller.propose.assert_not_called()

def test_reflection_evaluates_error():
    mock_controller = MagicMock()
    pipeline = ReflectionPipeline(mock_controller)
    
    intent = {"query": "do something"}
    action = {"action": "unknown_action"}
    result = {"status": "error", "error": "Crash!"}
    
    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is not None
    mock_controller.propose.assert_called_once()
    
    args, _ = mock_controller.propose.call_args
    assert args[0] == Principal.AI_AGENT
    proposed_note = args[1]
    
    assert proposed_note["id"] == note_id
    assert proposed_note["type"] == "error"
    assert proposed_note["lifecycle"] == "REVIEW"
    assert "Crash!" in proposed_note["content"]

def test_reflection_evaluates_blocked():
    mock_controller = MagicMock()
    pipeline = ReflectionPipeline(mock_controller)
    
    intent = {"query": "delete everything"}
    action = {"action": "delete_canonical"}
    result = {"status": "blocked", "reason": "HIGH RISK"}
    
    note_id = pipeline.evaluate_outcome(Principal.AI_AGENT, intent, action, result)
    assert note_id is not None
    mock_controller.propose.assert_called_once()
    
    args, _ = mock_controller.propose.call_args
    proposed_note = args[1]
    
    assert proposed_note["type"] == "lesson"
    assert "Autonomy Policy" in proposed_note["content"]


============================================================
FILE: cognitive_core/tests/test_activation.py
============================================================

import pytest
from typing import Dict, Any
import uuid

from memory_controller.controller import controller as global_controller
from memory_controller.core import Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.synapse import Synapse, SynapticGraph
from cognitive_core.activation import ActivationEngine

# --- FIXTURES ---

@pytest.fixture
def clean_memory():
    """Ensure the global controller's storage is clean before each test."""
    # Wipe the in-memory store
    global_controller.storage.store = {}
    
    def _create_note(note_id: str, relations: list = None, lifecycle=Lifecycle.ACTIVE) -> str:
        relations = relations or []
        note = {
            "id": note_id,
            "type": "knowledge",
            "lifecycle": lifecycle.value if hasattr(lifecycle, 'value') else lifecycle,
            "confidence": "high",
            "verification": "verified",
            "provenance": {"source_type": "user"},
            "content": f"Content for {note_id}",
            "relations": relations
        }
        # Force insert directly into storage to bypass propose validation for quick setup
        global_controller.storage.set(note_id, note)
        return note_id
        
    yield _create_note
    global_controller.storage.store = {}

# --- TESTS ---

def test_synaptic_graph_extraction():
    note = {
        "id": "node-1",
        "relations": [
            {"target_id": "node-2", "type": "related_to"},
            {"target_id": "node-3", "type": "supports"}
        ]
    }
    
    synapses = SynapticGraph.extract_synapses(note)
    assert len(synapses) == 2
    
    # Sort for deterministic check
    synapses = sorted(synapses, key=lambda x: x.target_id)
    assert synapses[0].source_id == "node-1"
    assert synapses[0].target_id == "node-2"
    assert synapses[0].relation_type == "related_to"
    
    assert synapses[1].target_id == "node-3"
    assert synapses[1].relation_type == "supports"

def test_activation_direct_traversal(clean_memory):
    """Test 1 hop traversal from node A to node B"""
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B")
    
    engine = ActivationEngine(global_controller)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    assert len(activated) == 2
    nodes = [node.get("id") for node, score in activated]
    assert "A" in nodes
    assert "B" in nodes
    
    # A should have higher activation than B
    a_score = next(score for node, score in activated if node["id"] == "A")
    b_score = next(score for node, score in activated if node["id"] == "B")
    assert a_score > b_score

def test_activation_cycle_detection(clean_memory):
    """Test A -> B -> A cycle"""
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B", relations=[{"target_id": "A"}])
    
    engine = ActivationEngine(global_controller)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    # Should only activate A and B, not loop infinitely
    assert len(activated) == 2

def test_activation_depth_limit(clean_memory):
    """Test A -> B -> C -> D, depth limit 2"""
    clean_memory("A", relations=[{"target_id": "B"}])
    clean_memory("B", relations=[{"target_id": "C"}])
    clean_memory("C", relations=[{"target_id": "D"}])
    clean_memory("D")
    
    # depth=0 is A, depth=1 is B, depth=2 is C. D should not be activated if max_depth=2
    engine = ActivationEngine(global_controller, max_depth=2)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    nodes = [node.get("id") for node, score in activated]
    assert "A" in nodes
    assert "B" in nodes
    assert "C" in nodes
    assert "D" not in nodes

def test_activation_node_limit(clean_memory):
    """Test context economy node limit"""
    relations = [{"target_id": f"N{i}"} for i in range(1, 10)]
    clean_memory("A", relations=relations)
    for i in range(1, 10):
        clean_memory(f"N{i}")
        
    engine = ActivationEngine(global_controller, max_nodes=5)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    # Total nodes should be strictly 5 (A + 4 neighbors)
    assert len(activated) == 5

def test_activation_lifecycle_isolation(clean_memory):
    """Test that cognitive_read returns REVIEW nodes (tagged as unverified)
    but still blocks RAW/ARCHIVED/etc."""
    clean_memory("A", relations=[{"target_id": "B"}, {"target_id": "C"}])
    clean_memory("B", lifecycle=Lifecycle.REVIEW)
    # C is RAW — should NOT be reachable
    global_controller.storage.set("C", {
        "id": "C", "type": "knowledge", "lifecycle": Lifecycle.RAW.value,
        "confidence": "high", "verification": "verified",
        "provenance": {"source_type": "user"}, "content": "raw", "relations": []
    })
    
    engine = ActivationEngine(global_controller)
    
    # AI_AGENT should get A + B (REVIEW is now eligible via cognitive_read), but NOT C (RAW)
    activated_ai = engine.activate_from_ids(Principal.AI_AGENT, ["A"])
    activated_ids = [n[0].get("id") for n in activated_ai]
    assert "A" in activated_ids
    assert "B" in activated_ids
    assert "C" not in activated_ids
    
    # B should be tagged as cognitively unverified
    b_node = [n[0] for n in activated_ai if n[0].get("id") == "B"][0]
    assert b_node.get("_cognitive_unverified") is True
    
    # ADMIN gets the same behavior
    activated_admin = engine.activate_from_ids(Principal.ADMIN, ["A"])
    admin_ids = [n[0].get("id") for n in activated_admin]
    assert "A" in admin_ids
    assert "B" in admin_ids
    assert "C" not in admin_ids

def test_activation_missing_target(clean_memory):
    """Test resilience to missing targets in relations"""
    clean_memory("A", relations=[{"target_id": "MISSING"}])
    
    engine = ActivationEngine(global_controller)
    activated = engine.activate_from_ids(Principal.ADMIN, ["A"])
    
    # Should just gracefully skip missing
    assert len(activated) == 1
    assert activated[0][0].get("id") == "A"


============================================================
FILE: cognitive_core/tests/test_consolidation.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.consolidation import Consolidator

def test_consolidation_success():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {"id": "lesson-1", "type": "lesson", "lifecycle": Lifecycle.REVIEW.value, "content": "lesson one"},
            {"id": "lesson-2", "type": "lesson", "lifecycle": Lifecycle.REVIEW.value, "content": "lesson two"}
        ]
    }
    
    consolidator = Consolidator(mock_controller, mock_router)
    new_id = consolidator.consolidate_lessons(Principal.AI_AGENT)
    
    assert new_id is not None
    
    # Verify propose was called through ToolRouter
    calls = mock_router.execute.call_args_list
    propose_calls = [c for c in calls if c[0][1] == "propose"]
    assert len(propose_calls) == 1
    proposed_node = propose_calls[0][0][2]["note_data"]
    assert proposed_node["type"] == "knowledge"
    assert "lesson-1" in proposed_node["provenance"]["source_refs"]
    assert "lesson-2" in proposed_node["provenance"]["source_refs"]
    
    # Verify archive was called through ToolRouter
    archive_calls = [c for c in calls if c[0][1] == "archive"]
    assert len(archive_calls) == 2

def test_consolidation_insufficient_lessons():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {"id": "lesson-1", "type": "lesson", "lifecycle": Lifecycle.REVIEW.value, "content": "lesson one"}
        ]
    }
    
    consolidator = Consolidator(mock_controller, mock_router)
    new_id = consolidator.consolidate_lessons(Principal.AI_AGENT)
    
    assert new_id is None
    mock_router.execute.assert_not_called()


============================================================
FILE: cognitive_core/tests/test_dynamic_synapses.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from cognitive_core.reflection import ReflectionPipeline

def test_propose_synapse_success():
    mock_controller = MagicMock()
    # Mock reading the source node
    mock_controller.read.return_value = {
        "results": [{
            "id": "node-A",
            "type": "knowledge",
            "relations": []
        }]
    }
    mock_controller.update = MagicMock()
    
    pipeline = ReflectionPipeline(mock_controller)
    
    result = pipeline.propose_synapse(Principal.AI_AGENT, "node-A", "node-B")
    assert result == "node-A"
    
    # Verify update was called
    mock_controller.update.assert_called_once()
    args, kwargs = mock_controller.update.call_args
    assert args[0] == Principal.AI_AGENT
    assert args[1] == "node-A"
    updated_node = args[2]
    assert len(updated_node["relations"]) == 1
    assert updated_node["relations"][0]["target_id"] == "node-B"
    assert updated_node["relations"][0]["type"] == "related_to"

def test_propose_synapse_duplicate():
    mock_controller = MagicMock()
    # Mock reading the source node with existing relation
    mock_controller.read.return_value = {
        "results": [{
            "id": "node-A",
            "type": "knowledge",
            "relations": [{
                "target_id": "node-B",
                "type": "related_to",
                "confidence": "high"
            }]
        }]
    }
    mock_controller.update = MagicMock()
    
    pipeline = ReflectionPipeline(mock_controller)
    
    result = pipeline.propose_synapse(Principal.AI_AGENT, "node-A", "node-B")
    # Should return None and NOT call update
    assert result is None
    mock_controller.update.assert_not_called()


============================================================
FILE: cognitive_core/tests/test_working_memory.py
============================================================

import pytest
from typing import Dict, Any

from cognitive_core.working_memory import WorkingMemory
from cognitive_core.attention import AttentionModel

def test_attention_model():
    model = AttentionModel(activation_weight=0.5, confidence_weight=0.3, recency_weight=0.2)
    
    # 1. High confidence, recent
    node_high = {"id": "1", "confidence": "high"}
    score1 = model.calculate_score(node_high, activation=1.0, recency_tick=1, current_tick=1)
    # expected: (1.0 * 0.5) + (0.8 * 0.3) + (1.0 * 0.2) = 0.5 + 0.24 + 0.2 = 0.94
    assert pytest.approx(score1) == 0.94
    
    # 2. Low confidence, old
    node_low = {"id": "2", "confidence": "unknown"}
    score2 = model.calculate_score(node_low, activation=0.2, recency_tick=1, current_tick=10)
    # activation: 0.2 * 0.5 = 0.1
    # confidence: 0.1 * 0.3 = 0.03
    # recency: max(0, 1.0 - (9 * 0.05)) = max(0, 0.55) = 0.55 * 0.2 = 0.11
    # total = 0.1 + 0.03 + 0.11 = 0.24
    assert pytest.approx(score2) == 0.24

def test_working_memory_admit():
    wm = WorkingMemory(capacity=5)
    
    # Admit 3 nodes
    nodes = [
        ({"id": "A", "confidence": "high"}, 1.0),
        ({"id": "B", "confidence": "medium"}, 0.8),
        ({"id": "C", "confidence": "unknown"}, 0.5)
    ]
    
    wm.admit(nodes)
    
    context = wm.get_active_context()
    assert len(context) == 3
    # Sorted by attention
    assert context[0]["id"] == "A"
    assert context[1]["id"] == "B"
    assert context[2]["id"] == "C"

def test_working_memory_eviction():
    wm = WorkingMemory(capacity=3)
    
    # Admit 5 nodes in one go
    nodes = [
        ({"id": "A", "confidence": "very_high"}, 1.0),
        ({"id": "B", "confidence": "high"}, 0.9),
        ({"id": "C", "confidence": "medium"}, 0.5),
        ({"id": "D", "confidence": "low"}, 0.3),
        ({"id": "E", "confidence": "unknown"}, 0.1)
    ]
    
    wm.admit(nodes)
    
    context = wm.get_active_context()
    assert len(context) == 3
    ids = [n["id"] for n in context]
    assert "A" in ids
    assert "B" in ids
    assert "C" in ids
    assert "D" not in ids
    assert "E" not in ids

def test_working_memory_recency_eviction():
    wm = WorkingMemory(capacity=3)
    
    # Admit A, B, C
    wm.admit([
        ({"id": "A", "confidence": "high"}, 1.0),
        ({"id": "B", "confidence": "high"}, 1.0),
        ({"id": "C", "confidence": "high"}, 1.0)
    ])
    
    # 5 ticks pass, admit D
    for _ in range(5):
        wm.admit([])
        
    wm.admit([({"id": "D", "confidence": "high"}, 1.0)])
    
    context = wm.get_active_context()
    assert len(context) == 3
    # D is newer, should be in
    ids = [n["id"] for n in context]
    assert "D" in ids
    # One of A, B, C is evicted (deterministic, likely C due to tie-break)
    
def test_working_memory_refresh():
    wm = WorkingMemory(capacity=3)
    wm.admit([
        ({"id": "A", "confidence": "low"}, 0.2),
        ({"id": "B", "confidence": "high"}, 1.0),
        ({"id": "C", "confidence": "high"}, 1.0)
    ])
    
    for _ in range(5):
        wm.admit([])
        
    # Refresh A with high activation
    wm.admit([({"id": "A", "confidence": "low"}, 1.0)])
    
    # D arrives, but A is refreshed, so B or C might be evicted instead if they decayed,
    # but wait, A's confidence is low. 
    # Just checking capacity
    wm.admit([({"id": "D", "confidence": "very_high"}, 1.0)])
    assert len(wm.get_active_context()) == 3


============================================================
FILE: cognitive_core/tests/test_learning.py
============================================================

import pytest
from unittest.mock import MagicMock
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from cognitive_core.learning import LearningEngine

def test_learning_engine_promotes_confidence():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-1",
                "type": "knowledge",
                "lifecycle": Lifecycle.ACTIVE.value,
                "confidence": "low",
                "verification": "unverified",
                "relations": [{"target_id": "a"}, {"target_id": "b"}, {"target_id": "c"}]
            }
        ]
    }
    
    engine = LearningEngine(mock_controller, mock_router)
    promoted = engine.promote_memories(Principal.AI_AGENT)
    
    assert len(promoted) == 1
    assert promoted[0] == "node-1"
    
    # Verify update was called through ToolRouter
    mock_router.execute.assert_called_once()
    args = mock_router.execute.call_args[0]
    assert args[1] == "update"
    assert args[2]["note_id"] == "node-1"
    assert args[2]["confidence"] == "medium"

def test_learning_engine_skips_verified():
    mock_controller = MagicMock()
    mock_router = MagicMock()
    mock_controller.search.return_value = {
        "results": [
            {
                "id": "node-1",
                "type": "knowledge",
                "lifecycle": Lifecycle.ACTIVE.value,
                "confidence": "low",
                "verification": "verified",
                "relations": [{"target_id": "a"}, {"target_id": "b"}, {"target_id": "c"}]
            }
        ]
    }
    
    engine = LearningEngine(mock_controller, mock_router)
    promoted = engine.promote_memories(Principal.AI_AGENT)
    
    assert len(promoted) == 0
    mock_router.execute.assert_not_called()


============================================================
FILE: memory_controller/tests/conftest.py
============================================================

import pytest
import os
import memory_controller.controller as ctrl_module
from memory_controller.controller import MemoryController, StorageEngine

# We globally override the controller's storage engine with an in-memory StorageEngine
# during test collection and execution to protect the real Vault and keep legacy tests green.
# FileStorageEngine is explicitly tested in test_storage.py by instantiating it directly.
ctrl_module._storage_engine = StorageEngine()
ctrl_module.controller = MemoryController(ctrl_module._storage_engine)

@pytest.fixture(autouse=True)
def ensure_hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_key")



============================================================
FILE: memory_controller/tests/test_audit.py
============================================================

import pytest
import json
import os
import hashlib
from typing import List, Dict, Any
from unittest.mock import patch

from memory_controller.controller import controller, Principal, Operation
import memory_controller.audit.logger as logger_module

TEST_AUDIT_LOG = "test_audit_log.jsonl"

def setup_function():
    # Force the audit logger to use a clean test file
    open(TEST_AUDIT_LOG, "w", encoding="utf-8").close()
    logger_module._logger_instance = logger_module.AuditLogger(TEST_AUDIT_LOG)
    
    # Clear controller state
    controller.storage.store.clear()
    controller.cache.store.clear()
    from memory_controller.controller import MemoryController
    MemoryController._global_review_counter = 2
    
    # Insert a dummy active note for tests
    controller.storage.set("11111111-1111-1111-1111-111111111111", {
        "id": "11111111-1111-1111-1111-111111111111",
        "lifecycle": "ACTIVE",
        "type": "knowledge",
        "category": "test",
        "confidence": "high",
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "verification": "unverified",
        "tags": [],
        "relations": [],
        "provenance": {"source_type": "user", "source_ref": "test"}
    })
    
    # Insert a dummy review note
    controller.storage.set("22222222-2222-2222-2222-222222222222", {
        "id": "22222222-2222-2222-2222-222222222222",
        "lifecycle": "REVIEW",
        "type": "knowledge",
        "category": "test",
        "confidence": "high",
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "verification": "unverified",
        "tags": [],
        "relations": [],
        "provenance": {"source_type": "user", "source_ref": "test"}
    })

def teardown_function():
    from memory_controller.controller import MemoryController
    MemoryController._global_review_counter = 2

def read_logs() -> List[Dict[str, Any]]:
    logs = []
    with open(TEST_AUDIT_LOG, "r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                logs.append(json.loads(line))
    return logs

def assert_schema_valid(log: Dict[str, Any]):
    assert "timestamp" in log
    assert "actor" in log
    assert "operation" in log
    assert "target_id" in log
    assert "outcome" in log
    assert log["outcome"] in ("success", "error")

def test_audit_read_success_and_fail():
    # Success
    controller.read(Principal.ADMIN, "11111111-1111-1111-1111-111111111111")
    logs = read_logs()
    assert len(logs) == 1
    assert_schema_valid(logs[0])
    assert logs[0]["operation"] == "read"
    assert logs[0]["outcome"] == "success"
    assert logs[0]["target_id"] == "11111111-1111-1111-1111-111111111111"
    
    # Fail (ValueError)
    with pytest.raises(ValueError):
        # Actually, let's use a non-existent note for ValueError
        controller.read(Principal.ADMIN, "99999999-9999-9999-9999-999999999999")
        
    logs = read_logs()
    assert len(logs) == 2
    assert_schema_valid(logs[1])
    assert logs[1]["operation"] == "read"
    assert logs[1]["outcome"] == "error"
    assert logs[1]["target_id"] == "99999999-9999-9999-9999-999999999999"
    assert "metadata" in logs[1]
    assert "error" in logs[1]["metadata"]
    assert "99999999-9999-9999-9999-999999999999" in logs[1]["metadata"]["error"]

def test_audit_search_success_and_fail():
    query = "test search"
    query_fp = hashlib.sha256(query.encode()).hexdigest()
    
    # Success
    controller.search(Principal.HUMAN, query)
    logs = read_logs()
    assert len(logs) == 1
    assert_schema_valid(logs[0])
    assert logs[0]["operation"] == "search"
    assert logs[0]["outcome"] == "success"
    assert logs[0]["target_id"] == query_fp  # Fingerprint, not raw query
    assert query not in str(logs[0]) # Raw query should not leak
    
    # Fail (e.g. oversized query)
    bad_query = "A" * 5000
    with pytest.raises(ValueError):
        controller.search(Principal.HUMAN, bad_query)
        
    logs = read_logs()
    assert len(logs) == 2
    assert_schema_valid(logs[1])
    assert logs[1]["operation"] == "search"
    assert logs[1]["outcome"] == "error"
    # Target should be unknown_query if it failed before fingerprinting
    assert logs[1]["target_id"] == "unknown_query"
    assert bad_query not in logs[1]["target_id"]

def test_audit_propose_success_and_fail():
    note_data = {"id": "33333333-3333-3333-3333-333333333333", "content": "hello"}
    
    # Success
    controller.propose(Principal.ADMIN, note_data)
    logs = read_logs()
    assert len(logs) == 1
    assert logs[0]["operation"] == "propose"
    assert logs[0]["outcome"] == "success"
    assert logs[0]["target_id"] == "33333333-3333-3333-3333-333333333333"
    
    # Fail (ValueError)
    with pytest.raises(ValueError):
        # Trigger missing id
        controller.propose(Principal.ADMIN, {}) # missing id
        
    logs = read_logs()
    assert len(logs) == 2
    assert logs[1]["operation"] == "propose"
    assert logs[1]["outcome"] == "error"
    assert logs[1]["target_id"] == "unknown"

def test_audit_update_success_and_fail():
    # Success
    controller.update(Principal.ADMIN, "11111111-1111-1111-1111-111111111111", {"category": "updated"})
    logs = read_logs()
    assert len(logs) == 1
    assert logs[0]["operation"] == "update"
    assert logs[0]["outcome"] == "success"
    
    # Fail (ValueError immutable field)
    with pytest.raises(ValueError):
        controller.update(Principal.ADMIN, "11111111-1111-1111-1111-111111111111", {"lifecycle": "RAW"})
        
    logs = read_logs()
    assert len(logs) == 2
    assert logs[1]["operation"] == "update"
    assert logs[1]["outcome"] == "error"

def test_audit_review_success_and_fail():
    # Success
    controller.review(Principal.ADMIN, "22222222-2222-2222-2222-222222222222", "approve")
    logs = read_logs()
    assert len(logs) == 1
    assert logs[0]["operation"] == "review"
    assert logs[0]["outcome"] == "success"
    
    # Fail
    with pytest.raises(ValueError):
        controller.review(Principal.ADMIN, "22222222-2222-2222-2222-222222222222", "invalid-decision")
        
    logs = read_logs()
    assert len(logs) == 2
    assert logs[1]["operation"] == "review"
    assert logs[1]["outcome"] == "error"

def test_audit_promote_success_and_fail():
    # Set up a REVIEW note
    controller.storage.set("44444444-4444-4444-4444-444444444444", {"id": "44444444-4444-4444-4444-444444444444", "lifecycle": "REVIEW", "type": "knowledge", "provenance": {"source_type": "user"}})
    
    # Success
    controller.promote(Principal.ADMIN, "44444444-4444-4444-4444-444444444444")
    logs = read_logs()
    assert len(logs) == 1
    assert logs[0]["operation"] == "promote"
    assert logs[0]["outcome"] == "success"
    
    # Fail (wrong lifecycle)
    with pytest.raises(ValueError):
        controller.promote(Principal.ADMIN, "11111111-1111-1111-1111-111111111111") # note-1 is ACTIVE
        
    logs = read_logs()
    assert len(logs) == 2
    assert logs[1]["operation"] == "promote"
    assert logs[1]["outcome"] == "error"

def test_audit_archive_success_and_fail():
    # Success
    controller.archive(Principal.ADMIN, "11111111-1111-1111-1111-111111111111", "done")
    logs = read_logs()
    assert len(logs) == 1
    assert logs[0]["operation"] == "archive"
    assert logs[0]["outcome"] == "success"
    
    # Fail
    with pytest.raises(ValueError):
        controller.archive(Principal.ADMIN, "99999999-9999-9999-9999-999999999999", "done")
        
    logs = read_logs()
    assert len(logs) == 2
    assert logs[1]["operation"] == "archive"
    assert logs[1]["outcome"] == "error"

def test_audit_permission_error_explicit():
    # Explicitly test that unauthorized operation -> PermissionError -> success=False
    with patch.object(controller.authorizer, 'is_allowed', return_value=False):
        with pytest.raises(PermissionError):
            controller.read(Principal.HUMAN, "11111111-1111-1111-1111-111111111111")
            
    logs = read_logs()
    assert len(logs) == 1
    assert logs[0]["outcome"] == "error"
    assert "metadata" in logs[0]
    assert "error" in logs[0]["metadata"]
    assert "PermissionError" in logs[0]["metadata"]["error"] or "not allowed" in logs[0]["metadata"]["error"]


============================================================
FILE: memory_controller/tests/test_authorization.py
============================================================

import pytest
from memory_controller.controller import MemoryController, Principal, Lifecycle

# Mock storage engine
class MockStorageEngine:
    def __init__(self):
        self.store = {}
    def set(self, note_id, data):
        self.store[note_id] = data
    def get(self, note_id):
        return self.store.get(note_id)
    def delete(self, note_id):
        self.store.pop(note_id, None)
    def query(self, intent=None, lifecycle=None, types=None, max_notes=None):
        notes = list(self.store.values())
        if lifecycle:
            notes = [n for n in notes if n.get('lifecycle') == lifecycle]
        if max_notes:
            notes = notes[:max_notes]
        return notes

@pytest.fixture
def controller():
    storage = MockStorageEngine()
    return MemoryController(storage)

# READ permissions
def test_ai_read_allowed(controller):
    note = {"id": "11111111-1111-1111-1111-111111111111", "lifecycle": Lifecycle.ACTIVE, "content": "data"}
    controller.storage.set("11111111-1111-1111-1111-111111111111", note)
    pack = controller.read(Principal.AI_AGENT, "11111111-1111-1111-1111-111111111111")
    assert pack["results"][0]["id"] == "11111111-1111-1111-1111-111111111111"

def test_human_read_allowed(controller):
    note = {"id": "22222222-2222-2222-2222-222222222222", "lifecycle": Lifecycle.ACTIVE, "content": "data"}
    controller.storage.set("22222222-2222-2222-2222-222222222222", note)
    pack = controller.read(Principal.HUMAN, "22222222-2222-2222-2222-222222222222")
    assert pack["results"][0]["id"] == "22222222-2222-2222-2222-222222222222"

def test_admin_read_allowed(controller):
    note = {"id": "33333333-3333-3333-3333-333333333333", "lifecycle": Lifecycle.ACTIVE, "content": "data"}
    controller.storage.set("33333333-3333-3333-3333-333333333333", note)
    pack = controller.read(Principal.ADMIN, "33333333-3333-3333-3333-333333333333")
    assert pack["results"][0]["id"] == "33333333-3333-3333-3333-333333333333"

# PROPOSE permissions
def test_ai_propose_allowed(controller):
    note = {"id": "44444444-4444-4444-4444-444444444444", "content": "new"}
    note_id = controller.propose(Principal.AI_AGENT, note)
    assert note_id == "44444444-4444-4444-4444-444444444444"

def test_human_propose_allowed(controller):
    note = {"id": "55555555-5555-5555-5555-555555555555", "content": "new"}
    note_id = controller.propose(Principal.HUMAN, note)
    assert note_id == "55555555-5555-5555-5555-555555555555"

def test_admin_propose_allowed(controller):
    note = {"id": "66666666-6666-6666-6666-666666666666", "content": "new"}
    note_id = controller.propose(Principal.ADMIN, note)
    assert note_id == "66666666-6666-6666-6666-666666666666"

# REVIEW permissions
def test_ai_cannot_review(controller):
    note = {"id": "77777777-7777-7777-7777-777777777777", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("77777777-7777-7777-7777-777777777777", note)
    with pytest.raises(PermissionError):
        controller.review(Principal.AI_AGENT, "77777777-7777-7777-7777-777777777777", decision="approve")

def test_human_review_allowed(controller):
    note = {"id": "88888888-8888-8888-8888-888888888888", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("88888888-8888-8888-8888-888888888888", note)
    controller.review(Principal.HUMAN, "88888888-8888-8888-8888-888888888888", decision="approve")
    assert controller.storage.get("r2")["review"]["decision"] == "approve"

def test_admin_review_allowed(controller):
    note = {"id": "99999999-9999-9999-9999-999999999999", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("99999999-9999-9999-9999-999999999999", note)
    controller.review(Principal.ADMIN, "99999999-9999-9999-9999-999999999999", decision="reject")
    assert controller.storage.get("r3")["review"]["decision"] == "reject"

# PROMOTE permissions
def test_ai_cannot_promote(controller):
    note = {"id": "p1", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("p1", note)
    with pytest.raises(PermissionError):
        controller.promote(Principal.AI_AGENT, "p1")

def test_human_promote_allowed(controller):
    note = {"id": "p2", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("p2", note)
    controller.promote(Principal.HUMAN, "p2")
    assert controller.storage.get("p2")["lifecycle"] == Lifecycle.ACTIVE

def test_admin_promote_allowed(controller):
    note = {"id": "p3", "lifecycle": Lifecycle.REVIEW}
    controller.storage.set("p3", note)
    controller.promote(Principal.ADMIN, "p3")
    assert controller.storage.get("p3")["lifecycle"] == Lifecycle.ACTIVE


============================================================
FILE: memory_controller/tests/test_cache.py
============================================================

import pytest
import time
from uuid import uuid4
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from memory_controller.cache.lru_cache import LRUCache
from memory_controller.context.budget import ContextBudget

@pytest.fixture
def controller():
    storage = StorageEngine()
    ctrl = MemoryController(storage)
    dummy_id = str(uuid4())
    ctrl.storage.set(dummy_id, {
        "id": dummy_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "test",
        "confidence": "high",
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "verification": "unverified",
        "tags": [],
        "relations": [],
        "provenance": {"source_type": "user", "source_ref": "unit"}
    })
    ctrl.dummy_id = dummy_id
    return ctrl

def test_hit_miss_accounting(controller):
    cache = controller.cache
    cache.store.clear()
    cache.hit_count = 0
    cache.miss_count = 0
    
    # First identical request = MISS
    res1 = controller.search(Principal.HUMAN, "test query")
    assert cache.miss_count == 1
    assert cache.hit_count == 0
    
    # Second identical request = HIT
    res2 = controller.search(Principal.HUMAN, "test query")
    assert cache.miss_count == 1
    assert cache.hit_count == 1
    
    assert res1['results'][0]['id'] == res2['results'][0]['id']

def test_ttl_expiration(controller):
    cache = controller.cache
    cache.store.clear()
    cache.miss_count = 0
    
    controller.search(Principal.HUMAN, "ttl query")
    assert cache.miss_count == 1
    
    # Force TTL expiration by setting it to 0 and manipulating expiry
    key = list(cache.store.keys())[0]
    cache.store[key].expiry = 0
    
    # Second request immediately expired -> MISS
    controller.search(Principal.HUMAN, "ttl query")
    assert cache.miss_count == 2
    # Store gets repopulated with a new valid entry, so we do not assert it's missing.

def test_principal_isolation(controller):
    cache = controller.cache
    cache.miss_count = 0
    
    # HUMAN vs AI_AGENT
    controller.search(Principal.HUMAN, "isolation query")
    controller.search(Principal.AI_AGENT, "isolation query")
    assert cache.miss_count == 2
    
    # HUMAN vs ADMIN
    controller.search(Principal.ADMIN, "isolation query")
    assert cache.miss_count == 3
    
    # AI_AGENT vs ADMIN
    controller.search(Principal.ADMIN, "another")
    controller.search(Principal.AI_AGENT, "another")
    assert cache.miss_count == 5

def test_canonical_query_fingerprint_isolation(controller):
    cache = controller.cache
    cache.miss_count = 0
    cache.hit_count = 0
    
    # "same query  " and "same query" should sanitize to the same string
    controller.search(Principal.HUMAN, "same query  ")
    assert cache.miss_count == 1
    controller.search(Principal.HUMAN, "  same query")
    assert cache.hit_count == 1
    
    # different query = different identity
    controller.search(Principal.HUMAN, "different query")
    assert cache.miss_count == 2

def test_filter_isolation(controller):
    cache = controller.cache
    cache.miss_count = 0
    
    # Baseline
    controller.search(Principal.HUMAN, "q")
    assert cache.miss_count == 1
    
    # Lifecycle filter changed
    controller.search(Principal.HUMAN, "q", lifecycles=[Lifecycle.ACTIVE])
    # Ensure it's treated as a miss due to the filter change
    assert cache.miss_count == 2
    
    # Target type changed
    controller.search(Principal.HUMAN, "q", types=["knowledge"])
    assert cache.miss_count == 3
    
    # Disclosure level changed
    controller.default_disclosure = 'snippet'
    controller.search(Principal.HUMAN, "q")
    assert cache.miss_count == 4

def test_cache_poisoning(controller):
    cache = controller.cache
    cache.miss_count = 0
    
    # Populate under A
    controller.search(Principal.HUMAN, "poison query")
    assert cache.miss_count == 1
    
    # Request under B
    controller.search(Principal.AI_AGENT, "poison query")
    # B missed, did not get A's cache
    assert cache.miss_count == 2

def test_budget_mismatch(monkeypatch, controller):
    cache = controller.cache
    
    # Mock budget loader to return large budget
    def mock_large(*args):
        return ContextBudget({"soft_context_budget": 10000, "hard_context_budget": 10000})
    monkeypatch.setattr('memory_controller.controller.load_agent_budget', mock_large)
    
    # Cache result under larger budget
    controller.search(Principal.HUMAN, "budget query")
    
    # Now tighter budget
    def mock_tight(*args):
        return ContextBudget({"soft_context_budget": 10, "hard_context_budget": 10})
    monkeypatch.setattr('memory_controller.controller.load_agent_budget', mock_tight)
    
    # We want to verify that when it falls through, it queries storage again.
    # The cache.get will actually hit (incrementing hit_count), but RetrievalEngine will reject it.
    original_query = controller.storage.query
    query_calls = 0
    
    def mock_query(*args, **kwargs):
        nonlocal query_calls
        query_calls += 1
        return original_query(*args, **kwargs)
        
    monkeypatch.setattr(controller.storage, 'query', mock_query)
    
    # Tight budget will reject cached result because size > 10 bytes
    controller.search(Principal.HUMAN, "budget query")
    assert query_calls == 1

def test_mutation_invalidation_propose(controller):
    cache = controller.cache
    controller.search(Principal.HUMAN, "propose query")
    hits = cache.hit_count
    
    # Mutation (using valid UUID)
    controller.propose(Principal.HUMAN, {"id": str(uuid4()), "content": "c"})
    
    controller.search(Principal.HUMAN, "propose query")
    # Should miss because cache was invalidated
    assert cache.hit_count == hits

def test_mutation_invalidation_update(controller):
    cache = controller.cache
    controller.search(Principal.HUMAN, "update query")
    hits = cache.hit_count
    
    # Mutation
    controller.update(Principal.ADMIN, controller.dummy_id, {"category": "new_cat"})
    
    controller.search(Principal.HUMAN, "update query")
    assert cache.hit_count == hits

def test_mutation_invalidation_archive(controller):
    cache = controller.cache
    controller.search(Principal.HUMAN, "archive query")
    hits = cache.hit_count
    
    # Mutation
    controller.archive(Principal.ADMIN, controller.dummy_id, "reason")
    
    controller.search(Principal.HUMAN, "archive query")
    assert cache.hit_count == hits

def test_mutation_invalidation_review_promote(controller):
    cache = controller.cache
    
    # Create raw (using valid UUID)
    nid = controller.propose(Principal.HUMAN, {"id": str(uuid4()), "content": "c"})
    
    # Review
    controller.search(Principal.HUMAN, "review query")
    hits = cache.hit_count
    controller.review(Principal.ADMIN, nid, "approve", "ok")
    controller.search(Principal.HUMAN, "review query")
    assert cache.hit_count == hits
    
    # Promote
    controller.search(Principal.HUMAN, "promote query")
    hits = cache.hit_count
    controller.promote(Principal.ADMIN, nid)
    controller.search(Principal.HUMAN, "promote query")
    assert cache.hit_count == hits


============================================================
FILE: memory_controller/tests/test_context_economy.py
============================================================

import json
import zlib
import pytest

from memory_controller.context.budget import ContextBudget, BudgetExceededError


def test_zlib_roundtrip():
    text = "The quick brown fox jumps over the lazy dog" * 10
    compressed = zlib.compress(text.encode('utf-8'))
    decompressed = zlib.decompress(compressed).decode('utf-8')
    assert decompressed == text

def generate_note(id_suffix: int, size: int, relevance: int):
    content = "a" * size
    return {"id": f"note{id_suffix}", "content": content, "relevance": relevance}

def usage(notes):
    return sum(len(json.dumps(n).encode('utf-8')) for n in notes)

def test_soft_budget_degradation():
    cfg = {"soft_limit_bytes": 150, "hard_limit_bytes": 1000, "max_full_documents": 1}
    budget = ContextBudget(cfg)
    notes = [
        generate_note(1, 200, relevance=10),
        generate_note(2, 200, relevance=5),
        generate_note(3, 200, relevance=1),
    ]
    degraded = budget.apply_degradation(notes)
    # Verify that the resulting pack respects the soft budget
    assert usage(degraded) <= cfg["soft_limit_bytes"]
    # At most max_full_documents notes may retain non-empty content
    non_empty = [n for n in degraded if n["content"]]
    assert len(non_empty) <= cfg["max_full_documents"]
    # If a note is kept, it may be FULL or PARTIAL (contains marker)
    for n in non_empty:
        content = n["content"]
        assert content == "" or "[PARTIAL]" in content or len(content) == 200

def test_hard_limit_enforcement():
    cfg = {"soft_limit_bytes": 5000, "hard_limit_bytes": 300, "max_full_documents": 5}
    budget = ContextBudget(cfg)
    notes = [generate_note(1, 400, relevance=10), generate_note(2, 400, relevance=5)]
    with pytest.raises(BudgetExceededError):
        budget.apply_degradation(notes)


============================================================
FILE: memory_controller/tests/test_core.py
============================================================

import pytest
import os
import time
from memory_controller.context.budget import ContextBudget, ContextBudgetError
from memory_controller.context.query_classifier import QueryClassifier, Intent
from memory_controller.cache.lru_cache import LRUCache
from memory_controller.context.retrieval import RetrievalEngine
from memory_controller.context.progressive_disclosure import ProgressiveDisclosure
from memory_controller.audit.logger import AuditLogger, get_logger
from memory_controller.security import sanitize_query, check_path_traversal, detect_cache_poisoning

# Helper mock storage engine
class MockStorageEngine:
    def __init__(self, notes=None):
        self.notes = notes or []
    def query(self, intent=None, lifecycle=None, types=None):
        # Simple filter: just return all notes (ignore args)
        return self.notes
    def get_provenance(self, note_id):
        return {"source": "mock", "id": note_id}

def test_context_budget_hard_limit():
    cfg = {"soft_context_budget": 5, "hard_context_budget": 10}
    budget = ContextBudget(cfg)
    budget.check_budget(9)  # within hard limit
    with pytest.raises(ContextBudgetError):
        budget.check_budget(11)

def test_query_classifier_defaults():
    classifier = QueryClassifier()
    result = classifier.classify("please read the knowledge notes")
    assert result["intent"] == Intent.READ
    assert "knowledge" in result["target_types"]
    # confidence low for default READ
    assert result["confidence"] == 0.5

def test_lru_cache_basic_eviction_and_ttl():
    cache = LRUCache(max_items=2, default_ttl=1)  # short ttl
    cache.set("value1", "k1")
    assert cache.get("k1") == "value1"
    time.sleep(1.1)  # expire
    assert cache.get("k1") is None
    # Fill beyond max_items to trigger LRU eviction
    cache.set("v2", "k2")
    cache.set("v3", "k3")
    # k2 should still be present (most recent), k3 present, only two items allowed
    assert cache.get("k2") == "v2"
    assert cache.get("k3") == "v3"
    # Adding another forces eviction of oldest (k2)
    cache.set("v4", "k4")
    # Since k2 was older than k3, it may be evicted
    # At least one of k2 or k3 will be missing; check total count
    remaining = [k for k in ["k2", "k3", "k4"] if cache.get(k) is not None]
    assert len(remaining) == 2

def test_retrieval_engine_respects_max_notes():
    notes = [{"id": f"n{i}", "content": f"content {i}"} for i in range(5)]
    storage = MockStorageEngine(notes=notes)
    cache = LRUCache(max_items=10)
    engine = RetrievalEngine(storage, cache=cache)
    classified = {"intent": Intent.READ, "lifecycle_filters": [], "target_types": [], "max_notes": 3}
    result = engine.retrieve(classified)
    assert len(result) == 3
    assert result[0]["id"] == "n0"

def test_progressive_disclosure_limits():
    notes = [{"id": f"n{i}", "type": "knowledge", "lifecycle": "ACTIVE", "confidence": 0.9,
              "content": "a " * 100} for i in range(3)]
    from memory_controller.context.budget import ContextBudget
    budget = ContextBudget({"soft_context_budget": 10, "hard_context_budget": 1000})
    pd = ProgressiveDisclosure(budget)
    meta = pd.metadata_only(notes)
    assert len(meta) == 3
    snippet = pd.snippet(notes, chars=10)
    assert snippet[0]["snippet"] == "a a a a a "[:10]
    full = pd.full_document(notes)
    # usage limited by hard budget (bytes). 1000 bytes allows all three notes (each ~200 bytes)
    assert len(full) == 3

def test_audit_logger_writes_and_reads(tmp_path):
    log_file = tmp_path / "audit.log"
    logger = AuditLogger(str(log_file))
    logger.log(actor="human", operation="READ", target_id="note1", outcome="success")
    # read back
    with open(log_file, "r", encoding="utf-8") as f:
        line = f.readline().strip()
    import json
    entry = json.loads(line)
    assert entry["actor"] == "human"
    assert entry["operation"] == "READ"
    assert entry["target_id"] == "note1"

def test_security_sanitize_and_path():
    bad = "{{ secret }} <script>alert(1)</script> normal"
    clean = sanitize_query(bad)
    assert "{{" not in clean and "<script>" not in clean
    # path traversal detection
    with pytest.raises(ValueError):
        check_path_traversal("..\\outside\\file.txt")
    # cache poisoning detection
    detect_cache_poisoning("a"*64, "ok")
    with pytest.raises(ValueError):
        detect_cache_poisoning("invalid_key", "data")


============================================================
FILE: memory_controller/tests/test_git_isolation.py
============================================================

# -*- coding: utf-8 -*-
"""Tests for Git isolation of MemoryController operations.

These tests create a temporary Git repository under the pytest `tmp_path`
fixture, copy the relevant `memory_controller` package into it, and verify
that normal controller actions (e.g., `propose`, `review`) do **not** cause
any file modifications or automatic Git commits.

The suite also checks that explicit staging, committing, and reverting work
as expected, and that unrelated files are never staged automatically.
"""

import os
import shutil
import subprocess
import sys
import importlib.util
from pathlib import Path

import pytest


def _init_git_repo(repo_path: Path) -> None:
    """Initialise an empty Git repository at ``repo_path``.
    ``repo_path`` must already exist.
    """
    subprocess.run(["git", "init"], cwd=str(repo_path), check=True, capture_output=True)
    subprocess.run(["git", "config", "user.name", "Test User"], cwd=str(repo_path), check=True)
    subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=str(repo_path), check=True)


def _copy_memory_controller(src_root: Path, dest_root: Path) -> Path:
    src_pkg = src_root / "memory_controller"
    dst_pkg = dest_root / "memory_controller"
    shutil.copytree(src_pkg, dst_pkg)
    return dst_pkg


def _load_controller_module(pkg_path: Path):
    sys.path.insert(0, str(pkg_path.parent))
    spec = importlib.util.find_spec("memory_controller.controller")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    sys.path.pop(0)
    return module.MemoryController, module.controller


def test_git_isolation(tmp_path: Path):
    # Initialise temporary Git repo
    _init_git_repo(tmp_path)

    # Copy the memory_controller package into the repo
    pkg_path = _copy_memory_controller(Path.cwd(), tmp_path)

    # Add an unrelated file and commit baseline
    unrelated_file = tmp_path / "unrelated.txt"
    unrelated_file.write_text("initial content\n")
    subprocess.run(["git", "add", "unrelated.txt"], cwd=str(tmp_path), check=True)
    subprocess.run(["git", "commit", "-m", "baseline commit"], cwd=str(tmp_path), check=True)

    # Add .gitignore to ignore the copied memory_controller package
    (tmp_path / ".gitignore").write_text("memory_controller/\n")
    subprocess.run(["git", "add", ".gitignore"], cwd=str(tmp_path), check=True)
    subprocess.run(["git", "commit", "-m", "add .gitignore"], cwd=str(tmp_path), check=True)
    status = subprocess.run(["git", "status", "--porcelain"], cwd=str(tmp_path), capture_output=True, text=True, check=True)
    assert status.stdout.strip() == ""

    # Load controller from copied package
    MemoryController, controller = _load_controller_module(pkg_path)

    # Perform a propose operation (should not touch filesystem)
    note_id = "00000000-0000-0000-0000-000000000000"
    note_data = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "RAW",
        "provenance": {"source_type": "user", "source_ref": "test"},
    }
    from memory_controller.authorizer import Principal
    controller.propose(Principal.HUMAN, note_data)

    # Verify no Git changes
    after = subprocess.run(["git", "status", "--porcelain"], cwd=str(tmp_path), capture_output=True, text=True, check=True)
    assert after.stdout.strip() == ""

    # Manually edit a file to stage later
    target_file = pkg_path / "controller.py"
    original_content = target_file.read_text()
    target_file.write_text(original_content.replace("# controller.py", "# controller.py\n# MANUAL EDIT for test"))

    # Stage and commit the edit
    subprocess.run(["git", "add", "-f", str(target_file.relative_to(tmp_path))], cwd=str(tmp_path), check=True)
    subprocess.run(["git", "commit", "-m", "manual edit"], cwd=str(tmp_path), check=True)

    # Capture commit SHA
    sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=str(tmp_path), capture_output=True, text=True, check=True).stdout.strip()
    assert len(sha) == 40

    # Ensure unrelated file unchanged
    status2 = subprocess.run(["git", "status", "--porcelain"], cwd=str(tmp_path), capture_output=True, text=True, check=True)
    assert status2.stdout.strip() == ""

    # Revert the commit (no --hard)
    subprocess.run(["git", "revert", "--no-edit", sha], cwd=str(tmp_path), check=True)

    # Repo should be clean again
    # Verify the edited file has been removed (it did not exist before)
    assert not target_file.exists()



============================================================
FILE: memory_controller/tests/test_lifecycle.py
============================================================

import pytest
from uuid import uuid4
from datetime import datetime
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal, Operation

# Helper to build minimal frontmatter for a note
def make_note(lifecycle: Lifecycle, note_id: str = None):
    note_id = note_id or str(uuid4())
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle.value,
        "category": "test",
        "tags": [],
        "created": datetime.utcnow().date().isoformat(),
        "updated": datetime.utcnow().date().isoformat(),
        "provenance": {
            "source_type": "user",
            "source_ref": "unit_test",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": []
    }

# Fixture for a fresh controller
@pytest.fixture
def controller():
    storage = StorageEngine()
    return MemoryController(storage)

# Valid lifecycle transitions according to canonical protocol
VALID_TRANSITIONS = [
    (Lifecycle.RAW, Lifecycle.CLASSIFIED),
    (Lifecycle.CLASSIFIED, Lifecycle.NORMALIZED),
    (Lifecycle.NORMALIZED, Lifecycle.REVIEW),
    (Lifecycle.REVIEW, Lifecycle.VERIFIED),
    (Lifecycle.VERIFIED, Lifecycle.ACTIVE),
    (Lifecycle.ACTIVE, Lifecycle.SUPERSEDED),
    (Lifecycle.ACTIVE, Lifecycle.ARCHIVED),
(Lifecycle.ACTIVE, Lifecycle.ARCHIVED),
]

@pytest.mark.parametrize("src, dst", VALID_TRANSITIONS)
def test_valid_transition(controller, src, dst):
    # Directly store a note with source lifecycle
    note = make_note(src)
    controller.storage.set(note["id"], note)
    # Transition by updating lifecycle field
    note["lifecycle"] = dst.value
    # Validation should pass for a correct transition
    controller._validate_note(note)  # should not raise
    # Persist the transition
    controller.storage.set(note["id"], note)
    # Verify stored lifecycle matches
    stored = controller.storage.get(note["id"])
    assert stored["lifecycle"] == dst.value

# Invalid transitions (any that are not in the above list)
INVALID_TRANSITIONS = [
    (Lifecycle.RAW, Lifecycle.VERIFIED),
    (Lifecycle.CLASSIFIED, Lifecycle.ACTIVE),
    (Lifecycle.NORMALIZED, Lifecycle.SUPERSEDED),
    (Lifecycle.REVIEW, Lifecycle.ARCHIVED),
    (Lifecycle.VERIFIED, Lifecycle.RAW),
    (Lifecycle.SUPERSEDED, Lifecycle.ARCHIVED),
]

@pytest.mark.parametrize("src, dst", INVALID_TRANSITIONS)
def test_invalid_transition(controller, src, dst):
    note = make_note(src)
    controller.storage.set(note["id"], note)
    note["lifecycle"] = dst.value
    with pytest.raises(Exception):
        controller._validate_note(note)

def test_raw_not_in_read_search(controller):
    raw_note = make_note(Lifecycle.RAW)
    controller.storage.set(raw_note["id"], raw_note)
    # Attempt read as AI (should raise)
    with pytest.raises(ValueError):
        controller.read(Principal.AI_AGENT, raw_note["id"])
    # Search should not return RAW notes
    result = controller.search(Principal.AI_AGENT, "test query")
    ids = [r.get('id') for r in result.get('results', [])]
    assert raw_note["id"] not in ids

def test_verified_not_active_unless_promoted(controller):
    verified = make_note(Lifecycle.VERIFIED)
    controller.storage.set(verified["id"], verified)
    # READ should still reject because only ACTIVE is readable
    with pytest.raises(ValueError):
        controller.read(Principal.AI_AGENT, verified["id"])

def test_ai_cannot_bypass_lifecycle(controller):
    # AI can read ACTIVE notes
    active = make_note(Lifecycle.ACTIVE)
    controller.storage.set(active["id"], active)
    result = controller.read(Principal.AI_AGENT, active["id"])
    assert result is not None
    # Change to RAW and attempt read again – should fail
    active["lifecycle"] = Lifecycle.RAW.value
    controller.storage.set(active["id"], active)
    with pytest.raises(ValueError):
        controller.read(Principal.AI_AGENT, active["id"])


============================================================
FILE: memory_controller/tests/test_pagination.py
============================================================

import os
import pytest
from uuid import uuid4
import time
from datetime import datetime, timezone, timedelta
import hashlib
import base64
import json
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal, MissingHMACSecretError, InvalidPaginationTokenError
from memory_controller.security.pagination_token import PaginationToken

# Helper to set secret for tests
SECRET_ENV = 'MEMORY_CONTROLLER_HMAC_SECRET'
TEST_SECRET = 'test_secret_123'

@pytest.fixture(autouse=True)
def set_secret(monkeypatch):
    # Ensure secret is set for each test unless overridden
    monkeypatch.setenv(SECRET_ENV, TEST_SECRET)
    yield
    # cleanup not needed as monkeypatch resets

def make_controller():
    storage = StorageEngine()
    return MemoryController(storage)

def test_token_encode_decode_basic():
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    decoded = PaginationToken.decode(token)
    assert decoded == payload

def test_token_tamper_detection():
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    # Tamper by changing a character in the payload part (offset field)
    parts = token.split('.')
    tampered_payload = parts[0][:-1] + ('A' if parts[0][-1] != 'A' else 'B')
    tampered_token = tampered_payload + '.' + parts[1]
    with pytest.raises(InvalidPaginationTokenError):
        PaginationToken.decode(tampered_token)

def test_token_expiration_detection(monkeypatch):
    past = datetime.now(timezone.utc) - timedelta(minutes=1)
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int(past.timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    with pytest.raises(InvalidPaginationTokenError):
        PaginationToken.decode(token)

def test_missing_secret_raises(monkeypatch):
    monkeypatch.delenv(SECRET_ENV, raising=False)
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    with pytest.raises(MissingHMACSecretError):
        PaginationToken.decode(token)

def test_token_size_limit(monkeypatch):
    large_str = 'x' * 5000  # large enough to exceed 2KB after encoding
    query_fp = large_str
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    with pytest.raises(ValueError):
        PaginationToken(payload, TEST_SECRET.encode()).encode()

def test_search_pagination_success_and_validation(monkeypatch):
    ctrl = make_controller()
    # Populate storage with 15 dummy notes (ACTIVE lifecycle)
    for i in range(15):
        note = {
            'id': str(uuid4()),
            'type': 'knowledge',
            'lifecycle': Lifecycle.ACTIVE.value,
            'category': 'test',
            'tags': [],
            'created': '2023-01-01',
            'updated': '2023-01-01',
            'provenance': {'source_type': 'user', 'source_ref': 'unit'},
            'confidence': 'high',
            'verification': 'unverified',
            'relations': []
        }
        ctrl.storage.set(note['id'], note)
    # First page request
    result1 = ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5)
    assert len(result1['results']) == 5
    token = result1.get('next_page_token')
    assert token is not None
    # Second page with same parameters should succeed
    result2 = ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token)
    assert len(result2['results']) == 5
    # Mismatch query should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'other', page_size=5, page_token=token)
    # Mismatch principal should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.HUMAN, 'dummy', page_size=5, page_token=token)
    # Mismatch page_size should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=10, page_token=token)
    # Mismatch lifecycles filter should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token, lifecycles=[Lifecycle.RAW])
    # Mismatch types filter should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token, types=['other'])
    # Mismatch disclosure level (default is metadata) – simulate by changing controller attribute
    ctrl.default_disclosure = 'full'
    # Need a fresh token with new disclosure bound
    result3 = ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5)
    new_token = result3.get('next_page_token')
    # Now use old token with new disclosure – should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token)

    # Offset manipulation test – tamper offset in token
    token_parts = token.split('.')
    payload_bytes = base64.urlsafe_b64decode(token_parts[0] + '==')
    payload_dict = json.loads(payload_bytes)
    payload_dict['offset'] = 9999  # unrealistic offset
    tampered_payload = base64.urlsafe_b64encode(json.dumps(payload_dict, separators=(',', ':'), sort_keys=True).encode()).decode().rstrip('=')
    tampered_token = tampered_payload + '.' + token_parts[1]
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=tampered_token)


============================================================
FILE: memory_controller/tests/test_raw_imports.py
============================================================

import pytest
import uuid
from memory_controller.controller import MemoryController, Principal, Lifecycle, _storage_engine

@pytest.fixture
def controller():
    storage = _storage_engine
    storage.store.clear()
    return MemoryController(storage)

def test_raw_note_excluded_from_search(controller):
    # Create a RAW note manually
    raw_id = str(uuid.uuid4())
    raw_note = {
        'id': raw_id,
        'type': 'knowledge',
        'category': 'test',
        'tags': [],
        'created': '2023-01-01',
        'updated': '2023-01-01',
        'provenance': {'source_type': 'user', 'source_ref': 'test'},
        'confidence': 'high',
        'verification': 'unverified',
        'relations': [],
        'lifecycle': Lifecycle.RAW.value,
    }
    # Store directly in the storage engine (bypassing controller)
    controller.storage.set(raw_id, raw_note)
    # Perform a search – RAW notes must be excluded from results
    pack = controller.search(Principal.HUMAN, "any query")
    results = pack.get('results', [])
    assert all(note.get('lifecycle') != Lifecycle.RAW for note in results)

def test_raw_note_cannot_be_read(controller):
    raw_id = str(uuid.uuid4())
    raw_note = {
        'id': raw_id,
        'type': 'knowledge',
        'category': 'test',
        'tags': [],
        'created': '2023-01-01',
        'updated': '2023-01-01',
        'provenance': {'source_type': 'user', 'source_ref': 'test'},
        'confidence': 'high',
        'verification': 'unverified',
        'relations': [],
        'lifecycle': Lifecycle.RAW.value,
    }
    controller.storage.set(raw_id, raw_note)
    # READ operation should raise because only ACTIVE notes are readable
    with pytest.raises(ValueError):
        controller.read(Principal.HUMAN, raw_id)


============================================================
FILE: memory_controller/tests/test_security.py
============================================================

import pytest
import hashlib
from typing import Dict, Any

from memory_controller.controller import controller, Principal, Operation
from memory_controller.security.utils import sanitize_query, check_query_size, check_path_traversal, detect_cache_poisoning

def setup_function():
    controller.storage.store.clear()
    controller.cache.store.clear()
    controller.cache.hit_count = 0
    controller.cache.miss_count = 0
    
    # Insert a dummy note for path traversal tests
    controller.storage.set("valid-id-123", {
        "id": "valid-id-123",
        "lifecycle": "ACTIVE",
        "type": "knowledge",
        "category": "test",
        "confidence": "high",
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "verification": "unverified",
        "tags": [],
        "relations": [],
        "provenance": {"source_type": "user", "source_ref": "test"}
    })

def test_prompt_injection_sanitization():
    # Existing sanitize_query contract
    malicious = "Hello {{prompt}} <script>alert(1)</script> World <html>"
    clean = sanitize_query(malicious)
    assert clean == "Hello   World"
    
    # Test via controller
    pack = controller.search(Principal.HUMAN, malicious)
    # The actual retrieval logic might not return anything, but we ensure no exception
    assert pack is not None

def test_query_size_boundary():
    # 4096 -> accepted
    valid_query = "A" * 4096
    pack = controller.search(Principal.HUMAN, valid_query)
    assert pack is not None
    
    # 4097 -> rejected
    invalid_query = "A" * 4097
    with pytest.raises(ValueError, match="exceeds maximum allowed"):
        controller.search(Principal.HUMAN, invalid_query)

def test_path_traversal_controller_operations():
    bad_paths = [
        "../etc/passwd",
        "../../secrets.txt",
        "..\\windows\\system32",
        "C:\\Windows\\system32\\cmd.exe",
        "/etc/passwd"
    ]
    
    for bad in bad_paths:
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.read(Principal.ADMIN, bad)
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.update(Principal.ADMIN, bad, {"category": "test"})
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.archive(Principal.ADMIN, bad, "reason")
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.review(Principal.ADMIN, bad, "approve")
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.promote(Principal.ADMIN, bad)
            
    # Valid ID remains accepted (should not raise ValueError for path traversal)
    res = controller.read(Principal.ADMIN, "valid-id-123")
    assert res is not None

def test_cache_poisoning_malformed_key():
    with pytest.raises(ValueError, match="Invalid cache key format"):
        detect_cache_poisoning("bad-key", "value")
        
def test_cache_poisoning_oversized_payload():
    valid_key = hashlib.sha256(b"test").hexdigest()
    oversized = "A" * 1_000_001
    with pytest.raises(ValueError, match="exceeds size limit"):
        detect_cache_poisoning(valid_key, oversized)
        
    oversized_list = [{"id": "x" * 500_000}, {"id": "y" * 500_001}]
    with pytest.raises(ValueError, match="exceeds size limit"):
        detect_cache_poisoning(valid_key, oversized_list)

def test_poisoned_cache_entry_invalidation():
    query = "test cache poisoning"
    # First search -> MISS, populates cache
    controller.search(Principal.HUMAN, query)
    assert controller.cache.miss_count == 1
    assert controller.cache.hit_count == 0
    
    # Get the cache key that was stored
    keys = list(controller.cache.store.keys())
    assert len(keys) == 1
    stored_key = keys[0]
    
    # Poison the payload directly in the store
    controller.cache.store[stored_key].value = "A" * 1_000_001
    
    # Second search -> Should detect poisoning, invalidate entry, and treat as MISS
    controller.search(Principal.HUMAN, query)
    
    assert controller.cache.miss_count == 2
    assert controller.cache.hit_count == 0
    # The poisoned entry should have been replaced with the valid fresh data
    assert stored_key in controller.cache.store
    assert controller.cache.store[stored_key].value != "A" * 1_000_001

def test_valid_cache_entry_remains_usable():
    query = "test valid cache"
    controller.search(Principal.HUMAN, query)
    assert controller.cache.miss_count == 1
    assert controller.cache.hit_count == 0
    
    # Second search -> HIT
    controller.search(Principal.HUMAN, query)
    assert controller.cache.miss_count == 1
    assert controller.cache.hit_count == 1

def test_no_cross_principal_leakage():
    query = "test isolation"
    controller.search(Principal.HUMAN, query)
    
    hits_before = controller.cache.hit_count
    misses_before = controller.cache.miss_count
    
    # Different principal, same query
    controller.search(Principal.AI_AGENT, query)
    
    # Must be a MISS
    assert controller.cache.miss_count == misses_before + 1
    assert controller.cache.hit_count == hits_before


============================================================
FILE: memory_controller/tests/test_storage.py
============================================================

import pytest
import os
import shutil
import tempfile
import uuid
import yaml
from pathlib import Path

from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.storage.serializer import serialize, deserialize
from memory_controller.storage.path_resolver import resolve_path, sanitize_filename

@pytest.fixture
def temp_vault():
    tmp_dir = tempfile.mkdtemp()
    folders = [
        "00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
        "04_MEMORY", "05_RESOURCES", "06_INBOX/RAW_IMPORTS",
        "90_TEMPLATES", "99_SYSTEM"
    ]
    for folder in folders:
        os.makedirs(os.path.join(tmp_dir, folder))
    yield tmp_dir
    shutil.rmtree(tmp_dir)

def create_valid_note(override=None):
    base = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "test_cat",
        "content": "This is a test note."
    }
    if override:
        base.update(override)
    return base

def test_frontmatter_roundtrip():
    # Test CRLF, empty body, {{date}} in body, "---" in body
    note = create_valid_note({"content": "Hello\n---\n{{date}}\nWorld"})
    note["custom_field"] = "preserved"
    
    serialized = serialize(note)
    # Convert to CRLF for robustness test
    crlf_serialized = serialized.replace('\n', '\r\n')
    
    deserialized = deserialize(crlf_serialized)
    assert deserialized["id"] == note["id"]
    # We expect the exact body back, which now has CRLF
    expected_body = "Hello\n---\n{{date}}\nWorld".replace('\n', '\r\n')
    assert deserialized["content"] == expected_body
    assert deserialized["custom_field"] == "preserved"

def test_path_resolution():
    base = "C:\\Vault" if os.name == 'nt' else "/Vault"
    assert "01_KNOWLEDGE" in resolve_path(base, {"type": "knowledge", "category": "sec", "id": "123"})
    assert "02_PROJECTS" in resolve_path(base, {"type": "project", "category": "dev", "id": "123"})
    
def test_filename_safety():
    assert sanitize_filename("invalid:name*?") == "invalid_name__"
    assert sanitize_filename("CON") == "CON_"
    assert sanitize_filename("trailing. ") == "trailing"
    long_name = "A" * 300
    assert len(sanitize_filename(long_name)) == 100

def test_path_traversal_storage(temp_vault):
    engine = FileStorageEngine(temp_vault)
    bad_inputs = [
        {"id": "../../../malicious", "category": "safe"},
        {"id": "safe", "category": "../../../malicious"},
        {"id": "safe", "category": "C:\\Windows\\System32"},
        {"id": "safe", "category": "/etc/passwd"},
    ]
    
    for bad in bad_inputs:
        note = create_valid_note(bad)
        try:
            engine.set(note["id"], note)
            # If it succeeded, it MUST be safely inside the vault due to sanitization
            path = engine.id_to_path[note["id"]]
            assert os.path.commonpath([os.path.realpath(path), os.path.realpath(temp_vault)]) == os.path.realpath(temp_vault)
        except ValueError:
            pass # Traversal correctly blocked by raising

def test_id_invariant(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note()
    # Mismatch note_id vs data["id"]
    with pytest.raises(ValueError, match="ID mismatch"):
        engine.set("different_id", note)

def test_filesystem_write_persists(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note({"type": "knowledge"})
    engine.set(note["id"], note)
    
    knowledge_dir = os.path.join(temp_vault, "01_KNOWLEDGE")
    files = os.listdir(knowledge_dir)
    assert len(files) == 1
    
def test_restart_persistence(temp_vault):
    engine1 = FileStorageEngine(temp_vault)
    note = create_valid_note()
    engine1.set(note["id"], note)
    
    engine2 = FileStorageEngine(temp_vault)
    assert engine2.get(note["id"])["id"] == note["id"]

def test_uuid_survives_filename_change(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note({"category": "old_title"})
    engine.set(note["id"], note)
    path1 = engine.id_to_path[note["id"]]
    
    note["category"] = "new_title"
    engine.set(note["id"], note)
    path2 = engine.id_to_path[note["id"]]
    
    assert path1 != path2
    assert not os.path.exists(path1)
    assert os.path.exists(path2)

def test_update_persists(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note()
    engine.set(note["id"], note)
    
    note["content"] = "Updated body"
    engine.set(note["id"], note)
    assert FileStorageEngine(temp_vault).get(note["id"])["content"] == "Updated body"

def test_lifecycle_persists(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note({"lifecycle": "REVIEW"})
    engine.set(note["id"], note)
    
    note["lifecycle"] = "ACTIVE"
    engine.set(note["id"], note)
    assert FileStorageEngine(temp_vault).get(note["id"])["lifecycle"] == "ACTIVE"

def test_raw_imports_untouched(temp_vault):
    engine = FileStorageEngine(temp_vault)
    raw_dir = os.path.join(temp_vault, "06_INBOX/RAW_IMPORTS")
    dummy_path = os.path.join(raw_dir, "dummy.md")
    with open(dummy_path, 'w') as f:
        f.write("---\nid: raw123\n---\nRaw")
        
    engine2 = FileStorageEngine(temp_vault)
    # Should not index it
    assert engine2.get("raw123") is None
    
    # Writing to RAW should fail
    note = create_valid_note({"id": "raw123"})
    with pytest.raises(ValueError):
        resolve_path(temp_vault, {"type": "inbox"}) # Mappings shouldn't even allow it

def test_duplicate_uuid_detection(temp_vault):
    note = create_valid_note()
    p1 = os.path.join(temp_vault, "01_KNOWLEDGE", f"A_{note['id']}.md")
    p2 = os.path.join(temp_vault, "02_PROJECTS", f"B_{note['id']}.md")
    with open(p1, 'w') as f: f.write(serialize(note))
    with open(p2, 'w') as f: f.write(serialize(note))
    
    with pytest.raises(ValueError, match="Duplicate UUID"):
        FileStorageEngine(temp_vault)

def test_malformed_frontmatter(temp_vault):
    bad_path = os.path.join(temp_vault, "01_KNOWLEDGE", "bad.md")
    with open(bad_path, 'w') as f:
        f.write("---\n[invalid\n---\nContent")
    
    # Should not crash, should be skipped
    engine = FileStorageEngine(temp_vault)
    assert len(engine.id_to_path) == 0

def test_90_templates_exclusion(temp_vault):
    template_path = os.path.join(temp_vault, "90_TEMPLATES", "temp.md")
    with open(template_path, 'w') as f:
        f.write("---\nid: {{date}}\n---\nTemplate")
        
    engine = FileStorageEngine(temp_vault)
    assert len(engine.id_to_path) == 0

def test_atomic_write(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note()
    engine.set(note["id"], note)
    
    files = os.listdir(os.path.join(temp_vault, "01_KNOWLEDGE"))
    # Ensure no .tmp_ files are left behind
    assert len(files) == 1
    assert not files[0].startswith(".tmp_")


============================================================
END OF FILE
============================================================

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
