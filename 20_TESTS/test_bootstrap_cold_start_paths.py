"""A cold-start agent follows BOOTSTRAP and the PROTOCOL literally.

Both once told agents to read `09_COORDINATION/AGENT_MEMORY/...`, a tree that
does not exist, so the first instruction of every session dead-ended. Every
backticked repo-relative path in the three entry documents must resolve
EXACTLY (no basename search), with `<AGENT>` / `<project-id>` expanded against
the real directories.
"""
from __future__ import annotations

import itertools
import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
COORD = REPO / "00_GOVERNANCE" / "coordination"
DOCS = ("BOOTSTRAP_ALL_AGENTS_V1.md", "UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md", "README.md")
_TICKS = re.compile(r"`([^`\n]+)`")
_ROOTS = re.compile(r"^(?:\d\d_[A-Z_]+)/")
RETIRED = ("09_COORDINATION", "AGENT_MEMORY/")


def _expand(path: str, root: Path = COORD) -> list[str]:
    agents = sorted(p.name for p in (root / "agents").iterdir() if p.is_dir())
    projects = sorted(p.name for p in (root / "projects").iterdir() if p.is_dir())
    options = []
    for token, values in (("<AGENT>", agents), ("<project-id>", projects)):
        options.append(values if token in path else [None])
    out = []
    for a, p in itertools.product(*options):
        s = path
        if a:
            s = s.replace("<AGENT>", a)
        if p:
            s = s.replace("<project-id>", p)
        out.append(s)
    return out


def cited_paths(text: str) -> set[str]:
    return {t for t in _TICKS.findall(text) if _ROOTS.match(t) and " " not in t}


def unresolved(text: str, repo: Path = REPO) -> list[str]:
    bad = []
    for cited in sorted(cited_paths(text)):
        for concrete in _expand(cited, repo / "00_GOVERNANCE" / "coordination"):
            if not (repo / concrete.rstrip("/")).exists():
                bad.append(concrete)
    return bad


@pytest.mark.parametrize("doc", DOCS)
def test_cited_paths_resolve_exactly(doc):
    text = (COORD / doc).read_text(encoding="utf-8")
    assert unresolved(text) == []


@pytest.mark.parametrize("doc", DOCS)
def test_no_retired_tree_remains(doc):
    text = (COORD / doc).read_text(encoding="utf-8")
    assert [r for r in RETIRED if r in text] == []


def test_bootstrap_cites_the_three_entry_files():
    text = (COORD / DOCS[0]).read_text(encoding="utf-8")
    cited = cited_paths(text)
    assert "00_GOVERNANCE/coordination/UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md" in cited
    assert "00_GOVERNANCE/coordination/agents/<AGENT>/CURRENT.md" in cited
    assert "00_GOVERNANCE/coordination/projects/<project-id>/CURRENT.md" in cited


def test_negative_missing_path_is_reported(tmp_path):
    (tmp_path / "00_GOVERNANCE" / "coordination" / "agents" / "X").mkdir(parents=True)
    (tmp_path / "00_GOVERNANCE" / "coordination" / "projects" / "P").mkdir(parents=True)
    assert unresolved("`00_GOVERNANCE/coordination/agents/<AGENT>/CURRENT.md`", tmp_path) == [
        "00_GOVERNANCE/coordination/agents/X/CURRENT.md"
    ]
