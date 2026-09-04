"""Stable launcher for the migrated browser gateway.

Sets the repository root explicitly, then loads the compatibility implementation.
"""
from __future__ import annotations
import os
from pathlib import Path
import runpy

REPO_ROOT = Path(__file__).resolve().parents[3]
os.environ.setdefault("AI_MEMORY_VAULT_ROOT", str(REPO_ROOT))

_TARGET = Path(__file__).with_name("api_server.py")
if not _TARGET.exists():
    raise ImportError(f"API server implementation not found: {_TARGET}")

runpy.run_path(str(_TARGET), run_name=__name__)
