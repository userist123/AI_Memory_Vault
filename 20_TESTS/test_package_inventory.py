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


# ----------------------------------------------------------------------------- WP-04 additions
PK = "03_IMPLEMENTATION/packages"
INI = "[pytest]\npythonpath = {pp}\ntestpaths = 20_TESTS\n"


def _mk(tmp_path: Path, files: dict[str, str], pythonpath: str = ". 03_IMPLEMENTATION/packages 03_IMPLEMENTATION"):
    root = tmp_path / "repo"
    _write(root, "pytest.ini", INI.format(pp=pythonpath))
    for rel, text in files.items():
        _write(root, rel, text)
    return root


def test_shim_from_string_constants(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/beta/__init__.py": "", f"{PK}/gamma/__init__.py": "",
        f"{PK}/facade/__init__.py": ('from pathlib import Path\n_B = Path(__file__).resolve().parents[1]\n'
                                     '__path__ = [str(_B / "alpha"), str(_B / "beta")]\n__all__ = ["alpha"]\n'),
    })
    _, data, _ = _run(root, tmp_path)
    f = _unit(data, "packages/facade")
    assert f["shim_target_source"] == "string_constants"
    assert f["shim_targets"] == ["packages/alpha", "packages/beta"]  # gamma is not named, so not a target
    assert _unit(data, "packages/alpha")["shim_target_source"] is None


def test_shim_from_path_expression_serves_all_siblings(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/beta/__init__.py": "", f"{PK}/gamma/__init__.py": "",
        f"{PK}/facade/__init__.py": ('from pathlib import Path\n_B = Path(__file__).resolve().parents[1]\n'
                                     '__path__ = [str(_B)]\n__all__ = ["alpha"]\n'),
        f"{PK}/direct/__init__.py": "from pathlib import Path\n__path__ = [str(Path(__file__).parent.parent)]\n",
        f"{PK}/user/a.py": "import facade.gamma\n",
    })
    _, data, _ = _run(root, tmp_path)
    expect = ["packages/alpha", "packages/beta", "packages/direct", "packages/gamma", "packages/user"]
    f = _unit(data, "packages/facade")
    assert f["shim_target_source"] == "path_expression"
    assert f["shim_targets"] == expect
    d = _unit(data, "packages/direct")
    assert d["shim_target_source"] == "path_expression"
    assert "packages/facade" in d["shim_targets"] and "packages/direct" not in d["shim_targets"]
    # `facade.gamma` is credited to the sibling package it serves
    assert set(_unit(data, "packages/user")["imports_out"]) == {"packages/facade", "packages/gamma"}


def test_collision_winner_follows_pythonpath_order(tmp_path):
    files = {f"{PK}/dup/__init__.py": "", f"{PK}/dup/a.py": "", "dup/__init__.py": "", "dup/b.py": ""}
    _, data, _ = _run(_mk(tmp_path / "one", files), tmp_path / "one")
    c = next(x for x in data["collisions"] if x["name"] == "dup")
    assert c["candidates_in_pythonpath_order"] == ["root/dup", "packages/dup"] and c["winner"] == "root/dup"
    assert _unit(data, "root/dup")["import_name_winner"] is True
    assert _unit(data, "packages/dup")["import_name_winner"] is False
    _, data, _ = _run(_mk(tmp_path / "two", files, "03_IMPLEMENTATION/packages ."),
                      tmp_path / "two")
    c = next(x for x in data["collisions"] if x["name"] == "dup")
    assert c["winner"] == "packages/dup"


def test_package_to_product_violation(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/lib/a.py": "import products.prod\n", "03_IMPLEMENTATION/products/prod/__init__.py": "",
        "03_IMPLEMENTATION/products/prod/m.py": "import lib\n",
    })
    _, data, _ = _run(root, tmp_path)
    v = data["layer_violations_package_to_product"]
    assert [(x["from_unit"], x["to_unit"], x["file"], x["test_file"]) for x in v] == [
        ("packages/lib", "products/prod", f"{PK}/lib/a.py", False)]  # product -> package is not a violation


def test_string_import_refs_each_kind_and_negative(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/alpha/cli.py": "", f"{PK}/beta/__init__.py": "",
        ".github/workflows/ci.yml": "steps:\n  - run: python -m alpha.cli --x\n  - run: python -m pip install foo\n",
        "tools/check.sh": 'python -c "import alpha; from beta import thing"\n',
        "pyproject.toml": ('[project.scripts]\nfoo = "alpha.cli:main"\n'
                           '[tool.setuptools.packages.find]\ninclude = ["beta*"]\n'),
        "scripts/run.py": 'import subprocess, sys\nsubprocess.run([sys.executable, "-m", "alpha.cli", "x"])\n',
        "docs/note.md": "import this\npython -m alpha.cli\npython -c \"import alpha\"\n",
        "scripts/plain.py": "x = ['-m', 'not a module']\n",
    })
    _, data, md = _run(root, tmp_path)
    refs = {(r["file"], r["kind"], r["module"]): r for r in data["string_import_refs"]}
    assert refs[(".github/workflows/ci.yml", "python_m", "alpha.cli")]["unit"] == "packages/alpha"
    assert refs[(".github/workflows/ci.yml", "python_m", "pip")]["unit"] is None
    assert refs[("tools/check.sh", "python_c", "alpha")]["unit"] == "packages/alpha"
    assert refs[("tools/check.sh", "python_c", "beta")]["unit"] == "packages/beta"
    assert refs[("pyproject.toml", "toml_entry", "alpha.cli")]["unit"] == "packages/alpha"
    assert refs[("pyproject.toml", "toml_entry", "beta")]["unit"] == "packages/beta"
    r = refs[("scripts/run.py", "py_argv_m", "alpha.cli")]
    assert r["unit"] == "packages/alpha" and r["line"] == 2
    assert not any(k[0] in ("docs/note.md", "scripts/plain.py") for k in refs)  # prose / non-module are ignored
    assert "## Importuri din șiruri" in md


def test_real_repo_string_imports_find_known_examples_and_memory_vault_shim(tmp_path):
    code, data, md = _run(REPO, tmp_path)
    assert code == 0
    refs = data["string_import_refs"]
    assert any(r["file"] == ".github/workflows/r001-enforcement.yml" and r["kind"] == "python_c"
               and r["module"] == "memory_vault.memory" for r in refs)
    assert any(r["file"] == ".claude/skills/cost-router/hook_agent_model.py" and r["kind"] == "py_argv_m"
               and r["module"] == "routing.route_cli" and r["unit"] == "packages/routing" for r in refs)
    mv = _unit(data, "packages/memory_vault")
    assert mv["shim_target_source"] == "path_expression"
    assert {"packages/routing", "packages/polymarket", "packages/agent_bridge", "packages/vault_access"} <= set(mv["shim_targets"])
