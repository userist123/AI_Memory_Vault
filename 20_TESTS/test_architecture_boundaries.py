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
    assert any(e.get("hard_forbidden") and e["to"] == "products/xau_kinetic" for e in config["baseline_edges"])


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


def test_build_baseline_preserves_metadata_and_assigns_owner(tmp_path):
    files = _base_files() | {f"{PK}/low/b.py": "import mid\n"}
    root = _tree(tmp_path, files)
    prev = _edge("packages/low", "packages/mid", 9)
    prev.update({"since": "2020-01-01", "owner_wp": "WP-99", "reason": "kept"})
    cfg = _cfg(LAYERS, [prev, _edge("packages/gone", "packages/mid", 1)])
    new = ab.build_baseline(cfg, ab._inventory(root))
    assert new == [{"from": "packages/low", "to": "packages/mid", "imports": 1, "since": "2020-01-01",
                    "owner_wp": "WP-99", "reason": "kept"}]
    assert ab.owner_wp("packages/a", "root/cognitive_core", False) == "WP-06"
    assert ab.owner_wp("packages/a", "packages/polymarket", False) == "WP-12"
    assert ab.owner_wp("packages/a", "root/security", False) == "WP-14"
    assert ab.owner_wp("packages/a", "packages/b", False) == "WP-10"


def test_cli_exit_codes(tmp_path):
    root = _tree(tmp_path, _base_files() | {f"{PK}/low/b.py": "import mid\n"})
    cfg = root / "cfg.json"
    cfg.write_text(json.dumps(_cfg(LAYERS)), "utf-8")
    assert ab.main(["--root", str(root), "--config", str(cfg)]) == 1
    assert ab.main(["--root", str(root), "--config", str(cfg), "--write-baseline"]) == 0
    assert ab.main(["--root", str(root), "--config", str(cfg)]) == 0
