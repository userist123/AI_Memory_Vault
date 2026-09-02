import pytest
import os
import subprocess
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.recall_cli import search_markdown_vault, get_memory_controller
from cognitive_core.orchestrator import MultiAgentDispatcher

def test_search_markdown_vault_valid_query():
    """Verify that search_markdown_vault returns valid results for a normal query."""
    results = search_markdown_vault("SQLite WAL", max_results=3)
    assert isinstance(results, list)
    assert len(results) <= 3
    for r in results:
        assert "id" in r
        assert "file" in r
        assert "score" in r
        assert "lifecycle" in r
        assert r["lifecycle"] != "RAW"


def test_search_markdown_vault_p0_p15_raw_lifecycle_excluded():
    """P0-P15 Invariant: RAW lifecycle notes must NEVER be exposed via search."""
    storage = StorageEngine()
    # Insert one ACTIVE note and one RAW note
    storage.set("note-active-001", {
        "id": "note-active-001",
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "content": "Canonical documentation about architecture patterns",
        "verification": "verified"
    })
    storage.set("note-raw-secret-002", {
        "id": "note-raw-secret-002",
        "type": "knowledge",
        "lifecycle": Lifecycle.RAW.value,
        "content": "Unverified raw secret content containing API_KEY=xyz123",
        "verification": "unverified"
    })

    controller = MemoryController(storage)
    # Search specifically for the secret token in raw note
    results = search_markdown_vault("xyz123", max_results=5, controller=controller)
    # Must NOT return the RAW note or any secret data from it
    assert not any(r["id"] == "note-raw-secret-002" for r in results)
    assert not any(r["lifecycle"] == Lifecycle.RAW.value for r in results)
    assert not any("xyz123" in r.get("content", "") for r in results)

    # Search for canonical architecture
    results_active = search_markdown_vault("architecture", max_results=5, controller=controller)
    assert len(results_active) == 1
    assert results_active[0]["id"] == "note-active-001"
    assert results_active[0]["lifecycle"] == "ACTIVE"


def test_search_markdown_vault_oversized_query_rejected():
    """P0-P15 Security: Oversized queries exceeding boundary limit must be rejected."""
    oversized = "A" * 5000
    with pytest.raises(ValueError, match="exceeds maximum allowed"):
        search_markdown_vault(oversized, max_results=3)


def test_multi_agent_dispatcher_execution():
    """Verify that MultiAgentDispatcher executes without ImportError and routes tasks."""
    dispatcher = MultiAgentDispatcher()
    response = dispatcher.dispatch(
        agent_role="router",
        system_prompt="Test system prompt",
        user_input="search architecture"
    )
    assert isinstance(response, str)
    assert "completed" in response


def test_recall_cli_cli_subprocess():
    """Verify that recall_cli runs cleanly from terminal."""
    cmd = [os.sys.executable, "cognitive_core/recall_cli.py", "--query", "knowledge"]
    res = subprocess.run(cmd, capture_output=True, text=True)
    assert res.returncode == 0
    assert "MEMORIE VAULT SECURIZATA" in res.stdout
