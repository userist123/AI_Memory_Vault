"""DEPRECATED compatibility namespace for legacy ``memory_controller`` imports.

Canonical implementations now live under ``memory_vault``. The shim only
exposes classified package directories and contains no duplicated logic.
"""
from __future__ import annotations
from pathlib import Path

_BASE = Path(__file__).resolve().parents[1] / "memory_vault"
__path__ = [
    str(_BASE / "retrieval"),
    str(_BASE / "memory"),
    str(_BASE / "security"),
    str(_BASE / "lifecycle"),
    str(_BASE / "interfaces"),
    str(_BASE / "observability"),
]
__all__ = []
