from __future__ import annotations

import os
from pathlib import Path

import pytest
from studio_core.config import Settings
from studio_core.policy import Actor
from studio_core.services import Studio

HUMAN = Actor("human", "owner", "gui")
AGENT = Actor("agent", "claude-code", "mcp")


@pytest.fixture()
def settings(tmp_path: Path):  # type: ignore[no-untyped-def]
    """SQLite by default. With AWS_TEST_PG_ADMIN_URL set, every test gets a fresh PostgreSQL database."""
    admin = os.environ.get("AWS_TEST_PG_ADMIN_URL")
    if not admin:
        yield Settings(data_dir=tmp_path / "data")
        return
    import uuid

    from sqlalchemy import create_engine, text
    name = f"aws_t_{uuid.uuid4().hex[:12]}"
    eng = create_engine(admin, isolation_level="AUTOCOMMIT")
    with eng.connect() as c:
        c.execute(text(f'CREATE DATABASE "{name}"'))
    url = admin.rsplit("/", 1)[0] + "/" + name
    yield Settings(data_dir=tmp_path / "data", database_url=url)
    with eng.connect() as c:
        c.execute(text(f"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}'"))  # noqa: S608 - name is a local uuid
        c.execute(text(f'DROP DATABASE IF EXISTS "{name}"'))
    eng.dispose()


@pytest.fixture()
def studio(settings: Settings) -> Studio:
    return Studio(settings)
