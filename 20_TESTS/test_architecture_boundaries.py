"""WP-04: the architecture layer ratchet on the real repository and on synthetic trees."""
from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "30_SCRIPTS" / "verification"))
import architecture_boundaries as ab  # noqa: E402

PK = "03_IMPLEMENTATION/packages"
PD = "03_IMPLEMENTATION/products"
INI = "[pytest]\npythonpath = . 03_IMPLEMENTATION/packages 03_IMPLEMENTATION\ntestpaths = 20_TESTS\n"


def _w(root: Path, rel: str, text: str = "") -> None:
    p = root / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")


def _tree(tmp: Path, files: dict[str, str]) -> Path:
    root = tmp / "repo"
    _w(root, "pytest.ini", INI)
    for rel, text in files.items():
        _w(root, rel, text)
    return root


def _cfg(layers: list[list[str]], baseline=None, collisions=(), hard=("packages", "products")) -> dict:
    return {
        "schema_version": 1,
        "layers": [{"name": f"L{i}", "units": u} for i, u in enumerate(layers)],
        "hard_forbidden": {"from_kind": hard[0], "to_kind": hard[1], "alias_units": []},
        "known_collisions": list(collisions),
        "baseline_edges": baseline or [],
    }


def _edge(a: str, b: str, n: int) -> dict:
    return {"from": a, "to": b, "imports": n, "since": "2026-11-09", "owner_wp": "WP-10", "reason": "x"}


def _base_files() -> dict[str, str]:
    return {
        f"{PK}/low/__init__.py": "",
        f"{PK}/low/a.py": "X = 1\n",
        f"{PK}/mid/__init__.py": "",
        f"{PK}/mid/a.py": "import low\n",
        f"{PK}/mid2/__init__.py": "",
        f"{PK}/mid2/a.py": "import mid\nimport low\n",
    }


LAYERS = [["packages/low"], ["packages/mid", "packages/mid2"]]


def _fails(root: Path, cfg: dict) -> list[str]:
    return ab.check(root, cfg)


def test_real_repo_passes_with_committed_config():
    config = json.loads((REPO / "04_CONFIG" / "architecture_layers.json").read_text("utf-8"))
    assert config["schema_version"] == 1 and config["baseline_commit"] == "0752ac2b7"
    assert ab.check(REPO, config) == []
    assert all(e["owner_wp"] in {"WP-06", "WP-10", "WP-12", "WP-14"} for e in config["baseline_edges"])
    # WP-12 removed the only hard_forbidden entry (interfaces -> xau_kinetic); none may come back.
    assert not any(e.get("hard_forbidden") for e in config["baseline_edges"])


def test_downward_and_same_layer_imports_are_allowed(tmp_path):
    root = _tree(tmp_path, _base_files())
    assert _fails(root, _cfg(LAYERS)) == []
    assert ab.current_edges(root) == {("packages/mid", "packages/low"): 1,
                                      ("packages/mid2", "packages/mid"): 1,
                                      ("packages/mid2", "packages/low"): 1}


def test_a_unassigned_and_missing_units(tmp_path):
    root = _tree(tmp_path, _base_files())
    f = _fails(root, _cfg([["packages/low"], ["packages/mid", "packages/ghost"]]))
    assert any(m.startswith("(a)") and "packages/mid2" in m and "not assigned" in m for m in f)
    assert any(m.startswith("(a)") and "packages/ghost" in m and "does not exist" in m for m in f)


def test_b_new_upward_edge(tmp_path):
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n"}
    f = _fails(_tree(tmp_path, files), _cfg(LAYERS))
    assert len(f) == 1 and f[0].startswith("(b)") and "packages/low (L0) -> packages/mid (L1)" in f[0]


def test_baselined_upward_edge_passes_and_test_files_are_ignored(tmp_path):
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n",
                             "20_TESTS/test_up.py": "import low\nimport mid\n",
                             f"{PK}/low/tests/test_x.py": "import mid\n"}
    root = _tree(tmp_path, files)
    assert _fails(root, _cfg(LAYERS, [_edge("packages/low", "packages/mid", 1)])) == []


