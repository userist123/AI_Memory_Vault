"""Drift check for code vendored from AI Website Studio (THREAT_MODEL.md review round 2, N6).

Exit 0 only when every file listed in VENDORED.json matches its recorded local hash, every unpatched file is
byte-identical to upstream, every patched file says why, and no unlisted file sits in a vendored tree.

    python scripts/check_vendored.py [--root DIR] [--upstream-repo AWS_GIT_DIR]

--upstream-repo additionally re-derives upstream_sha256 from `git show <commit>:<upstream_path>` (local use;
CI has no access to the upstream repository).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = "VENDORED.json"
# trees that may contain only listed vendored files or listed AAS-owned files
GUARDED_TREES = ("core/studio_core", "tests/vendored")
IGNORED_PARTS = {"__pycache__", ".pytest_cache"}
REQUIRED = ("path", "upstream_path", "upstream_commit", "upstream_sha256", "local_sha256", "patched", "patch_reason")


def sha256_file(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def check(root: Path, upstream_repo: Path | None = None) -> list[str]:
    errors: list[str] = []
    try:
        manifest = json.loads((root / MANIFEST).read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        return [f"{MANIFEST}: unreadable ({e})"]
    commit = manifest.get("upstream_commit")
    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        return [f"{MANIFEST}: 'files' must be a non-empty list"]
    listed: set[str] = set()
    for i, entry in enumerate(files):
        if not isinstance(entry, dict) or any(k not in entry for k in REQUIRED):
            errors.append(f"files[{i}]: missing keys (need {', '.join(REQUIRED)})")
            continue
        rel = entry["path"]
        if rel in listed:
            errors.append(f"{rel}: listed twice")
        listed.add(rel)
        if entry["upstream_commit"] != commit:
            errors.append(f"{rel}: upstream_commit {entry['upstream_commit']!r} != manifest {commit!r}")
        p = root / rel
        if not p.is_file():
            errors.append(f"{rel}: listed but missing")
            continue
        local = sha256_file(p)
        if local != entry["local_sha256"]:
            errors.append(f"{rel}: unlisted change (sha256 {local} != recorded local_sha256)")
        if entry["patched"] is True:
            if not isinstance(entry["patch_reason"], str) or not entry["patch_reason"].strip():
                errors.append(f"{rel}: patched without patch_reason")
            if entry["local_sha256"] == entry["upstream_sha256"]:
                errors.append(f"{rel}: marked patched but identical to upstream")
        elif entry["patched"] is False:
            if entry["local_sha256"] != entry["upstream_sha256"]:
                errors.append(f"{rel}: differs from upstream but not marked patched")
            if entry["patch_reason"]:
                errors.append(f"{rel}: patch_reason set on an unpatched file")
        else:
            errors.append(f"{rel}: 'patched' must be true or false")
        if upstream_repo is not None:
            r = subprocess.run(["git", "-C", str(upstream_repo), "show", f"{commit}:{entry['upstream_path']}"],  # noqa: S603,S607
                               capture_output=True, check=False)
            if r.returncode != 0:
                errors.append(f"{rel}: upstream {entry['upstream_path']} not found at {commit}")
            elif hashlib.sha256(r.stdout).hexdigest() != entry["upstream_sha256"]:
                errors.append(f"{rel}: upstream_sha256 does not match {commit}:{entry['upstream_path']}")
    local_files = manifest.get("local_files", [])
    if not isinstance(local_files, list):
        errors.append(f"{MANIFEST}: 'local_files' must be a list")
        local_files = []
    owned = {e.get("path") for e in local_files if isinstance(e, dict)}
    for e in local_files:
        if not isinstance(e, dict) or not e.get("path") or not str(e.get("reason", "")).strip():
            errors.append(f"local_files: entry needs path and reason: {e!r}")
    for tree in GUARDED_TREES:
        base = root / tree
        if not base.is_dir():
            continue
        for p in sorted(base.rglob("*")):
            if not p.is_file() or IGNORED_PARTS & set(p.relative_to(root).parts):
                continue
            rel = p.relative_to(root).as_posix()
            if rel not in listed and rel not in owned:
                errors.append(f"{rel}: unlisted file in vendored tree {tree}")
    for rel in sorted(owned - {None}):
        if not (root / str(rel)).is_file():
            errors.append(f"{rel}: listed in local_files but missing")
    if not isinstance(manifest.get("dropped_tests"), list):
        errors.append(f"{MANIFEST}: 'dropped_tests' must be a list")
    else:
        for d in manifest["dropped_tests"]:
            if not isinstance(d, dict) or not d.get("upstream_path") or not str(d.get("reason", "")).strip():
                errors.append(f"dropped_tests: entry needs upstream_path and reason: {d!r}")
    return errors


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", type=Path, default=ROOT)
    ap.add_argument("--upstream-repo", type=Path, default=None)
    args = ap.parse_args(argv)
    errors = check(args.root.resolve(), args.upstream_repo)
    for e in errors:
        print(f"VENDORED: {e}", file=sys.stderr)
    if errors:
        print(f"check_vendored: FAIL ({len(errors)} problem(s))", file=sys.stderr)
        return 1
    print("check_vendored: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
