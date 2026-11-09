#!/usr/bin/env python3
"""Architecture layer check as a ratchet (WP-04).

Read-only, standard library only. Reuses ``package_inventory.build_inventory`` (no second resolver).
Layers (``04_CONFIG/architecture_layers.json``) are ordered low -> high; a unit may import units of the
same or a LOWER layer. Every upward edge that exists today is recorded in ``baseline_edges`` with an
owner work package. The check fails on: a new upward edge, a count above baseline, and a STALE baseline
(edge gone or count lower), so every gain is locked in.

An edge count is the number of NON-TEST files of the source unit that import the target unit.

    python 30_SCRIPTS/verification/architecture_boundaries.py --root . \
        --config 04_CONFIG/architecture_layers.json [--write-baseline]
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import package_inventory as pi  # noqa: E402

DEFAULT_SINCE = "2026-11-09"
FACADE_NAMES = {"memory_controller", "memory_vault", "cognitive_core"}
EDGE_KEY = tuple  # (from_unit, to_unit)


def _inventory(root: Path) -> dict:
    return pi.build_inventory(Path(root))


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


def is_hard_forbidden(config: dict, kinds: dict[str, str], a: str, b: str) -> bool:
    hf = config.get("hard_forbidden", {})
    aliases = set(hf.get("alias_units", []))
    return kinds.get(a) == hf.get("from_kind", "packages") and (kinds.get(b) == hf.get("to_kind", "products")
                                                                  or b in aliases)


def _classify(config: dict, inv: dict, edges: dict[tuple[str, str], int]):
    idx = layer_index(config)
    kinds = {u["id"]: u["kind"] for u in inv["units"]}
    rows = {}
    for (a, b), n in edges.items():
        if a not in idx or b not in idx:
            continue
        upward = idx[a] < idx[b]
        hard = is_hard_forbidden(config, kinds, a, b)
        if upward or hard:
            rows[(a, b)] = {"count": n, "upward": upward, "hard": hard}
    return rows


def check(root: Path, config: dict, inv: dict | None = None) -> list[str]:
    inv = inv if inv is not None else _inventory(root)
    fails: list[str] = []
    idx = layer_index(config)
    ids = {u["id"] for u in inv["units"]}
    for uid in sorted(ids - set(idx)):
        fails.append(f"(a) unit {uid} exists but is not assigned to any layer in the config")
    for uid in sorted(set(idx) - ids):
        fails.append(f"(a) unit {uid} is assigned to a layer but does not exist in the inventory")
    edges = edges_from_inventory(inv)
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
                         f"tighten the baseline (--write-baseline) to lock the gain")
    for (a, b), e in sorted(base.items()):
        if (a, b) not in current:
            fails.append(f"(d) STALE baseline: edge {a} -> {b} (baseline {e['imports']}) no longer exists; "
                         f"remove it (--write-baseline) to lock the gain")
    found = {c["name"] for c in inv["collisions"]}
    known = set(config.get("known_collisions", []))
    if found != known:
        fails.append(f"(e) collisions differ from known_collisions: unexpected={sorted(found - known)} "
                     f"missing={sorted(known - found)}")
    return fails


def owner_wp(a: str, b: str, hard: bool) -> str:
    if hard:
        return "WP-12"
    names = {a.split("/", 1)[1], b.split("/", 1)[1]}
    if names & FACADE_NAMES:
        return "WP-06"
    if names & {"xau_kinetic", "polymarket"}:
        return "WP-12"
    if "security" in names:
        return "WP-14"
    return "WP-10"


def build_baseline(config: dict, inv: dict) -> list[dict]:
    old = {(e["from"], e["to"]): e for e in config.get("baseline_edges", [])}
    out = []
    for (a, b), row in sorted(_classify(config, inv, edges_from_inventory(inv)).items()):
        prev = old.get((a, b), {})
        la, lb = layer_name(config, a), layer_name(config, b)
        reason = prev.get("reason") or (
            f"hard-forbidden package -> product edge ({la} -> {lb})" if row["hard"]
            else f"upward edge {la} -> {lb} present at the baseline date")
        e = {"from": a, "to": b, "imports": row["count"], "since": prev.get("since", DEFAULT_SINCE),
             "owner_wp": prev.get("owner_wp") or owner_wp(a, b, row["hard"]), "reason": reason}
        if row["hard"]:
            e["hard_forbidden"] = True
        out.append(e)
    return out


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--root", default=".")
    ap.add_argument("--config", default="04_CONFIG/architecture_layers.json")
    ap.add_argument("--write-baseline", action="store_true")
    args = ap.parse_args(argv)
    root = Path(args.root)
    cfg_path = Path(args.config)
    if not cfg_path.is_absolute() and not cfg_path.exists():
        cfg_path = root / cfg_path
    config = json.loads(cfg_path.read_text("utf-8"))
    inv = _inventory(root)
    if args.write_baseline:
        config["baseline_edges"] = build_baseline(config, inv)
        cfg_path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", "utf-8", newline="\n")
        print(f"baseline written: {len(config['baseline_edges'])} edges")
    fails = check(root, config, inv)
    for f in fails:
        print("FAIL " + f)
    print(f"architecture_boundaries: {'FAIL' if fails else 'OK'} "
          f"(units={len(inv['units'])} baseline_edges={len(config.get('baseline_edges', []))})")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
