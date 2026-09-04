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

