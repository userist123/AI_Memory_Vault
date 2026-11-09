#!/usr/bin/env python3
"""Architecture layer check as a ratchet (WP-04).

Read-only, standard library only. Reuses ``package_inventory.build_inventory`` (no second resolver).
Layers (``04_CONFIG/architecture_layers.json``) are ordered low -> high; a unit may import units of the
same or a LOWER layer. Facade packages (``__path__`` shims such as ``memory_controller``) are not layers: they
are ``aliases`` with a declared ``__path__``, and an import through a facade is an edge to the unit that serves
the submodule (``package_inventory`` resolves it). The real ``__path__`` of every alias must equal the declared
one. Every upward edge that exists today is recorded in ``baseline_edges`` with an owner work package. The check fails on: a new upward edge, a count above baseline, and a STALE baseline
(edge gone or count lower), so every gain is locked in.

An edge count is the number of NON-TEST files of the source unit that import the target unit.

    python 30_SCRIPTS/verification/architecture_boundaries.py --root . \
        --config 04_CONFIG/architecture_layers.json [--write-baseline]

``--write-baseline`` only TIGHTENS (drops gone edges, lowers counts). Widening the baseline needs
``--allow-widen --owner-wp WP-xx --reason "..."`` and is reported pair by pair.
Non-test imports include ``-m``/``python -c`` strings in files inside a unit and test-path files that
production code imports. Non-literal ``import_module`` calls are counted per unit
(``baseline_dynamic_imports``), not resolved.
"""
from __future__ import annotations

import argparse
import datetime
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import package_inventory as pi  # noqa: E402

DEFAULT_SINCE = "2026-11-09"
# Residual upward edges (F2_memory_core_contract.md section 1.3): (from name, to name) -> owner work package.
FACADE_NAMES = {"memory_controller", "memory_vault", "cognitive_core"}  # legacy default of owner_wp()
OWNER_TABLE = {
    ("graph", "memory"): "WP-10", ("lifecycle", "memory"): "WP-10", ("memory", "interfaces"): "WP-10",
    ("retrieval", "memory"): "WP-10", ("retrieval", "interfaces"): "WP-10",
    ("lifecycle", "interfaces"): "WP-14", ("security", "interfaces"): "WP-14", ("vault_access", "interfaces"): "WP-14",
}
EDGE_KEY = tuple  # (from_unit, to_unit)


def also_units_of(config: dict) -> set[str]:
    return {a for a, d in config.get("aliases", {}).items() if d.get("also_unit")}


def _inventory(root: Path, config: dict | None = None) -> dict:
    """``config`` given: its ``also_unit`` aliases are the only shims that keep resolving their own name."""
    return pi.build_inventory(Path(root), also_units_of(config) if config is not None else None)


def edges_from_inventory(inv: dict) -> dict[tuple[str, str], int]:
    out: dict[tuple[str, str], int] = {}
    for u in inv["units"]:
        for t, n in u.get("imports_out_nontest", {}).items():
            if n:
                out[(u["id"], t)] = n
    return out


def current_edges(root: Path, inv: dict | None = None) -> dict[tuple[str, str], int]:
    """(from_unit, to_unit) -> number of non-test files importing it."""
    return edges_from_inventory(inv if inv is not None else _inventory(root))


def dynamic_counts(inv: dict) -> dict[str, int]:
    return {u["id"]: u["dynamic_imports_nontest"] for u in inv["units"] if u.get("dynamic_imports_nontest")}


def layer_index(config: dict) -> dict[str, int]:
    idx: dict[str, int] = {}
    for i, layer in enumerate(config["layers"]):
        for u in layer["units"]:
            idx[u] = i
    return idx


def layer_name(config: dict, unit: str) -> str | None:
    for layer in config["layers"]:
        if unit in layer["units"]:
            return layer["name"]
    return None


def aliases_of(config: dict) -> dict[str, dict]:
    return config.get("aliases", {})


def is_hard_forbidden(config: dict, kinds: dict[str, str], a: str, b: str) -> bool:
    """package -> product, also through an alias that serves a product (``alias_units`` or a declared ``__path__``)."""
    hf = config.get("hard_forbidden", {})
    to_kind = hf.get("to_kind", "products")
    via = set(hf.get("alias_units", [])) | {
        al for al, d in aliases_of(config).items() if any(kinds.get(x) == to_kind for x in d.get("__path__", []))}
    return kinds.get(a) == hf.get("from_kind", "packages") and (kinds.get(b) == to_kind or b in via)


def _classify(config: dict, inv: dict, edges: dict[tuple[str, str], int]):
    idx = layer_index(config)
    kinds = {u["id"]: u["kind"] for u in inv["units"]}
    rows = {}
    for (a, b), n in edges.items():
        hard = is_hard_forbidden(config, kinds, a, b)  # enforced even when the edge does not go up (WP-12)
        upward = a in idx and b in idx and idx[a] < idx[b]
        if upward or hard:
            rows[(a, b)] = {"count": n, "upward": upward, "hard": hard}
    return rows


