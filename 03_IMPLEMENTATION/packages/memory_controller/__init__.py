"""Compatibility namespace for legacy ``memory_controller`` imports.

The runtime was structurally decomposed into responsibility-focused sibling
packages under ``03_IMPLEMENTATION/packages``. This shim keeps the historical
import namespace without duplicating implementation files.
"""
from __future__ import annotations
from pathlib import Path

_BASE = Path(__file__).resolve().parents[1]
__path__ = [
    str(_BASE / "retrieval"),
    str(_BASE / "memory"),
    str(_BASE / "security"),
    str(_BASE / "lifecycle"),
    str(_BASE / "interfaces"),
    str(_BASE / "observability"),
]
__all__ = []
