"""ProjectKind seam (AAS-owned). Re-covers, through the NullKind, the generic engine behaviour that the dropped
website-only AWS tests exercised via components/templates (see VENDORED.json "dropped_tests")."""

from __future__ import annotations

import ast
import json
import os
import shutil
import subprocess
import zipfile
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from sqlalchemy import select
from studio_core import project_kind
from studio_core.config import Settings
from studio_core.db import Project, ToolExecution
from studio_core.errors import ConflictError, NotFound, Unavailable, ValidationFailed
from studio_core.project_kind import NullKind, Publication
from studio_core.services import Studio
from studio_core.workspace import Workspace

from tests.conftest import AGENT, HUMAN

CORE = Path(__file__).resolve().parents[2] / "core" / "studio_core"
WEBSITE_ONLY = {"renderer", "templates", "component_model", "codefiles", "scene3d", "animation_model", "exporters",
                "deploy", "terminal", "bim", "blender", "computer_use"}
needs_git = pytest.mark.skipif(shutil.which("git") is None, reason="git not installed")


@pytest.fixture()
def null_kind(monkeypatch: pytest.MonkeyPatch) -> Iterator[NullKind]:
    kind = NullKind()
    project_kind.register(kind)
    monkeypatch.setattr(project_kind, "DEFAULT_KIND", kind.name)
    yield kind
    project_kind.unregister(kind.name)


@pytest.fixture()
def st(settings: Settings, null_kind: NullKind) -> Studio:
    return Studio(settings)


def _imports(path: Path) -> set[str]:
    out: set[str] = set()
    for node in ast.walk(ast.parse(path.read_text(encoding="utf-8"))):
        if isinstance(node, ast.ImportFrom):
            out.update(p for p in (node.module or "").split(".") if p)
            out.update(a.name for a in node.names)
        elif isinstance(node, ast.Import):
            for a in node.names:
                out.update(a.name.split("."))
    return out


# ---------------------------------------------------------------- the seam itself
def test_services_does_not_import_website_modules() -> None:
    assert _imports(CORE / "services.py") & WEBSITE_ONLY == set()
    assert _imports(CORE / "project_kind.py") & WEBSITE_ONLY == set()


def test_website_modules_are_not_vendored() -> None:
    present = {p.stem for p in CORE.iterdir()}
    assert present & WEBSITE_ONLY == set()


def test_positive_control_import_scan_detects_a_website_import(tmp_path: Path) -> None:
    f = tmp_path / "x.py"
    f.write_text("from .renderer import render_html\n", encoding="utf-8")
    assert "renderer" in _imports(f)


def test_unregistered_kind_fails_closed(settings: Settings) -> None:
    assert "app" not in project_kind.registered()  # the app kind arrives in a later slice
    st = Studio(settings)
    assert st.kind_name == "app"
    with pytest.raises(Unavailable):
        st.create_project(HUMAN, "NoKind")
    with st.session() as s:
        assert s.execute(select(Project)).scalars().all() == []


def test_registry_rejects_bad_kinds(null_kind: NullKind) -> None:
    with pytest.raises(TypeError):
        project_kind.register(object())  # type: ignore[arg-type]
    bad = NullKind()
    bad.name = "Bad Name"  # type: ignore[misc]
    with pytest.raises(ValueError):
        project_kind.register(bad)
    with pytest.raises(ValueError):
        project_kind.register(NullKind())  # another object under an existing name
    assert project_kind.register(null_kind) is null_kind  # idempotent for the same object
    assert project_kind.resolve("null") is null_kind


def test_null_kind_satisfies_the_protocol(tmp_path: Path) -> None:
    assert isinstance(NullKind(), project_kind.ProjectKind)
    pub = NullKind().publish({"id": "x"}, Workspace(tmp_path / "ws"))
    assert pub == Publication(root="site", required=("index.html",), exclude=frozenset({"site/.revision"}))


def test_unknown_template_rejected(st: Studio) -> None:
    with pytest.raises(ValidationFailed):
        st.create_project(HUMAN, "Tpl", template="landing")


def test_create_builds_through_the_kind(st: Studio) -> None:
    p = st.create_project(HUMAN, "Demo")
    assert p["revision"] == 1
    page = (st.site_dir(p["id"]) / "index.html").read_text(encoding="utf-8")
    assert 'data-revision="1"' in page and "<title>Demo</title>" in page
    assert (st.site_dir(p["id"]) / ".revision").read_text(encoding="utf-8") == "1"