def check(root: Path, config: dict, inv: dict | None = None) -> list[str]:
    inv = inv if inv is not None else _inventory(root, config)
    fails: list[str] = []
    idx = layer_index(config)
    ids = {u["id"] for u in inv["units"]}
    aliases = aliases_of(config)
    for uid in sorted(ids - set(idx) - set(aliases)):
        fails.append(f"(a) unit {uid} exists but is not assigned to any layer or alias in the config")
    for uid in sorted((set(idx) | set(aliases)) - ids):
        fails.append(f"(a) unit {uid} is assigned to a layer or alias but does not exist in the inventory")
    by_id = {u["id"]: u for u in inv["units"]}
    for uid in sorted(set(aliases) & ids):
        real = by_id[uid]["shim_targets"]
        declared = aliases[uid].get("__path__")
        if real != declared:
            fails.append(f"(h) alias {uid}: real __path__ {real} differs from the declared {declared}")
        elif by_id[uid].get("shim_target_source") == "legacy_fallback":
            fails.append(f"(h) alias {uid}: its __path__ is not interpretable by the evaluator (legacy_fallback "
                         f"heuristic, order not guaranteed); write it as a literal list of Path(__file__) expressions")
    for uid in sorted(ids - set(aliases)):
        if by_id[uid].get("shim_targets") is not None:
            fails.append(f"(h) unit {uid} moves its __path__ (shim_targets {by_id[uid]['shim_targets']}) but is not "
                         f"declared in aliases")
    for r in inv["meta"].get("facade_unresolved", []):
        if not r.get("test"):
            fails.append(f"(i) non-test import {r['module']} ({r['file']}:{r['line']}) is not served by any entry of the "
                         f"__path__ of facade {r['facade']}")
    plain_aliases = set(aliases) - also_units_of(config)
    edges = edges_from_inventory(inv)
    for (a, b), n in sorted(edges.items()):
        if b in plain_aliases:
            fails.append(f"(j) {a} has a non-test edge to facade {b} ({n} files): a facade is not a unit, "
                         f"the import must resolve to the unit that serves it")
    current = _classify(config, inv, edges)
    base = {(e["from"], e["to"]): e for e in config.get("baseline_edges", [])}
    for (a, b), row in sorted(current.items()):
        n = row["count"]
        b_entry = base.get((a, b))
        if b_entry is None:
            if row["hard"]:
                fails.append(f"(f) hard-forbidden edge {a} -> {b} ({n} files) is not in the baseline")
            else:
                fails.append(f"(b) new upward edge {a} ({layer_name(config, a)}) -> {b} ({layer_name(config, b)}), "
                             f"{n} files, not in the baseline")
        elif n > b_entry["imports"]:
            fails.append(f"(c) edge {a} -> {b} grew from {b_entry['imports']} to {n} files (above baseline)")
        elif n < b_entry["imports"]:
            fails.append(f"(d) STALE baseline: edge {a} -> {b} dropped from {b_entry['imports']} to {n}; "
                         f"tighten the baseline with `--write-baseline` (tighten-only) to lock the gain")
    for (a, b), e in sorted(base.items()):
        if (a, b) not in current:
            fails.append(f"(d) STALE baseline: edge {a} -> {b} (baseline {e['imports']}) no longer exists; "
                         f"remove it with `--write-baseline` (tighten-only) to lock the gain")
    dyn_now = dynamic_counts(inv)
    dyn_base = config.get("baseline_dynamic_imports", {})
    for uid in sorted(set(dyn_now) | set(dyn_base)):
        n, b = dyn_now.get(uid, 0), dyn_base.get(uid, 0)
        if n > b:
            fails.append(f"(g) unit {uid} has {n} non-literal dynamic imports, baseline {b} "
                         f"({'new' if not b else 'above baseline'})")
        elif n < b:
            fails.append(f"(g) STALE baseline: unit {uid} dynamic imports dropped from {b} to {n}; "
                         f"tighten the baseline with `--write-baseline` (tighten-only) to lock the gain")
    found = {c["name"] for c in inv["collisions"]}
    known = set(config.get("known_collisions", []))
    if found != known:
        fails.append(f"(e) collisions differ from known_collisions: unexpected={sorted(found - known)} "
                     f"missing={sorted(known - found)}")
    return fails


def table_owner(a: str, b: str, hard: bool) -> str | None:
    """The owner fixed by the contract (hard_forbidden or a section 1.3 pair); ``None`` = not fixed."""
    if hard:
        return "WP-12"
    return OWNER_TABLE.get((a.split("/", 1)[1], b.split("/", 1)[1]))


