"""DEPRECATED compatibility namespace for legacy ``cognitive_core`` imports.

Canonical implementations now live under ``memory_vault``. This shim contains
no runtime logic and only exposes the classified package directories to legacy
imports while downstream callers migrate.
"""
from __future__ import annotations
from pathlib import Path

_BASE = Path(__file__).resolve().parents[1] / "memory_vault"
__path__ = [
    str(_BASE / "graph"),
    str(_BASE / "memory"),
    str(_BASE / "learning"),
    str(_BASE / "observability"),
    str(_BASE / "providers"),
    str(_BASE / "interfaces"),
    str(_BASE / "security"),
]
__all__ = []
