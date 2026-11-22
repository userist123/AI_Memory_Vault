"""scripts/check_vendored.py on the real tree, plus positive controls on a temp copy (THREAT_MODEL.md N6)."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "check_vendored.py"
TARGET = "core/studio_core/policy.py"


def run(root: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run([sys.executable, str(SCRIPT), "--root", str(root)],  # noqa: S603
                          capture_output=True, text=True, check=False)


@pytest.fixture()
def copy(tmp_path: Path) -> Path:
    dst = tmp_path / "product"
    manifest = json.loads((ROOT / "VENDORED.json").read_text(encoding="utf-8"))
    rels = [e["path"] for e in manifest["files"]] + [e["path"] for e in manifest["local_files"]] + ["VENDORED.json"]
    for rel in rels:
        (dst / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / rel, dst / rel)
    return dst


def _entry(manifest: dict, rel: str) -> dict:  # type: ignore[type-arg]
    return next(e for e in manifest["files"] if e["path"] == rel)


def test_real_tree_is_clean() -> None:
    r = run(ROOT)
    assert r.returncode == 0, r.stderr


def test_copy_is_clean(copy: Path) -> None:
    r = run(copy)
    assert r.returncode == 0, r.stderr


def test_unlisted_change_fails(copy: Path) -> None:
    with (copy / TARGET).open("ab") as fh:
        fh.write(b"\n# local tweak\n")
    r = run(copy)
    assert r.returncode == 1
    assert f"{TARGET}: unlisted change" in r.stderr


def test_listed_patched_change_passes(copy: Path) -> None:
    with (copy / TARGET).open("ab") as fh:
        fh.write(b"\n# local tweak\n")
    m = json.loads((copy / "VENDORED.json").read_text(encoding="utf-8"))
    e = _entry(m, TARGET)
    e.update(patched=True, patch_reason="test: documented local patch",
             local_sha256=hashlib.sha256((copy / TARGET).read_bytes()).hexdigest())
    (copy / "VENDORED.json").write_text(json.dumps(m), encoding="utf-8")
    r = run(copy)
    assert r.returncode == 0, r.stderr


def test_patched_without_reason_fails(copy: Path) -> None:
    with (copy / TARGET).open("ab") as fh:
        fh.write(b"\n# local tweak\n")
    m = json.loads((copy / "VENDORED.json").read_text(encoding="utf-8"))
    _entry(m, TARGET).update(patched=True, patch_reason=" ",
                             local_sha256=hashlib.sha256((copy / TARGET).read_bytes()).hexdigest())
    (copy / "VENDORED.json").write_text(json.dumps(m), encoding="utf-8")
    r = run(copy)
    assert r.returncode == 1 and "patched without patch_reason" in r.stderr


def test_rehashed_but_not_marked_patched_fails(copy: Path) -> None:
    with (copy / TARGET).open("ab") as fh:
        fh.write(b"\n# local tweak\n")
    m = json.loads((copy / "VENDORED.json").read_text(encoding="utf-8"))
    _entry(m, TARGET)["local_sha256"] = hashlib.sha256((copy / TARGET).read_bytes()).hexdigest()
    (copy / "VENDORED.json").write_text(json.dumps(m), encoding="utf-8")
    r = run(copy)
    assert r.returncode == 1 and "differs from upstream but not marked patched" in r.stderr


def test_unlisted_file_in_vendored_tree_fails(copy: Path) -> None:
    (copy / "core/studio_core/renderer.py").write_text("x = 1\n", encoding="utf-8")
    r = run(copy)
    assert r.returncode == 1 and "core/studio_core/renderer.py: unlisted file" in r.stderr


def test_missing_listed_file_fails(copy: Path) -> None:
    (copy / TARGET).unlink()
    r = run(copy)
    assert r.returncode == 1 and "listed but missing" in r.stderr


def _outside_pkg(tmp_path: Path) -> Path:
    outside = tmp_path / "outside"
    outside.mkdir()
    (outside / "evil.py").write_text("import os\n", encoding="utf-8")
    return outside


def test_symlinked_dir_with_unlisted_code_fails(copy: Path, tmp_path: Path) -> None:
    link = copy / "core/studio_core/linked"
    try:
        os.symlink(_outside_pkg(tmp_path), link, target_is_directory=True)
    except (OSError, NotImplementedError) as e:
        pytest.skip(f"cannot create a directory symlink here: {e}")
    r = run(copy)
    assert r.returncode == 1 and "core/studio_core/linked: symlink/junction" in r.stderr


@pytest.mark.skipif(os.name != "nt", reason="junctions are Windows-only")
def test_junction_with_unlisted_code_fails(copy: Path, tmp_path: Path) -> None:
    link = copy / "tests/vendored/linked"
    r0 = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(_outside_pkg(tmp_path))],  # noqa: S603,S607
                        capture_output=True, check=False)
    assert r0.returncode == 0, r0.stderr
    r = run(copy)
    assert r.returncode == 1 and "tests/vendored/linked: symlink/junction" in r.stderr


def test_symlinked_file_fails(copy: Path, tmp_path: Path) -> None:
    link = copy / "core/studio_core/evil.py"
    try:
        os.symlink(_outside_pkg(tmp_path) / "evil.py", link)
    except (OSError, NotImplementedError) as e:
        pytest.skip(f"cannot create a file symlink here: {e}")
    r = run(copy)
    assert r.returncode == 1 and "core/studio_core/evil.py: symlink/junction" in r.stderr


@pytest.mark.skipif(os.name != "nt", reason="junctions are Windows-only")
def test_self_parent_junction_finishes_fast_and_fails(copy: Path) -> None:
    """L1: a junction pointing at its own parent must not be descended into (os.walk follows junctions on 3.12)."""
    parent = copy / "core/studio_core"
    link = parent / "loop"
    r0 = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(parent)],  # noqa: S603,S607
                        capture_output=True, check=False)
    assert r0.returncode == 0, r0.stderr
    try:
        r = subprocess.run([sys.executable, str(SCRIPT), "--root", str(copy)],  # noqa: S603
                           capture_output=True, text=True, check=False, timeout=20)
    finally:
        os.rmdir(link)  # remove the junction itself, never its target
    assert r.returncode == 1 and "core/studio_core/loop: symlink/junction" in r.stderr
    assert r.stderr.count("symlink/junction") == 1  # nothing below the junction was reported
    assert (parent / "policy.py").is_file()


def test_ci_mode_requires_residuals_block(copy: Path) -> None:
    """L2: CI mode (no --upstream-repo) trusts the manifest, so the manifest must state that residual."""
    m = json.loads((copy / "VENDORED.json").read_text(encoding="utf-8"))
    assert m["residuals"], "real manifest must carry residuals"
    del m["residuals"]
    (copy / "VENDORED.json").write_text(json.dumps(m), encoding="utf-8")
    r = run(copy)
    assert r.returncode == 1 and "residuals" in r.stderr
    m["residuals"] = []
    (copy / "VENDORED.json").write_text(json.dumps(m), encoding="utf-8")
    assert run(copy).returncode == 1  # empty block is as bad as none
    m["residuals"] = [{"id": "manifest-trust", "text": "CI trusts the manifest hashes"}]
    (copy / "VENDORED.json").write_text(json.dumps(m), encoding="utf-8")
    assert run(copy).returncode == 0  # positive control
