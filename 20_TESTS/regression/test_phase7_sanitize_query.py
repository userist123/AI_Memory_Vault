"""Phase 7 (owner-approved 2026-10-22): sanitize_query keeps the query verbatim (only .strip()).

The query is data; HTML/template escaping belongs at render sites (sink audit: see
00_GOVERNANCE/coordination/tasks/todo-claude-overnight-reliability.md, PHASE 7). Both module copies
are loaded by file path (the name `security` is shadowed by the package).
"""
from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SEC = ROOT / "03_IMPLEMENTATION" / "packages" / "security"
COPIES = {"security.py": SEC / "security.py", "security/utils.py": SEC / "security" / "utils.py"}


def _load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location("p7_san_" + name.replace("/", "_").replace(".", "_"), path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


MODS = {k: _load(k, v) for k, v in COPIES.items()}
SAMPLES = [
    "a < b and c > d", "List<String>", r"<DRIVE>:\x", "ședință în București ăâîșț", "{{x}}",
    "<script>alert(1)</script>", "Hello {{prompt}} <script>alert(1)</script> World <html>",
    "x <= y -> z", "<!-- c --> <img src=x onerror=alert(1)>", "<" * 5000,
]


@pytest.fixture(params=sorted(COPIES))
def sanitize(request):
    return MODS[request.param].sanitize_query


@pytest.mark.parametrize("text", SAMPLES, ids=lambda t: repr(t[:20]))
def test_verbatim_preserved(sanitize, text):
    assert sanitize(text) == text
    assert len(sanitize(text)) <= len(text)


def test_only_surrounding_whitespace_stripped(sanitize):
    assert sanitize("  \n a < b {{x}}\t ") == "a < b {{x}}"


@pytest.mark.parametrize("text", SAMPLES + ["  pad  "], ids=lambda t: repr(t[:20]))
def test_both_copies_identical(text):
    outs = {k: m.sanitize_query(text) for k, m in MODS.items()}
    assert len(set(outs.values())) == 1


def test_check_query_size_unchanged():
    for m in MODS.values():
        m.check_query_size("A" * m.MAX_QUERY_LENGTH)
        with pytest.raises(ValueError):
            m.check_query_size("A" * (m.MAX_QUERY_LENGTH + 1))
