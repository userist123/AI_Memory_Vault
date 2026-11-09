"""Memory Core contract (WP-07 PR B): one module identity for the two facades.

``memory_controller.X`` and ``cognitive_core.X`` used to load the same file as two distinct
module objects (two ``Principal`` classes, two sets of module-level locks and caches).
``memory_controller/_alias.py`` now makes the first the same object as the second, but only
when both facades resolve the first component to the identical file.

Every probe runs in a fresh Python subprocess started from ``tmp_path`` with the import paths
set explicitly, because ``sys.modules`` of the pytest process is already contaminated by
other tests. ``-I`` is not used: the dependencies (``jsonschema``) live in the user
site-packages, which ``-I`` hides.

Reference: 07_EVALUATION/reports/modernization/F2_memory_core_contract.md section 2.2 and 4.
"""
from __future__ import annotations

import json
import subprocess
import sys
import textwrap
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"

# Ratchet for P3: files loaded as two or more distinct module objects. Measured before
# the alias (HEAD of modernize/f2-wp07a-facades): interfaces.api_server 11,
# cognitive_core.recall_cli 10. After the alias: 10 and 10. What is left are modules the
# production code imports by *physical* name (`retrieval.context.*`, `memory.data_router`),
# which the contract keeps importable and migrates later. The numbers may go down, never up.
DUAL_LOAD_BEFORE = {"interfaces.api_server": 11, "cognitive_core.recall_cli": 10}
DUAL_LOAD_CEILING = {"interfaces.api_server": 10, "cognitive_core.recall_cli": 10}

CONTRACT_SYMBOLS = {
    "controller": ["MemoryController", "StorageEngine", "Lifecycle", "GraphExpansionDegraded",
                   "AGENT_LIFECYCLE_FLOOR", "RANKING_ARM_BASELINE", "RANKING_ARM_FUSED_SCORE",
                   "RANKING_ARM_NO_CONFIDENCE", "RANKING_ARM_CONFIDENCE_TIEBREAK",
                   "RANKING_ARM_FUSED_PLUS_TIEBREAK", "RANKING_ARMS"],
    # The contract text says "storage.X"; physically the symbols live in the submodules of the
    # `storage` package (memory/storage/*.py), which is also how consumers import them.
    "storage.file_engine": ["FileStorageEngine", "CANONICAL_FOLDERS", "LEGACY_WRITE_ROOTS"],
    "storage.sqlite_engine": ["SQLiteStorageEngine"],
    "storage.serializer": ["deserialize"],
    "core": ["Lifecycle"],
}


def _probe(tmp_path: Path, body: str, *, with_root: bool) -> dict:
    """Run ``body`` in a clean interpreter and return the JSON it prints as the last line."""
    # root first, like pytest.ini and `python -m` from the repository root: it then owns `cognitive_core`
    paths = ([str(REPO)] if with_root else []) + [str(PACKAGES)]
    script = (
        "import sys, json, importlib\n"
        f"sys.path[:0] = {paths!r}\n"
        + textwrap.dedent(body)
    )
    proc = subprocess.run([sys.executable, "-c", script], cwd=str(tmp_path),
                          capture_output=True, text=True, timeout=180)
    assert proc.returncode == 0, f"probe failed\nstdout:\n{proc.stdout}\nstderr:\n{proc.stderr}"
    return json.loads(proc.stdout.strip().splitlines()[-1])


ROOT_MODES = pytest.mark.parametrize("with_root", [True, False], ids=["root+packages", "packages-only"])

_DUAL_LOAD = """
    import os, collections
    importlib.import_module(MOD)
    by_file = collections.defaultdict(dict)
    for name, mod in list(sys.modules.items()):
        f = getattr(mod, "__file__", None)
        if f and mod is not None:
            by_file[os.path.realpath(f)][id(mod)] = name
    dual = {f: sorted(v.values()) for f, v in by_file.items() if len(v) > 1}
    authorizer = [n for n in sys.modules if n.endswith(".authorizer") or n == "authorizer"]
    print(json.dumps({"count": len(dual), "authorizer_names": authorizer,
                      "authorizer_objects": len({id(sys.modules[n]) for n in authorizer})}))
"""


