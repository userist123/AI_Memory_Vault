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
    assert f["shim_target_source"] == "path_eval"
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
    # (WP-07: the edge goes only to the unit that serves it; the facade keeps the consumer record)
    assert set(_unit(data, "packages/user")["imports_out"]) == {"packages/gamma"}
    assert _unit(data, "packages/facade")["consumers_prod"] == [f"{PK}/user/a.py"]


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
    assert {"packages/routing", "packages/agent_bridge", "packages/vault_access"} <= set(mv["shim_targets"])
    # WP-12: polymarket is a product now, so the packages facade no longer serves it.
    assert not any(t.endswith("/polymarket") for t in mv["shim_targets"])


def test_string_import_f4_from_import_comments_and_interpreter_forms(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/x/__init__.py": "", f"{PK}/x/memory.py": "", f"{PK}/alpha/__init__.py": "",
        "a/from.sh": 'python -c "from x import memory"\n',
        "a/comments.yml": "# python -m alpha.zz\nsteps:\n  # python -c \"import alpha\"\n",
        "a/comments.sh": "  # python -m alpha.zz\n",
        "a/c.bat": "REM python -m alpha.rem\n:: python -m alpha.colon\nrem\npython -m alpha.real\n",
        "a/py.ps1": "py -3 -m alpha.pylauncher\npy -3.11 -m alpha.pylauncher2\n",
        "a/opts.sh": "python -X utf8 -W ignore -u -m alpha.opts\n",
        "a/env.sh": '$PYTHON -m alpha.d1\n"${PYTHON}" -m alpha.d2\n',
        "a/env.cmd": "%PYTHON% -m alpha.d3\n",
        "a/notmod.sh": "python script.py -m alpha.nope\ncopy -m alpha.nope2\n",
    })
    _, data, _ = _run(root, tmp_path)
    got = sorted((r["file"], r["kind"], r["module"]) for r in data["string_import_refs"])
    assert ("a/from.sh", "python_c", "x") in got
    assert not any(k[2] in ("memory",) for k in got)  # the name after `from x import` is not a module
    assert not any(k[0] in ("a/comments.yml", "a/comments.sh") for k in got)
    assert [k[2] for k in got if k[0] == "a/c.bat"] == ["alpha.real"]
    assert [k[2] for k in got if k[0] == "a/py.ps1"] == ["alpha.pylauncher", "alpha.pylauncher2"]
    assert [k[2] for k in got if k[0] == "a/opts.sh"] == ["alpha.opts"]
    assert [k[2] for k in got if k[0] == "a/env.sh"] == ["alpha.d1", "alpha.d2"]
    assert [k[2] for k in got if k[0] == "a/env.cmd"] == ["alpha.d3"]
    assert not any(k[0] == "a/notmod.sh" for k in got)


def test_aliased_dynamic_import_is_reported_as_unknown(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/d.py": ("from importlib import import_module as im\nimport importlib as il\n"
                             "im(a)\nil.import_module(b)\nil.reload(c)\n")})
    _, data, _ = _run(root, tmp_path)
    assert [(u["kind"], u["line"]) for u in _unit(data, "packages/alpha")["unknown"]] == [
        ("dynamic_import", 3), ("dynamic_import", 4)]
    assert _unit(data, "packages/alpha")["dynamic_imports_nontest"] == 2


def test_architecture_config_is_not_counted_as_ci_reference(tmp_path):
    root = _mk(tmp_path, {f"{PK}/alpha/__init__.py": "",
                          "04_CONFIG/architecture_layers.json": '{"u": "packages/alpha"}',
                          "04_CONFIG/other.json": '{"u": "packages/alpha"}'})
    _, data, _ = _run(root, tmp_path)
    assert _unit(data, "packages/alpha")["ci_config_refs"] == {"04_CONFIG/other.json": 1}


