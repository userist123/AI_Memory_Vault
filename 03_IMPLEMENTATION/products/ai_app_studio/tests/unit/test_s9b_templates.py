"""S9b item 4 - app templates beyond `blank`: `tabs` and `list-detail` (React + TS, mobile-first, accessible),
selectable at project create (REST `template`), every file inside the write allow-list."""
from __future__ import annotations

from pathlib import Path

import pytest
from studio_core import appfiles, project_kind
from studio_core.app_kind import AppKind, register_app_kind
from studio_core.config import Settings
from studio_core.errors import ValidationFailed
from studio_core.policy import Actor
from studio_core.services import Studio

OWNER = Actor("human", "owner", "rest")


@pytest.fixture()
def studio(tmp_path: Path):  # type: ignore[no-untyped-def]
    register_app_kind()
    yield Studio(Settings(data_dir=tmp_path / "data"), kind="app")
    project_kind.unregister("app")


def files_of(template: str) -> dict[str, str]:
    return {p: f["content"] for p, f in AppKind().initial_settings(template)["files"].items()}


def app_source(template: str) -> str:
    """W1: screens are built from the kit catalogue, so the a11y markers live in App.tsx or the src/ui components."""
    files = files_of(template)
    return "\n".join(c for p, c in files.items() if p == "src/App.tsx" or (p.startswith("src/ui/") and p.endswith(".tsx")))


def app_css(template: str) -> str:
    files = files_of(template)
    return files["src/app.css"] + files["src/ui/globals.css"]


def test_three_templates_are_offered() -> None:
    assert AppKind().templates == frozenset({"blank", "tabs", "list-detail"})
    with pytest.raises(ValidationFailed):
        AppKind().initial_settings("kanban")


@pytest.mark.parametrize("template", ["blank", "tabs", "list-detail"])
def test_template_files_pass_the_allow_list(template: str) -> None:
    files = files_of(template)
    assert {"src/main.tsx", "src/App.tsx"} <= set(files)
    for path, content in files.items():
        appfiles.validate_path(path)
        appfiles.validate_content(path, content)


def test_tabs_template_is_an_accessible_tablist() -> None:
    app = app_source("tabs")
    for marker in ('role="tablist"', 'role="tab"', 'role="tabpanel"', "aria-selected", "aria-controls",
                   "aria-labelledby", "ArrowRight", "ArrowLeft", "tabIndex"):
        assert marker in app, marker
    assert "<TabBar" in files_of("tabs")["src/App.tsx"]
    css = app_css("tabs")
    assert "@media (min-width" in files_of("tabs")["src/app.css"]  # mobile-first: base styles are the phone layout
    assert ":focus-visible" in css


def test_list_detail_template_is_accessible_and_mobile_first() -> None:
    app = app_source("list-detail")
    for marker in ("aria-current", "<button", "Back", "<h2", "focus()", "<ul", "aria-label"):
        assert marker in app, marker
    assert "<ListItem" in files_of("list-detail")["src/App.tsx"]
    css = app_css("list-detail")
    assert "@media (min-width" in files_of("list-detail")["src/app.css"] and ":focus-visible" in css


@pytest.mark.parametrize("template", ["tabs", "list-detail"])
def test_create_project_with_template_writes_its_files(studio: Studio, template: str) -> None:
    pid = studio.create_project(OWNER, f"t {template}", template, "")["id"]
    names = {f["path"] for f in studio.list_files(OWNER, pid)}
    assert {"src/main.tsx", "src/App.tsx", "src/app.css"} <= names
    ws = studio.workspace(pid, create=False)
    assert ws.read_bytes("src/App.tsx").decode() == files_of(template)["src/App.tsx"]
