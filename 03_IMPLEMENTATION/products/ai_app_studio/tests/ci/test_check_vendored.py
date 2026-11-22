"""scripts/check_vendored.py on the real tree, plus positive controls on a temp copy (THREAT_MODEL.md N6)."""

from __future__ import annotations

import hashlib
import json
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