def test_c_count_above_baseline(tmp_path):
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n", f"{PK}/low/c.py": "import mid\n"}
    f = _fails(_tree(tmp_path, files), _cfg(LAYERS, [_edge("packages/low", "packages/mid", 1)]))
    assert len(f) == 1 and f[0].startswith("(c)") and "1 to 2" in f[0]


def test_d_stale_baseline_edge_gone_or_lower(tmp_path):
    root = _tree(tmp_path, _base_files())
    f = _fails(root, _cfg(LAYERS, [_edge("packages/low", "packages/mid", 1)]))
    assert len(f) == 1 and f[0].startswith("(d)") and "no longer exists" in f[0] and "--write-baseline" in f[0]
    assert f[0].startswith("(d)")
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n"}
    f = _fails(_tree(tmp_path / "two", files), _cfg(LAYERS, [_edge("packages/low", "packages/mid", 3)]))
    assert len(f) == 1 and f[0].startswith("(d)") and "tighten the baseline" in f[0]


def test_e_collisions_must_match_known(tmp_path):
    files = _base_files() | {f"{PK}/dup/__init__.py": "", "dup/__init__.py": "", "dup/a.py": "X = 1\n"}
    root = _tree(tmp_path, files)
    layers = [["packages/low", "packages/dup", "root/dup"], ["packages/mid", "packages/mid2"]]
    f = _fails(root, _cfg(layers))
    assert any(m.startswith("(e)") and "unexpected=['dup']" in m for m in f)
    assert _fails(root, _cfg(layers, collisions=["dup"])) == []
    f = _fails(root, _cfg(layers, collisions=["dup", "gone"]))
    assert any(m.startswith("(e)") and "missing=['gone']" in m for m in f)


def test_f_hard_forbidden_edge_needs_baseline_even_within_a_layer(tmp_path):
    files = {f"{PK}/lib/__init__.py": "", f"{PK}/lib/a.py": "import products.prod\n",
             f"{PD}/prod/__init__.py": "", f"{PD}/prod/a.py": "X = 1\n"}
    root = _tree(tmp_path, files)
    layers = [["packages/lib", "products/prod"]]
    f = _fails(root, _cfg(layers))
    assert len(f) == 1 and f[0].startswith("(f)") and "packages/lib -> products/prod" in f[0]
    assert _fails(root, _cfg(layers, [_edge("packages/lib", "products/prod", 1)])) == []


def test_owner_wp_rules():
    assert ab.owner_wp("packages/a", "root/cognitive_core", False) == "WP-06"
    assert ab.owner_wp("packages/a", "products/polymarket", False) == "WP-12"
    assert ab.owner_wp("packages/a", "root/security", False) == "WP-14"
    assert ab.owner_wp("packages/a", "packages/b", False) == "WP-10"
    assert ab.owner_wp("packages/a", "products/b", True) == "WP-12"


def test_plan_baseline_only_tightens_and_keeps_metadata(tmp_path):
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n"}
    root = _tree(tmp_path, files)
    prev = _edge("packages/low", "packages/mid", 9)
    prev.update({"since": "2020-01-01", "owner_wp": "WP-99", "reason": "kept"})
    cfg = _cfg(LAYERS, [prev, _edge("packages/gone", "packages/mid", 1)])
    plan = ab.plan_baseline(cfg, ab._inventory(root))
    assert plan["edges"] == [{"from": "packages/low", "to": "packages/mid", "imports": 1, "since": "2020-01-01",
                              "owner_wp": "WP-99", "reason": "kept"}]
    assert len(plan["tightened"]) == 2 and plan["widened"] == [] and plan["refused"] == []


