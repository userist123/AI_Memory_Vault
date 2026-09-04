"""Canonical compatibility namespace for the decomposed Memory Vault runtime.

Implementation is physically classified by responsibility under the sibling
packages in ``03_IMPLEMENTATION/packages``. This namespace exposes those
components under one stable ``memory_vault`` import root without duplicating
runtime code.
"""
from __future__ import annotations
from pathlib import Path

_BASE = Path(__file__).resolve().parents[1]
__path__ = [
    str(_BASE / "graph"),
    str(_BASE / "interfaces"),
    str(_BASE / "learning"),
    str(_BASE / "lifecycle"),
    str(_BASE / "memory"),
    str(_BASE / "observability"),
    str(_BASE / "providers"),
    str(_BASE / "retrieval"),
    str(_BASE / "security"),
]
__all__ = [
    "graph",
    "interfaces",
    "learning",
    "lifecycle",
    "memory",
    "observability",
    "providers",
    "retrieval",
    "security",
]
