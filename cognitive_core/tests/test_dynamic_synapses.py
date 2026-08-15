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
    
    # Verify update was called with only {"relations": ...}
    mock_controller.update.assert_called_once()
    args, kwargs = mock_controller.update.call_args
    assert args[0] == Principal.AI_AGENT
    assert args[1] == "node-A"
    updates = args[2]
    assert "relations" in updates
    assert len(updates["relations"]) == 1
    assert updates["relations"][0]["target_id"] == "node-B"
    assert updates["relations"][0]["relation"] == "related_to"
    assert updates["relations"][0]["target"] == "knowledge"

def test_propose_synapse_duplicate():
    mock_controller = MagicMock()
    # Mock reading the source node with existing relation
    mock_controller.read.return_value = {
        "results": [{
            "id": "node-A",
            "type": "knowledge",
            "relations": [{
                "target_id": "node-B",
                "relation": "related_to",
                "target": "knowledge"
            }]
        }]
    }
    mock_controller.update = MagicMock()
    
    pipeline = ReflectionPipeline(mock_controller)
    
    result = pipeline.propose_synapse(Principal.AI_AGENT, "node-A", "node-B")
    # Should return None and NOT call update
    assert result is None
    mock_controller.update.assert_not_called()

def test_propose_synapse_real_controller_schema_validation():
    """Verify propose_synapse against a real MemoryController with active verified notes."""
    import uuid
    from memory_controller.controller import MemoryController, StorageEngine
    from memory_controller.validation.schema import validate_frontmatter

    storage = StorageEngine()
    controller = MemoryController(storage)
    pipeline = ReflectionPipeline(controller)

    u1 = str(uuid.uuid4())
    u2 = str(uuid.uuid4())
    note1 = {
        "id": u1,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "architecture",
        "tags": ["core"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "user", "source_ref": "user-session"},
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "content": "Source node content"
    }
    note2 = {
        "id": u2,
        "type": "procedure",
        "lifecycle": "ACTIVE",
        "category": "runbook",
        "tags": ["procedure"],
        "created": "2026-08-15",
        "updated": "2026-08-15",
        "provenance": {"source_type": "user", "source_ref": "user-session"},
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "content": "Target procedure node content"
    }
    storage.set(u1, note1)
    storage.set(u2, note2)

    # Propose synapse
    result = pipeline.propose_synapse(Principal.AI_AGENT, u1, u2, "implements")
    assert result == u1

    # Verify storage updated with canonical schema
    updated_note = storage.get(u1)
    assert updated_note is not None
    assert len(updated_note["relations"]) == 1
    rel = updated_note["relations"][0]
    assert rel["relation"] == "implements"
    assert rel["target"] == "procedure"
    assert rel["target_id"] == u2
    # Verify canonical schema passes validator
    frontmatter = {k: v for k, v in updated_note.items() if k != "content"}
    assert validate_frontmatter(frontmatter) is True
