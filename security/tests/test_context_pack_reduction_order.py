"""Structural contract for context-pack integration."""

import ast
from pathlib import Path


PACK_BUILDER = (
    Path(__file__).resolve().parents[2]
    / "03_IMPLEMENTATION"
    / "packages"
    / "retrieval"
    / "context"
    / "pack_builder.py"
)


def _build_method():
    tree = ast.parse(PACK_BUILDER.read_text(encoding="utf-8"))
    for node in ast.walk(tree):
        if isinstance(node, ast.ClassDef) and node.name == "ContextPackBuilder":
            for child in node.body:
                if isinstance(child, ast.FunctionDef) and child.name == "build":
                    return child
    raise AssertionError("ContextPackBuilder.build not found")


def _call_names(node):
    names = []
    for child in ast.walk(node):
        if isinstance(child, ast.Call):
            fn = child.func
            if isinstance(fn, ast.Attribute):
                names.append(fn.attr)
            elif isinstance(fn, ast.Name):
                names.append(fn.id)
    return names


def test_context_builder_has_verified_reduction_before_budget_degradation():
    build = _build_method()
    calls = list(ast.walk(build))
    reduction_lines = [
        n.lineno for n in calls
        if isinstance(n, ast.Call)
        and isinstance(n.func, ast.Attribute)
        and n.func.attr in {"_verify_and_reduce", "reduce", "compile"}
    ]
    budget_lines = [
        n.lineno for n in calls
        if isinstance(n, ast.Call)
        and isinstance(n.func, ast.Attribute)
        and n.func.attr == "apply_degradation"
    ]

    assert reduction_lines, "verified reduction/handoff must be part of context building"
    assert budget_lines, "budget degradation must remain part of context building"
    assert min(reduction_lines) < min(budget_lines), (
        "context must be verified and reduced before budget degradation"
    )


def test_context_builder_records_reduction_metrics():
    source = PACK_BUILDER.read_text(encoding="utf-8")
    assert "tokens_saved" in source
    assert "original_chars" in source
    assert "final_chars" in source
