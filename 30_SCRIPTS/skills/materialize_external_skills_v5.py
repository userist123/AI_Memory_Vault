#!/usr/bin/env python3
"""
AI Memory Vault - External Skill Scraper / Materializer v5

Scraper-first, API-free by default.

Purpose:
- Discover GitHub sources recorded under 01_KNOWLEDGE/EXTERNAL_SKILLS.
- Use GitHub web HTML and raw.githubusercontent.com instead of the GitHub REST API.
- Handle repo, tree and blob URLs.
- Discover complete skill/bundle directories, not just SKILL.md.
- Preserve all bundle files: docs, scripts, assets, references, templates,
  manifests, lock files, local helper modules and configuration.
- Track local dependency closure.
- Record external dependencies without installing/executing anything.
- Record provenance and hashes.
- Never execute downloaded code.

Modes:
    --inventory
    --discover
    --dry-run
    --materialize

Recommended sequence:
    python materialize_external_skills_v5.py --repo-root . --inventory
    python materialize_external_skills_v5.py --repo-root . --discover
    python materialize_external_skills_v5.py --repo-root . --dry-run
    python materialize_external_skills_v5.py --repo-root . --materialize

No GitHub token is required for the default scraper path.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import ssl
import sys
import time
from collections import defaultdict, deque
from dataclasses import dataclass, asdict, field
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath
from typing import Iterable
from urllib.error import HTTPError, URLError
from urllib.parse import quote, unquote, urljoin, urlparse
from urllib.request import Request, urlopen


TARGET = Path("01_KNOWLEDGE/EXTERNAL_SKILLS")
GITHUB_HOSTS = {"github.com", "www.github.com"}
RAW_HOST = "raw.githubusercontent.com"

KNOWN_COLLECTIONS = {
    "VoltAgent/awesome-agent-skills",
    "ComposioHQ/awesome-claude-skills",
    "sickn33/agentic-awesome-skills",
    "bergside/awesome-design-skills",
}

EXCLUDED_DIRS = {
    ".git", ".github", ".idea", ".vscode", "node_modules",
    "__pycache__", ".pytest_cache", "dist", "build", "coverage",
    ".venv", "venv", "vendor", "target", ".next", ".turbo",
}

TEXT_EXTENSIONS = {
    ".md", ".markdown", ".txt", ".json", ".yaml", ".yml", ".toml",
    ".ini", ".cfg", ".conf", ".xml", ".html", ".htm", ".css",
    ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx",
    ".py", ".pyw", ".rb", ".go", ".rs", ".java", ".kt",
    ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1",
    ".sql", ".jinja", ".j2", ".svg",
}

MANIFESTS = {
    "package.json", "package-lock.json", "npm-shrinkwrap.json",
    "pnpm-lock.yaml", "yarn.lock",
    "requirements.txt", "requirements-dev.txt",
    "pyproject.toml", "poetry.lock", "Pipfile", "Pipfile.lock",
    "environment.yml", "environment.yaml",
    "setup.py", "setup.cfg",
    "Cargo.toml", "Cargo.lock",
    "go.mod", "go.sum",
    "Gemfile", "Gemfile.lock",
    "composer.json", "composer.lock",
    "Dockerfile", "Makefile",
}

STRONG_MARKERS = {
    "SKILL.md", "skill.md", "skill.yaml", "skill.yml", "skill.json", "skill.toml",
    "PLUGIN.md", "plugin.yaml", "plugin.yml", "plugin.json",
    "agent.yaml", "agent.yml", "agent.json", "agent.toml",
    "AGENTS.md", "CLAUDE.md", "INSTRUCTIONS.md", "RULES.md",
    "mcp.json", "mcp.yaml", "mcp.yml",
}

CONVENTIONAL_CONTAINERS = {
    "skills", "skill", "agents", "agent", "plugins", "plugin",
    "commands", "tools", "tool", "instructions", "prompts",
}

SOURCE_URL_RE = re.compile(
    r'https?://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+'
    r'(?:/(?:tree|blob)/[^ \t\r\n<>\]\)"}]+)?'
)

LOCAL_REF_PATTERNS = [
    re.compile(r"""(?:from\s+|import\s+)[\"']([^\"']+)[\"']"""),
    re.compile(r"""require\(\s*[\"']([^\"']+)[\"']\s*\)"""),
    re.compile(r"""(?:source|include)\s+[\"']?([^\"' \t\r\n]+)""", re.I),
    re.compile(
        r"""(?:path|file|template|asset|script|reference|include)\s*[:=]\s*[\"']([^\"']+)[\"']""",
        re.I,
    ),
    re.compile(r"""[\"'(]((?:\./|\.\./)[^\"')\s]+)"""),
]


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def safe_name(value: str, fallback: str = "unnamed") -> str:
    value = re.sub(r"[^A-Za-z0-9._-]+", "-", value.strip()).strip("-")
    return value or fallback