def owner_wp(a: str, b: str, hard: bool) -> str:
    """Owner of a residual edge: the section 1.3 table, hard_forbidden -> WP-12, else a name-based default."""
    if hard:
        return "WP-12"
    names = {a.split("/", 1)[1], b.split("/", 1)[1]}
    if table_owner(a, b, False):
        return table_owner(a, b, False)
    if names & FACADE_NAMES:
        return "WP-06"
    if names & {"xau_kinetic", "polymarket"}:
        return "WP-12"
    if "security" in names:
        return "WP-14"
    return "WP-10"


def plan_baseline(config: dict, inv: dict, allow_widen: bool = False, owner: str | None = None,
                  reason: str | None = None, today: str | None = None) -> dict:
    """Next baseline. Tighten-only unless ``allow_widen``; returns edges, dynamic counts and a report."""
    today = today or datetime.date.today().isoformat()
    old = {(e["from"], e["to"]): e for e in config.get("baseline_edges", [])}
    current = _classify(config, inv, edges_from_inventory(inv))
    edges: list[dict] = []
    tightened: list[str] = []
    widened: list[str] = []
    refused: list[str] = []
    for key in sorted(set(old) | set(current)):
        a, b = key
        prev, row = old.get(key), current.get(key)
        if row is None:
            tightened.append(f"{a} -> {b}: removed (was {prev['imports']})")
            continue
        n = row["count"]
        if prev is not None and n <= prev["imports"]:
            e = dict(prev)
            if n < prev["imports"]:
                tightened.append(f"{a} -> {b}: {prev['imports']} -> {n}")
            e["imports"] = n
            want = table_owner(a, b, row["hard"])
            if allow_widen and want and prev.get("owner_wp") != want:
                widened.append(f"{a} -> {b}: owner {prev.get('owner_wp')} -> {want}")
                e["owner_wp"] = want
            if allow_widen and reason and e.get("reason") != reason:
                # re-attribution run (e.g. new layer order): the old reason names the old layers
                e["reason"] = reason
            edges.append(e)
            continue
        what = f"{a} -> {b}: {prev['imports'] if prev else 'new'} -> {n}"
        if not allow_widen:
            refused.append(what)
            if prev is not None:
                edges.append(dict(prev))
            continue
        widened.append(what)
        e = {"from": a, "to": b, "imports": n, "since": today,
             "owner_wp": table_owner(a, b, row["hard"]) or owner, "reason": reason}
        if e["owner_wp"] != owner:
            e["widened_by"] = owner
        if row["hard"]:
            e["hard_forbidden"] = True
        edges.append(e)
    dyn_old = dict(config.get("baseline_dynamic_imports", {}))
    dyn_now = dynamic_counts(inv)
    dyn: dict[str, int] = {}
    for uid in sorted(set(dyn_old) | set(dyn_now)):
        n, b = dyn_now.get(uid, 0), dyn_old.get(uid, 0)
        if n < b:
            tightened.append(f"dynamic imports {uid}: {b} -> {n}")
        elif n > b:
            (widened if allow_widen else refused).append(f"dynamic imports {uid}: {b} -> {n}")
        keep = n if (n <= b or allow_widen) else b
        if keep:
            dyn[uid] = keep
    return {"edges": edges, "dynamic": dyn, "tightened": tightened, "widened": widened, "refused": refused}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--root", default=".")
    ap.add_argument("--config", default="04_CONFIG/architecture_layers.json")
    ap.add_argument("--write-baseline", action="store_true", help="tighten-only regeneration of the baseline")
    ap.add_argument("--allow-widen", action="store_true", help="with --write-baseline: also add/raise entries")
    ap.add_argument("--owner-wp")
    ap.add_argument("--reason")
    args = ap.parse_args(argv)
    if args.allow_widen and not (args.write_baseline and args.owner_wp and args.reason):
        print("--allow-widen needs --write-baseline, --owner-wp WP-xx and --reason", file=sys.stderr)
        return 2
    root = Path(args.root)
    cfg_path = Path(args.config)
    if not cfg_path.is_absolute() and not cfg_path.exists():
        cfg_path = root / cfg_path
    config = json.loads(cfg_path.read_text("utf-8"))
    inv = _inventory(root, config)
    if args.write_baseline:
        plan = plan_baseline(config, inv, args.allow_widen, args.owner_wp, args.reason)
        for k in ("tightened", "widened", "refused"):
            for line in plan[k]:
                print(f"{k.upper()} {line}")
        config["baseline_edges"] = plan["edges"]
        config["baseline_dynamic_imports"] = plan["dynamic"]
        cfg_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", "utf-8", newline="\n")
        print(f"baseline written: {len(plan['edges'])} edges "
              f"(tightened={len(plan['tightened'])} widened={len(plan['widened'])} refused={len(plan['refused'])})")
        if plan["refused"]:
            print('REFUSED: widening needs --allow-widen --owner-wp WP-xx --reason "..."')
            return 1
    fails = check(root, config, inv)
    for f in fails:
        print("FAIL " + f)
    print(f"architecture_boundaries: {'FAIL' if fails else 'OK'} "
          f"(units={len(inv['units'])} baseline_edges={len(config.get('baseline_edges', []))})")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
