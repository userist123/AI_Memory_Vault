"""Repository layout policy enforcement for CI and local pre-commit use."""
from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path
from typing import Iterable

NUMBERED_ROOTS = {
    "00_GOVERNANCE", "01_ARCHITECTURE", "02_PRODUCT", "03_IMPLEMENTATION",
    "04_CONFIG", "05_DATA", "06_INBOX", "07_EVALUATION", "08_OBSERVABILITY",
    "09_SECURITY", "10_DOCUMENTATION", "20_TESTS", "30_SCRIPTS", "40_EXPERIMENTS",
    "50_ARTIFACTS", "60_DEPLOYMENT", "70_INTEGRATIONS", "80_ARCHIVE", "90_RELEASE", "99_META",
}
ROOT_ALLOWLIST = {
    "README.md", "LICENSE", "AGENTS.md", "CLAUDE.md", "pyproject.toml", ".gitignore", ".gitattributes",
    ".editorconfig", ".env.example", ".pre-commit-config.yaml", ".gitleaks.toml", "pytest.ini",
    "requirements.txt", "requirements-memory-v6.txt", "cognitive_core", "scripts",
    ".github", ".agents", ".claude-plugin", ".obsidian",
    *NUMBERED_ROOTS,
}
ROOT_FILE_ALLOWLIST = {"README.md", ".gitkeep"}
BANNED_IMPL_DIRS = {"__pycache__", "bin", "obj"}
BANNED_IMPL_SUFFIXES = {".ipynb", ".sqlite", ".sqlite3", ".db", ".sqlite-shm", ".sqlite-wal", ".sqlite3-shm", ".sqlite3-wal", ".png", ".jpg", ".jpeg", ".gif", ".pdf"}
BANNED_IMPORT_ROOTS = ("40_EXPERIMENTS", "50_ARTIFACTS", "06_INBOX", "80_ARCHIVE")
ABSOLUTE_PATH_RE = re.compile(r"(?:(?<![A-Za-z0-9_])[A-Za-z]:[\\/](?![nrt])|/home/|/mnt/data/|/workspace/)")


def git_files(root: Path) -> list[str]:
    out = subprocess.check_output(["git", "ls-files", "-z"], cwd=root)
    return sorted(p for p in out.decode("utf-8").split("\0") if p)


def validate(paths: Iterable[str], root: Path | None = None) -> list[str]:
    root = (root or Path.cwd()).resolve()
    paths = sorted(paths)
    errors: list[str] = []
    top_level = {p.split("/", 1)[0] for p in paths}

    for entry in sorted(top_level - ROOT_ALLOWLIST):
        errors.append(f"ROOT_DISALLOWED:{entry}")

    for numeric_root in sorted(NUMBERED_ROOTS):
        prefix = numeric_root + "/"
        members = [p for p in paths if p.startswith(prefix)]
        if not any(p == prefix + "README.md" for p in members):
            errors.append(f"NUMBERED_ROOT_MISSING_README:{numeric_root}")
        for path in members:
            rel = path[len(prefix):]
            if "/" not in rel and rel not in ROOT_FILE_ALLOWLIST:
                # Direct files are allowed for concise contracts, indexes,
                # configuration and reports; executable implementation is not.
                suffix = Path(rel).suffix.lower()
                if suffix in {".py", ".ps1", ".sh", ".exe", ".dll", ".so", ".bat", ".cmd"}:
                    if numeric_root not in {"20_TESTS", "07_EVALUATION"}:
                        errors.append(f"NUMBERED_ROOT_DIRECT_EXECUTABLE:{path}")

    for path in paths:
        parts = path.split("/")
        if path == "06_INBOX/README.md" or path == "06_INBOX/.gitkeep":
            continue
        if path.startswith("06_INBOX/"):
            errors.append(f"INBOX_TRACKED:{path}")
        if "__pycache__" in parts or path.endswith(".pyc"):
            errors.append(f"TRACKED_PYTHON_CACHE:{path}")
        if path.startswith("03_IMPLEMENTATION/"):
            if any(part in BANNED_IMPL_DIRS for part in parts):
                errors.append(f"IMPLEMENTATION_GENERATED_DIR:{path}")
            if any(path.lower().endswith(suffix) for suffix in BANNED_IMPL_SUFFIXES):
                errors.append(f"IMPLEMENTATION_ARTIFACT:{path}")
            if path.endswith(".py"):
                text = (root / path).read_text(encoding="utf-8", errors="replace")
                for import_root in BANNED_IMPORT_ROOTS:
                    if re.search(rf"(?:from|import)\s+{re.escape(import_root)}(?:[./]|\s|$)", text):
                        errors.append(f"IMPLEMENTATION_IMPORT_BOUNDARY:{path}:{import_root}")
                if ABSOLUTE_PATH_RE.search(text):
                    errors.append(f"IMPLEMENTATION_ABSOLUTE_PATH:{path}")

    return sorted(set(errors))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=None)
    args = parser.parse_args()
    root = Path(args.repo).resolve() if args.repo else Path.cwd().resolve()
    paths = git_files(root)
    errors = validate(paths, root)
    if errors:
        print("LAYOUT_STATUS=FAIL")
        print(f"ERROR_COUNT={len(errors)}")
        for error in errors:
            print(error)
        return 1
    print("LAYOUT_STATUS=PASS")
    print(f"TRACKED_FILE_COUNT={len(paths)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