def normalize_url(url: str) -> str:
    p = urlparse(url.strip().rstrip(".,;"))
    if p.netloc.lower() not in GITHUB_HOSTS:
        return url
    path = re.sub(r"/+", "/", p.path).rstrip("/")
    return f"https://github.com{path}"


def parse_github_url(url: str):
    p = urlparse(url)
    parts = [unquote(x) for x in p.path.split("/") if x]
    if p.netloc.lower() not in GITHUB_HOSTS or len(parts) < 2:
        raise ValueError("not a GitHub URL")

    owner, repo = parts[0], parts[1].removesuffix(".git")
    ref = None
    repo_path = ""

    if len(parts) >= 4 and parts[2] in {"tree", "blob"}:
        ref = parts[3]
        repo_path = "/".join(parts[4:])
    elif len(parts) > 2:
        repo_path = "/".join(parts[2:])

    return owner, repo, ref, repo_path


@dataclass(frozen=True)
class SourceRef:
    source_file: str
    category: str
    original_url: str
    normalized_url: str
    owner: str
    repo: str
    ref: str | None
    repo_path: str

    @property
    def full_name(self) -> str:
        return f"{self.owner}/{self.repo}"


@dataclass
class RemoteFile:
    path: str
    url: str
    kind: str = "file"


@dataclass
class RepoInfo:
    repository: str
    ref: str
    requested_path: str
    title: str = ""
    description: str = ""
    default_branch: str = ""
    source_url: str = ""
    fetched_url: str = ""
    status: str = "OK"


@dataclass
class Bundle:
    repository: str
    ref: str
    root: str
    name: str
    classification: str
    source_urls: list[str]
    files: list[str]
    strong_markers: list[str]
    manifests: list[str]
    local_dependencies: list[str]
    external_dependencies: list[str]
    bytes_total: int
    confidence: str


class Fetcher:
    def __init__(self, timeout: int = 40, delay: float = 0.35):
        self.timeout = timeout
        self.delay = delay
        self.last = 0.0
        self.cache: dict[str, bytes] = {}

    def get(self, url: str, accept: str = "*/*") -> bytes:
        if url in self.cache:
            return self.cache[url]

        wait = self.delay - (time.monotonic() - self.last)
        if wait > 0:
            time.sleep(wait)

        req = Request(
            url,
            headers={
                "User-Agent": "AI-Memory-Vault-External-Skill-Scraper/5.0",
                "Accept": accept,
            },
        )

        try:
            with urlopen(req, timeout=self.timeout) as response:
                self.last = time.monotonic()
                data = response.read()
                self.cache[url] = data
                return data
        except HTTPError as exc:
            self.last = time.monotonic()
            if exc.code == 429:
                raise RuntimeError(f"HTTP 429 rate limited: {url}") from exc
            if exc.code == 403:
                raise RuntimeError(f"HTTP 403 forbidden/rate limited: {url}") from exc
            if exc.code == 404:
                raise RuntimeError(f"HTTP 404 not found: {url}") from exc
            raise RuntimeError(f"HTTP {exc.code}: {url}") from exc
        except URLError as exc:
            self.last = time.monotonic()
            raise RuntimeError(f"Network error: {url} :: {exc}") from exc


class HrefParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.links: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag.lower() not in {"a", "link"}:
            return
        for key, value in attrs:
            if key.lower() == "href" and value:
                self.links.append(value)


def extract_sources(external: Path) -> list[SourceRef]:
    found: dict[tuple[str, str], SourceRef] = {}

    for path in external.rglob("*"):
        if not path.is_file() or path.name.startswith("_EXTRACTION_"):
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue

        rel_parent = path.parent.relative_to(external)
        category = rel_parent.parts[0] if rel_parent.parts else "_ROOT"
        category = safe_name(category, "_ROOT")

        for raw in SOURCE_URL_RE.findall(text):
            normalized = normalize_url(raw)
            try:
                owner, repo, ref, repo_path = parse_github_url(normalized)
            except ValueError:
                continue

            key = (str(path.relative_to(external)), normalized)
            found[key] = SourceRef(
                source_file=str(path.relative_to(external)),
                category=category,
                original_url=raw,
                normalized_url=normalized,
                owner=owner,
                repo=repo,
                ref=ref,
                repo_path=repo_path,
            )

    # Explicitly inject known skill collections as sources if not already present.
    known = {(x.owner.lower(), x.repo.lower()) for x in found.values()}
    for repo_full in sorted(KNOWN_COLLECTIONS):
        owner, repo = repo_full.split("/", 1)
        if (owner.lower(), repo.lower()) not in known:
            ref = SourceRef(
                source_file="KNOWN_COLLECTIONS",
                category="_KNOWN",
                original_url=f"https://github.com/{repo_full}",
                normalized_url=f"https://github.com/{repo_full}",
                owner=owner,
                repo=repo,
                ref=None,
                repo_path="",
            )
            found[(ref.source_file, ref.normalized_url)] = ref

    return list(found.values())


def is_excluded(path: str) -> bool:
    return any(part in EXCLUDED_DIRS for part in PurePosixPath(path).parts)


def raw_url(full_name: str, ref: str, path: str) -> str:
    owner, repo = full_name.split("/", 1)
    encoded_path = "/".join(quote(x, safe="-_.~") for x in path.split("/"))
    return f"https://raw.githubusercontent.com/{owner}/{repo}/{quote(ref, safe='')}/{encoded_path}"


def parse_tree_page(
    html: bytes,
    current_url: str,
    base_path: str,
) -> list[RemoteFile]:
    parser = HrefParser()
    parser.feed(html.decode("utf-8", errors="replace"))

    out: dict[str, RemoteFile] = {}
    base_parts = [p for p in base_path.split("/") if p]
    base_depth = len(base_parts)

    for href in parser.links:
        href = href.split("#", 1)[0]
        if not href:
            continue

        absolute = urljoin(current_url, href)
        p = urlparse(absolute)
        if p.netloc.lower() not in GITHUB_HOSTS:
            continue

        parts = [unquote(x) for x in p.path.split("/") if x]
        if len(parts) < 2:
            continue

        owner, repo = parts[0], parts[1]
        if parts[2:3] == ["tree"] and len(parts) >= 4:
            # URL: /owner/repo/tree/ref/path...
            remote_path = "/".join(parts[4:])
            if is_excluded(remote_path):
                continue
            out[remote_path] = RemoteFile(remote_path, absolute, "dir")
        elif parts[2:3] == ["blob"] and len(parts) >= 4:
            remote_path = "/".join(parts[4:])
            if is_excluded(remote_path):
                continue
            out[remote_path] = RemoteFile(remote_path, absolute, "file")

    return list(out.values())