def test_plan_baseline_refuses_to_widen_without_flag(tmp_path):
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n", f"{PK}/low/c.py": "import mid\n",
                             f"{PK}/low/d.py": "import mid2\n"}
    root = _tree(tmp_path, files)
    cfg = _cfg(LAYERS, [_edge("packages/low", "packages/mid", 1)])
    plan = ab.plan_baseline(cfg, ab._inventory(root))
    assert len(plan["refused"]) == 2 and plan["widened"] == []
    assert plan["edges"] == [_edge("packages/low", "packages/mid", 1)]  # nothing silently added or raised
    wide = ab.plan_baseline(cfg, ab._inventory(root), True, "WP-77", "because", today="2030-01-02")
    assert len(wide["widened"]) == 2 and wide["refused"] == []
    new = {(e["from"], e["to"]): e for e in wide["edges"]}
    e = new[("packages/low", "packages/mid2")]
    assert (e["imports"], e["since"], e["owner_wp"], e["reason"]) == (1, "2030-01-02", "WP-77", "because")
    assert new[("packages/low", "packages/mid")]["imports"] == 2


def test_cli_write_baseline_is_tighten_only(tmp_path, capsys):
    root = _tree(tmp_path, _base_files() | {f"{PK}/low/b.py": "import mid\n"})
    cfg = root / "cfg.json"
    cfg.write_text(json.dumps(_cfg(LAYERS)), "utf-8")
    assert ab.main(["--root", str(root), "--config", str(cfg)]) == 1
    assert ab.main(["--root", str(root), "--config", str(cfg), "--write-baseline"]) == 1
    assert json.loads(cfg.read_text("utf-8"))["baseline_edges"] == []
    assert "REFUSED" in capsys.readouterr().out
    assert ab.main(["--root", str(root), "--config", str(cfg), "--write-baseline", "--allow-widen"]) == 2
    args = ["--root", str(root), "--config", str(cfg), "--write-baseline", "--allow-widen",
            "--owner-wp", "WP-10", "--reason", "test"]
    assert ab.main(args) == 0
    saved = json.loads(cfg.read_text("utf-8"))["baseline_edges"]
    assert len(saved) == 1 and saved[0]["owner_wp"] == "WP-10" and saved[0]["reason"] == "test"
    assert ab.main(["--root", str(root), "--config", str(cfg)]) == 0
    (root / PK / "low" / "b.py").unlink()  # the edge disappears: stale, then tightened away
    assert ab.main(["--root", str(root), "--config", str(cfg)]) == 1
    assert "tighten-only" in capsys.readouterr().out
    assert ab.main(["--root", str(root), "--config", str(cfg), "--write-baseline"]) == 0
    assert json.loads(cfg.read_text("utf-8"))["baseline_edges"] == []


def test_g_dynamic_imports_are_ratcheted(tmp_path):
    files = _base_files() | {f"{PK}/low/dyn.py": "import importlib\n\ndef f(n):\n    return importlib.import_module(n)\n",
                             "20_TESTS/test_dyn.py": "import importlib\nimportlib.import_module(input())\n"}
    root = _tree(tmp_path, files)
    f = _fails(root, _cfg(LAYERS))
    assert len(f) == 1 and f[0].startswith("(g)") and "packages/low" in f[0] and "new" in f[0]
    cfg = _cfg(LAYERS) | {"baseline_dynamic_imports": {"packages/low": 1}}
    assert _fails(root, cfg) == []
    f = _fails(root, _cfg(LAYERS) | {"baseline_dynamic_imports": {"packages/low": 3}})
    assert len(f) == 1 and "STALE" in f[0]
    (root / PK / "low" / "dyn.py").write_text("X = 1\n", "utf-8")
    f = _fails(root, cfg)
    assert len(f) == 1 and "STALE" in f[0]


def test_aliased_dynamic_imports_are_counted(tmp_path):
    files = _base_files() | {
        f"{PK}/low/d1.py": "from importlib import import_module as im\nim(name)\n",
        f"{PK}/low/d2.py": "import importlib as il\nil.import_module(name)\n",
        f"{PK}/low/d3.py": "__import__(name)\n",
        f"{PK}/low/ok.py": "import importlib as il\nil.import_module('mid')\nil.reload(x)\n"}
    inv = ab._inventory(_tree(tmp_path, files))
    assert ab.dynamic_counts(inv) == {"packages/low": 3}
    # the literal import_module('mid') is a real edge low -> mid
    assert ab.current_edges(Path("."), inv)[("packages/low", "packages/mid")] == 1


