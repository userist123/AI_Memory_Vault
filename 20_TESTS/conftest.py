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
            "provenance": {
                "source_type": "user",
                "source_ref": "pytest-fixture",
            },
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


@pytest.fixture(autouse=True)
def book_to_memory_test_hmac_secret(request: pytest.FixtureRequest, monkeypatch):
    """Give the Book-to-Memory approval-token tests a throwaway, per-test HMAC secret.

    `book_to_memory_lifecycle` has no fallback secret: without one it refuses to issue or
    verify an owner-approval token. These tests need a working signer, so they get a random
    secret that exists only for the test. Tests that assert the fail-closed behaviour
    delete it themselves.
    """
    module = getattr(request.node, "module", None)
    if module is None or not module.__name__.split(".")[-1].startswith("test_book_to_memory"):
        return
    import secrets

    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", secrets.token_urlsafe(48))