def test_kind_validates_writes_and_deletes(st: Studio) -> None:
    pid = st.create_project(HUMAN, "Val")["id"]
    for bad in ("../x.txt", "a.js", "A.txt", "a/b/c/d/e/f.txt"):
        with pytest.raises(ValidationFailed):
            st.write_file(AGENT, pid, 1, bad, "x")
    with pytest.raises(ValidationFailed):
        st.write_file(AGENT, pid, 1, "a.txt", "nul\x00")
    with pytest.raises(ValidationFailed):
        st.delete_file(AGENT, pid, 1, "../a.txt")
    with pytest.raises(NotFound):
        st.delete_file(AGENT, pid, 1, "missing.txt")
    assert st.get_revision(pid) == 1  # nothing was committed


def test_file_limits_come_from_the_kind(st: Studio, null_kind: NullKind, monkeypatch: pytest.MonkeyPatch) -> None:
    pid = st.create_project(HUMAN, "Lim")["id"]
    monkeypatch.setattr(null_kind, "max_files", 1)
    st.write_file(AGENT, pid, 1, "a.txt", "a")
    with pytest.raises(ValidationFailed):
        st.write_file(AGENT, pid, 2, "b.txt", "b")


# ---------------------------------------------------------------- engine behaviour re-covered via files
def test_every_operation_writes_tool_execution_with_required_fields(st: Studio) -> None:
    p = st.create_project(AGENT, "Ops")
    st.write_file(AGENT, p["id"], 1, "a.txt", "hi")
    with st.session() as s:
        rows = s.execute(select(ToolExecution).where(ToolExecution.tool == "code.write_file")).scalars().all()
    assert len(rows) == 1
    r = rows[0]
    assert r.id.startswith("op_") and r.project_id == p["id"] and r.base_revision == 1 and r.result_revision == 2
    assert r.actor == "agent:claude-code@mcp" and r.status == "ok" and r.verification == "verified"
    assert r.created_at and r.finished_at


def test_stale_revision_is_a_conflict_and_does_not_overwrite(st: Studio) -> None:
    pid = st.create_project(HUMAN, "Conflict")["id"]
    st.write_file(HUMAN, pid, 1, "a.txt", "human")
    with pytest.raises(ConflictError) as e:
        st.write_file(AGENT, pid, 1, "a.txt", "agent")
    assert e.value.details["currentRevision"] == 2
    assert st.read_file(HUMAN, pid, "a.txt")["content"] == "human"
    assert st.write_file(AGENT, pid, st.get_revision(pid), "a.txt", "agent")["revision"] == 3


def test_mutation_requires_base_revision(st: Studio) -> None:
    pid = st.create_project(HUMAN, "NoBase")["id"]
    with pytest.raises(ValidationFailed):
        st.write_file(HUMAN, pid, None, "a.txt", "x")  # type: ignore[arg-type]


def test_undo_redo_and_restore(st: Studio) -> None:
    pid = st.create_project(HUMAN, "Undo")["id"]
    st.write_file(HUMAN, pid, 1, "a.txt", "A")  # 2
    st.write_file(HUMAN, pid, 2, "b.txt", "B")  # 3
    paths = lambda: [f["path"] for f in st.list_files(HUMAN, pid)]  # noqa: E731
    st.undo(HUMAN, pid, 3)
    assert paths() == ["a.txt"]
    st.undo(HUMAN, pid, 4)
    assert paths() == []
    st.redo(HUMAN, pid, 5)
    assert paths() == ["a.txt"]
    st.redo(HUMAN, pid, 6)
    assert paths() == ["a.txt", "b.txt"]
    with pytest.raises(ValidationFailed):
        st.redo(HUMAN, pid, 7)
    assert [h["revision"] for h in st.history(HUMAN, pid)][:3] == [7, 6, 5]


def test_restore_of_a_model_with_components_is_rejected(st: Studio) -> None:
    pid = st.create_project(HUMAN, "Comp")["id"]
    with st.session() as s:
        p = s.get(Project, pid)
        with pytest.raises(ValidationFailed):
            st._apply_components(s, p, [{"id": "hero", "type": "hero"}], {})


def test_export_uses_the_kind_publication(st: Studio) -> None:
    pid = st.create_project(HUMAN, "Export")["id"]
    out = st.export_project(HUMAN, pid)
    with zipfile.ZipFile(out["path"]) as z:
        names = set(z.namelist())
    assert "index.html" in names and ".revision" not in names and "site/.revision" not in names
    assert len(out["sha256"]) == 64
    with st.session() as s:
        row = s.execute(select(ToolExecution).where(ToolExecution.tool == "project.export")).scalar_one()
    assert row.verification == "verified"