# ---- P1 -------------------------------------------------------------------------------
@ROOT_MODES
@pytest.mark.parametrize("order", ["memory_controller_first", "cognitive_core_first"])
def test_p1_contract_symbols_are_identical_in_both_import_orders(tmp_path, with_root, order):
    first, second = (("memory_controller", "cognitive_core") if order == "memory_controller_first"
                     else ("cognitive_core", "memory_controller"))
    out = _probe(tmp_path, f"""
        SYMBOLS = {CONTRACT_SYMBOLS!r}
        importlib.import_module({first!r})
        importlib.import_module({second!r})
        bad = []
        for m, names in SYMBOLS.items():
            mc = importlib.import_module("memory_controller." + m)
            cc = importlib.import_module("cognitive_core." + m)
            if mc is not cc:
                bad.append(m + " (module object)")
            for n in names:
                if getattr(mc, n) is not getattr(cc, n):
                    bad.append(m + "." + n)
        mc = importlib.import_module("memory_controller.controller")
        cc = importlib.import_module("cognitive_core.controller")
        arms = [n for n in dir(cc) if n.startswith("RANKING_ARM")]
        bad += [n for n in arms if getattr(mc, n) is not getattr(cc, n)]
        print(json.dumps({{"bad": bad, "arms": len(arms)}}))
    """, with_root=with_root)
    assert out["bad"] == []
    assert out["arms"] >= 3


@ROOT_MODES
def test_p1_identity_holds_at_depth_and_canonical_attributes_are_not_rewritten(tmp_path, with_root):
    out = _probe(tmp_path, """
        cc_first = importlib.import_module("cognitive_core.authorizer")
        before = (cc_first.__name__, cc_first.__spec__.name, cc_first.__file__, type(cc_first.__loader__).__name__)
        mc = importlib.import_module("memory_controller.authorizer")
        after = (mc.__name__, mc.__spec__.name, mc.__file__, type(mc.__loader__).__name__)
        deep = {}
        for rel in ("context.pack_builder", "validation.schema", "audit.logger", "storage.file_engine"):
            a = importlib.import_module("memory_controller." + rel)
            b = importlib.import_module("cognitive_core." + rel)
            deep[rel] = [a is b, a.__name__, a.__spec__.name]
        from memory_controller import controller as via_from
        print(json.dumps({"same": mc is cc_first, "before": before, "after": after, "deep": deep,
                          "from_import": via_from is importlib.import_module("cognitive_core.controller"),
                          "alias_key_is_canonical": sys.modules["memory_controller.authorizer"] is cc_first}))
    """, with_root=with_root)
    assert out["same"] and out["alias_key_is_canonical"] and out["from_import"]
    assert out["before"] == out["after"], "importlib rewrote __name__/__spec__/__file__/__loader__"
    assert out["after"][0] == "cognitive_core.authorizer" == out["after"][1]
    for rel, (same, name, spec_name) in out["deep"].items():
        assert same, rel
        assert name == spec_name == "cognitive_core." + rel, rel


# ---- P2 -------------------------------------------------------------------------------
@ROOT_MODES
def test_p2_principal_is_one_class_so_isinstance_crosses_the_facades(tmp_path, with_root):
    out = _probe(tmp_path, """
        import cognitive_core.authorizer as cca
        import memory_controller.authorizer as mca
        cc_obj = cca.Principal("human")        # the real constructor argument (an Enum value)
        mc_obj = mca.Principal("ai_agent")
        print(json.dumps({"cc_in_mc": isinstance(cc_obj, mca.Principal),
                          "mc_in_cc": isinstance(mc_obj, cca.Principal),
                          "same_class": cca.Principal is mca.Principal,
                          "same_member": cca.Principal.HUMAN is mca.Principal.HUMAN}))
    """, with_root=with_root)
    assert out == {"cc_in_mc": True, "mc_in_cc": True, "same_class": True, "same_member": True}


# ---- P3 -------------------------------------------------------------------------------
@ROOT_MODES
@pytest.mark.parametrize("module", sorted(DUAL_LOAD_CEILING))
def test_p3_dual_load_ratchet_and_authorizer_is_loaded_once(tmp_path, with_root, module):
    out = _probe(tmp_path, f"MOD = {module!r}\n" + textwrap.dedent(_DUAL_LOAD), with_root=with_root)
    assert out["count"] <= DUAL_LOAD_CEILING[module], (
        f"{module}: {out['count']} files loaded as >=2 module objects "
        f"(before the alias {DUAL_LOAD_BEFORE[module]}, ceiling {DUAL_LOAD_CEILING[module]})")
    assert out["authorizer_objects"] == 1, out


