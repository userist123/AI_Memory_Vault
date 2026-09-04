"""Canonical HTTP API entrypoint for the Memory Vault."""
from __future__ import annotations

import os
import runpy
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[5]
os.environ.setdefault("AI_MEMORY_VAULT_ROOT", str(REPO_ROOT))
_TARGET = REPO_ROOT / "03_IMPLEMENTATION" / "packages" / "interfaces" / "api_server.py"
if not _TARGET.exists():
    raise ImportError(f"API gateway source not found: {_TARGET}")

runpy.run_path(str(_TARGET), run_name=__name__)
