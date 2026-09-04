#!/usr/bin/env python3
"""
AI Memory Vault - External Skill Bundle Materializer v4

Design goals
------------
1. Treat GitHub links as SOURCES, not automatically as skills.
2. Classify sources:
      SINGLE_SKILL
      SKILL_COLLECTION
      AWESOME_LIST
      KNOWLEDGE_REPOSITORY
      TOPIC_PAGE
      EXAMPLE_SOFTWARE
      UNKNOWN
3. Inspect each unique repository once through the Git tree API.
4. Discover skill/bundle roots using multiple signals:
      - SKILL.md / skill manifests
      - agent/plugin/instruction manifests
      - conventional skill directories
      - self-contained manifests + scripts/resources
5. Materialize complete bundles:
      - docs
      - scripts
      - assets
      - references
      - templates
      - local helper modules
      - package/dependency manifests
      - lock files
      - config
6. Never execute imported code.
7. Record provenance, file hashes and dependency inventory.
8. Be conservative: do not mirror entire generic knowledge/software repos as skills.

Modes
-----
    --inventory      local link inventory only; no network
    --classify       classify unique sources using GitHub metadata
    --dry-run        classify + discover bundle roots + dependency inventory
    --materialize    classify + discover + materialize bundles

Examples
--------
    python materialize_external_skills_v4.py --repo-root . --inventory
    python materialize_external_skills_v4.py --repo-root . --classify
    python materialize_external_skills_v4.py --repo-root . --dry-run
    python materialize_external_skills_v4.py --repo-root . --materialize

Optional:
    set GITHUB_TOKEN=...
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import sys
import time
from collections import defaultdict, deque
from dataclasses import dataclass, asdict, field
from pathlib import Path, PurePosixPath
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlparse
from urllib.request import Request, urlopen


API = "https://api.github.com"
TARGET = Path("01_KNOWLEDGE/EXTERNAL_SKILLS")

KNOWN_SKILL_COLLECTIONS = {
    "VoltAgent/awesome-agent-skills",
    "ComposioHQ/awesome-claude-skills",
    "sickn33/agentic-awesome-skills",
    "bergside/awesome-design-skills",
}

EXCLUDED_DIRS = {
    ".git", ".github", ".idea", ".vscode", "node_modules",
    "__pycache__", ".pytest_cache", "dist", "build", "coverage",
    ".venv", "venv", "vendor", "target",
}

TEXT_EXTENSIONS = {
    ".md", ".markdown", ".txt", ".json", ".yaml", ".yml", ".toml",
    ".ini", ".cfg", ".conf", ".xml", ".html", ".htm", ".css",
    ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx",
    ".py", ".pyw", ".rb", ".go", ".rs", ".java", ".kt",
    ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1",
    ".sql", ".jinja", ".j2",
}

MANIFEST_NAMES = {
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

SKILL_MARKERS = {
    "SKILL.md", "skill.md",
    "skill.yaml", "skill.yml", "skill.json", "skill.toml",
}

BUNDLE_MARKERS = {
    "AGENTS.md", "CLAUDE.md", "INSTRUCTIONS.md",
    "RULES.md", "PLUGIN.md", "plugin.yaml", "plugin.yml", "plugin.json",
    "agent.yaml", "agent.yml", "agent.json", "agent.toml",
    "mcp.json", "mcp.yaml", "mcp.yml",
}

CONVENTIONAL_DIRS = {
    "skills", "skill", "agents", "agent",
    "plugins", "plugin",
    "commands", "tools", "tool",
}

SOURCE_URL_RE = re.compile(
    r'https?://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+'
    r'(?:/(?:tree|blob)/[^ \t\r\n<>\]\)"}]+)?'
)

TOPIC_PREFIXES = {
    "topics", "collections", "trending", "marketplace",
}

GENERIC_REPO_HINTS = {
    "awesome-", "awesome_", "-awesome", "_awesome",
    "system-design", "system_design", "selfhosted",
}

SKILL_REPO_HINTS = {
    "skills", "skill", "agent", "plugin",
}

SOFTWARE_HINTS = {
    "samples", "sample", "realworld", "starter",
    "template", "boilerplate", "application", "app",
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def safe_name(value: str, fallback: str = "unnamed") -> str:
    value = re.sub(r"[^A-Za-z0-9._-]+", "-", value.strip()).strip("-")
    return value or fallback


def normalize_url(url: str) -> str:
    p = urlparse(url.strip().rstrip(".,;"))
    if p.netloc.lower() != "github.com":
        return url
    path = re.sub(r"/+", "/", p.path).rstrip("/")
    return f"https://github.com{path}"


def parse_github_url(url: str):
    p = urlparse(url)
    parts = [x for x in p.path.split("/") if x]
    if p.netloc.lower() != "github.com" or len(parts) < 2:
        raise ValueError("not github.com")

    owner = parts[0]
    repo = parts[1].removesuffix(".git")
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
class FileRecord:
    path: str
    size: int
    sha: str
    mode: str | None
    url: str


@dataclass
class RepoClassification:
    repository: str
    classification: str
    confidence: str
    reason: list[str] = field(default_factory=list)
    default_branch: str = ""
    description: str = ""
    stars: int = 0
    forks: int = 0
    topics: list[str] = field(default_factory=list)
    archived: bool = False
    fork: bool = False


class GitHub:
    def __init__(self, token: str | None, interval: float = 0.08):
        self.token = token
        self.interval = interval
        self.last_request = 0.0
        self.cache: dict[str, object] = {}

    def request_json(self, url: str):
        if url in self.cache:
            return self.cache[url]

        delay = self.interval - (time.monotonic() - self.last_request)
        if delay > 0:
            time.sleep(delay)

        headers = {
            "Accept": "application/vnd.github+json",
            "User-Agent": "AI-Memory-Vault-External-Skill-Materializer/4.0",
            "X-GitHub-Api-Version": "2022-11-28",
        }
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"

        try:
            with urlopen(Request(url, headers=headers), timeout=60) as response:
                self.last_request = time.monotonic()
                data = json.loads(response.read().decode("utf-8"))
                self.cache[url] = data
                return data
        except HTTPError as exc:
            self.last_request = time.monotonic()
            body = exc.read().decode("utf-8", errors="replace")
            if exc.code == 401:
                raise RuntimeError("GitHub token rejected (HTTP 401).") from exc
            if exc.code in (403, 429):
                raise RuntimeError(
                    "GitHub API rate/authentication limit reached. "
                    "Set GITHUB_TOKEN and retry."
                ) from exc
            raise RuntimeError(f"GitHub HTTP {exc.code}: {body[:500]}") from exc
        except URLError as exc:
            self.last_request = time.monotonic()
            raise RuntimeError(f"GitHub network error: {exc}") from exc

    def repo(self, full_name: str):
        return self.request_json(f"{API}/repos/{full_name}")

    def tree(self, full_name: str, ref: str | None):
        if not ref:
            meta = self.repo(full_name)
            ref = meta.get("default_branch", "main")
        data = self.request_json(
            f"{API}/repos/{full_name}/git/trees/{quote(ref, safe='')}?recursive=1"
        )
        return ref, data

    def blob(self, full_name: str, blob_sha: str) -> bytes:
        data = self.request_json(f"{API}/repos/{full_name}/git/blobs/{blob_sha}")
        if data.get("encoding") != "base64":
            raise RuntimeError(f"Unsupported blob encoding for {full_name}:{blob_sha}")
        return base64.b64decode(data["content"], validate=False)

    def commit(self, full_name: str, ref: str):
        data = self.request_json(
            f"{API}/repos/{full_name}/commits/{quote(ref, safe='')}"
        )
        return data.get("sha")


def is_excluded(path: str) -> bool:
    return any(part in EXCLUDED_DIRS for part in PurePosixPath(path).parts)


def extract_sources(external_root: Path) -> list[SourceRef]:
    found: dict[tuple[str, str], SourceRef] = {}

    for path in external_root.rglob("*"):
        if not path.is_file():
            continue
        if path.name.startswith("_EXTRACTION_"):
            continue

        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue

        relative_parent = path.parent.relative_to(external_root)
        category = relative_parent.parts[0] if relative_parent.parts else "_ROOT"
        category = safe_name(category, "_ROOT")

        for raw in SOURCE_URL_RE.findall(text):
            normalized = normalize_url(raw)
            try:
                owner, repo, ref, repo_path = parse_github_url(normalized)
            except ValueError:
                continue

            # Skip GitHub pseudo-repositories such as /topics/backend.
            if repo.lower() in {"topics", "collections", "trending"}:
                # This condition is mostly for malformed links where "repo" becomes a page name.
                continue

            key = (str(path.relative_to(external_root)), normalized)
            found[key] = SourceRef(
                source_file=str(path.relative_to(external_root)),
                category=category,
                original_url=raw,
                normalized_url=normalized,
                owner=owner,
                repo=repo,
                ref=ref,
                repo_path=repo_path,
            )

    return list(found.values())


def add_known_collections(sources: list[SourceRef]) -> list[SourceRef]:
    existing = {(s.owner.lower(), s.repo.lower()) for s in sources}
    out = list(sources)
    for full_name in sorted(KNOWN_SKILL_COLLECTIONS):
        owner, repo = full_name.split("/", 1)
        if (owner.lower(), repo.lower()) in existing:
            continue
        out.append(SourceRef(
            source_file="KNOWN_COLLECTIONS",
            category="_KNOWN",
            original_url=f"https://github.com/{full_name}",
            normalized_url=f"https://github.com/{full_name}",
            owner=owner,
            repo=repo,
            ref=None,
            repo_path="",
        ))
    return out


def looks_like_topic_or_collection_page(src: SourceRef) -> bool:
    path = src.repo_path.lower()
    return (
        src.repo.lower() in TOPIC_PREFIXES
        or path.startswith("topics/")
        or path.startswith("collections/")
        or src.owner.lower() in {"topics", "collections"}
    )


def classify_from_url(src: SourceRef) -> tuple[str, str, list[str]]:
    full = src.full_name.lower()
    repo = src.repo.lower()
    reasons: list[str] = []

    if looks_like_topic_or_collection_page(src):
        return "TOPIC_PAGE", "high", ["URL matches GitHub topic/collection page"]

    if any(x in repo for x in SOFTWARE_HINTS):
        reasons.append("repository name resembles software/sample/template")
        return "EXAMPLE_SOFTWARE", "medium", reasons

    if full in {x.lower() for x in KNOWN_SKILL_COLLECTIONS}:
        return "SKILL_COLLECTION", "high", ["known external skill collection"]

    if any(x in repo for x in SKILL_REPO_HINTS):
        reasons.append("repository name contains skill/agent/plugin signal")
        return "SKILL_COLLECTION", "medium", reasons

    if any(x in repo for x in GENERIC_REPO_HINTS):
        reasons.append("repository name resembles an awesome-list / curated collection")
        return "AWESOME_LIST", "medium", reasons

    if src.repo_path:
        lower = src.repo_path.lower()
        if any(seg in lower.split("/") for seg in CONVENTIONAL_DIRS):
            reasons.append("URL path enters a conventional skill/agent/plugin directory")
            return "SKILL_COLLECTION", "medium", reasons

    return "UNKNOWN", "low", ["URL alone is insufficient"]


def classify_repository(meta: dict, srcs: list[SourceRef], tree_index: dict[str, FileRecord]) -> RepoClassification:
    full_name = meta["full_name"]
    static_class, static_conf, static_reason = classify_from_url(srcs[0])

    files = list(tree_index)
    lower_files = [p.lower() for p in files]

    skill_markers = [p for p in files if PurePosixPath(p).name in SKILL_MARKERS]
    manifests = [p for p in files if PurePosixPath(p).name.lower() in {x.lower() for x in MANIFEST_NAMES}]
    bundle_markers = [p for p in files if PurePosixPath(p).name in BUNDLE_MARKERS]
    conventional = [
        p for p in files
        if any(seg.lower() in CONVENTIONAL_DIRS for seg in PurePosixPath(p).parts)
    ]

    reasons = list(static_reason)
    classification = static_class
    confidence = static_conf

    topics = [str(x) for x in meta.get("topics", [])]

    # Strong tree evidence.
    if skill_markers:
        if len(skill_markers) >= 2 or any("/skills/" in p.lower() for p in skill_markers):
            classification = "SKILL_COLLECTION"
            confidence = "high"
            reasons.append(f"tree contains {len(skill_markers)} skill marker file(s)")
        elif classification in {"UNKNOWN", "AWESOME_LIST", "KNOWLEDGE_REPOSITORY"}:
            classification = "SINGLE_SKILL"
            confidence = "high"
            reasons.append("tree contains an explicit skill marker")

    if bundle_markers and conventional and classification == "UNKNOWN":
        classification = "SKILL_COLLECTION"
        confidence = "medium"
        reasons.append("tree contains bundle manifests inside conventional agent/plugin directories")

    if classification == "UNKNOWN":
        if any("awesome" in t.lower() for t in topics) or "awesome" in meta.get("name", "").lower():
            classification = "AWESOME_LIST"
            confidence = "high"
            reasons.append("repository metadata identifies a curated/awesome collection")
        elif conventional and len(conventional) >= 5:
            classification = "KNOWLEDGE_REPOSITORY"
            confidence = "medium"
            reasons.append("tree is broad and lacks explicit skill markers")
        elif manifests and any(
            PurePosixPath(p).name.lower() in {"package.json", "pyproject.toml", "cargo.toml", "go.mod"}
            for p in manifests
        ):
            classification = "EXAMPLE_SOFTWARE"
            confidence = "medium"
            reasons.append("tree contains software dependency/build manifests without skill markers")
        else:
            classification = "KNOWLEDGE_REPOSITORY"
            confidence = "low"
            reasons.append("no strong skill-bundle evidence")

    return RepoClassification(
        repository=full_name,
        classification=classification,
        confidence=confidence,
        reason=reasons,
        default_branch=str(meta.get("default_branch") or ""),
        description=str(meta.get("description") or ""),
        stars=int(meta.get("stargazers_count") or 0),
        forks=int(meta.get("forks_count") or 0),
        topics=topics,
        archived=bool(meta.get("archived")),
        fork=bool(meta.get("fork")),
    )


def tree_index(tree_data: dict) -> dict[str, FileRecord]:
    if tree_data.get("truncated"):
        raise RuntimeError("GitHub recursive tree is truncated; use a narrower source URL.")

    out: dict[str, FileRecord] = {}
    for item in tree_data.get("tree", []):
        if item.get("type") != "blob":
            continue
        path = str(item.get("path", "")).replace("\\", "/")
        if not path or is_excluded(path):
            continue
        out[path] = FileRecord(
            path=path,
            size=int(item.get("size") or 0),
            sha=str(item.get("sha") or ""),
            mode=item.get("mode"),
            url=str(item.get("url") or ""),
        )
    return out


def is_under(path: str, root: str) -> bool:
    if not root:
        return True
    return path == root or path.startswith(root.rstrip("/") + "/")


def dirname(path: str) -> str:
    return str(PurePosixPath(path).parent).replace("\\", "/")


def basename(path: str) -> str:
    return PurePosixPath(path).name


def marker_roots(files: dict[str, FileRecord], requested_path: str) -> list[str]:
    roots: set[str] = set()
    scoped = [p for p in files if is_under(p, requested_path)]

    for path in scoped:
        name = basename(path)
        if name in SKILL_MARKERS:
            roots.add(dirname(path))

    # Explicit manifests can define a bundle even without SKILL.md.
    for path in scoped:
        name = basename(path)
        if name in BUNDLE_MARKERS:
            parent = dirname(path)
            parts = PurePosixPath(path).parts
            if any(seg.lower() in CONVENTIONAL_DIRS for seg in parts[:-1]):
                roots.add(parent)

    # Conventional collection directories: immediate children are candidate bundles.
    for path in scoped:
        parts = PurePosixPath(path).parts
        for i, part in enumerate(parts[:-1]):
            if part.lower() in CONVENTIONAL_DIRS and i + 1 < len(parts):
                child = "/".join(parts[:i + 2])
                roots.add(child)

    # A direct link to a path with meaningful bundle files is a candidate.
    if requested_path:
        direct_files = [p for p in scoped if p != requested_path]
        if any(
            basename(p) in SKILL_MARKERS
            or basename(p) in BUNDLE_MARKERS
            or basename(p) in MANIFEST_NAMES
            for p in direct_files
        ):
            roots.add(requested_path)

    # If repository is strongly identified as a skill repo but has one clear marker,
    # use marker parent. If there are no markers, do NOT import the whole repo.
    ordered = sorted(roots, key=lambda x: (x.count("/"), len(x), x))

    # Remove ancestor roots only when an explicit child root exists; this prevents
    # skills/foo and skills/foo/subskill from being swallowed accidentally.
    result: list[str] = []
    for root in ordered:
        if root in result:
            continue
        if any(root != prior and is_under(root, prior) for prior in result):
            continue
        result.append(root)

    return result


def content(client: GitHub, full_name: str, record: FileRecord, max_bytes: int) -> bytes:
    if record.size > max_bytes:
        raise RuntimeError(
            f"{record.path}: {record.size} bytes exceeds --max-file-mb limit"
        )
    return client.blob(full_name, record.sha)


def local_references(text: str) -> set[str]:
    patterns = [
        re.compile(r"""(?:from\s+|import\s+)["']([^"']+)["']"""),
        re.compile(r"""require\(\s*["']([^"']+)["']\s*\)"""),
        re.compile(r"""(?:source|include)\s+["']?([^"' \t\r\n]+)""", re.I),
        re.compile(
            r"""(?:path|file|template|asset|script|reference|include)\s*[:=]\s*["']([^"']+)["']""",
            re.I,
        ),
        re.compile(r"""["'(]((?:\./|\.\./)[^"')\s]+)"""),
    ]

    result: set[str] = set()
    for pattern in patterns:
        for match in pattern.findall(text):
            result.add(match if isinstance(match, str) else match[0])
    return result


