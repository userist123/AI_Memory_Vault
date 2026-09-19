"""The README must not cite what the repository does not have.

Its workflow list had fallen 22 files behind and still named two workflows that
had been deleted; its quick start called a module path that no longer resolved.
Nothing failed, because nothing read the README. These tests do, in both
directions for workflows (every workflow named exists, every workflow that
exists is named) and in one direction for paths (every repository path cited
exists).

Paths under 06_INBOX/ are exempt: that tree is gitignored by design, and the
README names it to say so.
"""
from __future__ import annotations

import fnmatch
import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
READMES = ("README.md", "README.en.md")
WORKFLOWS = REPO / ".github" / "workflows"
UNTRACKED_BY_DESIGN = ("06_INBOX/",)

_PATH_IN_CODE = re.compile(r"`((?:\d\d_[A-Z_]+|\.github|cognitive_core)/[^`\s<>*]+)`")
_PATH_IN_LINK = re.compile(r"\]\(((?:\d\d_[A-Z_]+|\.github|cognitive_core)/[^)\s#]+)\)")
_WORKFLOW_NAME = re.compile(r"`([A-Za-z0-9_.*-]+\.yml)`")


def cited_paths(text: str) -> set[str]:
    return set(_PATH_IN_CODE.findall(text)) | set(_PATH_IN_LINK.findall(text))


def missing_paths(text: str, root: Path) -> list[str]:
    return sorted(
        p for p in cited_paths(text)
        if not p.startswith(UNTRACKED_BY_DESIGN) and not (root / p.rstrip("/")).exists()
    )


def named_workflows(text: str) -> set[str]:
    return set(_WORKFLOW_NAME.findall(text))


def unmatched_names(names: set[str], files: set[str]) -> list[str]:
    return sorted(n for n in names if not fnmatch.filter(files, n))


def unnamed_files(names: set[str], files: set[str]) -> list[str]:
    return sorted(f for f in files if not any(fnmatch.fnmatch(f, n) for n in names))


def _workflow_files() -> set[str]:
    return {p.name for p in WORKFLOWS.glob("*.yml")}


@pytest.mark.parametrize("readme", READMES)
def test_every_cited_path_exists(readme):
    missing = missing_paths((REPO / readme).read_text(encoding="utf-8"), REPO)
    assert not missing, f"{readme} cites paths that do not exist: {missing}"


@pytest.mark.parametrize("readme", READMES)
def test_every_named_workflow_exists(readme):
    names = named_workflows((REPO / readme).read_text(encoding="utf-8"))
    assert names, f"{readme} names no workflow at all"
    unmatched = unmatched_names(names, _workflow_files())
    assert not unmatched, f"{readme} names workflows that do not exist: {unmatched}"


@pytest.mark.parametrize("readme", READMES)
def test_every_workflow_is_named(readme):
    names = named_workflows((REPO / readme).read_text(encoding="utf-8"))
    unnamed = unnamed_files(names, _workflow_files())
    assert not unnamed, (
        f"{readme} does not mention these workflows; add them to the CI table: {unnamed}"
    )


def test_both_languages_cite_the_same_paths_and_workflows():
    ro, en = ((REPO / r).read_text(encoding="utf-8") for r in READMES)
    assert cited_paths(ro) == cited_paths(en), (
        "paths differ between languages: "
        f"only RO {sorted(cited_paths(ro) - cited_paths(en))}, only EN {sorted(cited_paths(en) - cited_paths(ro))}"
    )
    assert named_workflows(ro) == named_workflows(en)


# --- the checks can fail --------------------------------------------------------

def test_a_missing_path_is_reported(tmp_path):
    (tmp_path / "01_ARCHITECTURE").mkdir()
    text = "see `01_ARCHITECTURE/` and [`07_EVALUATION/gone.md`](07_EVALUATION/gone.md)"
    assert missing_paths(text, tmp_path) == ["07_EVALUATION/gone.md"]


def test_the_untracked_inbox_is_exempt(tmp_path):
    assert missing_paths("raw imports live in `06_INBOX/RAW_IMPORTS/`", tmp_path) == []


def test_a_deleted_workflow_is_reported():
    names = {"r001-enforcement.yml", "process-raw-books.yml"}
    assert unmatched_names(names, {"r001-enforcement.yml"}) == ["process-raw-books.yml"]


def test_an_unlisted_workflow_is_reported():
    names = {"r001-enforcement.yml", "polymarket-phase*.yml"}
    files = {"r001-enforcement.yml", "polymarket-phase3-tests.yml", "write-path-audit.yml"}
    assert unnamed_files(names, files) == ["write-path-audit.yml"]
