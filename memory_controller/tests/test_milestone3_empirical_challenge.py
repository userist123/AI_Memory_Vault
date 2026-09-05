import pytest
import os
import uuid
import tempfile
import threading
import sqlite3
import time
import json
from concurrent.futures import ThreadPoolExecutor

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal, DefaultAuthorizer, Operation
import memory_controller.audit.logger as logger_module
from cognitive_core.tool_router import ToolRouter, ApprovalRequiredError
from cognitive_core.learning import LearningEngine, ContinualLearningGuard

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

def make_note(id_val, lifecycle="RAW", verification="unverified", provenance=None, content="test note payload"):
    if provenance is None:
        provenance = {"source_type": "inference", "source_ref": "test-src"}
    return {
        "id": id_val,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "security-challenge",
        "tags": ["stress", "attestation", "m3"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content
    }

# ============================================================================
# Objective 1: Multi-threaded / Concurrent Attestation Race Condition Stress
# ============================================================================

def test_concurrent_attest_and_update_race_sqlite(temp_db_path, test_audit_log):
    """Stress test concurrent attest(), update(), and illegal escalation attempts
    on the exact same note in SQLite WAL mode.
    
    Setup: Note in ACTIVE lifecycle (unverified).
    Threads:
    - Thread 1 (HUMAN): attempts legitimate attest()
    - Thread 2 (ADMIN): attempts legitimate attest()
    - Thread 3 (AI_AGENT): attempts legitimate content/tag updates on ACTIVE note
    - Thread 4 (AI_AGENT): aggressively attempts unauthorized verification escalation
    - Thread 5 (AI_AGENT): aggressively attempts unauthorized provenance mutation
    - Thread 6 (Query reader): repeatedly reads the note to detect corrupt/partial states
    """
    audit_path, audit_logger = test_audit_log
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True, timeout=10.0)
    controller = MemoryController(storage)

    note_id = str(uuid.uuid4())
    initial_note = make_note(note_id, lifecycle="REVIEW", verification="unverified", provenance={"source_type": "inference", "source_ref": "obs-1"})
    controller.propose(Principal.AI_AGENT, initial_note)
    # Lifecycle canon (see 00_GOVERNANCE/coordination/claude-code/ ADR
    # response): promote() requires verification == 'verified'; REVIEW ->
    # ACTIVE without attestation is rejected. The note is attested here as
    # setup so the race-condition stress below still starts from the
    # documented "ACTIVE" state; the concurrent human_attester/admin_attester
    # threads then race further (idempotent) re-attestations against the
    # illegal-escalation/provenance-forgery threads, which is what this test
    # actually stresses -- not the unverified->verified transition itself.
    controller.attest(Principal.ADMIN, note_id, "Initial setup attestation for concurrency stress", "setup-evidence")
    controller.promote(Principal.HUMAN, note_id)
    assert storage.get(note_id)["lifecycle"] == "ACTIVE"

    num_iterations = 25
    errors = []
    illegal_escalation_blocks = []
    provenance_mutation_blocks = []

    def human_attester():
        for i in range(num_iterations):
            try:
                controller.attest(
                    Principal.HUMAN,
                    note_id,
                    verification_reason=f"human audit pass {i}",
                    evidence_reference=f"evidence-doc-{i}"
                )
            except Exception as e:
                errors.append(f"Human attester error: {type(e).__name__}: {str(e)}")
            time.sleep(0.001)

    def admin_attester():
        for i in range(num_iterations):
            try:
                controller.attest(
                    Principal.ADMIN,
                    note_id,
                    verification_reason=f"admin audit pass {i}",
                    evidence_reference=f"admin-doc-{i}"
                )
            except Exception as e:
                errors.append(f"Admin attester error: {type(e).__name__}: {str(e)}")
            time.sleep(0.001)

    def ai_legitimate_updater():
        for i in range(num_iterations):
            try:
                controller.update(
                    Principal.AI_AGENT,
                    note_id,
                    {"content": f"updated content iteration {i}", "tags": ["stress", f"iter_{i}"]}
                )
            except Exception as e:
                errors.append(f"AI legitimate updater error: {type(e).__name__}: {str(e)}")
            time.sleep(0.001)

    def ai_illegal_escalator():
        for i in range(num_iterations):
            try:
                controller.update(
                    Principal.AI_AGENT,
                    note_id,
                    {"verification": "verified"}
                )
                errors.append(f"CRITICAL SECURITY FLAW: AI_AGENT successfully escalated verification to verified in iteration {i}!")
            except ValueError as e:
                illegal_escalation_blocks.append(str(e))
            except Exception as e:
                errors.append(f"AI illegal escalator unexpected exception: {type(e).__name__}: {str(e)}")
            time.sleep(0.001)

    def ai_provenance_forger():
        for i in range(num_iterations):
            try:
                controller.update(
                    Principal.AI_AGENT,
                    note_id,
                    {"provenance": {"source_type": "user", "source_ref": "forged"}}
                )
                errors.append(f"CRITICAL SECURITY FLAW: AI_AGENT successfully forged provenance source_type to user in iteration {i}!")
            except ValueError as e:
                provenance_mutation_blocks.append(str(e))
            except Exception as e:
                errors.append(f"AI provenance forger unexpected exception: {type(e).__name__}: {str(e)}")
            time.sleep(0.001)

    def consistency_reader():
        for _ in range(num_iterations * 2):
            try:
                n = storage.get(note_id)
                if n is not None:
                    # Invariants check on live reads
                    assert n["provenance"]["source_type"] == "inference"
                    assert n["verification"] in ("unverified", "verified")
                    if n["verification"] == "verified":
                        assert n.get("verification_source") in ("human", "admin")
            except Exception as e:
                errors.append(f"Consistency reader error: {type(e).__name__}: {str(e)}")
            time.sleep(0.0005)

    threads = [
        threading.Thread(target=human_attester),
        threading.Thread(target=admin_attester),
        threading.Thread(target=ai_legitimate_updater),
        threading.Thread(target=ai_illegal_escalator),
        threading.Thread(target=ai_provenance_forger),
        threading.Thread(target=consistency_reader),
    ]

    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(errors) == 0, f"Encountered unexpected concurrency/security errors: {errors}"
    assert len(illegal_escalation_blocks) == num_iterations
    assert len(provenance_mutation_blocks) == num_iterations

    # Validate final state in SQLite
    final_note = storage.get(note_id)
    assert final_note is not None
    assert final_note["verification"] == "verified"
    assert final_note["verification_source"] in ("human", "admin")
    assert final_note["provenance"]["source_type"] == "inference"

    storage.close()

