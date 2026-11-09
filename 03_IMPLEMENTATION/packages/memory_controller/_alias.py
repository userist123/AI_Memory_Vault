"""Single module identity for the ``memory_controller`` and ``cognitive_core`` facades.

Both facades are ``__path__`` unions over the same sibling packages, so the same file
used to be loaded twice, once per prefix, as two distinct module objects (two
``Principal`` classes, two sets of module-level locks and caches).

``AliasFinder`` makes ``memory_controller.<rest>`` the *same object* as
``cognitive_core.<rest>``, but only when both facades resolve the first component of
``<rest>`` to the identical file. Anything that resolves differently (``benchmarks``;
from the repository root ``recall_cli``, ``vault_cli``, ``telegram_vault_bot``) is left
to the ordinary import system, exactly as before.

Why a loader with ``create_module`` and not a plain ``sys.modules`` assignment:
``importlib`` calls ``_init_module_attrs(spec, module, override=False)`` on the object
that ``create_module`` returns, which only fills attributes that are missing. The
canonical module already has ``__name__``/``__spec__``/``__file__``/``__loader__``, so
they are not rewritten, with one exception: ``__spec__`` is always assigned, so
``exec_module`` restores the canonical spec (verified by the contract test).

Failures are never swallowed: an exception raised while importing the canonical module
propagates to the importer. The single deliberate non-alias case besides "files
differ" is ``cognitive_core`` itself not being importable (nothing to alias to).
"""
from __future__ import annotations

import importlib
import importlib.abc
import importlib.machinery
import os
import sys

_PREFIX = "memory_controller."
_CANONICAL = "cognitive_core"
_MARKER = "_memory_controller_alias_finder"


def _identity(spec):
    """What a spec resolves to: the file, or for a namespace package its directory portions."""
    if spec is None:
        return None
    origin = getattr(spec, "origin", None)
    if origin and origin not in ("built-in", "frozen", "namespace"):
        return ("file", os.path.normcase(os.path.realpath(origin)))
    portions = getattr(spec, "submodule_search_locations", None)
    if portions:  # namespace package (no __init__.py), e.g. ``audit``
        return ("namespace", tuple(os.path.normcase(os.path.realpath(p)) for p in portions))
    return None


class _AliasLoader(importlib.abc.Loader):
    def __init__(self, canonical_name: str) -> None:
        self._canonical_name = canonical_name

    def create_module(self, spec):
        # Any exception here propagates to the importer; there is no fallback.
        module = importlib.import_module(self._canonical_name)
        self._canonical_spec = module.__spec__
        return module

    def exec_module(self, module) -> None:
        # The canonical module is already executed. ``importlib`` assigns
        # ``module.__spec__ = <alias spec>`` unconditionally (``_init_module_attrs``
        # only guards the other attributes), which would rename the canonical module's
        # spec to ``memory_controller.X``. Put the canonical spec back.
        module.__spec__ = self._canonical_spec


class AliasFinder(importlib.abc.MetaPathFinder):
    """Maps ``memory_controller.<rest>`` onto ``cognitive_core.<rest>`` when files coincide."""

    _memory_controller_alias_finder = True

    def find_spec(self, fullname, path=None, target=None):
        if not fullname.startswith(_PREFIX):
            return None
        rest = fullname[len(_PREFIX):]
        first = rest.split(".", 1)[0]
        facade = sys.modules.get("memory_controller")
        facade_path = getattr(facade, "__path__", None)
        if not first or facade_path is None:
            return None
        own = importlib.machinery.PathFinder.find_spec(first, list(facade_path))
        if own is None:
            return None  # unknown name: the normal import raises ModuleNotFoundError
        try:
            canonical = importlib.import_module(_CANONICAL)
        except ModuleNotFoundError as exc:
            if exc.name == _CANONICAL:
                return None  # no canonical namespace on sys.path: nothing to alias to
            raise
        theirs = importlib.machinery.PathFinder.find_spec(first, list(canonical.__path__))
        own_id, their_id = _identity(own), _identity(theirs)
        if own_id is None or own_id != their_id:
            return None
        return importlib.machinery.ModuleSpec(
            fullname, _AliasLoader(f"{_CANONICAL}.{rest}"), origin=getattr(theirs, "origin", None)
        )


def install() -> AliasFinder:
    """Install the finder once, at the front of ``sys.meta_path``; idempotent."""
    for finder in sys.meta_path:
        if getattr(finder, _MARKER, False):
            return finder
    finder = AliasFinder()
    sys.meta_path.insert(0, finder)
    return finder