def test_p3_ceiling_for_api_server_is_below_the_measured_baseline():
    assert DUAL_LOAD_CEILING["interfaces.api_server"] < DUAL_LOAD_BEFORE["interfaces.api_server"]


# ---- P4 -------------------------------------------------------------------------------
@ROOT_MODES
@pytest.mark.parametrize("entry", ["cognitive_core.context.pack_builder", "memory_controller.context.pack_builder",
                                   "cognitive_core.recall_cli", "interfaces.api_server"])
def test_p4_trace_function_is_the_real_one_not_the_local_stub(tmp_path, with_root, entry):
    out = _probe(tmp_path, f"""
        importlib.import_module({entry!r})
        pb = sys.modules.get("memory_controller.context.pack_builder") or sys.modules["cognitive_core.context.pack_builder"]
        fn = pb.record_observed_memory_trace
        print(json.dumps({{"module": fn.__module__, "pb_name": pb.__name__}}))
    """, with_root=with_root)
    assert not out["module"].endswith("pack_builder"), f"stub in use: {out}"
    assert out["module"].endswith("memory_trace"), out


# ---- P5 -------------------------------------------------------------------------------
def test_p5_packages_only_benchmarks_is_not_aliased_and_loads_as_before(tmp_path):
    out = _probe(tmp_path, """
        mc = importlib.import_module("memory_controller.benchmarks")
        cc = importlib.import_module("cognitive_core.benchmarks")
        print(json.dumps({"distinct": mc is not cc, "mc_name": mc.__name__, "mc_file": mc.__file__,
                          "cc_file": cc.__file__, "loader": type(mc.__spec__.loader).__name__}))
    """, with_root=False)
    assert out["distinct"] and out["mc_name"] == "memory_controller.benchmarks"
    assert out["loader"] == "SourceFileLoader"
    assert out["mc_file"].replace("\\", "/").endswith("packages/retrieval/benchmarks/__init__.py")
    assert out["cc_file"].replace("\\", "/").endswith("packages/interfaces/benchmarks/__init__.py")


@pytest.mark.parametrize("name", ["recall_cli", "vault_cli", "telegram_vault_bot"])
def test_p5_root_shims_are_not_aliased(tmp_path, name):
    """With the repository root on the path ``cognitive_core.<name>`` is the root shim."""
    out = _probe(tmp_path, f"""
        import importlib.util
        cc_spec = importlib.util.find_spec("cognitive_core.{name}")
        mc_spec = importlib.util.find_spec("memory_controller.{name}")
        print(json.dumps({{"cc_origin": cc_spec.origin, "mc_origin": mc_spec.origin,
                          "mc_loader": type(mc_spec.loader).__name__}}))
    """, with_root=True)

    def norm(p):
        return p.replace("\\", "/")

    assert norm(out["mc_origin"]).endswith(f"packages/interfaces/{name}.py")
    assert norm(out["cc_origin"]) == norm(str(REPO / "cognitive_core" / f"{name}.py"))
    assert out["mc_loader"] == "SourceFileLoader"


@pytest.mark.parametrize("name", ["recall_cli", "vault_cli"])
def test_p5_root_shims_load_to_the_physical_module_and_stay_distinct(tmp_path, name):
    out = _probe(tmp_path, f"""
        mc = importlib.import_module("memory_controller.{name}")
        cc = importlib.import_module("cognitive_core.{name}")
        print(json.dumps({{"distinct": mc is not cc, "mc_name": mc.__name__, "mc_file": mc.__file__,
                          "cc_file": cc.__file__}}))
    """, with_root=True)
    assert out["distinct"] and out["mc_name"] == f"memory_controller.{name}"
    assert out["mc_file"].replace("\\", "/").endswith(f"packages/interfaces/{name}.py")
    assert out["cc_file"] != out["mc_file"]


@pytest.mark.parametrize("name", ["recall_cli", "vault_cli", "telegram_vault_bot"])
def test_p5_counter_check_packages_only_these_names_are_aliased(tmp_path, name):
    """Without the root both facades pick ``interfaces/<name>.py``, so aliasing is correct."""
    out = _probe(tmp_path, f"""
        import importlib.util
        cc = importlib.util.find_spec("cognitive_core.{name}")
        mc = importlib.util.find_spec("memory_controller.{name}")
        print(json.dumps({{"cc": cc.origin, "mc_loader": type(mc.loader).__name__}}))
    """, with_root=False)
    assert out["cc"].replace("\\", "/").endswith(f"packages/interfaces/{name}.py")
    assert out["mc_loader"] == "_AliasLoader"