def test_concurrent_multi_note_attestation_blitz(temp_db_path, test_audit_log):
    """50 distinct notes submitted in REVIEW.
    Concurrently:
    - 4 worker threads (HUMAN/ADMIN) attest odd notes.
    - 4 worker threads (AI_AGENT) attempt to attest even notes (MUST fail with PermissionError).
    - 2 worker threads (AI_AGENT) attempt illegal updates on REVIEW notes (MUST fail with ValueError).
    """
    audit_path, audit_logger = test_audit_log
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True, timeout=10.0)
    controller = MemoryController(storage)

    num_notes = 50
    note_ids = [str(uuid.uuid4()) for _ in range(num_notes)]
    for i, nid in enumerate(note_ids):
        controller.propose(
            Principal.AI_AGENT,
            make_note(nid, lifecycle="REVIEW", verification="unverified", provenance={"source_type": "inference", "source_ref": f"ref-{i}"})
        )

    errors = []
    ai_permission_denied_count = 0
    ai_lifecycle_denied_count = 0
    denied_lock = threading.Lock()

    def human_admin_worker(thread_idx):
        nonlocal errors
        for i in range(1, num_notes, 2):  # odd notes
            try:
                principal = Principal.HUMAN if (thread_idx % 2 == 0) else Principal.ADMIN
                controller.attest(
                    principal,
                    note_ids[i],
                    verification_reason=f"blitz attest by {principal.value}",
                    evidence_reference=f"ref-blitz-{i}"
                )
            except Exception as e:
                errors.append(f"Human/Admin worker error on note {i}: {type(e).__name__}: {str(e)}")

    def ai_attack_worker():
        nonlocal errors, ai_permission_denied_count
        for i in range(0, num_notes, 2):  # even notes
            try:
                controller.attest(
                    Principal.AI_AGENT,
                    note_ids[i],
                    verification_reason="illegal ai self attest",
                    evidence_reference="none"
                )
                errors.append(f"CRITICAL SECURITY FLAW: AI_AGENT attest() succeeded on note {i}!")
            except PermissionError:
                with denied_lock:
                    ai_permission_denied_count += 1
            except Exception as e:
                errors.append(f"AI attacker worker unexpected error: {type(e).__name__}: {str(e)}")

    def ai_updater_worker():
        nonlocal errors, ai_lifecycle_denied_count
        for i in range(1, num_notes, 2):  # odd notes in REVIEW
            try:
                controller.update(
                    Principal.AI_AGENT,
                    note_ids[i],
                    {"content": f"blitz content update for note {i}"}
                )
                errors.append(f"CRITICAL: AI update on REVIEW note succeeded without authorization!")
            except ValueError:
                with denied_lock:
                    ai_lifecycle_denied_count += 1
            except Exception as e:
                errors.append(f"AI updater unexpected error on note {i}: {type(e).__name__}: {str(e)}")

    threads = [
        threading.Thread(target=human_admin_worker, args=(0,)),
        threading.Thread(target=human_admin_worker, args=(1,)),
        threading.Thread(target=human_admin_worker, args=(2,)),
        threading.Thread(target=human_admin_worker, args=(3,)),
        threading.Thread(target=ai_attack_worker),
        threading.Thread(target=ai_attack_worker),
        threading.Thread(target=ai_attack_worker),
        threading.Thread(target=ai_attack_worker),
        threading.Thread(target=ai_updater_worker),
        threading.Thread(target=ai_updater_worker),
    ]

    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(errors) == 0, f"Blitz concurrency errors: {errors}"
    assert ai_permission_denied_count == 4 * (num_notes // 2)
    assert ai_lifecycle_denied_count == 2 * (num_notes // 2)

    # Verify final state of all notes
    for i, nid in enumerate(note_ids):
        note = storage.get(nid)
        assert note is not None
        if i % 2 == 1:
            # Odd notes were attested
            assert note["verification"] == "verified"
            assert note["verification_source"] in ("human", "admin")
        else:
            # Even notes were NEVER attested
            assert note["verification"] == "unverified"
            assert "verification_source" not in note

    storage.close()

# ============================================================================
# Objective 2: Boundary Input Fuzzing for attest() Arguments
# ============================================================================

def test_attest_reason_and_evidence_empty_and_whitespace_rejections(temp_db_path, test_audit_log):
    """Verify that attest() strictly rejects empty or whitespace-only strings
    for verification_reason and evidence_reference without persisting changes in SQLite."""
    audit_path, audit_logger = test_audit_log
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(note_id, lifecycle="REVIEW", verification="unverified"))

    empty_and_whitespace_payloads = [
        "",
        " ",
        "   ",
        "\t",
        "\n",
        "\r\n",
        "  \t  \n \r  ",
    ]

    # Test verification_reason empty/whitespace
    for payload in empty_and_whitespace_payloads:
        with pytest.raises(ValueError, match="Attestation requires a non-empty verification_reason"):
            controller.attest(
                Principal.HUMAN,
                note_id,
                verification_reason=payload,
                evidence_reference="valid-evidence-ref"
            )
        # Verify note state in SQLite is unchanged
        assert storage.get(note_id)["verification"] == "unverified"

    # Test evidence_reference empty/whitespace
    for payload in empty_and_whitespace_payloads:
        with pytest.raises(ValueError, match="Attestation requires a non-empty evidence_reference"):
            controller.attest(
                Principal.HUMAN,
                note_id,
                verification_reason="valid verification reason",
                evidence_reference=payload
            )
        # Verify note state in SQLite is unchanged
        assert storage.get(note_id)["verification"] == "unverified"

    storage.close()

def test_attest_arguments_hostile_payload_fuzzing(temp_db_path, test_audit_log):
    """Fuzz attest() verification_reason and evidence_reference with SQL injection,
    null bytes, Unicode exploits, long strings, XSS, and control characters.
    Assert that inputs are safely stored and audit log integrity is fully preserved."""
    audit_path, audit_logger = test_audit_log
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)

    fuzz_payloads = [
        ("SQL Injection 1", "'; DROP TABLE notes; --", "1' OR '1'='1"),
        ("SQL Injection 2", "' UNION SELECT * FROM notes --", "admin' --"),
        ("Null Byte", "reason with \x00 null byte", "evidence \x00 ref"),
        ("XSS Payload", "<script>alert('xss')</script>", "<img src=x onerror=alert(1)>"),
        ("JSON Injection", '{"tamper": true, "verification": "unverified"}', '{"source_type": "user"}'),
        ("Unicode RTL & Zero Width", "Verified \u202e\u200b\ufeff RTL Override", "Ref \u0000\u001f\u007f Control"),
        ("Unicode Emojis", "Verified 🔍 🔒 🛡️ 🚀 💯 🤖", "Doc 📖 📝 🎯"),
        ("Large String (20KB)", "A" * 20000, "B" * 20000),
        ("Multiline & Escapes", "Line 1\nLine 2\r\nLine 3\t\b\f\\\"'", "Evidence\nDetails\t\r"),
    ]

    for label, reason, evidence in fuzz_payloads:
        nid = str(uuid.uuid4())
        controller.propose(Principal.AI_AGENT, make_note(nid, lifecycle="REVIEW", verification="unverified"))
        
        # Attest with hostile payload
        controller.attest(
            Principal.HUMAN,
            nid,
            verification_reason=reason,
            evidence_reference=evidence
        )

        # Verify state in SQLite
        note = storage.get(nid)
        assert note is not None, f"Failed on payload '{label}': note not found"
        assert note["verification"] == "verified", f"Failed on payload '{label}': verification not verified"
        assert note["verification_source"] == "human", f"Failed on payload '{label}': verification_source wrong"

    # Verify audit log SHA-256 integrity across all fuzzed entries
    is_valid, violations = audit_logger.verify_integrity()
    assert is_valid is True, f"Audit log corrupted by fuzz payloads: {violations}"
    assert len(violations) == 0

    storage.close()

