#!/usr/bin/env python3
"""Repository hygiene checks for the R001 structural/security contract."""
from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path

ROOT_ALLOWED = {
    ".editorconfig",
    ".gitattributes",
    ".gitignore",
    ".pre-commit-config.yaml",
    "AGENTS.md",
    "LICENSE",
    "LICENSE.txt",
    "Makefile",
    "README.md",
    "justfile",
    "docker-compose.yml",
    "pyproject.toml",
}
ROOT_ALLOWED_PREFIXES = ("requirements", "uv.lock", "poetry.lock", "Pipfile", "package.json", "package-lock.json")
NUMBERED_RE = re.compile(r"^\d{2}_[^/]+$")
ABSOLUTE_PATH_RE = re.compile(r"(?:[A-Za-z]:[\\/]|\\\\|(?<![A-Za-z0-9])/(?:home|Users|mnt|tmp|var|opt)/)")
FORBIDDEN_PRODUCTION_SUFFIXES = {
    ".ipynb",
    ".jpg",
    ".jpeg",
    ".png",
    ".gif",
    ".sqlite",
    ".sqlite3",
    ".db",
    ".log",
}
FORBIDDEN_NAMES = {"__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache"}


def git_files(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files"], cwd=root, check=True, text=True, capture_output=True
    )
    return [line for line in result.stdout.splitlines() if line]


def check_numbered_roots(root: Path) -> list[str]:
    failures: list[str] = []
    for path in sorted(root.iterdir()):
        if not path.is_dir() or not NUMBERED_RE.match(path.name):
            continue
        children = sorted(child.name for child in path.iterdir())
        allowed = {"README.md", ".gitkeep"}
        extras = [name for name in children if name not in allowed]
        if extras:
            failures.append(f"NUMBERED_ROOT:{path.name}:unexpected={extras}")
    return failures


def check_inbox(files: list[str]) -> list[str]:
    failures: list[str] = []
    allowed = {"06_INBOX/README.md", "06_INBOX/.gitkeep"}
    for path in files:
        if path == "06_INBOX" or path.startswith("06_INBOX/"):
            if path not in allowed:
                failures.append(f"INBOX_TRACKED:{path}")
    return failures


def check_production(files: list[str]) -> list[str]:
    failures: list[str] = []
    production_roots = ("03_IMPLEMENTATION/", "cognitive_core/", "memory_controller/")
    for file in files:
        if not file.startswith(production_roots):
            continue
        parts = Path(file).parts
        if any(part in FORBIDDEN_NAMES for part in parts):
            failures.append(f"PRODUCTION_RUNTIME_ARTIFACT:{file}")
        if Path(file).suffix.lower() in FORBIDDEN_PRODUCTION_SUFFIXES:
            failures.append(f"PRODUCTION_FORBIDDEN_SUFFIX:{file}")
    return failures


def check_test_paths(root: Path, files: list[str]) -> list[str]:
    failures: list[str] = []
    for file in files:
        if not (file.startswith("20_TESTS/") or "/tests/" in file or file.startswith("tests/")):
            continue
        candidate = root / file
        if candidate.suffix.lower() not in {".py", ".ps1", ".cmd", ".sh", ".yml", ".yaml", ".toml", ".json"}:
            continue
        try:
            text = candidate.read_text(encoding="utf-8", errors="replace")
        except OSError as exc:
            failures.append(f"TEST_READ_ERROR:{file}:{exc}")
            continue
        if ABSOLUTE_PATH_RE.search(text):
            failures.append(f"TEST_ABSOLUTE_PATH:{file}")
    return failures


def check_root_files(root: Path) -> list[str]:
    failures: list[str] = []
    for path in sorted(root.iterdir()):
        if path.name == ".git" or path.is_dir():
            continue
        if path.name in ROOT_ALLOWED or any(path.name.startswith(prefix) for prefix in ROOT_ALLOWED_PREFIXES):
            continue
        failures.append(f"ROOT_FILE_NOT_ALLOWLISTED:{path.name}")
    return failures


def run(root: Path) -> list[str]:
    files = git_files(root)
    failures = []
    failures.extend(check_root_files(root))
    failures.extend(check_numbered_roots(root))
    failures.extend(check_inbox(files))
    failures.extend(check_production(files))
    failures.extend(check_test_paths(root, files))
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    root = args.root.resolve()
    failures = run(root)
    print(f"REPOSITORY_HYGIENE_ROOT={root}")
    print(f"TRACKED_FILES={len(git_files(root))}")
    print(f"FAILURES={len(failures)}")
    for failure in failures:
        print(failure)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
