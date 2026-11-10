"""WP-01: the AST package inventory on a synthetic tree and on the real repository."""
from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "30_SCRIPTS" / "verification" / "package_inventory.py"


def _write(root: Path, rel: str, text: str) -> None:
    p = root / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def _run(root: Path, tmp: Path) -> tuple[int, dict, str]:
    out_json, out_md = tmp / "o" / "inv.json", tmp / "o" / "inv.md"
    proc = subprocess.run([sys.executable, str(SCRIPT), "--root", str(root),
                           "--out-json", str(out_json), "--out-md", str(out_md)],
                          capture_output=True, text=True, timeout=300)
    data = json.loads(out_json.read_text("utf-8")) if out_json.exists() else {}
    md = out_md.read_text("utf-8") if out_md.exists() else ""
    return proc.returncode, data, md


def _unit(data: dict, uid: str) -> dict:
    return next(u for u in data["units"] if u["id"] == uid)


def test_synthetic_tree_splits_consumers_unknown_and_entry_points(tmp_path):
    root = tmp_path / "repo"
    pk = "03_IMPLEMENTATION/packages"
    _write(root, f"{pk}/alpha/__init__.py", "__all__ = []\n")
    _write(root, f"{pk}/alpha/core.py", "# comment\n\nVALUE = 1\n")
    _write(root, f"{pk}/beta/__init__.py", "")
    _write(root, f"{pk}/beta/__main__.py", "import alpha\n")
    _write(root, f"{pk}/beta/run.py",
           "import alpha.core\nimport requests\n\nif __name__ == \"__main__\":\n    pass\n")
    _write(root, f"{pk}/beta/dyn.py", "import importlib\n\ndef f(n):\n    return importlib.import_module(n)\n")
    _write(root, "20_TESTS/test_alpha.py", "import alpha\n")

    code, data, md = _run(root, tmp_path)
    assert code == 0
    alpha, beta = _unit(data, "packages/alpha"), _unit(data, "packages/beta")

    # consumers: beta files are production consumers of alpha, the test file is a test consumer
    assert alpha["consumers_prod"] == [f"{pk}/beta/__main__.py", f"{pk}/beta/run.py"]
    assert alpha["consumers_test"] == ["20_TESTS/test_alpha.py"]
    assert beta["consumers_prod"] == [] and beta["consumers_test"] == []
    assert beta["imports_out"] == {"packages/alpha": 2}
    assert alpha["has_all"] is True and beta["has_all"] is False

    # entry points
    assert beta["entry_points"]["main_guard"] == [f"{pk}/beta/run.py"]
    assert beta["entry_points"]["dunder_main"] == [f"{pk}/beta/__main__.py"]
    assert alpha["entry_points"]["main_guard"] == []

    # LOC ignores blank lines and comments; third-party import is external
    assert alpha["loc"] == 2  # __all__ line + VALUE line
    assert "requests" in beta["external_deps"]

    # UNKNOWN: the non-literal dynamic import is reported, not guessed
    assert [(u["kind"], u["file"]) for u in beta["unknown"]] == [("dynamic_import", f"{pk}/beta/dyn.py")]
    assert alpha["unknown"] == []
    assert "| `packages/alpha` |" in md


def test_smoke_on_the_real_repository(tmp_path):
    code, data, md = _run(REPO, tmp_path)
    assert code == 0
    ids = {u["id"] for u in data["units"]}
    for d in (REPO / "03_IMPLEMENTATION" / "packages").iterdir():
        if d.is_dir() and not d.name.startswith((".", "__")):
            assert f"packages/{d.name}" in ids
    assert "root/cognitive_core" in ids
    assert data["meta"]["testpaths"] == ["20_TESTS"]
    assert not re.search(r"[A-Za-z]:[\/]", md)  # no absolute Windows paths
