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
  * the routes cannot all be read back through VaultAccess as the owner (sample of 1 per domain);
  * a tracked .md note has no route and is neither listed under `unrouted` (with a reason), nor excluded
    by the domain whose root covers it, nor denylisted (NOTE_WITHOUT_ROUTE);
  * an `unrouted` entry matches no tracked note any more (UNROUTED_GLOB_UNUSED), lacks a reason, or the
    registry has an unknown top-level key (the router itself ignores those silently).
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from collections import Counter
from pathlib import Path
from typing import List, Optional

import yaml

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from vault_access.audit import AuditLog  # noqa: E402
from vault_access.core import VaultAccess  # noqa: E402
from vault_access.policy import AccessPolicy  # noqa: E402
from vault_access.router import DomainRouter  # noqa: E402
from vault_access.canonical import glob_match  # noqa: E402

EXEMPT_ROOTS = {"20_TESTS"}
TOP_LEVEL_KEYS = {"version", "defaults", "domains", "unrouted"}
UNROUTED_KEYS = {"glob", "reason"}
MIN_REASON_CHARS = 15


def _under_root(path: str, root: str) -> bool:
    if any(c in root for c in "*?["):
        return glob_match(path, root)
    # case-insensitive, like glob_match (canonical.glob_regex uses re.IGNORECASE)
    path, root = path.casefold(), root.rstrip("/").casefold()
    return path == root or path.startswith(root + "/")


def validate_unrouted(raw_registry: dict) -> tuple:
    """Return (entries, problems) for the top-level keys and the `unrouted` list of vault_domains.yaml."""
    problems: List[str] = []
    unknown = set(raw_registry) - TOP_LEVEL_KEYS
    if unknown:
        problems.append(f"TOP_LEVEL_UNKNOWN_KEYS:{sorted(unknown)}")
    entries = []
    raw = raw_registry.get("unrouted") or []
    if not isinstance(raw, list):
        return [], problems + ["UNROUTED_NOT_A_LIST"]
    for i, item in enumerate(raw):
        if not isinstance(item, dict):
            problems.append(f"UNROUTED_BAD_ENTRY:{i}")
            continue
        bad_keys = set(item) - UNROUTED_KEYS
        glob, reason = item.get("glob"), item.get("reason")
        if bad_keys:
            problems.append(f"UNROUTED_UNKNOWN_KEYS:{i}:{sorted(bad_keys)}")
        if not isinstance(glob, str) or not glob.strip() or glob.startswith("/") or ".." in glob.split("/"):
            problems.append(f"UNROUTED_BAD_GLOB:{i}:{glob!r}")
            continue
        if not isinstance(reason, str) or len(reason.strip()) < MIN_REASON_CHARS:
            problems.append(f"UNROUTED_NO_REASON:{glob}")
            continue
        entries.append({"glob": glob, "reason": reason.strip()})
    return entries, problems


def coverage_problems(tracked_md, routed, unrouted, domain_specs, defaults_exclude, denied) -> List[str]:
    """Every tracked .md note must be routed, explicitly unrouted with a reason, excluded by the domain that
    covers it, or denylisted. Anything else is a note no agent can reach by route: NOTE_WITHOUT_ROUTE.

    domain_specs: iterable of (roots, exclude) for repo-based domains; denied: callable(path) -> bool.
    """
    problems: List[str] = []
    used = {u["glob"]: 0 for u in unrouted}
    for path in sorted(tracked_md):
        if path in routed:
            continue
        hit = next((u["glob"] for u in unrouted if glob_match(path, u["glob"])), None)
        if hit is not None:
            used[hit] += 1
            continue
        excluded = any(_under_root(path, root) and any(glob_match(path, pat) for pat in tuple(defaults_exclude) + tuple(exclude))
                       for roots, exclude in domain_specs for root in roots)
        if excluded or denied(path):
            continue
        problems.append(f"NOTE_WITHOUT_ROUTE:{path}")
    problems.extend(f"UNROUTED_GLOB_UNUSED:{g}" for g, n in used.items() if n == 0)
    return problems


def tracked_markdown(repo: Path) -> Optional[List[tuple]]:
    """(mode, path) of every tracked .md; None when git is unavailable (the check then fails closed)."""
    try:
        out = subprocess.run(["git", "-c", "core.quotepath=off", "ls-files", "-s", "-z", "--", "*.md"], cwd=repo,
                             capture_output=True, check=True, timeout=120)
    except (OSError, subprocess.SubprocessError):
        return None
    entries = []
    for record in out.stdout.decode("utf-8").split("\0"):
        if record:
            meta, _, path = record.partition("\t")
            entries.append((meta.split(" ", 1)[0], path))
    return entries


def split_symlinks(entries) -> tuple:
    """(problems, paths): a tracked symlink (git mode 120000) is a note the router never reads, because
    canonical.contained_path refuses links; report it as TRACKED_SYMLINK instead of counting it as covered."""
    problems = [f"TRACKED_SYMLINK:{path}" for mode, path in entries if mode == "120000"]
    return problems, [path for mode, path in entries if mode != "120000"]


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

    # per-note coverage: every tracked .md is routed, explicitly unrouted (with a reason), excluded by the
    # domain that covers it, or denylisted; a note nobody can reach by route fails the check
    raw_registry = yaml.safe_load((repo / "04_CONFIG" / "vault_domains.yaml").read_text(encoding="utf-8")) or {}
    unrouted, unrouted_problems = validate_unrouted(raw_registry)
    problems.extend(unrouted_problems)
    tracked = tracked_markdown(repo)
    if tracked is None:
        problems.append("COVERAGE_UNCHECKED:git ls-files unavailable")
    else:
        link_problems, tracked = split_symlinks(tracked)
        problems.extend(link_problems)
        routed_paths = {r.path for r in routes if r.base == "repo"}
        specs = [(spec.roots, spec.exclude) for spec in domains.values() if spec.base == "repo"]
        problems.extend(coverage_problems(tracked, routed_paths, unrouted, specs,
                                          router.registry.defaults_exclude, policy.denied_path))

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
