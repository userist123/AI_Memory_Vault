#!/usr/bin/env python3
"""Validate the vault route registry and print the route table (nothing is written to the repo).

    python 30_SCRIPTS/routing/build_route_manifest.py --check            # CI gate
    python 30_SCRIPTS/routing/build_route_manifest.py --summary          # routes per domain
    python 30_SCRIPTS/routing/build_route_manifest.py --json out.json    # full table (local use)

`--check` fails when:
  * 04_CONFIG/vault_domains.yaml or access_policy.yaml is malformed (unknown keys, bad ids,
    unknown classification/trust, roots that escape the repository, a bad `expand`);
  * a numbered root of the repository (00_..99_) has no domain at all (except 20_TESTS);
  * a principal names a domain that does not exist (a typo would silently deny or allow);
  * a routed file matches the hard denylist;
  * a note in the repository declares `classification: SENSITIVE` or `RESTRICTED` — the
    repository is public; such content belongs in the private overlay;
  * the routes cannot all be read back through VaultAccess as the owner (sample of 1 per domain).
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path
from typing import List, Optional

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from vault_access.audit import AuditLog  # noqa: E402
from vault_access.core import VaultAccess  # noqa: E402
from vault_access.policy import AccessPolicy  # noqa: E402
from vault_access.router import DomainRouter  # noqa: E402

EXEMPT_ROOTS = {"20_TESTS"}


def collect(repo: Path = REPO) -> dict:
    policy = AccessPolicy.load(repo / "04_CONFIG" / "access_policy.yaml")
    router = DomainRouter(repo, repo / "04_CONFIG" / "vault_domains.yaml", policy, cache_path=False)
    router.build(force=True)
    router.load_all_meta()
    problems: List[str] = list(router.problems)
    domains = router.registry.domains

    # numbered roots with no domain
    covered = set()
    for spec in domains.values():
        if spec.base != "repo":
            continue
        for root in spec.roots:
            covered.add(root.split("/", 1)[0])
    for child in sorted(repo.iterdir()):
        if child.is_dir() and len(child.name) > 3 and child.name[:2].isdigit() and child.name[2] == "_":
            if child.name not in covered and child.name not in EXEMPT_ROOTS:
                problems.append(f"ROOT_WITHOUT_DOMAIN:{child.name}")

    # principals naming unknown domains
    for principal in policy.principals.values():
        for pat in principal.domains:
            name = pat.lstrip("!")
            if name == "*" or any(c in name for c in "*?["):
                continue
            if name not in domains and not any(d.startswith(name + ".") for d in domains):
                problems.append(f"PRINCIPAL_UNKNOWN_DOMAIN:{principal.name}:{name}")

    routes = list(router.routes.values())
    for route in routes:
        if policy.denied_path(route.path):
            problems.append(f"DENYLISTED_ROUTE:{route.uri}")
        raw = router._meta_cache.get(f"{route.base}:{route.path}", {})
        if route.base == "repo" and raw.get("classification") in ("SENSITIVE", "RESTRICTED"):
            problems.append(f"SENSITIVE_IN_PUBLIC_REPO:{route.path}")
        if route.base == "repo" and raw.get("classification") and raw["classification"] not in policy.classifications:
            problems.append(f"UNKNOWN_CLASSIFICATION:{route.path}:{raw['classification']}")

    # read-back sample: the first route of every domain must be readable by the owner
    access = VaultAccess("owner", "cli_owner", repo_root=repo, policy=policy, router=router,
                         audit=AuditLog(enabled=False))
    seen = set()
    for route in sorted(routes, key=lambda r: r.uri):
        if route.domain in seen:
            continue
        seen.add(route.domain)
        env = access.read(route.uri, line_start=1, line_end=1)
        if not env["ok"] and env["code"] not in ("DECODE_ERROR",):
            problems.append(f"UNREADABLE_ROUTE:{route.uri}:{env['code']}")

    per_domain = Counter(r.domain for r in routes)
    return {"problems": problems, "routes": len(routes), "domains": len(domains),
            "per_domain": dict(sorted(per_domain.items())),
            "table": [r.public() | {"path": r.path, "base": r.base} for r in sorted(routes, key=lambda r: r.uri)]}


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--summary", action="store_true")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args(argv)
    result = collect()
    if args.json:
        args.json.write_text(json.dumps(result["table"], ensure_ascii=False, indent=1), encoding="utf-8")
    if args.summary or not (args.check or args.json):
        for dom, n in result["per_domain"].items():
            print(f"{n:6d}  {dom}")
        print(f"{result['routes']:6d}  routes in {result['domains']} domains")
    for p in result["problems"]:
        print(f"PROBLEM {p}")
    if args.check:
        print("route registry: " + ("OK" if not result["problems"] else f"{len(result['problems'])} problem(s)"))
        return 1 if result["problems"] else 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