def discover_tree_scrape(
    fetcher: Fetcher,
    source: SourceRef,
    max_pages: int = 2500,
) -> tuple[RepoInfo, dict[str, RemoteFile]]:
    full_name = source.full_name
    ref = source.ref

    # If no explicit ref, GitHub usually renders the default branch link as /tree/<default>.
    # We first open repository root and inspect canonical links.
    root_url = f"https://github.com/{full_name}"
    root_html = fetcher.get(root_url, "text/html")

    parser = HrefParser()
    parser.feed(root_html.decode("utf-8", errors="replace"))

    discovered_ref = ref
    if not discovered_ref:
        for href in parser.links:
            p = urlparse(urljoin(root_url, href))
            parts = [unquote(x) for x in p.path.split("/") if x]
            if len(parts) >= 4 and parts[:3] == [source.owner, source.repo, "tree"]:
                discovered_ref = parts[3]
                break
    if not discovered_ref:
        discovered_ref = "main"

    requested = source.repo_path.strip("/")

    if requested:
        tree_url = (
            f"https://github.com/{full_name}/tree/"
            f"{quote(discovered_ref, safe='')}/{requested}"
        )
    else:
        tree_url = (
            f"https://github.com/{full_name}/tree/"
            f"{quote(discovered_ref, safe='')}"
        )

    queue = deque([(tree_url, requested)])
    seen_pages: set[str] = set()
    remote_files: dict[str, RemoteFile] = {}

    while queue and len(seen_pages) < max_pages:
        url, page_path = queue.popleft()
        if url in seen_pages:
            continue
        seen_pages.add(url)

        try:
            html = fetcher.get(url, "text/html")
        except Exception:
            continue

        links = parse_tree_page(html, url, page_path)
        for item in links:
            remote_files[item.path] = item

        # Extract explicit directory tree URLs to traverse.
        parser = HrefParser()
        parser.feed(html.decode("utf-8", errors="replace"))
        for href in parser.links:
            absolute = urljoin(url, href)
            p = urlparse(absolute)
            parts = [unquote(x) for x in p.path.split("/") if x]
            if (
                len(parts) >= 4
                and parts[:2] == [source.owner, source.repo]
                and parts[2] == "tree"
                and parts[3] == discovered_ref
            ):
                child_path = "/".join(parts[4:])
                if is_excluded(child_path):
                    continue
                # Traverse only descendants of requested path.
                if requested and not (
                    child_path == requested
                    or child_path.startswith(requested.rstrip("/") + "/")
                ):
                    continue
                queue.append((absolute, child_path))

    info = RepoInfo(
        repository=full_name,
        ref=discovered_ref,
        requested_path=requested,
        source_url=source.original_url,
        fetched_url=tree_url,
    )
    return info, remote_files


def candidate_bundle_roots(
    files: dict[str, RemoteFile],
    requested_path: str,
) -> list[str]:
    scoped = [p for p in files if not requested_path or (
        p == requested_path or p.startswith(requested_path.rstrip("/") + "/")
    )]

    roots: set[str] = set()

    # Explicit markers are the strongest signal.
    for path in scoped:
        name = PurePosixPath(path).name
        if name in STRONG_MARKERS:
            roots.add(str(PurePosixPath(path).parent).replace("\\", "/"))

    # Conventional container children.
    for path in scoped:
        parts = PurePosixPath(path).parts
        for i, part in enumerate(parts[:-1]):
            if part.lower() in CONVENTIONAL_CONTAINERS and i + 1 < len(parts):
                child = "/".join(parts[:i + 2])
                roots.add(child)

    # A direct tree link is already a strong semantic boundary. Use it only
    # if the page contains meaningful skill/bundle indicators.
    if requested_path:
        direct = [
            p for p in scoped
            if p == requested_path or p.startswith(requested_path.rstrip("/") + "/")
        ]
        if any(
            PurePosixPath(p).name in STRONG_MARKERS
            or PurePosixPath(p).name in MANIFESTS
            for p in direct
        ):
            roots.add(requested_path)

    # Single-file blob links: parent folder is the natural bundle.
    if requested_path and not any(r.startswith(requested_path) for r in roots):
        name = PurePosixPath(requested_path).name
        if name in STRONG_MARKERS or name in MANIFESTS:
            roots.add(str(PurePosixPath(requested_path).parent).replace("\\", "/"))

    # Remove generic repository root when we have explicit children.
    if "" in roots and len(roots) > 1:
        roots.remove("")

    ordered = sorted(roots, key=lambda x: (x.count("/"), len(x), x))
    result: list[str] = []

    for root in ordered:
        # Keep both sibling bundles, but don't keep a parent that would swallow
        # an already-identified child unless parent itself has a strong marker.
        parent_strong = any(
            p.startswith(root.rstrip("/") + "/")
            and PurePosixPath(p).parent.as_posix() == root
            for p in scoped
            if PurePosixPath(p).name in STRONG_MARKERS
        )
        if any(
            root != existing and root.startswith(existing.rstrip("/") + "/")
            and not parent_strong
            for existing in result
        ):
            continue
        result.append(root)

    return result