def resolve_local(source_file: str, ref: str, files: dict[str, FileRecord]) -> str | None:
    ref = ref.strip().replace("\\", "/")
    if not ref or ref.startswith(("#", "http://", "https://", "@")):
        return None

    base = PurePosixPath(source_file).parent
    candidate = str(PurePosixPath(base, ref)).replace("\\", "/")

    candidates = [
        candidate,
        candidate.lstrip("./"),
        candidate + ".py",
        candidate + ".js",
        candidate + ".ts",
        candidate + ".jsx",
        candidate + ".tsx",
        candidate + ".json",
        candidate + ".yaml",
        candidate + ".yml",
        candidate + ".md",
        candidate + "/index.js",
        candidate + "/index.ts",
        candidate + "/__init__.py",
        candidate + "/SKILL.md",
    ]

    for item in candidates:
        item = item.replace("//", "/")
        if item in files:
            return item
    return None


def dependency_inventory(
    client: GitHub,
    full_name: str,
    bundle_files: set[str],
    files: dict[str, FileRecord],
    max_bytes: int,
) -> tuple[set[str], set[str], dict[str, bytes]]:
    local = set()
    external = set()
    cache: dict[str, bytes] = {}

    def get(path: str) -> bytes:
        if path not in cache:
            cache[path] = content(client, full_name, files[path], max_bytes)
        return cache[path]

    queue = deque(sorted(bundle_files))
    visited = set()

    while queue:
        path = queue.popleft()
        if path in visited:
            continue
        visited.add(path)

        name = basename(path)
        suffix = PurePosixPath(path).suffix.lower()
        if suffix not in TEXT_EXTENSIONS and name not in MANIFEST_NAMES:
            continue

        try:
            text = get(path).decode("utf-8", errors="replace")
        except Exception:
            continue

        for ref in local_references(text):
            resolved = resolve_local(path, ref, files)
            if resolved:
                if resolved not in bundle_files:
                    bundle_files.add(resolved)
                    queue.append(resolved)
                local.add(resolved)
            elif not ref.startswith((".", "/")):
                external.add(ref)

        if suffix in {".py", ".pyw"}:
            for match in re.findall(
                r"^\s*(?:from|import)\s+([A-Za-z0-9_.-]+)", text, re.M
            ):
                package = match.split(".")[0]
                if package not in {
                    "os", "sys", "json", "re", "typing", "pathlib", "subprocess",
                    "argparse", "collections", "datetime", "hashlib", "math",
                }:
                    external.add(package)

        if suffix in {".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx"}:
            for match in re.findall(
                r"""(?:from\s+|require\(\s*)["']([^"']+)["']""", text
            ):
                if not match.startswith((".", "/")):
                    external.add(match)

        # Manifest-specific package extraction.
        if name == "package.json":
            try:
                obj = json.loads(text)
                for section in ("dependencies", "devDependencies", "peerDependencies", "optionalDependencies"):
                    if isinstance(obj.get(section), dict):
                        external.update(obj[section].keys())
            except Exception:
                pass

        if name in {"requirements.txt", "requirements-dev.txt"}:
            for line in text.splitlines():
                line = line.strip()
                if line and not line.startswith(("#", "-")):
                    external.add(re.split(r"[<=>!~\[]", line, 1)[0].strip())

    return local, external, cache


