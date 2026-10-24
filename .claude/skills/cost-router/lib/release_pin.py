#!/usr/bin/env python3
"""Pinned release manifest for the cost-router self-update (HIGH-2). Stdlib only.

A pin is `release_pin.json`: {"schema": 1, "seq": N, "commit": "<40 hex>", "files": {"<repo-relative posix path>":
"<sha256 of the git blob bytes>"}, "files_sha256": "<sha256 of the sorted files map>"}. `files` lists every blob under
SPARSE at `commit` except the pin file itself (it is committed after the release it pins). The trusted pin is the
INSTALLED copy; the network is only ever asked for the one commit the pin names, and every byte is checked against
the pin before anything is installed. Fetched code is never executed before verification; the pinned code runs
later as the installed hooks.

    python release_pin.py generate --repo <dir> --commit <sha> --seq <n> [--out <file>]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

SPARSE = (  # no leading "/": Git Bash would rewrite it into a Windows path
    ".claude/skills/cost-router",
    ".claude/agents",
    "03_IMPLEMENTATION/packages/routing/claude_model_router.py",
    "04_CONFIG/claude_model_routing.json",
)
PIN_NAME = "release_pin.json"
PIN_REPO_PATH = ".claude/skills/cost-router/" + PIN_NAME
SCHEMA = 1
_HEX40 = re.compile(r"[0-9a-f]{40}")
_HEX64 = re.compile(r"[0-9a-f]{64}")
_KEYS = {"schema", "seq", "commit", "files", "files_sha256"}


class PinError(Exception):
    """The pin or the fetched tree failed validation (never install)."""


class FetchError(PinError):
    """git could not deliver the pinned commit (offline, unknown sha, timeout)."""


def safe_rel(p) -> str:
    """Return `p` if it is a clean repo-relative posix path equal to or under SPARSE (and not the pin file)."""
    if not isinstance(p, str) or not p:
        raise PinError(f"bad path {p!r}")
    if any(c in p for c in ("\\", ":", "\x00")) or p.startswith("/"):
        raise PinError(f"bad path {p!r}")
    for part in p.split("/"):
        if part in ("", ".", "..") or part.endswith((".", " ")):  # also Windows-trimmed names
            raise PinError(f"bad path component in {p!r}")
    if p == PIN_REPO_PATH:
        raise PinError("the pin file cannot list itself")
    if not any(p == s or p.startswith(s + "/") for s in SPARSE):
        raise PinError(f"path outside the release set: {p!r}")
    return p


def files_digest(files: dict) -> str:
    return hashlib.sha256(json.dumps(dict(sorted(files.items())), sort_keys=True).encode("utf-8")).hexdigest()


def validate(data) -> dict:
    if not isinstance(data, dict) or set(data) != _KEYS:
        raise PinError("pin must be an object with exactly schema, seq, commit, files, files_sha256")
    if data["schema"] != SCHEMA or isinstance(data["schema"], bool):
        raise PinError("unsupported schema")
    seq = data["seq"]
    if not isinstance(seq, int) or isinstance(seq, bool) or seq < 1:
        raise PinError("seq must be an integer >= 1")
    if not isinstance(data["commit"], str) or not _HEX40.fullmatch(data["commit"]):
        raise PinError("commit must be 40 lowercase hex")
    files = data["files"]
    if not isinstance(files, dict) or not files:
        raise PinError("files must be a non-empty object")
    for rel, sha in files.items():
        safe_rel(rel)
        if not isinstance(sha, str) or not _HEX64.fullmatch(sha):
            raise PinError(f"bad sha256 for {rel!r}")
    if data["files_sha256"] != files_digest(files):
        raise PinError("files_sha256 does not match files")
    return data


def parse_pin(raw: bytes) -> dict:
    """Validate pin BYTES (read once by the caller, who then uses exactly these bytes)."""
    try:
        data = json.loads(raw.decode("utf-8"))
    except (AttributeError, ValueError) as exc:  # UnicodeDecodeError and JSONDecodeError are ValueErrors
        raise PinError(f"cannot read pin: {type(exc).__name__}") from exc
    return validate(data)


def load_pin(path) -> dict:
    try:
        raw = Path(path).read_bytes()
    except OSError as exc:
        raise PinError(f"cannot read pin: {type(exc).__name__}") from exc
    return parse_pin(raw)


# ---- git plumbing ------------------------------------------------------------------------------------------------

def _run(args: list[str], timeout: float, cwd: Path | None = None, lazy_fetch: bool = True) -> bytes:
    env = dict(os.environ, GIT_TERMINAL_PROMPT="0")
    if not lazy_fetch:
        env["GIT_NO_LAZY_FETCH"] = "1"  # never an implicit promisor fetch while reading (older git ignores it)
    try:
        r = subprocess.run(["git", "-c", "protocol.ext.allow=never", *args], cwd=cwd, capture_output=True,
                           timeout=max(1.0, timeout), stdin=subprocess.DEVNULL, env=env)
    except subprocess.TimeoutExpired as exc:
        raise FetchError("git timed out") from exc
    except OSError as exc:
        raise FetchError("git not runnable") from exc
    if r.returncode:
        raise FetchError("git " + args[0] + " failed: " + r.stderr.decode("utf-8", "replace").strip()[:200])
    return r.stdout


def _read_blob(repo: Path, oid: str, timeout: float) -> bytes:
    """Exact blob bytes (cat-file applies no eol or smudge conversion)."""
    return _run(["-C", str(repo), "cat-file", "blob", oid], timeout, lazy_fetch=False)


def _list_blobs(repo: Path, commit: str, timeout: float) -> dict[str, str]:
    """rel path -> blob oid for everything under SPARSE at `commit`, minus the pin file. Refuses symlinks, gitlinks
    and any other non-regular entry or unsafe path."""
    out = _run(["-C", str(repo), "ls-tree", "-r", "-z", "--full-tree", commit, "--", *SPARSE], timeout)
    blobs: dict[str, str] = {}
    for rec in out.split(b"\x00"):
        if not rec:
            continue
        try:
            meta, raw = rec.split(b"\t", 1)
            mode, kind, oid = meta.decode("ascii").split(" ")
            rel = raw.decode("utf-8")
        except ValueError as exc:
            raise PinError("unparseable ls-tree entry") from exc
        if rel == PIN_REPO_PATH:
            continue
        if mode not in ("100644", "100755") or kind != "blob":
            raise PinError(f"non-regular entry refused ({mode} {kind}): {rel!r}")
        safe_rel(rel)
        if not re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", oid):
            raise PinError("bad object id")
        blobs[rel] = oid
    if not blobs:
        raise PinError("no release files at the commit")
    return blobs


def generate(repo, commit: str, seq: int) -> dict:
    """Build the pin for `commit` of the local repo `repo` from its blobs."""
    repo = Path(repo)
    full = _run(["-C", str(repo), "rev-parse", "--verify", f"{commit}^{{commit}}"], 10).decode().strip()
    if not _HEX40.fullmatch(full):
        raise PinError("commit did not resolve to a 40-hex id")
    files = {rel: hashlib.sha256(_read_blob(repo, oid, 20)).hexdigest()
             for rel, oid in sorted(_list_blobs(repo, full, 20).items())}
    pin = {"schema": SCHEMA, "seq": seq, "commit": full, "files": files, "files_sha256": files_digest(files)}
    return validate(pin)


def fetch_verified(url: str, pin: dict, dest, timeout_budget: float = 30.0) -> None:
    """Fetch exactly pin['commit'] from `url`, write its release files under the new/empty dir `dest`, and check
    them against the pin. Returns only when everything matches; raises FetchError (git side) or PinError."""
    validate(pin)
    dest = Path(dest)
    if dest.exists() and (not dest.is_dir() or any(dest.iterdir())):
        raise PinError("destination must be new or empty")
    deadline = time.monotonic() + timeout_budget

    def left() -> float:
        return deadline - time.monotonic()

    commit = pin["commit"]
    tmp = Path(tempfile.mkdtemp(prefix="cost-router-pin-"))
    try:
        repo = tmp / "g"
        _run(["init", "-q", str(repo)], min(5, left()))
        _run(["-C", str(repo), "remote", "add", "origin", url], min(5, left()))
        # commit + trees only (blob:none): a plain depth-1 fetch would download the whole repository tree
        _run(["-C", str(repo), "-c", "core.autocrlf=false", "fetch", "-q", "--depth", "1", "--no-tags",
              "--filter=blob:none", "origin", commit], min(15, left()))
        got = _run(["-C", str(repo), "rev-parse", "FETCH_HEAD^{commit}"], min(5, left())).decode().strip()
        if got != commit:
            raise PinError("fetched commit id differs from the pin")
        blobs = _list_blobs(repo, commit, min(5, left()))
        oids = sorted(set(blobs.values()))  # exactly the release blobs, nothing else is requested
        for i in range(0, len(oids), 100):
            _run(["-C", str(repo), "-c", "core.autocrlf=false", "fetch", "-q", "--no-tags", "--filter=blob:none",
                  "origin", *oids[i:i + 100]], min(15, left()))
        dest.mkdir(parents=True, exist_ok=True)
        base = dest.resolve()
        for rel, oid in blobs.items():
            data = _read_blob(repo, oid, min(10, left()))
            target = (base / rel).resolve()
            if base not in target.parents:
                raise PinError(f"refusing to write outside the staging dir: {rel!r}")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        _check_dir(base, pin["files"])
    finally:
        if sys.version_info >= (3, 12):
            shutil.rmtree(tmp, onexc=_rm_readonly)
        else:
            shutil.rmtree(tmp, onerror=_rm_readonly)


def _rm_readonly(func, path, _exc):
    os.chmod(path, 0o700)  # git marks pack files read-only; Windows refuses to delete them otherwise
    func(path)


def _check_dir(base: Path, files: dict) -> None:
    """The bytes ON DISK under `base` are exactly the pinned set: nothing missing, nothing extra, nothing linked."""
    found: dict[str, str] = {}
    for dirpath, dirnames, filenames in os.walk(base):
        for name in (*dirnames, *filenames):
            if (Path(dirpath) / name).is_symlink():
                raise PinError("symlink in the staging tree")
        for name in filenames:
            p = Path(dirpath) / name
            found[p.relative_to(base).as_posix()] = hashlib.sha256(p.read_bytes()).hexdigest()
    if set(found) != set(files):
        raise PinError("file set differs from the pin")
    for rel, sha in files.items():
        if found[rel] != sha:
            raise PinError(f"hash mismatch: {rel}")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Generate the cost-router release pin from a local repository.")
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("generate")
    g.add_argument("--repo", required=True)
    g.add_argument("--commit", required=True)
    g.add_argument("--seq", type=int, required=True)
    g.add_argument("--out")
    a = ap.parse_args(argv)
    try:
        pin = generate(a.repo, a.commit, a.seq)
    except PinError as exc:
        print(f"not generated: {exc}", file=sys.stderr)
        return 1
    out = Path(a.out) if a.out else Path(a.repo) / PIN_REPO_PATH
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(pin, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"{out} (seq {pin['seq']}, commit {pin['commit']}, {len(pin['files'])} files)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
