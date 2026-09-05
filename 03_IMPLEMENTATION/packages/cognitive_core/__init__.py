"""Compatibility namespace for legacy ``cognitive_core`` imports.

The runtime was structurally decomposed into responsibility-focused sibling
packages under ``03_IMPLEMENTATION/packages``. This shim keeps the historical
import namespace without duplicating implementation files.
"""
from __future__ import annotations
from pathlib import Path

_BASE = Path(__file__).resolve().parents[1]
__path__ = [
    str(_BASE / "graph"),
    str(_BASE / "memory"),
    str(_BASE / "learning"),
    str(_BASE / "observability"),
    str(_BASE / "providers"),
    str(_BASE / "interfaces"),
    str(_BASE / "security"),
    str(_BASE / "retrieval"),
    str(_BASE / "lifecycle"),
]
__all__ = []
