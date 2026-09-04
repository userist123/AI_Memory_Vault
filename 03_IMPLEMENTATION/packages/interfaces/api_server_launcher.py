"""Stable repository-root launcher for the browser gateway."""
from __future__ import annotations
import os
import runpy
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
os.environ.setdefault("AI_MEMORY_VAULT_ROOT", str(REPO_ROOT))
runpy.run_path(str(REPO_ROOT / "interfaces" / "legacy" / "api_server.py"), run_name="__main__")
