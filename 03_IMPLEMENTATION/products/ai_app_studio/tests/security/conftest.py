"""AAS-owned: security tests that need a generic Studio run against the NullKind unless they pick a kind."""

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
