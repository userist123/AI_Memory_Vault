import json
import os
import shutil
import tempfile
import threading
import pytest
from memory_controller.audit.logger import AuditLogger

@pytest.fixture
def temp_log_path():
    temp_dir = tempfile.mkdtemp()
    log_file = os.path.join(temp_dir, "test_adversarial_audit.jsonl")
    yield log_file
    if os.path.exists(temp_dir):
        shutil.rmtree(temp_dir, ignore_errors=True)

def populate_chain(logger: AuditLogger, count: int = 10) -> list:
    entries = []
    for i in range(count):
        logger.log(
            actor=f"actor_{i % 3}",
            operation=f"OP_{i}",
            target_id=f"target-uuid-{i:04d}",
            outcome="success" if i % 4 != 0 else "error",
            error_details=f"error reason {i}" if i % 4 == 0 else None,
            metadata={"step": i, "data": f"payload_{i}", "nested": {"key": i * 10}}
        )
    with open(logger.log_path, "r", encoding="utf-8") as f:
        for line in f:
            if line.strip():
                entries.append(json.loads(line))
    return entries

def write_entries(log_path: str, entries: list):
    with open(log_path, "w", encoding="utf-8") as f:
        for entry in entries:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")

# ==============================================================================
# 1. BASELINE UNTAMPERED CHAINS
# ==============================================================================

def test_untampered_empty_file(temp_log_path):
    logger = AuditLogger(temp_log_path)
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert violations == []

def test_untampered_nonexistent_file():
    non_existent = "non_existent_file_path_12345.jsonl"
    if os.path.exists(non_existent):
        os.remove(non_existent)
    logger = AuditLogger(non_existent)
    os.remove(non_existent) # Force non-existence
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert violations == []

def test_untampered_single_entry(temp_log_path):
    logger = AuditLogger(temp_log_path)
    logger.log(actor="admin", operation="BOOTSTRAP", target_id="system-root")
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert violations == []

def test_untampered_multi_entry_chain(temp_log_path):
    logger = AuditLogger(temp_log_path)
    populate_chain(logger, count=50)
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert len(violations) == 0

def test_untampered_special_characters_and_unicode(temp_log_path):
    logger = AuditLogger(temp_log_path)
    logger.log(
        actor="ai_agent_🤖",
        operation="REASON_🧠",
        target_id="node_🔑_日本語_123",
        outcome="success",
        metadata={"quotes": "He said \"hello\\world\"", "unicode": "こんにちは мир 🚀", "newlines": "line1\nline2"}
    )
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert violations == []

# ==============================================================================
# 2. PAYLOAD MODIFICATION TAMPERING
# ==============================================================================

@pytest.mark.parametrize("field,new_val", [
    ("actor", "forged_admin"),
    ("operation", "UNAUTHORIZED_DELETE"),
    ("target_id", "forged-uuid-9999"),
    ("outcome", "tampered_outcome"),
    ("timestamp", "1970-01-01T00:00:00Z"),
    ("error_details", "faked_error_message"),
])
def test_tamper_genesis_payload_field(temp_log_path, field, new_val):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    entries[0][field] = new_val
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 1: entry_hash mismatch" in v or "Line 2: prev_hash mismatch" in v for v in violations)

@pytest.mark.parametrize("field,new_val", [
    ("actor", "malicious_actor"),
    ("operation", "FORGED_OPERATION"),
    ("target_id", "hijacked_target"),
    ("outcome", "tampered_outcome"),
    ("timestamp", "2099-12-31T23:59:59Z"),
])
def test_tamper_middle_payload_field(temp_log_path, field, new_val):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=7)
    
    entries[3][field] = new_val
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 4: entry_hash mismatch" in v for v in violations)