def test_attest_invalid_verification_state_rejection(temp_db_path, test_audit_log):
    """Attesting with an invalid verification_state (not in schema enum) must be rejected
    by schema validation or storage checks without modifying the persistent SQLite database."""
    audit_path, audit_logger = test_audit_log
    storage = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(storage)
    note_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(note_id, lifecycle="REVIEW", verification="unverified"))

    invalid_states = ["invalid_state", "ACTIVE", "RAW", "SUPERSEDED", "12345", ""]
    for inv_state in invalid_states:
        with pytest.raises(Exception):
            controller.attest(
                Principal.HUMAN,
                note_id,
                verification_reason="valid reason",
                evidence_reference="valid ref",
                verification_state=inv_state
            )
        # SQLite persistent state MUST remain unverified
        assert storage.get(note_id)["verification"] == "unverified"

    storage.close()

def test_attest_nonexistent_and_traversal_ids(test_audit_log):
    """Attesting non-existent note IDs or path traversal payloads must raise appropriate errors."""
    audit_path, audit_logger = test_audit_log
    storage = StorageEngine()
    controller = MemoryController(storage)

    # Non-existent note
    with pytest.raises(ValueError, match="Note not found"):
        controller.attest(Principal.HUMAN, str(uuid.uuid4()), "reason", "evidence")

    # Path traversal IDs
    traversal_ids = ["../00_CORE/Rules.md", "../../secret", "..\\..\\windows\\system32"]
    for tid in traversal_ids:
        with pytest.raises(ValueError):
            controller.attest(Principal.HUMAN, tid, "reason", "evidence")

