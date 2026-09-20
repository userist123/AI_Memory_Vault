"""Legacy ``cognitive_core`` namespace, resolvable from the repository root.

The runtime lives in sibling packages under ``03_IMPLEMENTATION/packages`` and
is reached through this historical name. A twin shim exists there; this one
exists because entry points are started from the repository root, where Python
binds ``cognitive_core`` to this directory before anything adds the packages
directory to ``sys.path``. Without the same ``__path__``, imports such as
``cognitive_core.working_memory`` raise ModuleNotFoundError — which is what
broke ``python -m cognitive_core.recall_cli`` when the cognitive modules were
wired into the controller.

The union of directories also makes the relative imports inside those modules
resolve: ``attention`` imports ``.motivation``, which lives in ``learning``.
"""
from __future__ import annotations

from pathlib import Path

_PACKAGES = Path(__file__).resolve().parent.parent / "03_IMPLEMENTATION" / "packages"

__path__ = [str(Path(__file__).resolve().parent)] + [
    str(_PACKAGES / name)
    for name in (
        "graph", "memory", "learning", "observability",
        "providers", "interfaces", "security", "retrieval", "lifecycle",
    )
]
__all__: list[str] = []