def test_tamper_last_payload_field(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    entries[4]["actor"] = "attacker"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 5: entry_hash mismatch" in v for v in violations)

def test_tamper_metadata_nested_value(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    entries[2]["metadata"]["nested"]["key"] = 999999
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 3: entry_hash mismatch" in v for v in violations)

def test_tamper_add_extra_rogue_field(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    entries[1]["unauthorized_field"] = "escalated_privilege"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 2: entry_hash mismatch" in v for v in violations)

def test_tamper_delete_payload_field(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    del entries[2]["target_id"]
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 3: entry_hash mismatch" in v for v in violations)

# ==============================================================================
# 3. PREV_HASH CORRUPTION TAMPERING
# ==============================================================================

def test_tamper_genesis_prev_hash(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    entries[0]["prev_hash"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 1: prev_hash mismatch (expected GENESIS" in v for v in violations)

def test_tamper_middle_prev_hash(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=6)
    
    entries[3]["prev_hash"] = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 4: prev_hash mismatch" in v for v in violations)

def test_tamper_last_prev_hash(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=4)
    
    entries[3]["prev_hash"] = "0000000000000000000000000000000000000000000000000000000000000000"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 4: prev_hash mismatch" in v for v in violations)

def test_tamper_prev_hash_null_or_none(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=3)
    
    entries[1]["prev_hash"] = None
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 2: prev_hash mismatch" in v for v in violations)

# ==============================================================================
# 4. ENTRY_HASH CORRUPTION TAMPERING
# ==============================================================================

def test_tamper_genesis_entry_hash(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=4)
    
    entries[0]["entry_hash"] = "1111111111111111111111111111111111111111111111111111111111111111"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    # Will fail on Line 1 (entry_hash mismatch) and Line 2 (prev_hash mismatch)
    assert any("Line 1: entry_hash mismatch" in v for v in violations)
    assert any("Line 2: prev_hash mismatch" in v for v in violations)

def test_tamper_middle_entry_hash(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    entries[2]["entry_hash"] = "2222222222222222222222222222222222222222222222222222222222222222"
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 3: entry_hash mismatch" in v for v in violations)

def test_tamper_remove_entry_hash(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=4)
    
    del entries[1]["entry_hash"]
    write_entries(temp_log_path, entries)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 2: entry_hash mismatch" in v for v in violations)

# ==============================================================================
# 5. DELETION & TRUNCATION TAMPERING
# ==============================================================================

def test_tamper_delete_middle_record(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=6)
    
    # Delete entry index 2 (third entry)
    tampered = [entries[0], entries[1], entries[3], entries[4], entries[5]]
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    # Line 3 (which was original line 4) will have prev_hash pointing to deleted entry 2, not entry 1
    assert any("Line 3: prev_hash mismatch" in v for v in violations)

def test_tamper_delete_multiple_consecutive_records(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=8)
    
    # Delete entries index 2, 3, 4
    tampered = [entries[0], entries[1], entries[5], entries[6], entries[7]]
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 3: prev_hash mismatch" in v for v in violations)

def test_tamper_delete_first_record(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    # Delete entry 0 (genesis)
    tampered = entries[1:]
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    # First line now expects GENESIS, but gets old entry 0 hash
    assert any("Line 1: prev_hash mismatch (expected GENESIS" in v for v in violations)

# ==============================================================================
# 6. REORDERING, INSERTION & DUPLICATION TAMPERING
# ==============================================================================

def test_tamper_swap_adjacent_records(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=6)
    
    # Swap index 2 and index 3
    tampered = list(entries)
    tampered[2], tampered[3] = tampered[3], tampered[2]
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert len(violations) >= 2

def test_tamper_reverse_all_records(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    tampered = list(reversed(entries))
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert len(violations) > 0

def test_tamper_insert_foreign_record(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    foreign_entry = {
        "actor": "attacker",
        "operation": "EXECUTE_MALWARE",
        "target_id": "target-000",
        "timestamp": "2026-08-14T12:00:00Z",
        "outcome": "success",
        "prev_hash": "GENESIS",
        "entry_hash": "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890"
    }
    tampered = entries[:2] + [foreign_entry] + entries[2:]
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert len(violations) >= 2

def test_tamper_duplicate_record(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    tampered = entries[:3] + [entries[2]] + entries[3:]
    write_entries(temp_log_path, tampered)
    
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("prev_hash mismatch" in v for v in violations)

# ==============================================================================
# 7. FORMAT & CORRUPTION TAMPERING
# ==============================================================================

def test_tamper_non_json_line_in_middle(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=5)
    
    with open(temp_log_path, "w", encoding="utf-8") as f:
        for i, entry in enumerate(entries):
            if i == 2:
                f.write("MALFORMED_NON_JSON_LINE_HERE\n")
            f.write(json.dumps(entry) + "\n")
            
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 3: JSON parse or validation error" in v for v in violations)

def test_tamper_truncated_json_line(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=4)
    
    with open(temp_log_path, "w", encoding="utf-8") as f:
        f.write(json.dumps(entries[0]) + "\n")
        f.write('{"actor": "admin", "operation": "WRITE", "tar\n') # Truncated
        f.write(json.dumps(entries[2]) + "\n")
        
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("Line 2: JSON parse or validation error" in v for v in violations)

def test_tamper_injected_utf8_null_bytes(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=3)
    
    with open(temp_log_path, "w", encoding="utf-8") as f:
        f.write(json.dumps(entries[0]) + "\n")
        f.write("\x00\x00\x00\n")
        f.write(json.dumps(entries[1]) + "\n")
        
    valid, violations = logger.verify_integrity()
    assert valid is False
    assert any("JSON parse or validation error" in v for v in violations)

def test_tamper_injected_non_utf8_bytes_behavior(temp_log_path):
    logger = AuditLogger(temp_log_path)
    entries = populate_chain(logger, count=3)
    
    with open(temp_log_path, "wb") as f:
        f.write(json.dumps(entries[0]).encode("utf-8") + b"\n")
        f.write(b"\xff\xff\xfe\xfe\n")
        f.write(json.dumps(entries[1]).encode("utf-8") + b"\n")
        
    # verify_integrity with open(..., encoding="utf-8") raises UnicodeDecodeError
    # when reading invalid UTF-8 bytes from the file stream iterator
    with pytest.raises(UnicodeDecodeError):
        logger.verify_integrity()

# ==============================================================================
# 8. CONCURRENCY AND STRESS
# ==============================================================================

def test_stress_sequential_logging_and_verification(temp_log_path):
    logger = AuditLogger(temp_log_path)
    populate_chain(logger, count=150)
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert len(violations) == 0

def test_concurrent_logging_chain_integrity(temp_log_path):
    logger = AuditLogger(temp_log_path)
    num_threads = 5
    entries_per_thread = 20
    errors = []
    
    lock = threading.Lock()
    def worker(tid):
        for i in range(entries_per_thread):
            try:
                with lock:
                    logger.log(actor=f"worker_{tid}", operation="CONCURRENT_OP", target_id=f"id_{tid}_{i}")
            except Exception as e:
                errors.append(e)
                
    threads = [threading.Thread(target=worker, args=(t,)) for t in range(num_threads)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
        
    assert len(errors) == 0
    valid, violations = logger.verify_integrity()
    assert valid is True
    assert len(violations) == 0