def test_export_verification_fails_when_required_file_missing(st: Studio, null_kind: NullKind,
                                                               monkeypatch: pytest.MonkeyPatch) -> None:
    pid = st.create_project(HUMAN, "ExportMissing")["id"]
    monkeypatch.setattr(null_kind, "publish", lambda project, ws: Publication(root="site", required=("app.apk",)))
    st.export_project(HUMAN, pid)
    with st.session() as s:
        row = s.execute(select(ToolExecution).where(ToolExecution.tool == "project.export")).scalar_one()
    assert row.verification == "failed"


@pytest.mark.skipif(os.name != "nt", reason="junctions are Windows-only")
def test_export_does_not_follow_junctions(st: Studio, tmp_path: Path) -> None:
    pid = st.create_project(HUMAN, "ExpJ")["id"]
    secret = tmp_path / "outside_secret"
    secret.mkdir()
    (secret / "id_rsa").write_text("PRIVATE-KEY-CANARY-123")
    link = st.site_dir(pid) / "assets"
    r = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(secret)], capture_output=True)  # noqa: S603,S607
    assert r.returncode == 0
    out = st.export_project(HUMAN, pid)
    with zipfile.ZipFile(out["path"]) as z:
        assert not any("id_rsa" in n for n in z.namelist())
        assert all(b"CANARY" not in z.read(n) for n in z.namelist())


# ---------------------------------------------------------------- git, re-covered via files
@needs_git
def test_every_revision_is_a_commit_with_the_real_author(st: Studio) -> None:
    pid = st.create_project(HUMAN, "GitLog")["id"]
    r = st.write_file(AGENT, pid, 1, "a.txt", "by the agent")
    assert r["git"]["ok"] and len(r["git"]["commit"]) == 40
    log = st.git_log(HUMAN, pid)
    assert [e["revision"] for e in log] == [2, 1]
    assert log[0]["author"] == "agent:claude-code@mcp" and log[1]["author"] == "human:owner@gui"
    root = st.workspace(pid).root
    model: dict[str, Any] = json.loads((root / "model" / "project.json").read_text(encoding="utf-8"))
    assert model["revision"] == 2 and model["project"]["settings"]["files"]["a.txt"]["content"] == "by the agent"
    tracked = subprocess.run(["git", "-C", str(root), "ls-files"], capture_output=True, text=True).stdout.split()  # noqa: S603,S607
    assert "model/project.json" in tracked and "site/index.html" in tracked
    assert not any(t.startswith(("artifacts/", "exports/", "bim/")) for t in tracked)


@needs_git
def test_restore_from_git_creates_a_new_revision(st: Studio) -> None:
    pid = st.create_project(HUMAN, "GitRestore")["id"]
    st.write_file(HUMAN, pid, 1, "a.txt", "kept")
    first = st.git_log(HUMAN, pid)[0]["commit"]
    st.delete_file(AGENT, pid, 2, "a.txt")
    out = st.restore_from_git(HUMAN, pid, 3, first)
    assert out["revision"] == 4 and out["restoredFromRevision"] == 2
    assert st.read_file(HUMAN, pid, "a.txt")["content"] == "kept"
    assert len(st.git_log(HUMAN, pid)) == 4
    with pytest.raises(ValidationFailed):
        st.restore_from_git(HUMAN, pid, 4, "HEAD; rm -rf /")


@needs_git
def test_recover_project_from_git_after_database_loss(tmp_path: Path, null_kind: NullKind) -> None:
    settings = Settings(data_dir=tmp_path / "data")
    a = Studio(settings)
    pid = a.create_project(HUMAN, "Survivor")["id"]
    a.write_file(HUMAN, pid, 1, "a.txt", "kept in git")
    a.engine.dispose()
    for suffix in ("", "-wal", "-shm"):
        Path(str(settings.db_path) + suffix).unlink(missing_ok=True)
    b = Studio(settings)
    out = b.recover_from_git(HUMAN, pid)
    assert out["revision"] == 3 and out["name"] == "Survivor"
    assert b.read_file(HUMAN, pid, "a.txt")["content"] == "kept in git"
    with pytest.raises(ValidationFailed):
        b.recover_from_git(HUMAN, pid)


@needs_git
def test_owner_git_config_and_hooks_are_isolated(st: Studio, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    hooks = tmp_path / "evil-hooks"
    hooks.mkdir()
    (hooks / "pre-commit").write_text("#!/bin/sh\necho pwned > \"$HOME/aws-hook-ran\"\nexit 1\n")
    gcfg = tmp_path / "global.gitconfig"
    gcfg.write_text(f"[core]\n\thooksPath = {hooks.as_posix()}\n[commit]\n\tgpgsign = true\n")
    monkeypatch.setenv("GIT_CONFIG_GLOBAL", str(gcfg))
    pid = st.create_project(HUMAN, "GitIsolated")["id"]
    r = st.write_file(AGENT, pid, 1, "a.txt", "x")
    assert r["git"]["ok"], r["git"]
