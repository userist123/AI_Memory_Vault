"""AAS-owned (not vendored): the vendored AWS tests run against the NullKind behind the ProjectKind seam."""

from __future__ import annotations

from collections.abc import Iterator

import pytest
from studio_core import project_kind


@pytest.fixture(autouse=True)
def _null_project_kind(monkeypatch: pytest.MonkeyPatch) -> Iterator[None]:
    kind = project_kind.register(project_kind.NullKind())
    monkeypatch.setattr(project_kind, "DEFAULT_KIND", kind.name)
    yield
    project_kind.unregister(kind.name)
