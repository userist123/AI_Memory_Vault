"""P1a / P5d / P8a: positive write allow-list for app projects (DESIGN.md 3.2)."""

from __future__ import annotations

from collections.abc import Iterator

import pytest
from tests.winpaths import abs_win_path
from studio_core import project_kind
from studio_core.app_kind import AppKind, register_app_kind
from studio_core.appfiles import validate_path
from studio_core.config import Settings
from studio_core.errors import ValidationFailed, WorkspaceViolation
from studio_core.services import Studio

from tests.conftest import AGENT, HUMAN

REFUSED = [
    "package.json", "PACKAGE.JSON", "vite.config.ts", "capacitor.config.json", "index.html", ".npmrc",
    ".pnpmfile.cjs", ".yarnrc.yml", "android/app/build.gradle", "src/../package.json", "src/a.ts::$DATA",
    "src/a.ts:stream", "SRC~1/a.ts", "src/A~1.ts", "src/a.ts.", "src/a.ts ", "AGENTS.md", "CLAUDE.md", "claude.md",
    ".mcp.json", ".claude/settings.json", ".CLAUDE/x", ".vscode/settings.json", "src/.claude/settings.json",
    "CLAUDE~1.MD", "src/x.ts:Zone.Identifier", "src/package.json", "src/tsconfig.json", "/src/a.ts", "\\src\\a.ts",
    "//host/share/src/a.ts", abs_win_path("src", "a.ts"), abs_win_path("src", "a.ts", sep="\\"), "src/con.ts", "src/NUL.tsx", "src/aux.css", "src/com1.json",
    "src/a.exe", "src/a.js", "src/a.html", "public/a.html", "public/a.js", "public/a.svg.exe", "src/.hidden.ts",
    "src/a/b/c/d/e/f.ts", "public/a/b/c/d/e.png", "src", "src/", "src/a", "public/", "other/a.ts", "SRC/a.ts",
    "src//a.ts", "src/./a.ts", "src/a b.ts", "src/\x00.ts", "src/a\n.ts", "", "src/" + "a" * 42 + ".ts",
]


@pytest.mark.parametrize("path", REFUSED)
def test_refused(path: str) -> None:
    with pytest.raises((ValidationFailed, WorkspaceViolation)):
        validate_path(path)


@pytest.mark.parametrize("path", ["src/App.tsx", "src/main.tsx", "src/a/b.ts", "src/styles.CSS", "src/data.json",
                                  "src/icon.svg", "public/logo.png", "public/a/font.woff2", "public/robots.txt",
                                  "src/a/b/c/d/e.ts", "public/a/b/c/d.png", "src/notes.ts", "src/A_b-c1.tsx"])
def test_accepted_positive_control(path: str) -> None:
    assert validate_path(path) == path


@pytest.fixture()
def app_studio(settings: Settings) -> Iterator[Studio]:
    kind = register_app_kind()
    assert kind.name == "app"
    try:
        yield Studio(settings, kind="app")
    finally:
        project_kind.unregister("app")


def test_studio_write_refusals_leave_revision_unchanged(app_studio: Studio) -> None:
    p = app_studio.create_project(HUMAN, "AppOne", "blank")
    pid, rev = p["id"], p["revision"]
    for bad in ("package.json", "PACKAGE.JSON", "vite.config.ts", "index.html", "src/a.ts::$DATA", "SRC~1/a.ts",
                "src/a.ts.", "CLAUDE.md", ".mcp.json"):
        with pytest.raises((ValidationFailed, WorkspaceViolation)):
            app_studio.write_file(AGENT, pid, rev, bad, "x")
    assert app_studio.get_revision(pid) == rev
    r = app_studio.write_file(AGENT, pid, rev, "src/Extra.tsx", "export const x = 1\n")  # positive control
    assert r["revision"] == rev + 1


def test_case_collision_with_existing_file_refused(app_studio: Studio) -> None:
    p = app_studio.create_project(HUMAN, "AppCase", "blank")
    pid = p["id"]
    r = app_studio.write_file(AGENT, pid, p["revision"], "src/a.ts", "export {}\n")
    with pytest.raises(ValidationFailed):
        app_studio.write_file(AGENT, pid, r["revision"], "src/A.ts", "export {}\n")
    assert app_studio.get_revision(pid) == r["revision"]


def test_size_limits() -> None:
    k = AppKind()
    k.validate("src/ok.ts", "x" * 200_000)  # at the limit
    with pytest.raises(ValidationFailed):
        k.validate("src/big.ts", "x" * 200_001)
    with pytest.raises(ValidationFailed):
        k.validate("src/nul.ts", "a\x00b")
    assert k.max_files == 300 and k.max_total_bytes == 4 * 1024 * 1024


def test_studio_owned_files_never_materialised(app_studio: Studio) -> None:
    """build() never writes package.json/vite config/index.html, even if a replayed snapshot carries them."""
    p = app_studio.create_project(HUMAN, "AppBuild", "blank")
    ws = app_studio.workspace(p["id"])
    assert ws.read_bytes("src/main.tsx") and ws.read_bytes("src/App.tsx") and ws.read_bytes("public/icon.svg")
    for forbidden in ("package.json", "vite.config.ts", "index.html"):
        assert not (ws.root / forbidden).exists()
    kind = project_kind.resolve("app")
    evil = {"components": [], "project": {"settings": {"files": {"package.json": {"content": "{}", "language": "json"}}}}}
    with pytest.raises(ValidationFailed):
        kind.build({"name": "x", "revision": 1}, evil, ws)
    assert not (ws.root / "package.json").exists()