def discover_bundles(
    client: GitHub,
    source: SourceRef,
    classification: RepoClassification,
    files: dict[str, FileRecord],
    max_bytes: int,
) -> list[dict]:
    if classification.classification in {
        "TOPIC_PAGE", "AWESOME_LIST", "KNOWLEDGE_REPOSITORY", "EXAMPLE_SOFTWARE"
    }:
        # They may still contain actual skill bundles, but only extract explicit
        # markers, never treat the generic repository as one giant skill.
        pass

    roots = marker_roots(files, source.repo_path)
    if not roots:
        return []

    bundles = []
    commit = None
    if classification.default_branch:
        try:
            commit = client.commit(source.full_name, classification.default_branch)
        except Exception:
            commit = None

    for root in roots:
        initial = {p for p in files if is_under(p, root)}
        if not initial:
            continue

        bundle_files = set(initial)
        local, external, _ = dependency_inventory(
            client, source.full_name, bundle_files, files, max_bytes
        )

        markers = sorted([
            p for p in bundle_files
            if basename(p) in SKILL_MARKERS or basename(p) in BUNDLE_MARKERS
        ])
        manifests = sorted([
            p for p in bundle_files
            if basename(p) in MANIFEST_NAMES
        ])

        bundles.append({
            "repository": source.full_name,
            "ref": source.ref or classification.default_branch,
            "commit": commit,
            "root": root,
            "name": safe_name(PurePosixPath(root).name or source.repo),
            "file_count": len(bundle_files),
            "files": sorted(bundle_files),
            "skill_markers": markers,
            "manifests": manifests,
            "local_dependencies": sorted(local),
            "external_dependencies": sorted(external),
            "source_urls": sorted({x.original_url for x in [source]}),
        })

    return bundles


