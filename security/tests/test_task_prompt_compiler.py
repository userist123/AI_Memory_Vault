"""Regression tests for the task prompt compiler integration."""

from pathlib import Path


SCRIPT = (
    Path(__file__).resolve().parents[2]
    / "30_SCRIPTS"
    / "prompt"
    / "compile_task_prompt.py"
)


def test_task_compiler_has_no_unfinished_prompt_todos():
    source = SCRIPT.read_text(encoding="utf-8")
    assert "TODO" not in source


def test_task_compiler_routes_through_verified_compiler():
    source = SCRIPT.read_text(encoding="utf-8")
    assert "VerifiedPromptCompiler" in source
    assert "compiler.compile(" in source
    assert 'source_language="en"' in source


def test_task_compiler_has_no_legacy_template_path():
    source = SCRIPT.read_text(encoding="utf-8")
    assert "TEMPLATE =" not in source
    assert "return TEMPLATE" not in source
