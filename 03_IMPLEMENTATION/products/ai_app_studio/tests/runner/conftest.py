"""S6 runner tests: a real Studio of kind ``app`` in a temp data dir and the fake CLI fixture."""
from __future__ import annotations

import sys
from collections.abc import Iterator
from pathlib import Path

import pytest
from studio_core import project_kind
from studio_core.app_kind import register_app_kind
from studio_core.config import Settings
from studio_core.policy import Actor
from studio_core.services import Studio

PRODUCT = Path(__file__).resolve().parents[2]
FAKE_CLI = PRODUCT / "tests" / "fixtures" / "fake_claude.py"
OWNER = Actor("human", "owner", "rest")


@pytest.fixture()
def studio(tmp_path: Path) -> Iterator[Studio]:
    register_app_kind()
    yield Studio(Settings(data_dir=tmp_path / "data"), kind="app")
    project_kind.unregister("app")


@pytest.fixture()
def fake_cli() -> list[str]:
    return [sys.executable, str(FAKE_CLI)]