def read_text_file(
    fetcher: Fetcher,
    full_name: str,
    ref: str,
    remote_path: str,
    max_bytes: int,
) -> bytes:
    # GitHub's raw host does not need API access.
    url = raw_url(full_name, ref, remote_path)
    data = fetcher.get(url)
    if len(data) > max_bytes:
        raise RuntimeError(
            f"{remote_path} is {len(data)} bytes > --max-file-mb limit"
        )
    return data


def local_refs(text: str) -> set[str]:
    out = set()
    for pattern in LOCAL_REF_PATTERNS:
        for value in pattern.findall(text):
            out.add(value if isinstance(value, str) else value[0])
    return out


def resolve_local(source: str, ref: str, files: dict[str, RemoteFile]) -> str | None:
    ref = ref.strip().replace("\\", "/")
    if not ref or ref.startswith(("http://", "https://", "@", "#")):
        return None

    base = PurePosixPath(source).parent
    target = str(PurePosixPath(base, ref)).replace("\\", "/")

    candidates = [
        target,
        target.lstrip("./"),
        target + ".py",
        target + ".js",
        target + ".jsx",
        target + ".ts",
        target + ".tsx",
        target + ".json",
        target + ".yaml",
        target + ".yml",
        target + ".md",
        target + "/index.js",
        target + "/index.ts",
        target + "/__init__.py",
        target + "/SKILL.md",
    ]
    for item in candidates:
        if item in files:
            return item
    return None


def package_dependencies(name: str, text: str) -> set[str]:
    out: set[str] = set()

    if name == "package.json":
        try:
            obj = json.loads(text)
            for section in (
                "dependencies", "devDependencies",
                "peerDependencies", "optionalDependencies"
            ):
                block = obj.get(section) or {}
                if isinstance(block, dict):
                    out.update(block.keys())
        except Exception:
            pass

    if name in {"requirements.txt", "requirements-dev.txt"}:
        for line in text.splitlines():
            line = line.strip()
            if line and not line.startswith(("#", "-", ";")):
                out.add(re.split(r"[<=>!~\[]", line, 1)[0].strip())

    return out


def dependency_closure(
    fetcher: Fetcher,
    full_name: str,
    ref: str,
    selected: set[str],
    files: dict[str, RemoteFile],
    max_bytes: int,
) -> tuple[set[str], set[str], dict[str, bytes]]:
    local = set()
    external = set()
    content_cache: dict[str, bytes] = {}

    queue = deque(sorted(selected))
    visited = set()

    while queue:
        path = queue.popleft()
        if path in visited:
            continue
        visited.add(path)

        record = files.get(path)
        if not record:
            continue

        name = PurePosixPath(path).name
        suffix = PurePosixPath(path).suffix.lower()
        if suffix not in TEXT_EXTENSIONS and name not in MANIFESTS:
            continue

        try:
            data = read_text_file(fetcher, full_name, ref, path, max_bytes)
        except Exception:
            continue

        content_cache[path] = data
        text = data.decode("utf-8", errors="replace")

        external.update(package_dependencies(name, text))

        for imported in local_refs(text):
            resolved = resolve_local(path, imported, files)
            if resolved:
                local.add(resolved)
                if resolved not in selected:
                    selected.add(resolved)
                    queue.append(resolved)
            elif not imported.startswith((".", "/")):
                # A bare import is a likely external runtime package.
                if not imported.startswith(("{", "[")):
                    external.add(imported.split("/")[0])

        if suffix in {".py", ".pyw"}:
            for match in re.findall(
                r"^\s*(?:from|import)\s+([A-Za-z0-9_.-]+)", text, re.M
            ):
                package = match.split(".")[0]
                if package not in {
                    "os", "sys", "json", "re", "pathlib", "typing", "collections",
                    "datetime", "hashlib", "math", "argparse", "subprocess",
                    "logging", "time", "functools", "itertools",
                }:
                    external.add(package)

        if suffix in {".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx"}:
            for match in re.findall(
                r"""(?:from\s+|require\(\s*)["']([^"']+)["']""",
                text,
            ):
                if not match.startswith((".", "/")):
                    external.add(match)

    return local, external, content_cache


