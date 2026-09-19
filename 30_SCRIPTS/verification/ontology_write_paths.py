"""Inventory of the Python code that can write into 01_ARCHITECTURE/ontology/slots/.

Static, AST-based. A module is "in a write context for the slots" when it both

  * refers to the canonical slots directory (a string containing
    ``ontology/slots``, a ``"ontology"`` + ``"slots"`` path built from parts, or
    a name such as ``SLOT_DIRECTORY`` / ``slots_dir`` / ``slot_file``), and
  * contains a call that writes or removes files (``open(..., "w"/"a"/"x")``,
    ``write_text``, ``write_bytes``, ``shutil.copy*/move/rmtree``,
    ``os.replace/rename/remove/unlink``, ``.unlink``).

That over-approximates on purpose: a module that reads the slots and writes a
report elsewhere is flagged too, and is then recorded as reviewed read-only.
The point is that no writer can appear without somebody classifying it.

Usage:
    python 30_SCRIPTS/verification/ontology_write_paths.py [--json OUT] [--report OUT.md]
"""
from __future__ import annotations

import argparse
import ast
import json
import os
import re
import sys
import warnings
from pathlib import Path
from typing import Any, Dict, List, Optional

REPO = Path(__file__).resolve().parents[2]
DISPOSITIONS = REPO / "20_TESTS" / "fixtures" / "ontology_slot_writers.json"

_SKIP_DIRS = {".git", "__pycache__", "node_modules", ".venv", "venv", ".claude", ".pytest_cache", "site-packages"}

_SLOT_NAME = re.compile(
    r"^(?:[A-Za-z_]*SLOTS?_(?:DIR|DIRECTORY|PATH)|slots?_(?:dir|directory|path|file|files)|slot_file_path)$", re.I)
_SLOT_LITERAL = re.compile(r"ontology[\\/]+slots")

_WRITE_ATTRS = {"write_text", "write_bytes", "unlink"}
_SHUTIL_WRITES = {"copy", "copy2", "copyfile", "copytree", "move", "rmtree"}
_OS_WRITES = {"replace", "rename", "remove", "unlink"}


def _is_write_open(call: ast.Call) -> bool:
    func = call.func
    name = func.id if isinstance(func, ast.Name) else (func.attr if isinstance(func, ast.Attribute) else None)
    if name != "open":
        return False
    mode = None
    if len(call.args) >= 2 and isinstance(call.args[1], ast.Constant):
        mode = call.args[1].value
    for kw in call.keywords:
        if kw.arg == "mode" and isinstance(kw.value, ast.Constant):
            mode = kw.value.value
    return isinstance(mode, str) and any(c in mode for c in "wax+")


def _write_call_name(call: ast.Call) -> Optional[str]:
    func = call.func
    if _is_write_open(call):
        return "open(write)"
    if isinstance(func, ast.Attribute):
        if func.attr in _WRITE_ATTRS:
            return func.attr
        base = func.value
        if isinstance(base, ast.Name) and base.id == "shutil" and func.attr in _SHUTIL_WRITES:
            return f"shutil.{func.attr}"
        if isinstance(base, ast.Name) and base.id == "os" and func.attr in _OS_WRITES:
            return f"os.{func.attr}"
    return None


def _category(rel: str) -> str:
    if rel.startswith("20_TESTS/") or "/test_" in rel or rel.split("/")[-1].startswith("test_"):
        return "test"
    if rel.startswith("03_IMPLEMENTATION/"):
        return "production"
    return "script"


class _Visitor(ast.NodeVisitor):
    def __init__(self) -> None:
        self.stack: List[str] = []
        self.writes: List[Dict[str, Any]] = []
        self.slot_refs: List[Dict[str, Any]] = []
        self.literals: set = set()

    def _scope(self) -> str:
        return ".".join(self.stack) or "<module>"

    def visit_FunctionDef(self, node):  # noqa: N802
        self.stack.append(node.name)
        self.generic_visit(node)
        self.stack.pop()

    visit_AsyncFunctionDef = visit_FunctionDef

    def visit_ClassDef(self, node):  # noqa: N802
        self.stack.append(node.name)
        self.generic_visit(node)
        self.stack.pop()

    def visit_Call(self, node):  # noqa: N802
        name = _write_call_name(node)
        if name:
            self.writes.append({"function": self._scope(), "line": node.lineno, "call": name})
        self.generic_visit(node)

    def visit_Constant(self, node):  # noqa: N802
        if isinstance(node.value, str):
            # Only path-like strings: a docstring that mentions the directory is not a reference.
            if len(node.value) <= 120 and "\n" not in node.value and _SLOT_LITERAL.search(node.value):
                self.slot_refs.append({"line": node.lineno, "what": f"literal {node.value!r}"[:90]})
            self.literals.add(node.value)

    def visit_Name(self, node):  # noqa: N802
        if _SLOT_NAME.match(node.id):
            self.slot_refs.append({"line": node.lineno, "what": f"name {node.id}"})

    def visit_arg(self, node):  # noqa: N802
        if _SLOT_NAME.match(node.arg):
            self.slot_refs.append({"line": node.lineno, "what": f"parameter {node.arg}"})

    def visit_Attribute(self, node):  # noqa: N802
        if _SLOT_NAME.match(node.attr):
            self.slot_refs.append({"line": node.lineno, "what": f"attribute {node.attr}"})
        self.generic_visit(node)


