"""A bare `pytest` must run the whole suite.

Six polymarket modules import `from packages.polymarket.x` while the rest of
the repository imports `from polymarket.x`. Each convention needs a different
sys.path root, and only one of them was in pytest.ini. The other was supplied
by `PYTHONPATH: 03_IMPLEMENTATION` inside the phase 3-7 CI workflow files, so
those tests were green in CI and failed collection for anyone running pytest by
hand — six of eight modules, zero tests executed, on code already merged.

A gap that only appears outside CI is the worst kind: CI says the suite passes,
and it does, for the subset CI happens to invoke.
"""
from __future__ import annotations

import configparser
import pathlib
import re

_REPO = pathlib.Path(__file__).resolve().parents[1]


def _pythonpath_roots() -> list[str]:
    parser = configparser.ConfigParser()
    parser.read(_REPO / "pytest.ini", encoding="utf-8")
    return parser["pytest"]["pythonpath"].split()


def test_both_import_roots_are_configured():
    roots = _pythonpath_roots()
    assert "03_IMPLEMENTATION" in roots, (
        "needed by `from packages.polymarket.x` imports"
    )
    assert "03_IMPLEMENTATION/packages" in roots, (
        "needed by `from polymarket.x` and `from memory_controller.x` imports"
    )


def test_every_import_convention_in_the_suite_has_a_root():
    """Written as a scan rather than a fixed list, so a third convention
    introduced later fails here instead of in someone's terminal."""
    roots = set(_pythonpath_roots())
    top_level: set[str] = set()
    for path in (_REPO / "20_TESTS").rglob("test_*.py"):
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            match = re.match(r"^(?:from|import)\s+([A-Za-z_][\w]*)", line)
            if match:
                top_level.add(match.group(1))

    unresolved = [
        name for name in top_level
        if any((_REPO / root / name).is_dir() for root in
               ("03_IMPLEMENTATION", "03_IMPLEMENTATION/packages"))
        and not any((_REPO / root / name).is_dir() for root in roots)
    ]
    assert not unresolved, (
        f"these packages exist under 03_IMPLEMENTATION but no configured "
        f"pythonpath root reaches them: {sorted(unresolved)}"
    )
