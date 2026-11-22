from __future__ import annotations

import zipfile

import pytest
from sqlalchemy import inspect, select
from studio_core.db import ToolExecution
from studio_core.errors import ConflictError, NotFound, ValidationFailed
from studio_core.services import Studio

from tests.conftest import AGENT, HUMAN


def test_migrations_create_all_spec_entities(studio: Studio) -> None:
    tables = set(inspect(studio.engine).get_table_names())
    expected = {"projects", "project_revisions", "components", "assets", "tasks", "agent_runs", "artifacts",
                "screenshots", "visual_analyses", "tool_executions", "change_sets", "approvals",
                "audit_events", "test_runs", "deployments"}
    assert expected <= tables


def test_unknown_project_is_not_found(studio: Studio) -> None:
    with pytest.raises(NotFound):
        studio.get_project(HUMAN, "prj_0000000000000000")


def test_migrations_match_the_models(studio: Studio) -> None:
    """Guard: every model change ships with a migration (checked on SQLite and on PostgreSQL)."""
    from alembic.autogenerate import compare_metadata
    from alembic.migration import MigrationContext
    from studio_core.db import Base
    with studio.engine.connect() as conn:
        diff = compare_metadata(MigrationContext.configure(conn, opts={"compare_type": True}), Base.metadata)
    assert diff == [], diff
