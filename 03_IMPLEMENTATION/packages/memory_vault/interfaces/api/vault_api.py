"""HTTP API compatibility entrypoint for the Memory Vault.

The original gateway remains under the canonical implementation tree so
callers can migrate without keeping executable code at repository root.
"""
from __future__ import annotations

from pathlib import Path
import runpy

_LEGACY = Path(__file__).resolve().parents[3] / "interfaces" / "api_server.py"
if not _LEGACY.exists():
    raise ImportError(f"API gateway source not found: {_LEGACY}")

if __name__ == "__main__":
    runpy.run_path(str(_LEGACY), run_name="__main__")
else:
    _namespace = runpy.run_path(str(_LEGACY), run_name=__name__)
    globals().update({k: v for k, v in _namespace.items() if not k.startswith("__")})