def write_bundle(
    external_root: Path,
    bundle: dict,
    client: GitHub,
    files: dict[str, FileRecord],
    max_bytes: int,
):
    category = safe_name(bundle.get("category") or "_RAW", "_RAW")
    destination = external_root / category / bundle["name"]

    if destination.exists():
        destination = external_root / category / (
            bundle["name"] + "__" + safe_name(bundle["repository"].replace("/", "__"))
        )

    destination.mkdir(parents=True, exist_ok=True)

    records = []
    for source_path in bundle["files"]:
        data = content(client, bundle["repository"], files[source_path], max_bytes)
        local = destination / Path(source_path)
        local.parent.mkdir(parents=True, exist_ok=True)
        local.write_bytes(data)
        records.append({
            "source_path": source_path,
            "bytes": len(data),
            "source_git_sha": files[source_path].sha,
            "sha256": sha256(data),
        })

    manifest = dict(bundle)
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
            "repository": bundle["repository"],
            "source_ref": bundle.get("ref", ""),
            "source_commit": bundle.get("commit", ""),
            "source_urls": bundle.get("source_urls", []),
            "retrieved_at_utc": manifest["materialized_at_utc"],
            "file_count": len(records),
            "files_sha256": sha256(
                json.dumps(records, sort_keys=True).encode("utf-8")
            ),
        }, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    return destination