def test_test_path_file_imported_by_production_is_promoted(tmp_path):
    root = _mk(tmp_path, {f"{PK}/alpha/tests/__init__.py": "", f"{PK}/alpha/tests/helpers.py": "",
                          f"{PK}/beta/use.py": "from alpha.tests import helpers\n"})
    _, data, _ = _run(root, tmp_path)
    assert data["meta"]["test_paths_promoted_to_production"] == [
        f"{PK}/alpha/tests/__init__.py", f"{PK}/alpha/tests/helpers.py"]


# ----------------------------------------------------------------------------- WP-07 PR A: facade resolution
FAC = ('from pathlib import Path\n_B = Path(__file__).resolve().parents[1]\n'
       '__path__ = [{elems}]\n')


def _fac(*names: str) -> str:
    return FAC.format(elems=", ".join(f'str(_B / "{n}")' for n in names))


def _edges(data: dict, uid: str) -> dict:
    return _unit(data, uid)["imports_out_nontest"]


def test_facade_path_order_wins_over_alphabetical_order(tmp_path):
    """Python takes the first ``__path__`` entry that has the submodule; the targets keep that order."""
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/alpha/shared.py": "", f"{PK}/zeta/__init__.py": "",
        f"{PK}/zeta/shared.py": "", f"{PK}/fac/__init__.py": _fac("zeta", "alpha"),
        f"{PK}/user/a.py": "import fac.shared\n"})
    _, data, _ = _run(root, tmp_path)
    assert _unit(data, "packages/fac")["shim_targets"] == ["packages/zeta", "packages/alpha"]
    assert _edges(data, "packages/user") == {"packages/zeta": 1}


def test_facade_import_credits_only_the_serving_unit_but_consumers_stay_reported(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/alpha/core.py": "", f"{PK}/fac/__init__.py": _fac("alpha"),
        f"{PK}/user/a.py": "from fac.core import X\n", f"{PK}/user/b.py": "from fac import core\n",
        "20_TESTS/test_fac.py": "import fac.core\n"})
    _, data, _ = _run(root, tmp_path)
    for key in ("imports_out", "imports_out_nontest"):
        assert _unit(data, "packages/user")[key] == {"packages/alpha": 2}  # no edge to the facade
    fac = _unit(data, "packages/fac")
    assert fac["consumers_prod"] == [f"{PK}/user/a.py", f"{PK}/user/b.py"]  # who uses the facade is still reported
    assert fac["consumers_test"] == ["20_TESTS/test_fac.py"]
    assert _unit(data, "packages/alpha")["consumers_prod"] == [f"{PK}/user/a.py", f"{PK}/user/b.py"]
    assert data["meta"]["facade_unresolved"] == []


def test_unresolved_facade_submodule_keeps_the_facade_edge_and_is_reported(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/alpha/core.py": "", f"{PK}/beta/__init__.py": "",
        f"{PK}/fac/__init__.py": _fac("alpha", "beta"),
        f"{PK}/user/a.py": "import fac.nothing\n", f"{PK}/user/b.py": "from fac.core import X\n"})
    _, data, _ = _run(root, tmp_path)
    assert _edges(data, "packages/user") == {"packages/alpha": 1, "packages/fac": 1}
    assert [(r["file"], r["module"], r["facade"]) for r in data["meta"]["facade_unresolved"]] == [
        (f"{PK}/user/a.py", "fac.nothing", "packages/fac")]


def test_facade_from_import_of_a_non_module_name_is_reported_not_hidden(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/alpha/core.py": "", f"{PK}/fac/__init__.py": _fac("alpha"),
        f"{PK}/user/a.py": "from fac import Thing\n", f"{PK}/user/b.py": "from fac import core, Thing\n"})
    _, data, _ = _run(root, tmp_path)
    assert _edges(data, "packages/user") == {"packages/alpha": 1, "packages/fac": 1}  # a.py stays on the facade
    assert [r["file"] for r in data["meta"]["facade_unresolved"]] == [f"{PK}/user/a.py"]


ROOT_CC = ('from pathlib import Path\n_P = Path(__file__).resolve().parent.parent / "03_IMPLEMENTATION" / "packages"\n'
           '__path__ = [str(Path(__file__).resolve().parent)] + [\n'
           '    str(_P / name) for name in ("lrn", "interfaces")\n]\n')


