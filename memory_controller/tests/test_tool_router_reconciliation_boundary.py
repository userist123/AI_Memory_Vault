import uuid

import pytest

from cognitive_core.tool_router import ApprovalRequiredError, ToolRouter
from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def _note(note_id: str):
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.REVIEW.value,
        "category": "security-adversarial",
        "tags": ["brain-13"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": {"source_type": "user", "source_ref": "human-review"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "verified review boundary regression",
    }


def test_admin_cannot_archive_verified_review_memory(tmp_path):
    storage = SQLiteStorageEngine(str(tmp_path / "router.sqlite3"))
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    note_id = str(uuid.uuid4())

    controller.propose(Principal.HUMAN, _note(note_id))
    controller.attest(Principal.HUMAN, note_id, "human verification", "review-evidence")

    assert storage.get(note_id)["lifecycle"] == Lifecycle.REVIEW.value
    assert storage.get(note_id)["verification"] == "verified"

    with pytest.raises(ApprovalRequiredError, match="targets a human-verified memory"):
        router.execute(Principal.ADMIN, "archive", {"note_id": note_id, "reason": "router regression"})

    assert storage.get(note_id)["lifecycle"] == Lifecycle.REVIEW.value