def build_bundles(
    fetcher: Fetcher,
    source: SourceRef,
    info: RepoInfo,
    files: dict[str, RemoteFile],
    max_bytes: int,
) -> list[Bundle]:
    roots = candidate_bundle_roots(files, info.requested_path)
    bundles: list[Bundle] = []

    for root in roots:
        selected = {
            p for p in files
            if p == root or p.startswith(root.rstrip("/") + "/")
        }
        if not selected:
            continue

        local, external, _ = dependency_closure(
            fetcher,
            info.repository,
            info.ref,
            selected,
            files,
            max_bytes,
        )

        markers = sorted(
            p for p in selected if PurePosixPath(p).name in STRONG_MARKERS
        )
        manifests = sorted(
            p for p in selected if PurePosixPath(p).name in MANIFESTS
        )
        total = sum(0 if not files[p].url else 1 for p in selected)

        confidence = "high" if markers else "medium" if manifests else "low"

        bundles.append(Bundle(
            repository=info.repository,
            ref=info.ref,
            root=root,
            name=safe_name(PurePosixPath(root).name or info.repository.split("/")[-1]),
            classification="SKILL_BUNDLE",
            source_urls=[source.original_url],
            files=sorted(selected),
            strong_markers=markers,
            manifests=manifests,
            local_dependencies=sorted(local),
            external_dependencies=sorted(external),
            bytes_total=total,
            confidence=confidence,
        ))

    return bundles