def test_root_shim_reexport_is_followed_to_the_sibling_package(tmp_path):
    root = _mk(tmp_path, {
        "cognitive_core/__init__.py": ROOT_CC,
        "cognitive_core/closed.py": "from packages.lrn.closed import *\n",
        "cognitive_core/cli.py": "import runpy\nfrom interfaces.cli import *  # noqa\n",
        "cognitive_core/orphan.py": "X = 1\n",
        f"{PK}/lrn/__init__.py": "", f"{PK}/lrn/closed.py": "", f"{PK}/interfaces/__init__.py": "",
        f"{PK}/interfaces/cli.py": "",
        f"{PK}/app/a.py": "from cognitive_core.closed import f\nimport cognitive_core.cli\nimport cognitive_core.orphan\n"})
    _, data, _ = _run(root, tmp_path)
    cc = _unit(data, "root/cognitive_core")
    assert cc["shim_targets"] == ["self", "packages/lrn", "packages/interfaces"]
    assert _edges(data, "packages/app") == {"packages/interfaces": 1, "packages/lrn": 1, "root/cognitive_core": 1}
    assert cc["consumers_prod"] == [f"{PK}/app/a.py"]
    # the module that re-exports nothing stays on the shim and is reported
    assert [r["module"] for r in data["meta"]["facade_unresolved"]] == ["cognitive_core.orphan"]


def test_memory_vault_style_facade_resolves_package_and_module_to_the_package(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/alpha/core.py": "", f"{PK}/beta/__init__.py": "",
        f"{PK}/mv/__init__.py": 'from pathlib import Path\n__path__ = [str(Path(__file__).resolve().parents[1])]\n',
        f"{PK}/user/a.py": "import mv.alpha.core\n", f"{PK}/user/b.py": "from mv import beta\n"})
    _, data, _ = _run(root, tmp_path)
    assert _edges(data, "packages/user") == {"packages/alpha": 1, "packages/beta": 1}
    assert _unit(data, "packages/mv")["consumers_prod"] == [f"{PK}/user/a.py", f"{PK}/user/b.py"]


def test_single_target_product_facade_credits_the_product(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/xk/__init__.py": ('from pathlib import Path\n_E = Path(__file__).resolve().parents[2] / "products" / "prod"\n'
                                 '__path__ = [str(_E)]\n'),
        "03_IMPLEMENTATION/products/prod/__init__.py": "", "03_IMPLEMENTATION/products/prod/m.py": "",
        f"{PK}/user/a.py": "import xk.m\n"})
    _, data, _ = _run(root, tmp_path)
    assert _unit(data, "packages/xk")["shim_targets"] == ["products/prod"]
    assert _edges(data, "packages/user") == {"products/prod": 1}
    assert [(v["from_unit"], v["to_unit"]) for v in data["layer_violations_package_to_product"]] == [
        ("packages/user", "products/prod")]


def test_package_that_extends_its_own_path_records_targets_without_changing_resolution(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/sec/__init__.py": ('from pathlib import Path\n_R = Path(__file__).resolve().parents[3] / "sec"\n'
                                  'if _R.is_dir():\n    __path__.append(str(_R))\n'),
        "sec/__init__.py": "", "sec/x.py": "", f"{PK}/user/a.py": "import sec.x\n"})
    _, data, _ = _run(root, tmp_path)
    assert _unit(data, "packages/sec")["shim_targets"] == ["self", "root/sec"]
    assert _edges(data, "packages/user") == {"root/sec": 1}  # root/sec wins on the path, as before
    assert data["meta"]["facade_unresolved"] == []


_PH = 'from pathlib import Path\n_B = Path(__file__).resolve().parents[1]\n'


