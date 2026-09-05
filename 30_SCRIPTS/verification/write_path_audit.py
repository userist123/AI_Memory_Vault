#!/usr/bin/env python3
"""Static inventory of direct memory-storage and filesystem write paths.

This audit is intentionally read-only. It scans Python source with the AST and
reports calls that may mutate storage outside canonical controller boundaries.
It does not decide whether a finding is safe; findings require architectural
classification.
"""
from __future__ import annotations

import argparse
import ast
import json
from dataclasses import asdict, dataclass
from pathlib import Path

SKIP_PARTS = {".git", "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache"}


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    column: int
    kind: str
    expression: str
    classification: str


def iter_python_files(root: Path):
    for path in sorted(root.rglob("*.py")):
        if any(part in SKIP_PARTS for part in path.parts):
            continue
        yield path


def call_name(node: ast.Call) -> str:
    fn = node.func
    if isinstance(fn, ast.Attribute):
        parts = []
        cur: ast.AST | None = fn
        while isinstance(cur, ast.Attribute):
            parts.append(cur.attr)
            cur = cur.value
        if isinstance(cur, ast.Name):
            parts.append(cur.id)
            return ".".join(reversed(parts))
    elif isinstance(fn, ast.Name):
        return fn.id
    return "<dynamic>"


def classify(path: Path, name: str) -> str:
    text = path.as_posix()
    if text.endswith("memory_controller/controller.py"):
        return "CANONICAL_CONTROLLER"
    if text.endswith("cognitive_core/consolidation.py"):
        return "CANONICAL_RECONSOLIDATION_BOUNDARY"
    if _is_file_mutator(name):
        return "FILE_WRITE"
    if name.endswith(".set") or name.endswith(".delete"):
        return "DIRECT_STORAGE_MUTATION"
    return "OTHER_MUTATION"


def _is_file_mutator(name: str) -> bool:
    """Return True for common Python filesystem mutation APIs."""
    direct = {
        "open",
        "os.remove",
        "os.unlink",
        "os.rmdir",
        "Path.write_text",
        "Path.write_bytes",
        "Path.unlink",
        "Path.rmdir",
        "shutil.copy",
        "shutil.copy2",
        "shutil.copyfile",
        "shutil.move",
        "shutil.rmtree",
    }
    if name in direct:
        return True
    return name.endswith(".write") or name.endswith(".writelines")


def audit(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    for path in iter_python_files(root):
        try:
            tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        except (OSError, SyntaxError):
            continue
        rel = path.relative_to(root).as_posix()
        for node in ast.walk(tree):
            if not isinstance(node, ast.Call):
                continue
            name = call_name(node)
            interesting = (
                name.endswith(".storage.set")
                or name.endswith(".storage.delete")
                or name.endswith(".store.set")
                or name.endswith(".store.delete")
                or name.endswith(".set")
                or name.endswith(".delete")
                or _is_file_mutator(name)
            )
            if not interesting:
                continue
            findings.append(
                Finding(
                    path=rel,
                    line=node.lineno,
                    column=node.col_offset,
                    kind="CALL",
                    expression=name,
                    classification=classify(path, name),
                )
            )
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    args = parser.parse_args()

    root = args.root.resolve()
    findings = audit(root)
    if args.json:
        print(json.dumps([asdict(item) for item in findings], indent=2, sort_keys=True))
        return 0

    print(f"WRITE_PATH_AUDIT_ROOT={root}")
    print(f"FINDINGS={len(findings)}")
    for item in findings:
        print(f"{item.path}:{item.line}:{item.column}: {item.classification}: {item.expression}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