# ---- P6 -------------------------------------------------------------------------------
@ROOT_MODES
def test_p6_unknown_submodule_is_still_module_not_found(tmp_path, with_root):
    out = _probe(tmp_path, """
        try:
            importlib.import_module("memory_controller.nu_exista")
            res = {"raised": None}
        except ModuleNotFoundError as exc:
            res = {"raised": "ModuleNotFoundError", "name": exc.name}
        try:
            importlib.import_module("memory_controller.nu_exista.nici_asta")
            res["deep"] = None
        except ModuleNotFoundError:
            res["deep"] = "ModuleNotFoundError"
        res["leaked"] = [n for n in sys.modules if "nu_exista" in n]
        print(json.dumps(res))
    """, with_root=with_root)
    assert out == {"raised": "ModuleNotFoundError", "name": "memory_controller.nu_exista",
                   "deep": "ModuleNotFoundError", "leaked": []}


@ROOT_MODES
def test_p6_error_from_the_canonical_module_propagates_and_leaves_no_alias(tmp_path, with_root):
    out = _probe(tmp_path, """
        import importlib.abc
        class Boom(importlib.abc.MetaPathFinder):
            def find_spec(self, fullname, path=None, target=None):
                if fullname == "cognitive_core.authorizer":
                    raise RuntimeError("boom from the canonical import")
        import memory_controller
        sys.meta_path.insert(0, Boom())
        try:
            importlib.import_module("memory_controller.authorizer")
            res = {"raised": None}
        except RuntimeError as exc:
            res = {"raised": "RuntimeError", "msg": str(exc)}
        res["registered"] = [n for n in sys.modules if n.endswith(".authorizer")]
        print(json.dumps(res))
    """, with_root=with_root)
    assert out == {"raised": "RuntimeError", "msg": "boom from the canonical import", "registered": []}


# ---- P7 -------------------------------------------------------------------------------
@ROOT_MODES
def test_p7_finder_is_installed_once_even_when_the_facade_is_reloaded(tmp_path, with_root):
    out = _probe(tmp_path, """
        def count():
            return sum(1 for f in sys.meta_path if getattr(f, "_memory_controller_alias_finder", False))
        import memory_controller
        c0 = count()
        importlib.reload(memory_controller)
        c1 = count()
        for n in [n for n in sys.modules if n == "memory_controller" or n.startswith("memory_controller.")]:
            del sys.modules[n]
        import memory_controller as again
        c2 = count()
        importlib.reload(again)
        first = getattr(sys.meta_path[0], "_memory_controller_alias_finder", False)
        print(json.dumps({"c0": c0, "c1": c1, "c2": c2, "c3": count(), "first": first}))
    """, with_root=with_root)
    assert (out["c0"], out["c1"], out["c2"], out["c3"]) == (1, 1, 1, 1)
    assert out["first"] is True


@ROOT_MODES
def test_p7_finder_only_claims_the_memory_controller_prefix(tmp_path, with_root):
    out = _probe(tmp_path, """
        import memory_controller
        finder = next(f for f in sys.meta_path if getattr(f, "_memory_controller_alias_finder", False))
        names = ["memory_controller", "memory_controllerX.controller", "memory_controller_extra.a",
                 "cognitive_core.controller", "retrieval.hybrid_retrieval", "memory.controller",
                 "json", "json.decoder", "xmemory_controller.controller"]
        claimed = [n for n in names if finder.find_spec(n) is not None]
        mine = finder.find_spec("memory_controller.controller")
        print(json.dumps({"claimed": claimed, "mine": mine is not None}))
    """, with_root=with_root)
    assert out == {"claimed": [], "mine": True}