def save_json(path: Path, obj: object):
    path.write_text(
        json.dumps(obj, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    parser.add_argument("--token", default=os.environ.get("GITHUB_TOKEN"))
    parser.add_argument("--inventory", action="store_true")
    parser.add_argument("--classify", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--materialize", action="store_true")
    parser.add_argument("--no-known-collections", action="store_true")
    parser.add_argument("--max-file-mb", type=int, default=20)
    args = parser.parse_args()

    mode_count = sum([
        args.inventory, args.classify, args.dry_run, args.materialize
    ])
    if mode_count != 1:
        raise SystemExit(
            "Use exactly one of --inventory, --classify, --dry-run, --materialize."
        )

    root = args.repo_root.resolve()
    external = root / TARGET
    if not external.is_dir():
        raise SystemExit(f"ERROR: target directory not found: {external}")

    sources = extract_sources(external)
    if not args.no_known_collections:
        sources = add_known_collections(sources)

    repositories = defaultdict(list)
    for source in sources:
        repositories[source.full_name].append(source)

    print(f"LINK_OCCURRENCES={len(sources)}")
    print(f"UNIQUE_URLS={len({x.normalized_url for x in sources})}")
    print(f"UNIQUE_REPOSITORIES={len(repositories)}")
    print("EXTERNAL_CODE_EXECUTED=NO")

    if args.inventory:
        for full_name in sorted(repositories):
            print(full_name)
        return 0

    client = GitHub(args.token)
    max_bytes = args.max_file_mb * 1024 * 1024

    classifications: dict[str, RepoClassification] = {}
    source_bundles: list[dict] = []
    failures: list[dict] = []

    for full_name, repo_sources in sorted(repositories.items()):
        src = repo_sources[0]
        try:
            meta = client.repo(full_name)
            effective_ref, tree_data = client.tree(full_name, src.ref)
            index = tree_index(tree_data)

            classification = classify_repository(meta, repo_sources, index)
            classification.default_branch = effective_ref
            classifications[full_name] = classification

            if args.classify:
                continue

            bundles = discover_bundles(
                client, src, classification, index, max_bytes
            )
            for bundle in bundles:
                # Carry all source URL occurrences for this repository.
                bundle["source_urls"] = sorted({
                    x.original_url for x in repo_sources
                })
                bundle["category"] = src.category
                bundle["classification"] = classification.classification
                bundle["classification_confidence"] = classification.confidence
                source_bundles.append(bundle)

                if args.materialize:
                    write_bundle(external, bundle, client, index, max_bytes)

        except Exception as exc:
            failures.append({
                "repository": full_name,
                "source_urls": sorted({x.original_url for x in repo_sources}),
                "error": str(exc),
            })

    if args.classify:
        save_json(
            external / "_EXTRACTION_CLASSIFICATION.json",
            {
                "generated_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                "repositories": {
                    k: asdict(v) for k, v in classifications.items()
                },
                "failures": failures,
            },
        )

        print("CLASSIFICATION")
        for name in sorted(classifications):
            item = classifications[name]
            print(
                f"{name} | {item.classification} | {item.confidence} | "
                f"{'; '.join(item.reason)}"
            )
        print(f"CLASSIFIED={len(classifications)}")
        print(f"FAILURES={len(failures)}")
        return 0 if not failures else 1

    report = {
        "generated_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "link_occurrences": len(sources),
        "unique_urls": len({x.normalized_url for x in sources}),
        "unique_repositories": len(repositories),
        "classifications": {
            k: asdict(v) for k, v in classifications.items()
        },
        "bundles": source_bundles,
        "failures": failures,
        "external_code_executed": False,
    }

    if args.dry_run:
        save_json(external / "_EXTRACTION_DRY_RUN_V4.json", report)
        print("BUNDLE_DISCOVERY")
        print(f"BUNDLES_FOUND={len(source_bundles)}")
        print(f"FAILURES={len(failures)}")

        # Human-readable summary.
        by_class = defaultdict(int)
        for item in classifications.values():
            by_class[item.classification] += 1
        print("BY_CLASSIFICATION")
        for key in sorted(by_class):
            print(f"{key}={by_class[key]}")

        for bundle in source_bundles:
            print(
                f"{bundle['repository']} | {bundle['classification']} | "
                f"{bundle['root']} | files={bundle['file_count']} | "
                f"markers={len(bundle['skill_markers'])} | "
                f"manifests={len(bundle['manifests'])} | "
                f"external_deps={len(bundle['external_dependencies'])}"
            )
        return 0 if not failures else 1

    save_json(external / "_EXTRACTION_BUNDLE_REPORT_V4.json", report)
    print(f"BUNDLES_MATERIALIZED={len(source_bundles)}")
    print(f"FAILURES={len(failures)}")
    print(f"REPORT={TARGET / '_EXTRACTION_BUNDLE_REPORT_V4.json'}")
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
