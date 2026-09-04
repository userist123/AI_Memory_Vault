# core.py
"""Backwards-compatibility shim.

All canonical definitions live in memory_controller.controller.
This module re-exports the symbols that downstream code imports via
`from memory_controller.core import Lifecycle`.
"""

from memory_controller.controller import Lifecycle, StorageEngine, MemoryController  # noqa: F401
