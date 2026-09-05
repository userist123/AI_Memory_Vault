"""Backward-compatibility shim for cognitive_core.recall_cli."""
from __future__ import annotations
import os
import sys
import runpy
from pathlib import Path

_PKG_DIR = str(Path(__file__).resolve().parent.parent / '03_IMPLEMENTATION' / 'packages')
if _PKG_DIR not in sys.path:
    sys.path.insert(0, _PKG_DIR)

from interfaces.recall_cli import *

if __name__ == '__main__':
    target = Path(__file__).resolve().parent.parent / '03_IMPLEMENTATION' / 'packages' / 'interfaces' / 'recall_cli.py'
    runpy.run_path(str(target), run_name='__main__')
