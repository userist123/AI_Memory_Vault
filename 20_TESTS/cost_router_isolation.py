"""Keep the Claude-side and the Antigravity-side cost-router libs from sharing bare module names.

Both families put a `lib/` directory on sys.path and import `route_state`, `transcript_usage`, ...;
sys.modules would keep whichever loaded first. These helpers drop those names (and the .claude lib
path) around each test so the order of test files in one pytest process no longer matters.
"""
from __future__ import annotations

import sys
from pathlib import Path

BARE_NAMES = ("route_state", "transcript_usage", "route", "repo_map", "local_llm", "claude_model_router")


def purge() -> None:
    for name in BARE_NAMES:
        sys.modules.pop(name, None)


def enter(lib: Path) -> None:
    purge()
    if str(lib) not in sys.path:
        sys.path.insert(0, str(lib))


def leave(lib: Path) -> None:
    purge()
    while str(lib) in sys.path:
        sys.path.remove(str(lib))