def test_string_argv_imports_are_edges(tmp_path):
    files = _base_files() | {f"{PK}/low/run.py": 'import subprocess, sys\nsubprocess.run([sys.executable, "-m", "mid.a"])\n',
                             "20_TESTS/test_run.py": 'x = ["-m", "mid.a"]\n'}
    root = _tree(tmp_path, files)
    f = _fails(root, _cfg(LAYERS))
    assert len(f) == 1 and f[0].startswith("(b)") and "packages/low (L0) -> packages/mid (L1)" in f[0]


def test_test_path_file_imported_by_production_counts_as_production(tmp_path):
    files = _base_files() | {
        f"{PK}/low/tests/__init__.py": "", f"{PK}/low/tests/helpers.py": "import mid\n",
        f"{PK}/mid/use.py": "from low.tests import helpers\n"}
    root = _tree(tmp_path, files)
    f = _fails(root, _cfg(LAYERS))
    assert len(f) == 1 and f[0].startswith("(b)") and "packages/low" in f[0] and "packages/mid " in f[0]
    # the same file with no production importer stays excluded
    files.pop(f"{PK}/mid/use.py")
    assert _fails(_tree(tmp_path / "two", files), _cfg(LAYERS)) == []


# ----------------------------------------------------------------------------- WP-07 PR A: aliases (facades)
def _fac_files() -> dict[str, str]:
    return {
        f"{PK}/low/__init__.py": "", f"{PK}/low/a.py": "X = 1\n",
        f"{PK}/high/__init__.py": "", f"{PK}/high/controller.py": "Y = 1\n",
        f"{PK}/fac/__init__.py": ('from pathlib import Path\n_B = Path(__file__).resolve().parents[1]\n'
                                  '__path__ = [str(_B / "low"), str(_B / "high")]\n'),
    }


FAC_LAYERS = [["packages/low"], ["packages/high"]]
FAC_ALIASES = {"packages/fac": {"__path__": ["packages/low", "packages/high"]}}


def _acfg(layers=FAC_LAYERS, aliases=FAC_ALIASES, baseline=None) -> dict:
    cfg = _cfg(layers, baseline)
    cfg["aliases"] = aliases
    return cfg


def test_alias_is_a_valid_unit_and_a_downward_import_through_it_passes(tmp_path):
    files = _fac_files() | {f"{PK}/high/h.py": "import fac.a\n"}  # fac.a is packages/low: downward
    root = _tree(tmp_path, files)
    assert _fails(root, _acfg()) == []
    assert ab.current_edges(root) == {("packages/high", "packages/low"): 1}  # no edge to the facade


def test_f_new_upward_edge_introduced_through_a_facade_fails(tmp_path):
    files = _fac_files() | {f"{PK}/low/up.py": "import fac.controller\n"}  # L0 -> high, spelled via the facade
    root = _tree(tmp_path, files)
    f = _fails(root, _acfg())
    assert len(f) == 1 and f[0].startswith("(b)") and "packages/low (L0) -> packages/high (L1)" in f[0]
    assert _fails(root, _acfg(baseline=[_edge("packages/low", "packages/high", 1)])) == []


def test_g_alias_path_drift_fails(tmp_path):
    root = _tree(tmp_path, _fac_files())
    assert _fails(root, _acfg()) == []
    for declared in (["packages/high", "packages/low"], ["packages/low"], ["packages/low", "packages/high", "self"]):
        f = _fails(root, _acfg(aliases={"packages/fac": {"__path__": declared}}))
        assert len(f) == 1 and f[0].startswith("(h)") and "packages/fac" in f[0], declared


def test_h_unit_in_neither_a_layer_nor_an_alias_fails(tmp_path):
    root = _tree(tmp_path, _fac_files())
    f = _fails(root, _acfg(aliases={}))
    assert any(m.startswith("(a)") and "packages/fac" in m and "not assigned" in m for m in f)
    f = _fails(root, _acfg(aliases=FAC_ALIASES | {"packages/ghost": {"__path__": []}}))
    assert any(m.startswith("(a)") and "packages/ghost" in m and "does not exist" in m for m in f)


