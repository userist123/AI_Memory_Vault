"""Repository-level security boundary package."""
from __future__ import annotations

import os

_canonical_security_dir = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "03_IMPLEMENTATION", "packages", "security")
)
if os.path.isdir(_canonical_security_dir) and _canonical_security_dir not in __path__:
    __path__.append(_canonical_security_dir)
