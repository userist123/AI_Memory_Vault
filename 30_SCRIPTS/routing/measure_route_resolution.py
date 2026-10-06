#!/usr/bin/env python3
"""Measure whether every routed file is reachable by its direct route, and never by a wrong one.

For every route in the table, as the owner (so policy does not hide anything):
  * by URI       — vault_resolve(uri) must return exactly that route;
  * by name      — vault_resolve(<last slug segment>) is RESOLVED to itself, AMBIGUOUS (several
                   files share the name: e.g. README, CURRENT) or, the failure that matters,
                   RESOLVED to a DIFFERENT file.

    python 30_SCRIPTS/routing/measure_route_resolution.py --out 07_EVALUATION/vault_routing/route_resolution.json
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from vault_access.audit import AuditLog  # noqa: E402
from vault_access.core import VaultAccess  # noqa: E402


def measure() -> dict:
    access = VaultAccess("owner", "cli_owner", audit=AuditLog(enabled=False))
    access.router.build(force=True)
    access.router.load_all_meta()
    routes = sorted(access.router.routes.values(), key=lambda r: r.uri)
    by_uri = Counter()
    by_name = Counter()
    wrong, ambiguous_names = [], Counter()
    started = time.time()
    for route in routes:
        env = access.resolve(route.uri)
        by_uri["self" if env["code"] == "OK" and env["route"]["uri"] == route.uri else "fail"] += 1
        leaf = route.uri.rsplit("/", 1)[-1]
        env = access.resolve(leaf)
        if env["code"] == "OK":
            if env["route"]["uri"] == route.uri:
                by_name["self"] += 1
            else:
                by_name["wrong"] += 1
                wrong.append({"name": leaf, "expected": route.uri, "got": env["route"]["uri"]})
        elif env["code"] == "AMBIGUOUS":
            by_name["ambiguous"] += 1
            ambiguous_names[leaf] += 1
            if route.uri in {c["uri"] for c in env["candidates"]}:
                by_name["ambiguous_self_in_candidates"] += 1
        else:
            by_name[env["code"]] += 1
    commit = subprocess.run(["git", "rev-parse", "HEAD"], cwd=REPO, capture_output=True, text=True).stdout.strip()
    return {
        "schema": "vault-route-resolution.v1",
        "commit": commit,
        "routes": len(routes),
        "domains": len(access.router.registry.domains),
        "by_uri": dict(by_uri),
        "by_name": dict(by_name),
        "wrong_resolutions": wrong,
        "most_shared_names": ambiguous_names.most_common(15),
        "seconds": round(time.time() - started, 1),
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path)
    args = ap.parse_args(argv)
    result = measure()
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(text + "\n", encoding="utf-8")
    print(text)
    return 1 if result["wrong_resolutions"] or result["by_uri"].get("fail") else 0


if __name__ == "__main__":
    raise SystemExit(main())