# ============================================================================
# Objective 3: Audit Log SHA-256 Chain Integrity under Security Attacks
# ============================================================================

def test_audit_log_sha256_chain_integrity_under_attack_barrage(test_audit_log):
    """Execute a dense barrage of 70+ hostile operations and security attacks
    interleaved with legitimate operations, verifying that every rejected and successful
    event maintains an unbroken SHA-256 cryptographic hash chain."""
    audit_path, audit_logger = test_audit_log
    storage = StorageEngine()
    controller = MemoryController(storage)

    # Initialize a base note
    base_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(base_id, lifecycle="REVIEW", verification="unverified"))

    # Attack barrage definitions
    attack_scenarios = [
        # 1. AI proposes verified
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), verification="verified")),
        # 2. AI claims official provenance
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), provenance={"source_type": "official", "source_ref": "x"})),
        # 3. AI claims user provenance
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), provenance={"source_type": "user", "source_ref": "x"})),
        # 4. AI claims experience provenance
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), provenance={"source_type": "experience", "source_ref": "x"})),
        # 5. AI claims import provenance
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), provenance={"source_type": "import", "source_ref": "x"})),
        # 6. AI proposes prohibited lifecycle ACTIVE
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), lifecycle="ACTIVE")),
        # 7. AI proposes prohibited lifecycle VERIFIED
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), lifecycle="VERIFIED")),
        # 8. AI proposes prohibited lifecycle SUPERSEDED
        lambda: controller.propose(Principal.AI_AGENT, make_note(str(uuid.uuid4()), lifecycle="SUPERSEDED")),
        # 9. AI calls attest()
        lambda: controller.attest(Principal.AI_AGENT, base_id, "ai attest", "none"),
        # 10. AI escalates verification via update()
        lambda: controller.update(Principal.AI_AGENT, base_id, {"verification": "verified"}),
        # 11. AI mutates provenance source_type via update()
        lambda: controller.update(Principal.AI_AGENT, base_id, {"provenance": {"source_type": "user"}}),
        # 12. Human calls attest() with empty reason
        lambda: controller.attest(Principal.HUMAN, base_id, "", "evidence"),
        # 13. Human calls attest() with whitespace evidence
        lambda: controller.attest(Principal.HUMAN, base_id, "reason", "   "),
        # 14. Path traversal proposal
        lambda: controller.propose(Principal.AI_AGENT, make_note("../../../etc/passwd")),
    ]

    total_attacks = 0
    total_successes = 0

    # Repeat attack barrage for 5 iterations (70 attack attempts total)
    for iteration in range(5):
        for attack_fn in attack_scenarios:
            try:
                attack_fn()
                pytest.fail("Security attack did not raise an exception!")
            except (ValueError, PermissionError):
                total_attacks += 1
            except Exception as e:
                pytest.fail(f"Unexpected exception type for security attack: {type(e).__name__}: {str(e)}")

        # Interleave with legitimate actions
        legit_id = str(uuid.uuid4())
        controller.propose(Principal.AI_AGENT, make_note(legit_id, lifecycle="REVIEW", verification="unverified"))
        controller.attest(Principal.HUMAN, legit_id, f"legit verification {iteration}", f"evidence-{iteration}")
        controller.promote(Principal.HUMAN, legit_id)
        controller.update(Principal.HUMAN, legit_id, {"content": f"legit update {iteration}"})
        total_successes += 4

    # Verify audit log integrity
    is_valid, violations = audit_logger.verify_integrity()
    assert is_valid is True, f"Hash chain integrity broken after attack barrage: {violations}"
    assert len(violations) == 0

    # Verify audit records count and content
    with open(audit_path, "r", encoding="utf-8") as f:
        entries = [json.loads(line) for line in f if line.strip()]

    error_entries = [e for e in entries if e["outcome"] == "error"]
    success_entries = [e for e in entries if e["outcome"] == "success"]

    assert len(error_entries) == total_attacks
    assert len(success_entries) >= total_successes

    # Verify exact genesis and linkage
    assert entries[0]["prev_hash"] == "GENESIS"
    for i in range(1, len(entries)):
        assert entries[i]["prev_hash"] == entries[i - 1]["entry_hash"]

