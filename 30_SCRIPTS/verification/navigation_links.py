"""Every path an agent is told to follow in the navigation files must exist.

Agents navigate the vault from a small set of files: the entry contracts (AGENTS.md, CLAUDE.md, GEMINI.md,
README*.md), the governance protocols/policies/agents/commands, the agent definitions and rules, the Claude Code
config and 04_CONFIG notes. A dead path there sends every agent that reads it to a file that is not there.

Checked, outside fenced code blocks:
    md     [text](relative/path.md)          resolved from the file's folder
    wiki   [[Note name]] / [[Note|alias]]    resolved like the vault's own resolver (retrieval/vault_index.py):
                                             note title, or file name with `_`/`-` read as spaces, or path
    tick   `00_GOVERNANCE/x.md`              a repo-rooted path in inline code (first segment NN_Name, .agents,
                                             .claude or .github)
A target is fine when it is a tracked file, a folder holding tracked files, or a path .gitignore keeps local on
purpose (06_INBOX/*, settings.local.json ...). Links that leave the repository (GitHub-relative ../../actions)
and placeholders (<x>, {x}, *, $VAR, YYYY) are skipped. Anything else must be fixed or listed, with a reason,
in 20_TESTS/fixtures/navigation_links_allowlist.json; an entry that no longer matches fails as stale.

Not checked: imported skills under .agents/skills (they cite paths of the projects they were written for),
reports, archives, and anchors inside a file (#heading).

Usage:
    python 30_SCRIPTS/verification/navigation_links.py            # exit 1 on a problem
    python 30_SCRIPTS/verification/navigation_links.py --json
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath
from typing import Callable, Dict, Iterable, List, Optional, Set, Tuple
from urllib.parse import unquote

REPO = Path(__file__).resolve().parents[2]
ALLOWLIST = REPO / "20_TESTS" / "fixtures" / "navigation_links_allowlist.json"

SCOPE = re.compile(
    r"^(AGENTS|CLAUDE|GEMINI|README(\.en)?)\.md$"
    r"|^00_GOVERNANCE/(protocols|policies|agents|commands)/.+\.md$"
    r"|^\.agents/(agents|rules)/.+\.md$"
    r"|^\.claude/(agents|skills)/.+\.md$"
    r"|^04_CONFIG/.+\.md$")

FENCE = re.compile(r"^\s*(```|~~~)")
INLINE_CODE = re.compile(r"`[^`\n]*`")
MD_LINK = re.compile(r"(?<!!)\[[^\]\n]*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
WIKI = re.compile(r"(?<!!)\[\[([^\]\n|#^]+)(?:[#^][^\]\n|]*)?(?:\|[^\]\n]*)?\]\]")
TICK = re.compile(r"`((?:\d\d_[A-Za-z][A-Za-z0-9_.\-]*|\.agents|\.claude|\.github)(?:/[A-Za-z0-9_.\-]+)*/?)`")
PLACEHOLDER = re.compile(r"[<>{}*$]|YYYY|\.\.\.")
SCHEME = re.compile(r"^[a-z][a-z0-9+.\-]*:", re.I)


class Index:
    """What exists in the repository, from git (CI sees exactly this)."""

    def __init__(self, files: Iterable[str], titles: Optional[Callable[[], Set[str]]] = None):
        self.files = sorted(files)
        self.lower: Set[str] = {f.lower() for f in self.files}
        self.dirs: Set[str] = set()
        self.names: Set[str] = set()
        self._titles = titles
        self._title_set: Optional[Set[str]] = None
        for f in self.files:
            p = PurePosixPath(f)
            stem = p.stem.strip().lower()
            # the same keys as retrieval/vault_index.VaultIndex.by_slug
            self.names.update({p.name.lower(), stem, stem.replace("_", " "), stem.replace("-", " ")})
            parent = p.parent
            while str(parent) != ".":
                self.dirs.add(str(parent).lower())
                parent = parent.parent

    def exists(self, rel: str) -> bool:
        rel = rel.strip("/").lower()
        return bool(rel) and (rel in self.lower or rel in self.dirs)

    def wiki_exists(self, name: str) -> bool:
        """Resolved like VaultIndex.resolve (title, then file name with `_`/`-` read as spaces), or by path."""
        key = name.strip().lower()
        if key in self.names or self.exists(key) or self.exists(key + ".md"):
            return True
        if self._titles is not None:
            if self._title_set is None:
                self._title_set = self._titles()
            return key in self._title_set
        return False


def _resolve(base_dir: str, target: str) -> Optional[str]:
    parts: List[str] = []
    for seg in (PurePosixPath(base_dir) / target).parts:
        if seg == "..":
            if not parts:
                return None  # leaves the repository (e.g. GitHub-relative ../../actions)
            parts.pop()
        elif seg not in (".", ""):
            parts.append(seg)
    return "/".join(parts)


def references(path: str, text: str) -> List[Tuple[int, str, str, Optional[str]]]:
    """(line, kind, raw target, repo-relative candidate or None when it cannot be resolved inside the repo)."""
    out = []
    base = str(PurePosixPath(path).parent)
    in_fence = False
    for n, line in enumerate(text.splitlines(), 1):
        if FENCE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for m in TICK.finditer(line):
            if not PLACEHOLDER.search(m.group(1)):
                out.append((n, "tick", m.group(1), m.group(1)))
        prose = INLINE_CODE.sub("", line)
        for m in MD_LINK.finditer(prose):
            raw = m.group(1)
            if SCHEME.match(raw) or raw.startswith("#") or PLACEHOLDER.search(raw):
                continue
            target = unquote(raw.split("#", 1)[0].split("?", 1)[0])
            if not target:
                continue
            rel = target.lstrip("/") if target.startswith("/") else _resolve(base, target)
            if rel is None:
                continue
            out.append((n, "md", raw, rel))
        for m in WIKI.finditer(prose):
            name = m.group(1).strip()
            if name and not PLACEHOLDER.search(name):
                out.append((n, "wiki", name, None))
    return out


def tracked_files(repo: Path) -> List[str]:
    out = subprocess.run(["git", "-c", "core.quotepath=off", "ls-files", "-z"], cwd=repo,
                         capture_output=True, check=True, timeout=120)
    return [p for p in out.stdout.decode("utf-8").split("\0") if p]


def ignored(repo: Path, paths: Iterable[str]) -> Set[str]:
    """Paths .gitignore keeps local on purpose (checked without needing the file to exist)."""
    paths = sorted(set(paths))
    if not paths:
        return set()
    # a folder rule (`/04_MEMORY/`) only matches something inside the folder: probe a child as well
    probe = "__navigation_links_probe__"
    queries = [q for p in paths for q in (p, f"{p}/{probe}")]
    res = subprocess.run(["git", "check-ignore", "--no-index", "--stdin", "-z"], cwd=repo,
                         input="\0".join(queries).encode("utf-8"), capture_output=True, timeout=120)
    hits = {p for p in res.stdout.decode("utf-8").split("\0") if p}
    return {p for p in paths if p in hits or f"{p}/{probe}" in hits}


_FRONTMATTER = re.compile(r"\A---\s*\n(.*?)\n---\s*\n", re.S)
_TITLE_LINE = re.compile(r"^title:\s*[\"']?(.+?)[\"']?\s*$", re.M)
_HEADING = re.compile(r"^#\s+(.+)$", re.M)


def note_title(text: str) -> Optional[str]:
    """The title VaultIndex gives a note: frontmatter `title`, else the first `# heading`."""
    body = text
    m = _FRONTMATTER.match(text)
    if m:
        t = _TITLE_LINE.search(m.group(1))
        if t and t.group(1).strip():
            return t.group(1).strip()
        body = text[m.end():]
    h = _HEADING.search(body)
    return h.group(1).strip() if h else None


def find_broken(repo: Path, files: List[str], read=None) -> List[Dict[str, object]]:
    read = read or (lambda rel: (repo / rel).read_text(encoding="utf-8", errors="replace"))

    def titles() -> Set[str]:
        out = set()
        for rel in files:
            if rel.endswith(".md"):
                try:
                    t = note_title(read(rel))
                except OSError:
                    continue
                if t:
                    out.add(t.lower())
        return out

    index = Index(files, titles)
    candidates = []
    for rel in files:
        if not SCOPE.search(rel):
            continue
        for line, kind, raw, target in references(rel, read(rel)):
            if kind == "wiki":
                if not index.wiki_exists(raw):
                    candidates.append({"file": rel, "line": line, "kind": kind, "target": raw, "check": None})
            elif not index.exists(target):
                candidates.append({"file": rel, "line": line, "kind": kind, "target": raw, "check": target})
    local_only = ignored(repo, [c["check"].rstrip("/") for c in candidates if c["check"]])
    broken = [c for c in candidates if not (c["check"] and c["check"].rstrip("/") in local_only)]
    for c in broken:
        c.pop("check")
    return broken


def apply_allowlist(broken: List[Dict[str, object]], allowlist: dict) -> Tuple[List[str], List[str]]:
    """(problems, stale): unlisted dead links and allowlist entries that no longer match anything."""
    entries = allowlist.get("entries", [])
    used = [False] * len(entries)
    problems = []
    for i, e in enumerate(entries):
        if len(str(e.get("reason", "")).strip()) < 15:
            problems.append(f"ALLOWLIST_NO_REASON:{e.get('file')}:{e.get('target')}")
    for b in broken:
        hit = next((i for i, e in enumerate(entries)
                    if e.get("file") == b["file"] and e.get("target") == b["target"]), None)
        if hit is None:
            problems.append(f"BROKEN_LINK:{b['file']}:{b['line']}:{b['kind']}:{b['target']}")
        else:
            used[hit] = True
    stale = [f"ALLOWLIST_STALE:{e.get('file')}:{e.get('target')}" for e, u in zip(entries, used) if not u]
    return problems, stale


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)
    broken = find_broken(REPO, tracked_files(REPO))
    allowlist = json.loads(ALLOWLIST.read_text(encoding="utf-8")) if ALLOWLIST.exists() else {"entries": []}
    problems, stale = apply_allowlist(broken, allowlist)
    if args.json:
        print(json.dumps({"broken": broken, "problems": problems + stale}, indent=1, ensure_ascii=False))
    else:
        for p in problems + stale:
            print(p)
        print("navigation links: OK" if not problems + stale else f"navigation links: {len(problems + stale)} problem(s)")
    return 1 if problems + stale else 0


if __name__ == "__main__":
    raise SystemExit(main())
