"""Shim: `python -m cognitive_core.telegram_vault_bot` runs interfaces/telegram_vault_bot.py."""
from __future__ import annotations

import runpy
import sys
from pathlib import Path

_PKG_DIR = str(Path(__file__).resolve().parent.parent / "03_IMPLEMENTATION" / "packages")
if _PKG_DIR not in sys.path:
    sys.path.insert(0, _PKG_DIR)

from interfaces.telegram_vault_bot import *  # noqa: E402,F401,F403

if __name__ == "__main__":
    target = Path(_PKG_DIR) / "interfaces" / "telegram_vault_bot.py"
    runpy.run_path(str(target), run_name="__main__")
