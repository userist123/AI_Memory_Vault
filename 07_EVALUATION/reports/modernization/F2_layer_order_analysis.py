"""WP-06 evidence: real (de-aliased) dependency graph and layer-order comparison.

Read-only. Run from the repository root:

    python 07_EVALUATION/reports/modernization/F2_layer_order_analysis.py

The facade packages (``memory_controller``, ``cognitive_core``, ``memory_vault``, ``xau_kinetic``) are
``__path__`` unions over sibling packages. An import ``memory_controller.controller`` therefore depends
on ``packages/memory``, not on a "facade layer". This script resolves every facade import to the
physical unit that owns the submodule (first match in the facade's ``__path__`` order, as Python does),
counts non-test files per (from, to) pair, and compares three layer orders:

* ``config``  - the order in ``04_CONFIG/architecture_layers.json`` (WP-04);
* ``A3``      - the order from the external Masterplan (section A3), as transcribed in the F1 review;
* ``WP06``    - the order chosen in WP-06 (``F2_memory_core_contract.md``).

It also computes the exact minimum number of upward files over all total orders (DP over subsets).
"""
from __future__ import annotations

import ast
import collections
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "30_SCRIPTS" / "verification"))
import package_inventory as pi  # noqa: E402

PK = ROOT / "03_IMPLEMENTATION" / "packages"
INV = json.loads((ROOT / "07_EVALUATION/reports/modernization/F0_package_inventory.json").read_text(encoding="utf-8"))
UNITS = {u["id"]: ROOT / u["dir"] for u in INV["units"]}
FACADES = {"packages/memory_controller", "packages/cognitive_core", "packages/memory_vault", "packages/xau_kinetic"}
# __path__ order copied from each shim's __init__.py; "__root__" = root cognitive_core/ (its modules are
# shims that re-export a sibling package, followed by _root_target).
ALIAS = {
    "memory_controller": ["retrieval", "memory", "security", "lifecycle", "interfaces", "observability"],
    "cognitive_core": ["__root__", "graph", "memory", "learning", "observability", "providers", "interfaces",
                       "security", "retrieval", "lifecycle"],
}
UNRESOLVED: list[tuple[str, str]] = []


def _root_target(sub: str) -> str:
    """Root cognitive_core/<sub>.py re-exports a sibling package (``from [packages.]X.<mod> import *``)."""
    m = re.search(r"^from (?:packages\.)?(\w+)\.", (ROOT / "cognitive_core" / f"{sub}.py").read_text(encoding="utf-8"),
                  re.M)
    if not m or f"packages/{m.group(1)}" not in UNITS:
        UNRESOLVED.append(("cognitive_core(root)", sub))
        return "root/cognitive_core"
    return f"packages/{m.group(1)}"


def _owner(alias: str, sub: str) -> str | None:
    if alias == "memory_vault":  # __path__ = [packages/]: memory_vault.<pkg>.<mod> is packages/<pkg>
        if f"packages/{sub}" in UNITS:
            return f"packages/{sub}"
        UNRESOLVED.append((alias, sub))
        return None
    for s in ALIAS[alias]:
        d = ROOT / "cognitive_core" if s == "__root__" else PK / s
        if (d / f"{sub}.py").exists() or (d / sub).is_dir():
            return _root_target(sub) if s == "__root__" else f"packages/{s}"
    UNRESOLVED.append((alias, sub))
    return None


def _targets(mod: str, names: list[str] | tuple = ()) -> set[str]:
    parts = mod.split(".")
    if parts[0] == "packages" and len(parts) > 1:
        parts = parts[1:]
    top = parts[0]
    if top in ALIAS or top == "memory_vault":
        subs = [parts[1]] if len(parts) > 1 else list(names)
        out = {o for o in (_owner(top, s) for s in subs) if o}
        return out or {"root/cognitive_core" if top == "cognitive_core" else f"packages/{top}"}
    if top == "xau_kinetic" or parts[:2] == ["products", "xau_kinetic"]:
        return {"products/xau_kinetic"}
    if top == "security":  # root/security wins on sys.path from the repository root
        return {"root/security"}
    if top == "scripts":
        return {"root/scripts"}
    return {f"packages/{top}"} if f"packages/{top}" in UNITS else set()