def test_audit_log_concurrent_multithreaded_attack_barrage(test_audit_log):
    """Stress test multi-threaded logging where multiple threads trigger both security
    rejections and successful events concurrently, verifying the hash chain stays intact."""
    audit_path, audit_logger = test_audit_log
    storage = StorageEngine()
    controller = MemoryController(storage)

    num_threads = 6
    iterations_per_thread = 15
    errors = []
    log_lock = threading.Lock()

    def attack_and_audit_worker(worker_id):
        for i in range(iterations_per_thread):
            # 1. Trigger failed security attempt
            with log_lock:
                try:
                    controller.propose(
                        Principal.AI_AGENT,
                        make_note(str(uuid.uuid4()), verification="verified")
                    )
                except ValueError:
                    pass
                except Exception as e:
                    errors.append(f"Worker {worker_id} attack error: {str(e)}")

            # 2. Trigger successful legitimate action
            with log_lock:
                try:
                    nid = str(uuid.uuid4())
                    controller.propose(
                        Principal.AI_AGENT,
                        make_note(nid, lifecycle="REVIEW", verification="unverified")
                    )
                    controller.attest(
                        Principal.HUMAN,
                        nid,
                        verification_reason=f"worker {worker_id} attest {i}",
                        evidence_reference=f"doc-{worker_id}-{i}"
                    )
                except Exception as e:
                    errors.append(f"Worker {worker_id} legit action error: {str(e)}")

    threads = [threading.Thread(target=attack_and_audit_worker, args=(w,)) for w in range(num_threads)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(errors) == 0, f"Errors during concurrent attack/audit: {errors}"

    is_valid, violations = audit_logger.verify_integrity()
    assert is_valid is True, f"Concurrent audit log hash chain broken: {violations}"
    assert len(violations) == 0

    with open(audit_path, "r", encoding="utf-8") as f:
        entries = [json.loads(line) for line in f if line.strip()]

    # Expected: 1 propose error + 1 propose success + 1 attest success per iteration per thread
    assert len(entries) == num_threads * iterations_per_thread * 3

# ============================================================================
# Additional Adversarial Objectives: ToolRouter & Continual Learning Boundaries
# ============================================================================

def test_tool_router_unauthorized_attest_and_high_risk_actions(test_audit_log):
    """Verify that ToolRouter strictly rejects unauthorized actions and high-risk operations:
    - 'attest' (defaults to HIGH risk -> ApprovalRequiredError)
    - 'delete_canonical' (HIGH risk -> ApprovalRequiredError)
    - 'modify_raw_imports' (HIGH risk -> ApprovalRequiredError)
    """
    audit_path, audit_logger = test_audit_log
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)

    # 1. AI tries to execute 'attest' through ToolRouter
    with pytest.raises(ApprovalRequiredError, match="HIGH RISK and requires explicit user approval"):
        router.execute(Principal.AI_AGENT, "attest", {
            "note_id": str(uuid.uuid4()),
            "verification_reason": "router attest attack",
            "evidence_reference": "none"
        })

    # 2. AI tries 'delete_canonical'
    with pytest.raises(ApprovalRequiredError, match="HIGH RISK"):
        router.execute(Principal.AI_AGENT, "delete_canonical", {"note_id": str(uuid.uuid4())})

    # 3. AI tries 'modify_raw_imports'
    with pytest.raises(ApprovalRequiredError, match="HIGH RISK"):
        router.execute(Principal.AI_AGENT, "modify_raw_imports", {"target": "06_INBOX/RAW_IMPORTS"})

