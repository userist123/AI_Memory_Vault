"""No annotation may name a module that `controller.py` imports at the bottom.

`reasoning` and `executive` take the controller itself, so the four cognitive
modules are imported at the end of the file to avoid a circular import. Any
annotation above that line which names one of them is evaluated first — on
Python 3.11 a return annotation runs when its `def` runs — and the import
explodes with `NameError: name 'WorkingMemory' is not defined`.

It passed locally on Python 3.14, where PEP 649 made annotations lazy, and
failed the zero-regression gate on CI's 3.11. This test does not depend on
which interpreter runs it: it reads the file.
"""
from __future__ import annotations

import ast
from pathlib import Path

CONTROLLER = Path(__file__).resolve().parents[1] / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py"


def late_imported_names(tree: ast.Module) -> tuple[set[str], int]:
    """Names imported after the last definition, and the line the first such import sits on.

    That line is the boundary: everything above it runs before the name exists.
    """
    last_def = max(
        (node.lineno for node in tree.body if isinstance(node, (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef))),
        default=0,
    )
    names: set[str] = set()
    first_late = None
    for node in tree.body:
        if isinstance(node, (ast.Import, ast.ImportFrom)) and node.lineno > last_def:
            names.update(alias.asname or alias.name.split(".")[-1] for alias in node.names)
            first_late = node.lineno if first_late is None else min(first_late, node.lineno)
    return names, (first_late or 0)


def unquoted_annotation_uses(tree: ast.Module, names: set[str], before_line: int) -> list[str]:
    """Every annotation before `before_line` that names one of `names` without quoting it."""
    found = []
    for node in ast.walk(tree):
        annotations = []
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            annotations = [node.returns] + [a.annotation for a in node.args.args + node.args.kwonlyargs]
        elif isinstance(node, ast.AnnAssign):
            annotations = [node.annotation]
        for annotation in annotations:
            if annotation is None or getattr(annotation, "lineno", 0) >= before_line:
                continue
            for sub in ast.walk(annotation):
                # A string annotation is an ast.Constant, never an ast.Name, so
                # quoting is exactly what this check is looking for.
                if isinstance(sub, ast.Name) and sub.id in names:
                    found.append(f"line {sub.lineno}: {sub.id}")
    return found


def test_no_annotation_names_a_late_import():
    source = CONTROLLER.read_text(encoding="utf-8")
    tree = ast.parse(source)
    names, boundary = late_imported_names(tree)
    assert names, "controller.py no longer imports anything after its definitions; drop this test"
    assert unquoted_annotation_uses(tree, names, boundary) == []


def test_the_check_catches_the_bug_it_was_written_for():
    """The exact shape that broke CI: a return annotation above a bottom import."""
    # The class is called Holder rather than C because "class C:" followed by an
    # escaped newline reads as a Windows drive path to the repository hygiene
    # validator, which fails any test file containing an absolute path.
    broken = ast.parse(
        "class Holder:\n"
        "    def get(self) -> WorkingMemory:\n"
        "        return WorkingMemory()\n"
        "from cognitive_core.working_memory import WorkingMemory\n"
    )
    names, boundary = late_imported_names(broken)
    assert "WorkingMemory" in names
    hits = unquoted_annotation_uses(broken, names, boundary)
    assert hits, "an unquoted return annotation over a late import must be caught"


def test_quoting_the_same_annotation_passes():
    fixed = ast.parse(
        "class Holder:\n"
        '    def get(self) -> "WorkingMemory":\n'
        "        return WorkingMemory()\n"
        "from cognitive_core.working_memory import WorkingMemory\n"
    )
    names, boundary = late_imported_names(fixed)
    assert unquoted_annotation_uses(fixed, names, boundary) == []