def test_i_hard_forbidden_through_an_alias_fails_even_when_the_edge_goes_down(tmp_path):
    files = {f"{PK}/lib/__init__.py": "", f"{PK}/lib/a.py": "import xk.m\n",
             f"{PK}/xk/__init__.py": ('from pathlib import Path\n_E = Path(__file__).resolve().parents[2] / "products" / "prod"\n'
                                      '__path__ = [str(_E)]\n'),
             f"{PD}/prod/__init__.py": "", f"{PD}/prod/m.py": "X = 1\n"}
    root = _tree(tmp_path, files)
    layers = [["products/prod"], ["packages/lib"]]  # lib above prod: not upward
    aliases = {"packages/xk": {"__path__": ["products/prod"]}}
    f = _fails(root, _acfg(layers, aliases))
    assert len(f) == 1 and f[0].startswith("(f)") and "packages/lib -> products/prod" in f[0]
    assert _fails(root, _acfg(layers, aliases, [_edge("packages/lib", "products/prod", 1)])) == []


def test_owner_wp_follows_the_contract_table():
    for a, b, wp in [("graph", "memory", "WP-10"), ("lifecycle", "memory", "WP-10"), ("memory", "interfaces", "WP-10"),
                     ("retrieval", "memory", "WP-10"), ("retrieval", "interfaces", "WP-10"),
                     ("lifecycle", "interfaces", "WP-14"), ("security", "interfaces", "WP-14"),
                     ("vault_access", "interfaces", "WP-14")]:
        assert ab.owner_wp(f"packages/{a}", f"packages/{b}", False) == wp, (a, b)
    assert ab.owner_wp("packages/interfaces", "products/xau_kinetic", True) == "WP-12"


def test_regenerated_baseline_takes_the_owner_from_the_table_and_refreshes_old_reasons(tmp_path):
    files = _fac_files() | {f"{PK}/low/up.py": "import fac.controller\n"}
    root = _tree(tmp_path, files)
    old = _edge("packages/low", "packages/high", 1) | {"owner_wp": "WP-06", "reason": "old"}
    plan = ab.plan_baseline(_acfg(baseline=[old]), ab._inventory(root), True, "WP-07", "reattributed")
    # (low, high) is not a contract pair: the old owner is kept, only the reason is refreshed
    assert [(e["owner_wp"], e["reason"]) for e in plan["edges"]] == [("WP-06", "reattributed")]
    files = {f"{PK}/graph/__init__.py": "", f"{PK}/graph/a.py": "import memory\n", f"{PK}/memory/__init__.py": ""}
    cfg = _acfg(layers=[["packages/graph"], ["packages/memory"]], aliases={})
    plan = ab.plan_baseline(cfg, ab._inventory(_tree(tmp_path / "t", files)), True, "WP-07", "why")
    assert [(e["owner_wp"], e["widened_by"]) for e in plan["edges"]] == [("WP-10", "WP-07")]


# ----------------------------------------------------------------------------- WP-07 PR A review round 1 (H1, H2, M1)
_HDR = 'from pathlib import Path\n_B = Path(__file__).resolve().parents[1]\n'


def test_h1_undeclared_unit_that_appends_to_its_path_fails(tmp_path):
    """routing (L0) appends memory (L4) to its __path__: `import routing.controller` is really an L0 -> L4 edge."""
    files = {
        f"{PK}/routing/__init__.py": _HDR + '__path__.append(str(_B / "memory"))\n',
        f"{PK}/memory/__init__.py": "", f"{PK}/memory/controller.py": "X = 1\n",
        f"{PK}/graph/__init__.py": "", f"{PK}/graph/zz_probe.py": "import routing.controller\n"}
    root = _tree(tmp_path, files)
    f = _fails(root, _cfg([["packages/routing", "packages/graph"], ["packages/memory"]]))
    assert any(m.startswith("(h)") and "packages/routing" in m and "not declared in aliases" in m for m in f), f
    assert any(m.startswith("(b)") and "packages/graph" in m and "packages/memory" in m for m in f), f


