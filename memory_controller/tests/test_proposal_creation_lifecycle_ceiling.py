from datetime import datetime, timezone

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle


def _note(note_id: str, lifecycle: str):
    today = datetime.now(timezone.utc).date().isoformat()
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": today,
        "updated": today,
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "proposal",
    }


@pytest.mark.parametrize("principal", list(Principal))
@pytest.mark.parametrize("lifecycle", [Lifecycle.VERIFIED.value, Lifecycle.ACTIVE.value, Lifecycle.RECONSOLIDATING.value])
def test_propose_rejects_privileged_creation_lifecycle_for_every_principal(principal, lifecycle):
    controller = MemoryController(StorageEngine())

    with pytest.raises(ValueError, match="cannot set lifecycle"):
        controller.propose(principal, _note(f"00000000-0000-4000-8000-{abs(hash((principal, lifecycle))) % 10**12:012d}", lifecycle))