def test_p7_without_a_canonical_namespace_the_finder_declines_instead_of_erroring(tmp_path):
    """A lone copy of the shim with no ``cognitive_core`` anywhere keeps its legacy behaviour."""
    lone = tmp_path / "lone" / "memory_controller"
    lone.mkdir(parents=True)
    for name in ("__init__.py", "_alias.py"):
        (lone / name).write_text(
            (PACKAGES / "memory_controller" / name).read_text(encoding="utf-8"), encoding="utf-8")
    (lone.parent / "memory").mkdir()
    (lone.parent / "memory" / "controller.py").write_text("MARK = 'legacy'\n", encoding="utf-8")
    script = (
        f"import sys, json; sys.path[:0] = [{str(lone.parent)!r}]\n"
        "import memory_controller\n"
        "finder = next(f for f in sys.meta_path if getattr(f, '_memory_controller_alias_finder', False))\n"
        "declined = finder.find_spec('memory_controller.controller') is None\n"
        "import memory_controller.controller as c\n"
        "print(json.dumps([declined, c.MARK, c.__name__]))\n"
    )
    proc = subprocess.run([sys.executable, "-c", script], cwd=str(tmp_path),
                          capture_output=True, text=True, timeout=60)
    assert proc.returncode == 0, proc.stderr
    assert json.loads(proc.stdout.strip().splitlines()[-1]) == [True, "legacy", "memory_controller.controller"]


# ---- P8: `python -m memory_controller.<X>` (documented in the Browser AI agent runbook) ---------
def _run_dash_m(tmp_path: Path, module: str, *args: str, with_root: bool = False):
    import os
    env = dict(os.environ)
    paths = ([str(REPO)] if with_root else []) + [str(PACKAGES)]
    env["PYTHONPATH"] = os.pathsep.join(paths)
    return subprocess.run([sys.executable, "-m", module, *args], cwd=str(tmp_path), env=env,
                          capture_output=True, text=True, timeout=120)


@pytest.mark.parametrize("module", ["memory_controller.controller", "memory_controller.version"])
def test_p8_dash_m_on_a_module_without_import_side_effects_exits_zero(tmp_path, module):
    proc = _run_dash_m(tmp_path, module)
    assert proc.returncode == 0, proc.stderr


def test_p8_dash_m_runs_the_main_block_in_a_fresh_namespace(tmp_path):
    """``vault_cli`` has an ``if __name__ == "__main__"`` block; ``--help`` must reach argparse."""
    proc = _run_dash_m(tmp_path, "memory_controller.vault_cli", "--help")
    assert proc.returncode == 0, proc.stderr
    assert proc.stdout.lower().startswith("usage:"), proc.stdout[:200]


@ROOT_MODES
def test_p8_runpy_executes_as_main_and_does_not_reuse_the_canonical_module(tmp_path, with_root):
    out = _probe(tmp_path, """
        import runpy
        canonical = importlib.import_module("cognitive_core.controller")
        before = (canonical.__name__, canonical.__spec__.name)
        ns = runpy.run_module("memory_controller.controller", run_name="__main__")
        print(json.dumps({"name": ns["__name__"], "fresh": ns.get("MemoryController") is not canonical.MemoryController,
                          "canonical_untouched": before == (canonical.__name__, canonical.__spec__.name),
                          "same_registered": sys.modules["cognitive_core.controller"] is canonical}))
    """, with_root=with_root)
    assert out == {"name": "__main__", "fresh": True, "canonical_untouched": True, "same_registered": True}


# ---- P9: the alias spec looks like the canonical one --------------------------------------------
@ROOT_MODES
def test_p9_alias_spec_of_a_package_is_a_package_spec(tmp_path, with_root):
    out = _probe(tmp_path, """
        import importlib.util, runpy
        spec = importlib.util.find_spec("memory_controller.storage")
        canon = importlib.util.find_spec("cognitive_core.storage")
        mod_spec = importlib.util.find_spec("memory_controller.controller")
        runpy_ok = True
        for name in ("memory_controller.controller", "memory_controller.authorizer"):
            try:
                runpy._get_module_details(name)
            except SystemExit:
                pass
            except BaseException as exc:
                runpy_ok = repr(exc)
        print(json.dumps({"locations": list(spec.submodule_search_locations or []),
                          "canon_locations": list(canon.submodule_search_locations or []),
                          "is_package": spec.loader.is_package(spec.name),
                          "module_is_package": mod_spec.loader.is_package(mod_spec.name),
                          "module_locations": mod_spec.submodule_search_locations,
                          "has_get_code": hasattr(spec.loader, "get_code") and hasattr(spec.loader, "get_source"),
                          "runpy_ok": runpy_ok}))
    """, with_root=with_root)
    assert out["locations"] and out["locations"] == out["canon_locations"]
    assert out["is_package"] is True and out["module_is_package"] is False
    assert out["module_locations"] is None
    assert out["has_get_code"] is True
    assert out["runpy_ok"] is True
