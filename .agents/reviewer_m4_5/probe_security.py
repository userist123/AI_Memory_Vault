import sys
import os
import uuid
import tempfile
import pytest
sys.path.insert(0, os.path.abspath("."))

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from memory_controller.audit.logger import AuditLogger
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine

os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = "p0-p15-test-secret-12345"

storage = StorageEngine()
controller = MemoryController(storage)

# 1. AI_AGENT cannot propose verification='verified' (P0-001)
u1 = str(uuid.uuid4())
note1 = {
    "id": u1,
    "type": "knowledge",
    "lifecycle": "REVIEW",
    "category": "test",
    "tags": ["test"],
    "created": "2026-08-15",
    "updated": "2026-08-15",
    "provenance": {"source_type": "ai", "source_ref": "test"},
    "confidence": "medium",
    "verification": "verified",
    "content": "Test content",
    "relations": []
}
try:
    controller.propose(Principal.AI_AGENT, note1)
    assert False, "Should reject AI self-verification"
except (PermissionError, ValueError):
    print("Security Check 1 (Reject AI self-verification): PASSED")

# 2. AI_AGENT cannot propose privileged source_type 'user' (P0-003)
note2 = {
    "id": str(uuid.uuid4()),
    "type": "knowledge",
    "lifecycle": "REVIEW",
    "category": "test",
    "tags": ["test"],
    "created": "2026-08-15",
    "updated": "2026-08-15",
    "provenance": {"source_type": "user", "source_ref": "test"},
    "confidence": "medium",
    "verification": "unverified",
    "content": "Test content",
    "relations": []
}
try:
    controller.propose(Principal.AI_AGENT, note2)
    assert False, "Should reject AI forging user provenance"
except (PermissionError, ValueError):
    print("Security Check 2 (Reject AI forging user provenance): PASSED")

# 3. AI_AGENT cannot propose directly to ACTIVE (P0-004)
note3 = {
    "id": str(uuid.uuid4()),
    "type": "knowledge",
    "lifecycle": "ACTIVE",
    "category": "test",
    "tags": ["test"],
    "created": "2026-08-15",
    "updated": "2026-08-15",
    "provenance": {"source_type": "ai", "source_ref": "test"},
    "confidence": "medium",
    "verification": "unverified",
    "content": "Test content",
    "relations": []
}
try:
    controller.propose(Principal.AI_AGENT, note3)
    assert False, "Should reject AI proposing directly to ACTIVE"
except (PermissionError, ValueError):
    print("Security Check 3 (Reject AI proposing to ACTIVE): PASSED")

# 4. AI_AGENT cannot invoke attest (P0-002)
valid_review_id = str(uuid.uuid4())
valid_review_note = {
    "id": valid_review_id,
    "type": "knowledge",
    "lifecycle": "REVIEW",
    "category": "test",
    "tags": ["test"],
    "created": "2026-08-15",
    "updated": "2026-08-15",
    "provenance": {"source_type": "inference", "source_ref": "test"},
    "confidence": "medium",
    "verification": "unverified",
    "content": "Valid review note content",
    "relations": []
}
storage.set(valid_review_id, valid_review_note)

try:
    controller.attest(Principal.AI_AGENT, valid_review_id, verification_reason="User tested", evidence_reference="Manual QA")
    assert False, "Should reject AI_AGENT attestation"
except (PermissionError, ValueError):
    print("Security Check 4 (Reject AI attestation): PASSED")

# 5. HUMAN can attest and promote to verified
controller.attest(Principal.HUMAN, valid_review_id, verification_reason="User tested", evidence_reference="Manual QA")
attested = storage.get(valid_review_id)
assert attested["verification"] == "verified"
assert attested["verification_source"] == "human"
print("Security Check 5 (Human attestation succeeds): PASSED")

print("ALL SECURITY P0-P15 ADVERSARIAL CHECKS PASSED")
