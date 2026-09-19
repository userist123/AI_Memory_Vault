"""Deterministic runtime-vault fixtures for tests that exercise the real CLI path."""
from __future__ import annotations

from pathlib import Path

import pytest

from memory_controller.storage.file_engine import FileStorageEngine


_RETRIEVAL_FIXTURE_TESTS = {
    "test_treatment_condition_executes_secure_retrieval",
    "test_recall_cli_cli_subprocess",
}


@pytest.fixture(autouse=True)
def isolated_runtime_vault(request: pytest.FixtureRequest, tmp_path_factory, monkeypatch):
    """Provide a populated temporary vault only to tests requiring global CLI discovery."""
    if request.node.name not in _RETRIEVAL_FIXTURE_TESTS:
        return

    root = Path(tmp_path_factory.mktemp("runtime_vault"))
    storage = FileStorageEngine(str(root))
    storage.set(
        "fixture-circuit-breaker",
        {
            "id": "fixture-circuit-breaker",
            "type": "knowledge",
            "category": "test",
            "lifecycle": "ACTIVE",
            "verification": "verified",
            "created": "2026-01-01T00:00:00Z",
            "updated": "2026-01-01T00:00:00Z",
            "tags": ["circuit", "breaker", "retrieval"],
            "content": (
                "Deterministic test knowledge: circuit breaker pattern states, "
                "including closed, open, and half-open transitions."
            ),
        },
    )
    monkeypatch.setenv("MEMORY_VAULT_ROOT", str(root))


@pytest.fixture(autouse=True)
def isolate_audit_log(monkeypatch, tmp_path_factory):
    """Ensure audit logger defaults to a temporary directory instead of repository root."""
    tmp_art = tmp_path_factory.mktemp("artifacts")
    monkeypatch.setenv("ANTIGRAVITY_ARTIFACT_DIR", str(tmp_art))


@pytest.fixture(autouse=True)
def isolate_vault_runtime_home(monkeypatch, tmp_path_factory):
    """Keep the tests away from the owner's real per-user directory.

    The HMAC secret, the usage log and the audit trail of the vault interfaces live in
    %APPDATA%/ai-memory-vault (or the XDG equivalent). A test that runs the CLI or the MCP server
    must never write a line into the real usage log: it would count as real use in
    memory_usage_report.py. Tests that need a specific directory set AI_MEMORY_VAULT_HOME themselves.
    """
    monkeypatch.setenv("AI_MEMORY_VAULT_HOME", str(tmp_path_factory.mktemp("vault_home")))
