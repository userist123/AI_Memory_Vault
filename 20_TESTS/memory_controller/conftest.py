import pytest
import os
import memory_controller.controller as ctrl_module
from memory_controller.controller import MemoryController, StorageEngine

# We globally override the controller's storage engine with an in-memory StorageEngine
# during test collection and execution to protect the real Vault and keep legacy tests green.
# FileStorageEngine is explicitly tested in test_storage.py by instantiating it directly.
ctrl_module._storage_engine = StorageEngine()
ctrl_module.controller = MemoryController(ctrl_module._storage_engine)

@pytest.fixture(autouse=True)
def ensure_hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_key")