def test_shim_sources_are_path_eval_versus_legacy_fallback(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/beta/__init__.py": "",
        f"{PK}/ev/__init__.py": _PH + '__path__ = [str(_B / "beta"), str(_B / "alpha")]\n',
        f"{PK}/lg/__init__.py": _PH + 'def p(x):\n    return x\n__path__ = [str(_B / p("beta")), str(_B / "alpha")]\n'})
    _, data, _ = _run(root, tmp_path)
    assert _unit(data, "packages/ev")["shim_target_source"] == "path_eval"
    assert _unit(data, "packages/ev")["shim_targets"] == ["packages/beta", "packages/alpha"]
    assert _unit(data, "packages/lg")["shim_target_source"] == "legacy_fallback"


def test_unrecognised_path_mutations_make_the_list_uninterpretable(tmp_path):
    for i, mut in enumerate(['__path__.insert(0, str(_B / "beta"))\n', '__path__ += [str(_B / "beta")]\n',
                             '__path__.extend(str(_B / n) for n in ["beta"])\n',
                             'def f():\n    __path__.extend([str(_B / "beta")])\nf()\n',
                             'def f():\n    __path__.append(str(_B / "beta"))\nf()\n']):
        root = _mk(tmp_path / f"m{i}", {
            f"{PK}/alpha/__init__.py": "", f"{PK}/beta/__init__.py": "",
            f"{PK}/fac/__init__.py": _PH + '__path__ = [str(_B / "alpha")]\n' + mut})
        _, data, _ = _run(root, tmp_path / f"m{i}")
        assert _unit(data, "packages/fac")["shim_target_source"] == "legacy_fallback", mut


def test_undeclared_append_only_shim_resolves_through_its_path_unless_also_unit(tmp_path):
    files = {f"{PK}/memory/__init__.py": "", f"{PK}/memory/controller.py": "",
             f"{PK}/routing/__init__.py": _PH + '__path__.append(str(_B / "memory"))\n',
             f"{PK}/graph/__init__.py": "", f"{PK}/graph/zz.py": "import routing.controller\n"}
    root = _mk(tmp_path / "plain", files)
    _, data, _ = _run(root, tmp_path / "plain")
    assert _edges(data, "packages/graph") == {"packages/memory": 1}
    root = _mk(tmp_path / "also", files)
    _write(root, "04_CONFIG/architecture_layers.json",
           json.dumps({"aliases": {"packages/routing": {"__path__": [], "also_unit": True}}}))
    _, data, _ = _run(root, tmp_path / "also")
    assert _edges(data, "packages/graph") == {"packages/routing": 1}


def test_facade_unresolved_records_carry_the_test_flag(tmp_path):
    root = _mk(tmp_path, {
        f"{PK}/alpha/__init__.py": "", f"{PK}/fac/__init__.py": _fac("alpha"),
        f"{PK}/user/a.py": "import fac.nothing\n", f"{PK}/user/test_b.py": "import fac.nothing\n"})
    _, data, _ = _run(root, tmp_path)
    assert {(r["file"], r["test"]) for r in data["meta"]["facade_unresolved"]} == {
        (f"{PK}/user/a.py", False), (f"{PK}/user/test_b.py", True)}


def test_build_metadata_directories_are_not_units(tmp_path):
    """WP-18 review L1: `pip wheel .` leaves `<dist>.egg-info/` next to the packages; it must not
    become an (unassigned) unit, or a local build would fail the layer check."""
    root = tmp_path / "repo"
    _write(root, "pytest.ini", "[pytest]\npythonpath = . 03_IMPLEMENTATION/packages\n")
    _write(root, "03_IMPLEMENTATION/packages/alpha/__init__.py", "")
    _write(root, "03_IMPLEMENTATION/packages/ai_memory_vault.egg-info/top_level.txt", "alpha\n")
    _write(root, "03_IMPLEMENTATION/packages/alpha-0.1.dist-info/METADATA", "Name: alpha\n")
    rc, data, _ = _run(root, tmp_path)
    assert rc == 0
    ids = {u["id"] for u in data["units"]}
    assert "packages/alpha" in ids
    assert not any(i.endswith((".egg-info", ".dist-info")) for i in ids), ids
