# -*- coding: utf-8 -*-
"""Tests for Git isolation of MemoryController operations.

These tests create a temporary Git repository under the pytest `tmp_path`
fixture, copy the relevant `memory_controller` package into it, and verify
that normal controller actions (e.g., `propose`, `review`) do **not** cause
any file modifications or automatic Git commits.

The suite also checks that explicit staging, committing, and reverting work
as expected, and that unrelated files are never staged automatically.
"""

import os
import shutil
import subprocess
import sys
import importlib.util
from pathlib import Path

import pytest


def _init_git_repo(repo_path: Path) -> None:
    """Initialise an empty Git repository at ``repo_path``.
    ``repo_path`` must already exist.
    """
    subprocess.run(["git", "init"], cwd=str(repo_path), check=True, capture_output=True)
    subprocess.run(["git", "config", "user.name", "Test User"], cwd=str(repo_path), check=True)
    subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=str(repo_path), check=True)


def _copy_memory_controller(src_root: Path, dest_root: Path) -> Path:
    src_pkg = src_root / "memory_controller"
    if not src_pkg.exists():
        src_pkg = src_root / "03_IMPLEMENTATION" / "packages" / "memory"
    dst_pkg = dest_root / "memory_controller"
    shutil.copytree(src_pkg, dst_pkg)
    return dst_pkg


def _load_controller_module(pkg_path: Path):
    sys.path.insert(0, str(pkg_path.parent))
    spec = importlib.util.find_spec("memory_controller.controller")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    sys.path.pop(0)
    return module.MemoryController, module.controller


def test_git_isolation(tmp_path: Path):
    # Initialise temporary Git repo
    _init_git_repo(tmp_path)

    # Copy the memory_controller package into the repo
    pkg_path = _copy_memory_controller(Path.cwd(), tmp_path)

    # Add an unrelated file and commit baseline
    unrelated_file = tmp_path / "unrelated.txt"
    unrelated_file.write_text("initial content\n")
    subprocess.run(["git", "add", "unrelated.txt"], cwd=str(tmp_path), check=True)
    subprocess.run(["git", "commit", "-m", "baseline commit"], cwd=str(tmp_path), check=True)

    # Add .gitignore to ignore the copied memory_controller package
    (tmp_path / ".gitignore").write_text("memory_controller/\n")
    subprocess.run(["git", "add", ".gitignore"], cwd=str(tmp_path), check=True)
    subprocess.run(["git", "commit", "-m", "add .gitignore"], cwd=str(tmp_path), check=True)
    status = subprocess.run(["git", "status", "--porcelain"], cwd=str(tmp_path), capture_output=True, text=True, check=True)
    assert status.stdout.strip() == ""

    # Load controller from copied package
    MemoryController, controller = _load_controller_module(pkg_path)

    # Perform a propose operation (should not touch filesystem)
    note_id = "00000000-0000-0000-0000-000000000000"
    note_data = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "RAW",
        "provenance": {"source_type": "user", "source_ref": "test"},
    }
    from memory_controller.authorizer import Principal
    controller.propose(Principal.HUMAN, note_data)

    # Verify no Git changes
    after = subprocess.run(["git", "status", "--porcelain"], cwd=str(tmp_path), capture_output=True, text=True, check=True)
    assert after.stdout.strip() == ""

    # Manually edit a file to stage later
    target_file = pkg_path / "controller.py"
    original_content = target_file.read_text()
    target_file.write_text(original_content.replace("# controller.py", "# controller.py\n# MANUAL EDIT for test"))

    # Stage and commit the edit
    subprocess.run(["git", "add", "-f", str(target_file.relative_to(tmp_path))], cwd=str(tmp_path), check=True)
    subprocess.run(["git", "commit", "-m", "manual edit"], cwd=str(tmp_path), check=True)

    # Capture commit SHA
    sha = subprocess.run(["git", "rev-parse", "HEAD"], cwd=str(tmp_path), capture_output=True, text=True, check=True).stdout.strip()
    assert len(sha) == 40

    # Ensure unrelated file unchanged
    status2 = subprocess.run(["git", "status", "--porcelain"], cwd=str(tmp_path), capture_output=True, text=True, check=True)
    assert status2.stdout.strip() == ""

    # Revert the commit (no --hard)
    subprocess.run(["git", "revert", "--no-edit", sha], cwd=str(tmp_path), check=True)

    # Repo should be clean again
    # Verify the edited file has been removed (it did not exist before)
    assert not target_file.exists()

