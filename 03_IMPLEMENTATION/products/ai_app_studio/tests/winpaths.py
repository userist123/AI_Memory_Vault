"""Absolute Windows paths for tests, derived from the environment (D2: no drive-letter literals in the repository).

Tests that need "some absolute path" (fake tool locations, traversal / refusal inputs) build it from the system drive
instead of hard-coding one, so the intent (any absolute path) is kept and the assertions stay identical.
"""
from __future__ import annotations

import os


def system_drive() -> str:
    """``C:``-style drive of the running Windows system (empty off Windows)."""
    return os.environ.get("SystemDrive") or os.path.splitdrive(os.getcwd())[0]


def abs_win_path(*parts: str, sep: str = "/") -> str:
    """Absolute path on the system drive, e.g. ``abs_win_path("fake", "docker.exe")`` -> ``<drive>/fake/docker.exe``."""
    return system_drive() + sep + sep.join(parts)


def system_root() -> str:
    """The Windows directory (SystemRoot, else <system drive>\Windows)."""
    return os.environ.get("SystemRoot") or abs_win_path("Windows", sep="\\")
