#!/usr/bin/env python3
"""Compact map of a git repository, so an agent opens files by path instead of exploring.

Usage: python3 repo_map.py [<dir>] [--max-chars 4000] [--no-cache]

Reads only `git ls-files` and file sizes (never file contents, except the first line of a
directory's README for its one-line description). Output is Markdown capped at --max-chars
(about 1k tokens at the default), cached under ~/.claude/cache/repo-map/ by repository path
and HEAD commit, so a session start in a large repository costs one cache read.
Stdlib only; no network. Prints nothing and exits 0 outside a git repository.
"""
from __future__ import annotations

import argparse
import hashlib
import os
import subprocess
import sys
from collections import Counter
from pathlib import Path

HEAVY_FILES = 300          # a directory with more tracked files than this is never scanned whole
HEAVY_MB = 50              # ... or more megabytes than this
MAX_SUBDIRS = 6
ROOT_FILES_SHOWN = 14
TEST_HINTS = (             # (marker file at the root, command shown)
    ("pytest.ini", "pytest"), ("pyproject.toml", "pytest"), ("setup.cfg", "pytest"),
    ("package.json", "npm test"), ("Cargo.toml", "cargo test"), ("go.mod", "go test ./..."),
    ("Makefile", "make test"), ("pom.xml", "mvn test"), ("build.gradle", "gradle test"),
)
RULES = ("Open files by the paths below; search with a narrow grep/glob inside one directory; "
         "read excerpts (offset/limit), not whole large files; never list or read a ⚠ directory wholesale. "
         "Delegate broad inventories to an Explore subagent and keep only its conclusion.")


def _git(root: Path, *args: str) -> str:
    r = subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True, timeout=20,
                       encoding="utf-8", errors="replace")
    if r.returncode != 0:
        raise RuntimeError(r.stderr.strip())
    return r.stdout


def repo_root(start: Path) -> Path | None:
    try:
        return Path(_git(start, "rev-parse", "--show-toplevel").strip())
    except Exception:
        return None


def _readme_line(path: Path) -> str:
    for name in ("README.md", "README.en.md", "README.txt", "README"):
        f = path / name
        if not f.is_file():
            continue
        try:
            lines = f.read_text(encoding="utf-8", errors="replace").splitlines()[:40]
        except OSError:
            return ""
        in_fm = bool(lines) and lines[0].strip() == "---"
        for i, line in enumerate(lines):
            s = line.strip()
            if in_fm:
                if i and s == "---":
                    in_fm = False
                continue
            s = s.lstrip("#>*- ").strip()
            if s and not s.startswith(("<!--", "[!", "![", "```")):
                return s[:90]
    return ""


def _ext(path: str) -> str:
    name = path.rsplit("/", 1)[-1]
    return name.rsplit(".", 1)[-1].lower() if "." in name.lstrip(".") else name


def build(root: Path, max_chars: int = 4000, listing: str | None = None) -> str:
    listing = _git(root, "ls-files", "-z") if listing is None else listing
    files = [p for p in listing.split("\0") if p]
    try:
        head = _git(root, "rev-parse", "--short", "HEAD").strip()
    except Exception:
        head = "no commits"
    by_top: dict[str, list[str]] = {}
    root_files: list[str] = []
    for p in files:
        top, sep, _ = p.partition("/")
        (by_top.setdefault(top, []) if sep else root_files).append(p)

    def size_mb(paths: list[str]) -> float:
        total = 0
        for p in paths:
            try:
                total += (root / p).stat().st_size
            except OSError:
                pass
        return total / 1_048_576

    out = [f"# Repo map: {root.name} (auto, HEAD {head}, {len(files)} tracked files)", RULES]
    shown = sorted(root_files, key=lambda p: (not p.upper().startswith(("README", "CLAUDE", "AGENTS")), p.startswith("."), p))
    if shown:
        extra = f" (+{len(shown) - ROOT_FILES_SHOWN})" if len(shown) > ROOT_FILES_SHOWN else ""
        out.append("Root files: " + ", ".join(shown[:ROOT_FILES_SHOWN]) + extra)
    tests = []
    for marker, cmd in TEST_HINTS:
        if marker in root_files and cmd not in tests:
            tests.append(cmd)
    if tests:
        out.append("Tests: " + " | ".join(tests) + " (run only the changed area's tests until the final check)")
    out.append("Directories (files, size, main types; ⚠ = heavy, never scan whole):")
    lines = []
    for top, paths in sorted(by_top.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        mb = size_mb(paths)
        heavy = len(paths) > HEAVY_FILES or mb > HEAVY_MB
        types = ", ".join(f".{e} {n}" for e, n in Counter(_ext(p) for p in paths).most_common(3))
        subs = Counter(p.split("/")[1] for p in paths if p.count("/") >= 2)
        sub_txt = ", ".join(f"{s}/ {n}" for s, n in subs.most_common(MAX_SUBDIRS))
        if len(subs) > MAX_SUBDIRS:
            sub_txt += f" (+{len(subs) - MAX_SUBDIRS})"
        desc = _readme_line(root / top)
        line = f"- {'⚠ ' if heavy else ''}{top}/ {len(paths)} files, {mb:.0f} MB ({types})"
        if desc:
            line += f" — {desc}"
        if sub_txt:
            line += f"\n  sub: {sub_txt}"
        lines.append(line)
    body = "\n".join(out)
    for i, line in enumerate(lines):
        if len(body) + len(line) + 80 > max_chars:
            body += f"\n- … {len(lines) - i} smaller directories omitted (list one with `git ls-files <dir>`)"
            break
        body += "\n" + line
    return body[:max_chars]


def cache_path(root: Path, max_chars: int, state: str) -> Path:
    """One entry per (repository, max_chars); `state` = HEAD + tracked-file listing, so a commit,
    a staged add/remove or a first commit all rebuild the map."""
    key = hashlib.sha256(str(root.resolve()).encode("utf-8")).hexdigest()[:16]
    digest = hashlib.sha256(state.encode("utf-8", "replace")).hexdigest()[:16]
    return Path.home() / ".claude" / "cache" / "repo-map" / f"{key}-{max_chars}-{digest}.md"


def get_map(start: Path, max_chars: int = 4000, use_cache: bool = True) -> str:
    root = repo_root(start)
    if root is None:
        return ""
    try:
        head = _git(root, "rev-parse", "HEAD").strip()
    except Exception:
        head = "nohead"
    listing = _git(root, "ls-files", "-z")
    cache = cache_path(root, max_chars, head + "\0" + listing)
    if use_cache and cache.is_file():
        return cache.read_text(encoding="utf-8")
    text = build(root, max_chars, listing)
    if use_cache:
        try:
            cache.parent.mkdir(parents=True, exist_ok=True)
            prefix = cache.name.rsplit("-", 1)[0]
            for old in cache.parent.glob(prefix + "-*.md"):
                old.unlink()
            tmp = cache.with_name(f"{cache.name}.{os.getpid()}.tmp")
            tmp.write_text(text, encoding="utf-8")
            os.replace(tmp, cache)  # a concurrent session never reads a half-written map
        except OSError:
            pass
    return text


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dir", nargs="?", default=".")
    ap.add_argument("--max-chars", type=int, default=4000)
    ap.add_argument("--no-cache", action="store_true")
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    text = get_map(Path(a.dir), a.max_chars, not a.no_cache)
    if text:
        sys.stdout.write(text + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
