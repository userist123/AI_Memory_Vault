"""Canonical compatibility namespace for the decomposed Memory Vault runtime.

Implementation is physically classified by responsibility under the sibling
packages in ``03_IMPLEMENTATION/packages``. This namespace exposes those
components under one stable ``memory_vault`` import root without duplicating
runtime code.
"""
from __future__ import annotations
from pathlib import Path

_BASE = Path(__file__).resolve().parents[1]
# ``memory_vault`` is a namespace facade over the sibling implementation
# packages. Its import path must point at the package root so that
# ``memory_vault.memory``, ``memory_vault.graph``, etc. resolve to the actual
# classified packages rather than looking for nested modules inside each one.
__path__ = [str(_BASE)]
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