def real_edges(trace: set | None = None) -> collections.Counter:
    edges: collections.Counter = collections.Counter()
    for uid, d in UNITS.items():
        if uid in FACADES:
            continue
        for f in sorted(d.rglob("*.py")):
            rel = f.relative_to(ROOT).as_posix()
            if pi.is_test_path(rel):
                continue
            try:
                tree = ast.parse(f.read_text(encoding="utf-8", errors="replace"))
            except SyntaxError:
                continue
            tg: set[str] = set()
            for n in ast.walk(tree):
                if isinstance(n, ast.Import):
                    for a in n.names:
                        tg |= _targets(a.name)
                elif isinstance(n, ast.ImportFrom) and n.module and n.level == 0:
                    tg |= _targets(n.module, [a.name for a in n.names])
            tg.discard(uid)
            for t in tg:
                edges[(uid, t)] += 1
                if trace is not None and (uid, t) in trace:
                    print(f"    {uid} -> {t}: {rel}")
    return edges


def upward(order: dict[str, int], edges) -> list[tuple[str, str, int]]:
    return sorted((a, b, n) for (a, b), n in edges.items() if order.get(a, 99) < order.get(b, 99))


def minimum(edges) -> tuple[int, list[str]]:
    ns = sorted({x for e in edges for x in e})
    ix = {n: i for i, n in enumerate(ns)}
    w = [[0] * len(ns) for _ in ns]
    for (a, b), n in edges.items():
        w[ix[a]][ix[b]] += n
    inf = 10**9
    dp, par = [inf] * (1 << len(ns)), [-1] * (1 << len(ns))
    dp[0] = 0
    for s in range(1 << len(ns)):
        if dp[s] == inf:
            continue
        for v in range(len(ns)):
            if not s >> v & 1:
                c = dp[s] + sum(w[u][v] for u in range(len(ns)) if s >> u & 1)
                if c < dp[s | 1 << v]:
                    dp[s | 1 << v], par[s | 1 << v] = c, v
    s, order = (1 << len(ns)) - 1, []
    while s:
        order.append(ns[par[s]])
        s ^= 1 << par[s]
    return dp[-1], order[::-1]


def layered(layers: list[list[str]]) -> dict[str, int]:
    return {u: i for i, layer in enumerate(layers) for u in layer}


P = "packages/"
A3 = layered([[P + "security", P + "observability", "root/security"],
              [P + "memory", P + "memory_vault", P + "vault_access"],
              [P + "retrieval", P + "graph"],
              [P + "cognitive_core", P + "learning", P + "lifecycle", "root/cognitive_core"],
              [P + "memory_controller", P + "agent_bridge", P + "routing"],
              [P + "interfaces", P + "providers"],
              ["products/xau_kinetic", P + "polymarket", P + "xau_kinetic", "root/scripts"]])
WP06 = layered([[P + "security", P + "providers", P + "routing", P + "graph"],
                [P + "lifecycle"],
                ["root/security"],
                [P + "observability", P + "retrieval"],
                [P + "memory", P + "vault_access"],
                ["products/xau_kinetic", P + "polymarket"],
                [P + "interfaces", P + "agent_bridge"],
                [P + "learning"],
                ["root/cognitive_core", "root/scripts"]])


def main() -> int:
    cfg = json.loads((ROOT / "04_CONFIG/architecture_layers.json").read_text(encoding="utf-8"))
    config = layered([layer["units"] for layer in cfg["layers"]])
    aliased = collections.Counter()
    for u in INV["units"]:
        for t, n in u.get("imports_out_nontest", {}).items():
            aliased[(u["id"], t)] += n
    edges = real_edges()
    print(f"aliased graph (WP-04 inventory): pairs={len(aliased)} files={sum(aliased.values())}")
    print(f"real graph (facades resolved):   pairs={len(edges)} files={sum(edges.values())} "
          f"unresolved={sorted(set(UNRESOLVED))}")
    for name, order, g in [("config/aliased", config, aliased), ("config/real", config, edges),
                           ("A3/real", A3, edges), ("WP06/real", WP06, edges)]:
        up = upward(order, g)
        print(f"{name:15s} upward pairs={len(up):3d} files={sum(n for *_, n in up):4d}")
    best, order = minimum(edges)
    print(f"minimum over all total orders: files={best}  order={order}")
    print("WP06 upward edges and the files behind them:")
    real_edges(trace={(a, b) for a, b, _ in upward(WP06, edges)})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
