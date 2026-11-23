"""F3: an app project's revision commits the app kind's file roots (src/, public/), not only `site/`."""
from __future__ import annotations

import shutil
import subprocess
from collections.abc import Iterator
from pathlib import Path

import pytest
from studio_core import project_kind
from studio_core.app_kind import register_app_kind
from studio_core.config import Settings
from studio_core.services import Studio

from tests.conftest import AGENT, HUMAN

pytestmark = pytest.mark.skipif(shutil.which("git") is None, reason="git not installed")


@pytest.fixture()
def studio(settings: Settings) -> Iterator[Studio]:
    register_app_kind()
    try:
        yield Studio(settings, kind="app")
    finally:
        project_kind.unregister("app")


def _tracked(root: Path) -> list[str]:
    return subprocess.run(["git", "-C", str(root), "ls-files"], capture_output=True, text=True,  # noqa: S603,S607
                          check=True).stdout.split()


def _show(root: Path, commit: str, path: str) -> str:
    return subprocess.run(["git", "-C", str(root), "show", f"{commit}:{path}"], capture_output=True,  # noqa: S603,S607
                          text=True, check=True).stdout


def test_app_kind_declares_its_file_roots(studio: Studio) -> None:
    assert tuple(studio.kind.file_roots) == ("src", "public", "tests")  # W3: tests/flows/*.spec.ts is versioned too


def test_app_revision_commits_src_and_public_and_restore_works(studio: Studio) -> None:
    pid = studio.create_project(HUMAN, "AppGit")["id"]
    root = studio.workspace(pid).root
    tracked = _tracked(root)
    assert "src/App.tsx" in tracked and "src/main.tsx" in tracked and "model/project.json" in tracked
    r = studio.write_file(AGENT, pid, 1, "src/App.tsx", "export default function App() { return <p>v2</p>; }\n")
    assert r["git"]["ok"] and len(r["git"]["commit"]) == 40
    v2 = r["git"]["commit"]
    assert "v2" in _show(root, v2, "src/App.tsx")
    first = studio.git_log(HUMAN, pid)[-1]["commit"]
    diff = studio.git_diff(HUMAN, pid, first, v2)
    assert "src/App.tsx" in [f["path"] for f in diff["files"]]
    out = studio.restore_from_git(HUMAN, pid, 2, first)
    assert out["revision"] == 3
    assert "v2" not in studio.read_file(HUMAN, pid, "src/App.tsx")["content"]
    head = studio.git_log(HUMAN, pid)[0]["commit"]
    assert _show(root, head, "src/App.tsx") == _show(root, first, "src/App.tsx")
    assert (root / "src" / "App.tsx").read_text(encoding="utf-8") == _show(root, first, "src/App.tsx")
