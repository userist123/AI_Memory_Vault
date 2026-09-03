import json
import pytest
import os
import subprocess
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from cognitive_core.recall_cli import (
    search_markdown_vault,
    get_memory_controller,
    validate_hmac_secret,
    MissingHMACSecretError,
    InvalidHMACSecretError,
)
from cognitive_core.orchestrator import (
    MultiAgentDispatcher,
    UnknownAgentRoleError,
    AgentRole,
)

VALID_HMAC_SECRET = "test_secret_for_recall_cli_32chars_min"

@pytest.fixture(autouse=True)
def setup_test_hmac_secret(monkeypatch):
    """Ensure a valid test HMAC secret is available by default for tests."""
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", VALID_HMAC_SECRET)


# ============================================================================
# PART A: HMAC Secret Hardening Tests
# ============================================================================

def test_recall_cli_missing_secret_fails_closed(monkeypatch):
    """A2: Missing secret fails closed with clear actionable error; no fallback."""
    monkeypatch.delenv("MEMORY_CONTROLLER_HMAC_SECRET", raising=False)
    with pytest.raises(MissingHMACSecretError, match="MEMORY_CONTROLLER_HMAC_SECRET environment variable is missing"):
        validate_hmac_secret()

    with pytest.raises(MissingHMACSecretError, match="MEMORY_CONTROLLER_HMAC_SECRET environment variable is missing"):
        search_markdown_vault("SQLite WAL")


def test_recall_cli_invalid_secret_too_short_fails_closed(monkeypatch):
    """A3: Invalid/too short secret fails closed with clear actionable error."""
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "short_secret_123")
    with pytest.raises(InvalidHMACSecretError, match="must be at least 32 characters"):
        validate_hmac_secret()

    with pytest.raises(InvalidHMACSecretError, match="must be at least 32 characters"):
        search_markdown_vault("SQLite WAL")


def test_recall_cli_valid_secret_succeeds(monkeypatch):
    """A1: Present and valid secret is accepted and functions normally."""
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "a" * 32)
    assert validate_hmac_secret() == "a" * 32
    results = search_markdown_vault("SQLite WAL", max_results=2)
    assert isinstance(results, list)


def test_recall_cli_cli_subprocess_missing_secret_fails_closed():
    """A2: Subprocess execution without HMAC secret fails closed with non-zero exit."""
    env = {k: v for k, v in os.environ.items() if k != "MEMORY_CONTROLLER_HMAC_SECRET"}
    cmd = [os.sys.executable, "cognitive_core/recall_cli.py", "--query", "knowledge"]
    res = subprocess.run(cmd, capture_output=True, text=True, env=env)
    assert res.returncode != 0
    assert "MEMORY_CONTROLLER_HMAC_SECRET" in res.stderr


# ============================================================================
# PART B: MultiAgentDispatcher Role Routing Tests
# ============================================================================

def test_dispatcher_rejects_unknown_role():
    """B1: Unknown/unsupported role is rejected deterministically."""
    dispatcher = MultiAgentDispatcher()
    with pytest.raises(UnknownAgentRoleError, match="Unknown or unsupported agent role"):
        dispatcher.dispatch(
            agent_role="unsupported_agent_xyz",
            system_prompt="Test system prompt",
            user_input="Test query"
        )


def test_dispatcher_routes_role_retrieval_to_retrieval_worker():
    """B2: Role 'retrieval' maps to intended worker and performs deep retrieval."""
    dispatcher = MultiAgentDispatcher()
    response_str = dispatcher.dispatch(
        agent_role="retrieval",
        system_prompt="Test system prompt",
        user_input="search architecture"
    )
    data = json.loads(response_str)
    assert data["status"] == "completed"
    assert data["worker"] == "retrieval"
    assert data["model_tier"] == "light"
    assert data["action"] == "deep_retrieval"
    assert "retrieved_nodes" in data
    assert "retrieved_count" in data


def test_dispatcher_routes_role_verifier_to_verifier_worker():
    """B2: Role 'verifier' maps to intended worker and audits verification."""
    dispatcher = MultiAgentDispatcher()
    response_str = dispatcher.dispatch(
        agent_role="verifier",
        system_prompt="Node content to verify",
        user_input="audit verification status"
    )
    data = json.loads(response_str)
    assert data["status"] == "completed"
    assert data["worker"] == "verifier"
    assert data["model_tier"] == "light"
    assert data["action"] == "verify_claims"
    assert "audit" in data
    assert "verified_nodes" in data["audit"]
    assert "unverified_nodes" in data["audit"]


def test_dispatcher_routes_role_critic_to_critic_worker():
    """B2: Role 'critic' maps to intended worker and performs reflection/critique."""
    dispatcher = MultiAgentDispatcher()
    response_str = dispatcher.dispatch(
        agent_role="critic",
        system_prompt="System instructions",
        user_input="evaluate reasoning consistency"
    )
    data = json.loads(response_str)
    assert data["status"] == "completed"
    assert data["worker"] == "critic"
    assert data["model_tier"] == "standard"
    assert data["action"] == "critique_and_reflect"
    assert "critique" in data


def test_dispatcher_different_roles_do_not_collapse():
    """B3: Different supported roles do not silently collapse to the same generic worker."""
    dispatcher = MultiAgentDispatcher()
    res_retrieval = json.loads(dispatcher.dispatch("retrieval", "", "test query"))
    res_verifier = json.loads(dispatcher.dispatch("verifier", "", "test query"))
    res_critic = json.loads(dispatcher.dispatch("critic", "", "test query"))
    res_router = json.loads(dispatcher.dispatch("router", "", "test query"))

    # Assert distinct workers
    assert res_retrieval["worker"] == "retrieval"
    assert res_verifier["worker"] == "verifier"
    assert res_critic["worker"] == "critic"
    assert res_router["worker"] == "router"

    # Assert distinct actions
    assert res_retrieval["action"] == "deep_retrieval"
    assert res_verifier["action"] == "verify_claims"
    assert res_critic["action"] == "critique_and_reflect"
    assert res_router["action"] == "triage_and_route"

    # Assert model tiers reflect SubagentSpec
    assert res_retrieval["model_tier"] == "light"
    assert res_verifier["model_tier"] == "light"
    assert res_critic["model_tier"] == "standard"


# ============================================================================
# Existing Recall CLI & Security Boundary Tests
# ============================================================================

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
    results = search_markdown_vault("xyz123", max_results=5, controller=controller)
    assert not any(r["id"] == "note-raw-secret-002" for r in results)
    assert not any(r["lifecycle"] == Lifecycle.RAW.value for r in results)
    assert not any("xyz123" in r.get("content", "") for r in results)

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
    """Verify that MultiAgentDispatcher executes and routes tasks."""
    dispatcher = MultiAgentDispatcher()
    response = dispatcher.dispatch(
        agent_role="router",
        system_prompt="Test system prompt",
        user_input="search architecture"
    )
    assert isinstance(response, str)
    assert "completed" in response


def test_recall_cli_cli_subprocess():
    """Verify that recall_cli runs cleanly from terminal with valid HMAC secret."""
    env = {**os.environ, "MEMORY_CONTROLLER_HMAC_SECRET": VALID_HMAC_SECRET}
    cmd = [os.sys.executable, "cognitive_core/recall_cli.py", "--query", "knowledge"]
    res = subprocess.run(cmd, capture_output=True, text=True, env=env)
    assert res.returncode == 0
    assert "MEMORIE VAULT SECURIZATA" in res.stdout

