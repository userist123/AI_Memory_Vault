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

def test_audit_hash_chaining_and_tamper_detection():
    logger = logger_module._logger_instance
    logger.log("agent", "read", "node-1")
    logger.log("human", "attest", "node-1")
    logger.log("admin", "promote", "node-1")

    # Verify initial chain is valid
    is_valid, violations = logger.verify_integrity()
    assert is_valid is True
    assert len(violations) == 0

    # Tamper with the log file
    lines = []
    with open(TEST_AUDIT_LOG, "r", encoding="utf-8") as f:
        for line in f:
            lines.append(json.loads(line))

    # Alter an actor in middle entry
    lines[1]["actor"] = "malicious_actor"
    with open(TEST_AUDIT_LOG, "w", encoding="utf-8") as f:
        for entry in lines:
            f.write(json.dumps(entry) + "\n")

    # Verify tampering is detected
    is_valid_tampered, violations_tampered = logger.verify_integrity()
    assert is_valid_tampered is False
    assert len(violations_tampered) > 0

def test_audit_empty_and_nonexistent_log():
    # Non-existent file
    non_existent_logger = logger_module.AuditLogger("non_existent_log_path.jsonl")
    if os.path.exists("non_existent_log_path.jsonl"):
        os.remove("non_existent_log_path.jsonl")
    is_valid, violations = non_existent_logger.verify_integrity()
    assert is_valid is True
    assert len(violations) == 0

    # Empty file
    empty_path = "empty_audit_log.jsonl"
    open(empty_path, "w", encoding="utf-8").close()
    empty_logger = logger_module.AuditLogger(empty_path)
    is_valid, violations = empty_logger.verify_integrity()
    assert is_valid is True
    assert len(violations) == 0
    if os.path.exists(empty_path):
        os.remove(empty_path)

def test_audit_tamper_prev_hash_and_deletion():
    logger = logger_module._logger_instance
    logger.log("agent", "read", "node-1")
    logger.log("human", "attest", "node-1")
    logger.log("admin", "promote", "node-1")
    logger.log("agent", "search", "query-hash-1")

    # Read entries
    with open(TEST_AUDIT_LOG, "r", encoding="utf-8") as f:
        entries = [json.loads(line) for line in f if line.strip()]

    # Case 1: Corrupted prev_hash
    tampered_prev = [dict(e) for e in entries]
    tampered_prev[2]["prev_hash"] = "0000000000000000000000000000000000000000000000000000000000000000"
    with open(TEST_AUDIT_LOG, "w", encoding="utf-8") as f:
        for e in tampered_prev:
            f.write(json.dumps(e) + "\n")

    is_valid, violations = logger.verify_integrity()
    assert is_valid is False
    assert any("prev_hash mismatch" in v for v in violations)

    # Case 2: Deleted entry (delete entry index 1)
    deleted_entries = [entries[0], entries[2], entries[3]]
    with open(TEST_AUDIT_LOG, "w", encoding="utf-8") as f:
        for e in deleted_entries:
            f.write(json.dumps(e) + "\n")

    is_valid, violations = logger.verify_integrity()
    assert is_valid is False
    assert any("prev_hash mismatch" in v for v in violations)

def test_audit_corrupted_json_entry():
    logger = logger_module._logger_instance
    logger.log("agent", "read", "node-1")
    
    with open(TEST_AUDIT_LOG, "a", encoding="utf-8") as f:
        f.write("CORRUPTED_NON_JSON_LINE\n")

    is_valid, violations = logger.verify_integrity()
    assert is_valid is False
    assert any("JSON parse or validation error" in v for v in violations)

