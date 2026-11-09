"""Compatibility namespace for legacy ``memory_controller`` imports.

The runtime was structurally decomposed into responsibility-focused sibling
packages under ``03_IMPLEMENTATION/packages``. This shim keeps the historical
import namespace without duplicating implementation files.

Module identity: ``memory_controller.X`` is the same module object as
``cognitive_core.X`` whenever both facades resolve ``X`` to the same file. The finder
that does this lives in ``_alias.py`` and is installed once from here; the
``__path__`` below is unchanged.
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



def _load_alias_module():
    # ``__path__`` is the union of the sibling packages and deliberately does not
    # contain this directory, so ``from ._alias import ...`` cannot find the file.
    # Load it by path, once, and reuse it if this package is re-executed.
    import importlib.util
    import sys

    name = f"{__name__}._alias"
    module = sys.modules.get(name)
    if module is None:
        spec = importlib.util.spec_from_file_location(
            name, Path(__file__).resolve().with_name("_alias.py")
        )
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        try:
            spec.loader.exec_module(module)
        except BaseException:
            sys.modules.pop(name, None)
            raise
    return module


_load_alias_module().install()