def test_tool_router_guards_verified_note_against_supersession_and_archival(test_audit_log):
    """Verify that ToolRouter prevents automatic modification, supersession,
    or archival of human-verified notes (BRAIN-13)."""
    audit_path, audit_logger = test_audit_log
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)

    verified_id = str(uuid.uuid4())
    controller.propose(Principal.ADMIN, make_note(verified_id, lifecycle="REVIEW", provenance={"source_type": "user", "source_ref": "official-spec"}))
    controller.attest(Principal.HUMAN, verified_id, "Human verified ground truth", "spec-v1")
    controller.promote(Principal.HUMAN, verified_id)

    assert storage.get(verified_id)["verification"] == "verified"
    assert storage.get(verified_id)["lifecycle"] == "ACTIVE"

    # Attempt 1: AI_AGENT tries to update verified note via ToolRouter
    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.AI_AGENT, "update", {
            "note_id": verified_id,
            "updates": {"content": "tampered content"}
        })

    # Attempt 2: AI_AGENT tries to archive verified note via ToolRouter
    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.AI_AGENT, "archive", {
            "note_id": verified_id,
            "reason": "adversarial archive attempt"
        })

    # Attempt 3: AI_AGENT tries to supersede verified note via ToolRouter
    new_id = str(uuid.uuid4())
    controller.propose(Principal.AI_AGENT, make_note(new_id, lifecycle="REVIEW", verification="unverified"))
    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.AI_AGENT, "supersede", {
            "old_id": verified_id,
            "new_id": new_id,
            "evidence": "adversarial supersession"
        })