def materialize_bundle(
    external_root: Path,
    bundle: Bundle,
    fetcher: Fetcher,
    files: dict[str, RemoteFile],
    max_bytes: int,
):
    category = safe_name(
        Path(bundle.source_urls[0]).name if bundle.source_urls else "_RAW",
        "_RAW",
    )

    # Better category comes from source file metadata in the caller; folder name
    # collision handling is done by repository + bundle name.
    category = "_SCRAPED"

    destination = (
        external_root
        / category
        / safe_name(bundle.repository.replace("/", "__"))
        / bundle.name
    )

    if destination.exists():
        destination.mkdir(parents=True, exist_ok=True)

    records = []
    for path in bundle.files:
        data = read_text_file(
            fetcher,
            bundle.repository,
            bundle.ref,
            path,
            max_bytes,
        )
        local_path = destination / Path(path).relative_to(Path(bundle.root).parent)
        local_path.parent.mkdir(parents=True, exist_ok=True)
        local_path.write_bytes(data)
        records.append({
            "source_path": path,
            "bytes": len(data),
            "sha256": sha256(data),
        })

    manifest = asdict(bundle)
    manifest["files"] = records
    manifest["materialized_at_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    manifest["external_code_executed"] = False

    (destination / "_BUNDLE.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    (destination / "PROVENANCE.json").write_text(
        json.dumps({
            "status": "RAW_EXTERNAL_BUNDLE",
            "repository": bundle.repository,
            "ref": bundle.ref,
            "root": bundle.root,
            "source_urls": bundle.source_urls,
            "retrieved_at_utc": manifest["materialized_at_utc"],
            "external_code_executed": False,
            "file_count": len(records),
        }, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def save_json(path: Path, value: object):
    path.write_text(
        json.dumps(value, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    parser.add_argument("--inventory", action="store_true")
    parser.add_argument("--discover", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--materialize", action="store_true")
    parser.add_argument("--max-file-mb", type=int, default=20)
    parser.add_argument("--max-pages", type=int, default=2500)
    args = parser.parse_args()

    if sum([
        args.inventory, args.discover, args.dry_run, args.materialize
    ]) != 1:
        raise SystemExit(
            "Use exactly one of --inventory, --discover, --dry-run, --materialize."
        )

    root = args.repo_root.resolve()
    external = root / TARGET
    if not external.is_dir():
        raise SystemExit(f"ERROR: target directory not found: {external}")

    sources = extract_sources(external)
    repos = defaultdict(list)
    for src in sources:
        repos[src.full_name].append(src)

    print(f"LINK_OCCURRENCES={len(sources)}")
    print(f"UNIQUE_URLS={len({s.normalized_url for s in sources})}")
    print(f"UNIQUE_REPOSITORIES={len(repos)}")
    print("EXTERNAL_CODE_EXECUTED=NO")
    print("SCRAPER_MODE=YES")
    print("GITHUB_API_USED=NO")

    if args.inventory:
        for name in sorted(repos):
            print(name)
        return 0

    fetcher = Fetcher()
    all_repos = {}
    all_bundles = []
    failures = []

    for full_name, repo_sources in sorted(repos.items()):
        source = repo_sources[0]
        try:
            info, files = discover_tree_scrape(
                fetcher,
                source,
                max_pages=args.max_pages,
            )
            all_repos[full_name] = asdict(info)

            if args.discover:
                print(
                    f"{full_name} | ref={info.ref} | "
                    f"path={info.requested_path or '/'} | files={len(files)}"
                )
                continue

            bundles = build_bundles(
                fetcher,
                source,
                info,
                files,
                args.max_file_mb * 1024 * 1024,
            )

            for bundle in bundles:
                # Preserve all source URLs pointing at this repository.
                bundle.source_urls = sorted({
                    s.original_url for s in repo_sources
                })
                all_bundles.append(asdict(bundle))

                if args.materialize:
                    materialize_bundle(
                        external,
                        bundle,
                        fetcher,
                        files,
                        args.max_file_mb * 1024 * 1024,
                    )

        except Exception as exc:
            failures.append({
                "repository": full_name,
                "source_urls": sorted({s.original_url for s in repo_sources}),
                "error": str(exc),
            })

    if args.discover:
        save_json(
            external / "_SCRAPE_DISCOVERY_V5.json",
            {
                "generated_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                "repositories": all_repos,
                "failures": failures,
            },
        )
        print(f"DISCOVERED_REPOSITORIES={len(all_repos)}")
        print(f"FAILURES={len(failures)}")
        return 0 if not failures else 1

    report = {
        "generated_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "links": [asdict(s) for s in sources],
        "repositories": all_repos,
        "bundles": all_bundles,
        "failures": failures,
        "scraper_mode": True,
        "github_api_used": False,
        "external_code_executed": False,
    }

    if args.dry_run:
        save_json(external / "_SCRAPE_DRY_RUN_V5.json", report)
        print(f"BUNDLES_FOUND={len(all_bundles)}")
        print(f"FAILURES={len(failures)}")
        for b in all_bundles:
            print(
                f"{b['repository']} | {b['root']} | files={len(b['files'])} | "
                f"markers={len(b['strong_markers'])} | "
                f"manifests={len(b['manifests'])} | "
                f"deps={len(b['external_dependencies'])} | "
                f"confidence={b['confidence']}"
            )
        return 0 if not failures else 1

    save_json(external / "_SCRAPE_BUNDLE_REPORT_V5.json", report)
    print(f"BUNDLES_MATERIALIZED={len(all_bundles)}")
    print(f"FAILURES={len(failures)}")
    print(f"REPORT={TARGET / '_SCRAPE_BUNDLE_REPORT_V5.json'}")
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