def analyse_source(rel: str, source: str) -> Optional[Dict[str, Any]]:
    """The module's inventory row, or None when it is not in a write context."""
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            tree = ast.parse(source)
    except (SyntaxError, ValueError):
        return None
    visitor = _Visitor()
    visitor.visit(tree)
    if {"ontology", "slots"} <= visitor.literals:
        visitor.slot_refs.append({"line": 0, "what": "path built from 'ontology' + 'slots'"})
    if not visitor.slot_refs or not visitor.writes:
        return None
    seen, refs = set(), []
    for ref in sorted(visitor.slot_refs, key=lambda r: (r["line"], r["what"])):
        key = (ref["line"], ref["what"])
        if key not in seen:
            seen.add(key)
            refs.append(ref)
    return {"file": rel, "category": _category(rel), "writes": visitor.writes, "slot_refs": refs[:6]}


def scan(repo_root: Path) -> List[Dict[str, Any]]:
    rows = []
    for dirpath, dirnames, filenames in os.walk(repo_root):
        dirnames[:] = sorted(d for d in dirnames if d not in _SKIP_DIRS)
        for name in sorted(filenames):
            if not name.endswith(".py"):
                continue
            path = Path(dirpath) / name
            rel = path.relative_to(repo_root).as_posix()
            try:
                source = path.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            row = analyse_source(rel, source)
            if row:
                rows.append(row)
    return rows


def load_dispositions(path: Path = DISPOSITIONS) -> Dict[str, Dict[str, Any]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    return {e["path"]: e for e in data["writers"]}


def unlisted(rows: List[Dict[str, Any]], dispositions: Dict[str, Dict[str, Any]]) -> List[str]:
    return sorted(r["file"] for r in rows if r["file"] not in dispositions)


def stale(rows: List[Dict[str, Any]], dispositions: Dict[str, Dict[str, Any]]) -> List[str]:
    found = {r["file"] for r in rows}
    return sorted(p for p in dispositions if p not in found)


def render_report(rows: List[Dict[str, Any]], dispositions: Dict[str, Dict[str, Any]],
                  generic: List[Dict[str, Any]]) -> str:
    lines = [
        "# Write paths into `01_ARCHITECTURE/ontology/slots/`",
        "",
        "Generated by `30_SCRIPTS/verification/ontology_write_paths.py` from a static scan of every Python file",
        "in the repository, joined with the reviewed dispositions in `20_TESTS/fixtures/ontology_slot_writers.json`.",
        "No row below is hand-typed: the file, function, line and call come from the scan; the role, gate and",
        "conclusion come from the committed dispositions.",
        "",
        "The scan over-approximates: a module that mentions the slots and writes anywhere is listed, and the",
        "conclusion column says whether it really writes into them.",
        "",
        "## Modules in a write context",
        "",
        "| file | function : line | call | kind | gate | conclusion |",
        "|---|---|---|---|---|---|",
    ]
    for row in rows:
        disp = dispositions.get(row["file"])
        gate = disp["gate"] if disp else "**UNREVIEWED**"
        concl = disp["conclusion"] if disp else "**UNREVIEWED: classify this module**"
        writes = row["writes"]
        first = writes[0]
        where = f"{first['function']} : {first['line']}"
        if len(writes) > 1:
            where += f" (+{len(writes) - 1} more)"
        lines.append(f"| `{row['file']}` | {where} | {first['call']} | {row['category']} | {gate} | {concl} |")
    lines += ["", "## Paths the static scan cannot see", "",
              "| path | gate | conclusion |", "|---|---|---|"]
    for g in generic:
        lines.append(f"| {g['path']} | {g['gate']} | {g['conclusion']} |")
    counts: Dict[str, int] = {}
    for row in rows:
        disp = dispositions.get(row["file"])
        key = disp["role"] if disp else "UNREVIEWED"
        counts[key] = counts.get(key, 0) + 1
    lines += ["", "## Totals (from the scan)", ""]
    for key in sorted(counts):
        lines.append(f"- {key}: {counts[key]}")
    lines.append("")
    return "\n".join(lines)


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--json", help="write the raw inventory here")
    parser.add_argument("--report", help="write the markdown report here")
    args = parser.parse_args(argv)
    rows = scan(REPO)
    disp = load_dispositions() if DISPOSITIONS.exists() else {}
    if args.json:
        Path(args.json).parent.mkdir(parents=True, exist_ok=True)
        Path(args.json).write_text(json.dumps({"modules": rows}, indent=2, ensure_ascii=False) + "\n",
                                   encoding="utf-8", newline="\n")
    if args.report:
        generic = json.loads(DISPOSITIONS.read_text(encoding="utf-8")).get("not_visible_to_the_scan", [])
        Path(args.report).parent.mkdir(parents=True, exist_ok=True)
        Path(args.report).write_text(render_report(rows, disp, generic), encoding="utf-8", newline="\n")
    print(f"MODULES_IN_WRITE_CONTEXT={len(rows)}")
    print(f"UNREVIEWED={len(unlisted(rows, disp))}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