def test_continual_learning_confidence_promotion_requires_execution_provenance(test_audit_log):
    """Verify that LearningEngine:
    1. Promotes confidence to 'very_high' ONLY when source_type == 'execution'.
    2. Refuses promotion to 'very_high' when source_type == 'inference' or 'ai'.
    3. Even when promoted to 'very_high', verification state becomes 'partially_verified', NEVER 'verified'.
    """
    audit_path, audit_logger = test_audit_log
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)

    # 1. Note with source_type='inference', high confidence, 10 relations (>= threshold*3 = 9)
    inf_id = str(uuid.uuid4())
    inf_relations = [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(10)]
    inf_note = make_note(inf_id, lifecycle="ACTIVE", verification="unverified", provenance={"source_type": "inference", "source_ref": "inf-gen"})
    inf_note["confidence"] = "high"
    inf_note["relations"] = inf_relations
    storage.set(inf_id, inf_note)

    # 2. Note with source_type='execution', high confidence, 10 relations
    exec_id = str(uuid.uuid4())
    exec_relations = [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(10)]
    exec_note = make_note(exec_id, lifecycle="ACTIVE", verification="unverified", provenance={"source_type": "execution", "source_ref": "test-runner"})
    exec_note["confidence"] = "high"
    exec_note["relations"] = exec_relations
    storage.set(exec_id, exec_note)

    promoted_ids = engine.promote_memories(Principal.AI_AGENT)

    # Execution note promoted
    assert exec_id in promoted_ids
    updated_exec = storage.get(exec_id)
    assert updated_exec["confidence"] == "very_high"
    assert updated_exec["verification"] == "partially_verified"  # MUST NOT BE 'verified'

    # Inference note NOT promoted to very_high
    assert inf_id not in promoted_ids
    unchanged_inf = storage.get(inf_id)
    assert unchanged_inf["confidence"] == "high"
    assert unchanged_inf["verification"] == "unverified"

def test_continual_learning_guard_detects_anchor_corruption():
    """Verify that ContinualLearningGuard detects removal or tampering of anchor notes."""
    guard = ContinualLearningGuard()
    
    anchor_id = str(uuid.uuid4())
    anchor_note = make_note(anchor_id, lifecycle="ACTIVE", verification="verified", content="Permanent ground truth")
    guard.register_anchor_node(anchor_note)

    # Test 1: Intact storage notes
    passed, violations = guard.verify_no_catastrophic_regression([anchor_note])
    assert passed is True
    assert len(violations) == 0

    # Test 2: Anchor removed from storage
    passed, violations = guard.verify_no_catastrophic_regression([])
    assert passed is False
    assert len(violations) >= 1
    assert any(anchor_id in v for v in violations)
