"""Process-wide factory for the secured MemoryController and HMAC secret validation.

Lives in the memory layer (L4) so that lower or equal layers can obtain a
controller without importing the CLI. ``interfaces.recall_cli`` re-exports
``get_memory_controller``, ``validate_hmac_secret`` and the two error classes
under the same names.
"""
import os
from pathlib import Path
from typing import Optional

from memory_controller.controller import MemoryController
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine

MIN_HMAC_SECRET_LENGTH = 32

_REPO_ROOT = Path(__file__).resolve().parents[3]
if (_REPO_ROOT / "AGENTS.md").exists():
    VAULT_ROOT = str(_REPO_ROOT)
else:
    VAULT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))


class MissingHMACSecretError(ValueError):
    """Raised when MEMORY_CONTROLLER_HMAC_SECRET is missing from the environment."""
    pass


class InvalidHMACSecretError(ValueError):
    """Raised when MEMORY_CONTROLLER_HMAC_SECRET is present but invalid or too short."""
    pass


def validate_hmac_secret() -> str:
    """Validates that MEMORY_CONTROLLER_HMAC_SECRET is present in the environment and valid.

    A1: If present and valid (>= 32 chars): returns the secret string.
    A2: If missing: fails closed with MissingHMACSecretError.
    A3: If invalid / too short (< 32 chars): fails closed with InvalidHMACSecretError.
    A4: No hardcoded fallback or invented secret.
    """
    secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET")
    if not secret:
        raise MissingHMACSecretError(
            "MEMORY_CONTROLLER_HMAC_SECRET environment variable is missing. "
            "Please set MEMORY_CONTROLLER_HMAC_SECRET with at least 32 characters."
        )
    if len(secret.strip()) < MIN_HMAC_SECRET_LENGTH:
        raise InvalidHMACSecretError(
            f"MEMORY_CONTROLLER_HMAC_SECRET is invalid: must be at least {MIN_HMAC_SECRET_LENGTH} characters "
            f"(got {len(secret.strip())})."
        )
    return secret


_CACHED_CONTROLLER: Optional[MemoryController] = None
_CACHED_CONTROLLER_ROOT: Optional[str] = None


def _effective_vault_root() -> str:
    """Resolve the runtime vault root, allowing deterministic test/embedded deployments."""
    configured = os.getenv("MEMORY_VAULT_ROOT")
    root = Path(configured).expanduser() if configured else Path(VAULT_ROOT)
    return str(root.resolve())


def get_memory_controller() -> MemoryController:
    """Initializeaza si returneaza instanta securizata de MemoryController."""
    global _CACHED_CONTROLLER, _CACHED_CONTROLLER_ROOT
    vault_root = _effective_vault_root()
    if _CACHED_CONTROLLER is not None and _CACHED_CONTROLLER_ROOT == vault_root:
        return _CACHED_CONTROLLER

    db_path = os.path.join(vault_root, "vault_memory.sqlite3")
    if os.path.exists(db_path):
        try:
            storage = SQLiteStorageEngine(db_path, wal_mode=True)
            with storage._get_connection() as conn:
                cur = conn.cursor()
                cur.execute("SELECT COUNT(*) FROM notes")
                count = cur.fetchone()[0]
            if count > 0:
                _CACHED_CONTROLLER = MemoryController(storage)
                _CACHED_CONTROLLER_ROOT = vault_root
                return _CACHED_CONTROLLER
        except Exception:
            pass

    # Fallback catre FileStorageEngine care scaneaza folderele canonice.
    # MEMORY_VAULT_ROOT permite rularea in alt vault fara a modifica codul.
    storage = FileStorageEngine(vault_root)
    _CACHED_CONTROLLER = MemoryController(storage)
    _CACHED_CONTROLLER_ROOT = vault_root
    return _CACHED_CONTROLLER