@pytest.mark.parametrize("mutation", [
    '__path__.insert(0, str(_B / "high"))\n',
    '__path__ += [str(_B / "high")]\n',
    'def _late():\n    __path__.extend([str(_B / "high")])\n_late()\n',
    'def _late():\n    __path__[:] = [str(_B / "high")]\n_late()\n',
])
def test_h2_unrecognised_path_mutation_on_a_declared_facade_fails(tmp_path, mutation):
    files = _fac_files()
    files[f"{PK}/fac/__init__.py"] += mutation
    root = _tree(tmp_path, files)
    f = _fails(root, _acfg())
    assert any(m.startswith("(h)") and "packages/fac" in m for m in f), (mutation, f)


def test_h2_insert_probe_with_a_graph_import_is_not_silent(tmp_path):
    files = {
        f"{PK}/agent_bridge/__init__.py": "", f"{PK}/agent_bridge/bridge.py": "X = 1\n",
        f"{PK}/memory_controller/__init__.py": _HDR + '__path__ = [str(_B / "memory")]\n'
                                              '__path__.insert(0, str(_B / "agent_bridge"))\n',
        f"{PK}/memory/__init__.py": "",
        f"{PK}/graph/__init__.py": "", f"{PK}/graph/zz_probe.py": "import memory_controller.bridge\n"}
    root = _tree(tmp_path, files)
    cfg = _cfg([["packages/graph"], ["packages/memory"], ["packages/agent_bridge"]])
    cfg["aliases"] = {"packages/memory_controller": {"__path__": ["packages/memory"]}}  # what HEAD computes, insert ignored
    assert any(m.startswith("(h)") for m in _fails(root, cfg))


def test_h2_non_test_unresolved_facade_submodule_fails_but_test_code_is_only_reported(tmp_path):
    files = _fac_files() | {f"{PK}/low/x.py": "import fac.nope_xyz\n"}
    root = _tree(tmp_path, files)
    f = _fails(root, _acfg())
    assert any(m.startswith("(i)") and "fac.nope_xyz" in m for m in f), f
    assert any(m.startswith("(j)") and "packages/fac" in m for m in f), f
    files.pop(f"{PK}/low/x.py")
    files["20_TESTS/test_x.py"] = "import fac.nope_xyz\n"
    assert _fails(_tree(tmp_path / "t", files), _acfg()) == []


def test_m1_alias_that_falls_back_to_the_legacy_heuristic_fails(tmp_path):
    files = {
        f"{PK}/alpha/__init__.py": "", f"{PK}/beta/__init__.py": "",
        f"{PK}/fac/__init__.py": _HDR + 'def pick(x):\n    return x\n'
                                 '__path__ = [str(_B / pick("alpha")), str(_B / "beta")]\n'}
    root = _tree(tmp_path, files)
    cfg = _cfg([["packages/alpha", "packages/beta"]])
    cfg["aliases"] = {"packages/fac": {"__path__": ["packages/alpha", "packages/beta"]}}  # alphabetical, as the fallback
    f = _fails(root, cfg)
    assert any(m.startswith("(h)") and "legacy_fallback" in m for m in f), f


def test_also_unit_alias_keeps_resolving_its_own_name_and_a_plain_alias_edge_is_checked(tmp_path):
    files = {
        f"{PK}/sec/__init__.py": _HDR + '__path__.append(str(_B / "extra"))\n',
        f"{PK}/extra/__init__.py": "", f"{PK}/extra/y.py": "",
        f"{PK}/user/__init__.py": "", f"{PK}/user/a.py": "import sec\n"}
    root = _tree(tmp_path, files)
    cfg = _cfg([["packages/extra", "packages/sec", "packages/user"]])
    cfg["aliases"] = {"packages/sec": {"__path__": ["self", "packages/extra"], "also_unit": True}}
    assert _fails(root, cfg) == []
    del cfg["aliases"]["packages/sec"]["also_unit"]
    assert any(m.startswith("(j)") for m in _fails(root, cfg))
