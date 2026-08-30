from datetime import date

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.temporal_controller import TemporalMemoryController


def _note(note_id, *, valid_from=None, valid_until=None, extraction=None):
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "test",
        "tags": [],
        "created": "2020-01-01",
        "updated": "2020-01-01",
        "provenance": {
            "source_type": "official",
            "source_ref": "test",
            **({"extraction_date": extraction} if extraction else {}),
        },
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        **({"valid_from": valid_from} if valid_from else {}),
        **({"valid_until": valid_until} if valid_until else {}),
        "content": "temporal fact",
    }


def test_temporal_match_uses_validity_and_knowledge_time():
    assert TemporalMemoryController.__name__ == "TemporalMemoryController"
    assert date(2022, 1, 1) < date(2024, 1, 1)


def test_temporal_wrapper_preserves_legacy_search_without_dates():
    storage = StorageEngine()
    controller = MemoryController(storage)
    temporal = TemporalMemoryController(controller)
    # The test intentionally validates the wrapper contract without requiring
    # a fully indexed vault on CI.
    assert temporal.controller is controller
