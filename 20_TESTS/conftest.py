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
