"""App ProjectKind: kind column, migration 0003, blank template, Publication."""

from __future__ import annotations

import sqlite3
from collections.abc import Iterator

import pytest
from studio_core import project_kind
from studio_core.app_kind import AppKind, register_app_kind
from studio_core.config import Settings
from studio_core.errors import ValidationFailed
from studio_core.migrations import upgrade_to_head
from studio_core.services import Studio

from tests.conftest import AGENT, HUMAN


@pytest.fixture()
def app_studio(settings: Settings) -> Iterator[Studio]:
    register_app_kind()
    try:
        yield Studio(settings, kind="app")
    finally:
        project_kind.unregister("app")


def test_migration_0003_adds_kind_activity_and_settings(settings: Settings) -> None:
    settings.ensure_dirs()
    upgrade_to_head(settings.db_url)
    con = sqlite3.connect(settings.db_path)
    try:
        cols = {r[1] for r in con.execute("PRAGMA table_info(projects)")}
        assert "kind" in cols
        assert {r[1] for r in con.execute("PRAGMA table_info(activity_events)")} >= {
            "seq", "prev_seq", "ts", "run_id", "project_id", "kind", "source", "audit_id", "payload",
            "artifact_sha256", "redactions"}
        assert {r[1] for r in con.execute("PRAGMA table_info(app_settings)")} >= {"key", "value"}
        assert con.execute("SELECT version_num FROM alembic_version").fetchone()[0] == "0005"  # head: 0005 (chat.prompt, chat.fallback); S6 added 0004 (cost.update, preview.captured)
    finally:
        con.close()
    upgrade_to_head(settings.db_url)  # idempotent


def test_project_has_kind_app_and_blank_template(app_studio: Studio) -> None:
    p = app_studio.create_project(HUMAN, "Blank1", "blank")
    assert p["kind"] == "app"
    files = app_studio.list_files(AGENT, p["id"])
    assert {f["path"] for f in files} == {"src/App.tsx", "src/main.tsx", "public/icon.svg"}
    assert "createRoot" in app_studio.read_file(AGENT, p["id"], "src/main.tsx")["content"]


def test_unknown_template_rejected(app_studio: Studio) -> None:
    with pytest.raises(ValidationFailed):
        app_studio.create_project(HUMAN, "Bad1", "landing")


def test_publication_required_non_empty(app_studio: Studio) -> None:
    p = app_studio.create_project(HUMAN, "Pub1", "blank")
    pub = AppKind().publish({"name": "x"}, app_studio.workspace(p["id"]))
    assert pub.required
    ex = app_studio.export_project(HUMAN, p["id"])
    assert "main.tsx" in ex["files"]
