"""The direct route table: every routable file of every domain gets one `vault://` URI.

The table is built at runtime from 04_CONFIG/vault_domains.yaml by scanning ONLY the declared
roots (never the whole tree), applying the domain excludes and the hard denylist of the access
policy. Nothing generated is committed. Rebuilds are incremental: a file whose size and mtime
did not change keeps its parsed metadata.

Resolution (`resolve`) is deterministic and metadata-only: route id, URI, file stem, title,
aliases, headings and the domain's keywords. It never reads note bodies and never ranks by
model output; content search stays with MemoryController (see core.VaultAccess.search).
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import threading
import time
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import Dict, Iterable, Iterator, List, Optional, Sequence, Tuple

import yaml

from .canonical import (contained_path, domain_id, fold, glob_match, has_glob, slugify,
                        static_prefix)
from .errors import ErrorCode, VaultAccessError
from .markdown import aliases_of, heading_scan, parse_frontmatter, title_of
from .policy import TRUST_LEVELS, AccessPolicy

PRIVATE_ROOT_ENV = "AI_MEMORY_VAULT_PRIVATE_ROOT"
DOMAIN_KEYS = {"title", "roots", "expand", "base", "extensions", "exclude", "classification",
               "trust", "keywords"}
# Lifecycle is an ALLOWLIST: anything not listed here (ARCHIVED, RAW, SUPERSEDED, typos...) is
# treated as archived and is not served to agents.
ACTIVE_LIFECYCLES = {None, "ACTIVE"}
UNVERIFIED_LIFECYCLES = {"REVIEW", "DRAFT", "CANDIDATE", "PROPOSED"}
METADATA_BYTES = 16384
FRONTMATTER_MAX_BYTES = 262144
CACHE_VERSION = 2
_STOP = {"si", "sau", "de", "la", "in", "din", "cu", "pe", "pentru", "ce", "cum", "care", "este",
         "sunt", "un", "o", "a", "al", "ale", "lui", "le", "se", "nu", "the", "of", "and", "or",
         "to", "for", "a", "an", "is", "are", "what", "how", "in", "on", "vault", "fisier", "file",
         "scrie", "spune", "citeste", "arata", "deschide", "despre", "about", "show", "read", "md",
         "continut", "continutul", "fisierul", "fisierului", "mi", "te", "rog", "contine"}


def _default_cache_path() -> Optional[Path]:
    try:
        from interfaces import vault_runtime
        return vault_runtime.state_dir() / "route_meta_cache.json"
    except Exception:  # noqa: BLE001
        return None


@dataclass(frozen=True)
class DomainSpec:
    id: str
    title: str
    roots: Tuple[str, ...]
    base: str
    extensions: Tuple[str, ...]
    exclude: Tuple[str, ...]
    classification: str
    trust: str
    keywords: Tuple[str, ...]
    parent: Optional[str] = None


@dataclass
class Route:
    route_id: str
    uri: str
    domain: str
    base: str            # "repo" | "private"
    path: str            # POSIX path relative to the base
    bytes: int
    mtime_ns: int
    classification: str
    trust: str
    note_id: Optional[str] = None
    title: str = ""
    aliases: Tuple[str, ...] = ()
    headings: Tuple[str, ...] = ()
    lifecycle: Optional[str] = None
    _meta_loaded: bool = False
    _tok: Optional[Tuple[Tuple[str, ...], Tuple[str, ...], Tuple[str, ...]]] = None

    def public(self) -> Dict[str, object]:
        return {"route_id": self.route_id, "uri": self.uri, "domain": self.domain,
                "title": self.title, "classification": self.classification, "trust": self.trust,
                "lifecycle": self.lifecycle, "note_id": self.note_id, "bytes": self.bytes}


class DomainRegistry:
    def __init__(self, domains: Dict[str, DomainSpec], defaults_exclude: Tuple[str, ...],
                 problems: List[str]):
        self.domains = domains
        self.defaults_exclude = defaults_exclude
        self.problems = problems

    @classmethod
    def load(cls, path: Path, repo_root: Path, policy: AccessPolicy,
             private_root: Optional[Path] = None) -> "DomainRegistry":
        data = yaml.safe_load(Path(path).read_text(encoding="utf-8")) or {}
        if data.get("version") != 1:
            raise ValueError("vault_domains: unsupported version")
        defaults = data.get("defaults") or {}
        d_ext = tuple(defaults.get("extensions") or (".md",))
        d_cls = defaults.get("classification", "INTERNAL")
        d_trust = defaults.get("trust", "verified")
        d_excl = tuple(defaults.get("exclude") or ())
        problems: List[str] = []
        domains: Dict[str, DomainSpec] = {}
        for raw_id, spec in (data.get("domains") or {}).items():
            spec = spec or {}
            unknown = set(spec) - DOMAIN_KEYS
            if unknown:
                problems.append(f"DOMAIN_UNKNOWN_KEYS:{raw_id}:{sorted(unknown)}")
            did = str(raw_id)
            if not re.fullmatch(r"[a-z0-9_]+(?:\.[a-z0-9_]+)*", did):
                problems.append(f"DOMAIN_BAD_ID:{did}")
                continue
            base = spec.get("base", "repo")
            classification = spec.get("classification", d_cls)
            trust = spec.get("trust", d_trust)
            if base not in ("repo", "private"):
                problems.append(f"DOMAIN_BAD_BASE:{did}")
            if classification not in policy.classifications:
                problems.append(f"DOMAIN_BAD_CLASSIFICATION:{did}")
                classification = policy.classifications[-1]
            if trust not in TRUST_LEVELS:
                problems.append(f"DOMAIN_BAD_TRUST:{did}")
                trust = "untrusted"
            roots = tuple(str(r) for r in spec.get("roots") or ())
            if not roots:
                problems.append(f"DOMAIN_NO_ROOTS:{did}")
            for r in roots:
                if r.startswith("/") or "\\" in r or ".." in PurePosixPath(r).parts or ":" in r:
                    problems.append(f"DOMAIN_ROOT_ESCAPES:{did}:{r}")
            common = dict(title=str(spec.get("title") or did), base=base,
                          extensions=tuple(spec.get("extensions") or d_ext),
                          exclude=tuple(spec.get("exclude") or ()),
                          classification=classification, trust=trust,
                          keywords=tuple(str(k) for k in spec.get("keywords") or ()))
            if spec.get("expand") == "subdirs":
                if len(roots) != 1 or has_glob(roots[0]):
                    problems.append(f"DOMAIN_EXPAND_NEEDS_ONE_DIR:{did}")
                    continue
                base_dir = repo_root if base == "repo" else private_root
                domains[did] = DomainSpec(did, roots=roots, parent=None, **common)
                root_dir = (base_dir / roots[0]) if base_dir else None
                if root_dir and root_dir.is_dir():
                    for child in sorted(root_dir.iterdir()):
                        if child.is_dir() and not child.name.startswith(".") and not child.is_symlink():
                            cid = f"{did}.{domain_id(child.name)}"
                            domains[cid] = DomainSpec(
                                cid, roots=(f"{roots[0]}/{child.name}",), parent=did,
                                **{**common, "title": f"{common['title']} — {child.name}",
                                   "keywords": common["keywords"] + (child.name.replace("_", " ").replace("-", " "),)})
                # the parent keeps only the files directly inside the root
                domains[did] = DomainSpec(did, roots=(f"{roots[0]}/*",), parent=None, **common)
            elif spec.get("expand") not in (None, ""):
                problems.append(f"DOMAIN_BAD_EXPAND:{did}")
            else:
                domains[did] = DomainSpec(did, roots=roots, **common)
        return cls(domains, d_excl, problems)


class DomainRouter:
    """Route table over the registry, rebuilt incrementally at most every `ttl` seconds."""

    def __init__(self, repo_root: Path, registry_path: Path, policy: AccessPolicy,
                 private_root: Optional[Path] = None, ttl: float = 120.0, cache_path=None):
        self.repo_root = Path(repo_root).resolve()
        env_private = os.environ.get(PRIVATE_ROOT_ENV)
        self.private_root = Path(private_root or env_private).resolve() if (private_root or env_private) else None
        self.registry_path = Path(registry_path)
        self.policy = policy
        self.ttl = ttl
        self._lock = threading.RLock()
        self._built_at = 0.0
        self._registry_mtime = 0
        self.registry: Optional[DomainRegistry] = None
        self.routes: Dict[str, Route] = {}          # uri -> route
        self.by_id: Dict[str, Route] = {}
        self.by_path: Dict[Tuple[str, str], Route] = {}
        self.problems: List[str] = []
        self.notes: List[str] = []
        self.cache_path = cache_path if cache_path is not False else None
        if cache_path is None:
            self.cache_path = _default_cache_path()
        self._meta_cache: Dict[str, Dict[str, object]] = {}
        self._cache_dirty = False
        self._load_cache()

    # ── building ─────────────────────────────────────────────────────────────────────────
    def base_dir(self, base: str) -> Optional[Path]:
        return self.repo_root if base == "repo" else self.private_root

    def _iter_files(self, spec: DomainSpec) -> Iterator[str]:
        base_dir = self.base_dir(spec.base)
        if base_dir is None or not base_dir.is_dir():
            return
        for root in spec.roots:
            if has_glob(root):
                start_rel = static_prefix(root)
                pattern = root
            else:
                start_rel = root
                pattern = None
            if start_rel in ("", "."):
                start = base_dir
            else:
                try:  # refuses a root that is, or sits under, a symlink/junction
                    start = contained_path(base_dir, PurePosixPath(start_rel).as_posix())
                except VaultAccessError:
                    continue
            if start.is_file() and pattern is None:
                yield PurePosixPath(start_rel).as_posix()
                continue
            if not start.is_dir() or start.is_symlink():
                continue
            for dirpath, dirnames, filenames in os.walk(start, followlinks=False):
                dirnames[:] = sorted(d for d in dirnames if d not in (".git", "node_modules", "__pycache__")
                                     and not os.path.islink(os.path.join(dirpath, d)))
                rel_dir = Path(dirpath).relative_to(base_dir.resolve()).as_posix()
                for name in sorted(filenames):
                    rel = name if rel_dir == "." else f"{rel_dir}/{name}"
                    if pattern is None or glob_match(rel, pattern):
                        yield rel

    def _slug(self, spec: DomainSpec, rel: str) -> str:
        # Strip the static prefix of the most specific root that contains the file.
        within = rel
        best = -1
        for root in spec.roots:
            anchor_dir = (static_prefix(root) if has_glob(root) else root).rstrip("/")
            if anchor_dir in ("", "."):
                if best < 0:
                    within, best = rel, 0
            elif rel == anchor_dir:
                if len(anchor_dir) > best:
                    within, best = PurePosixPath(rel).name, len(anchor_dir)
            elif rel.startswith(anchor_dir + "/") and len(anchor_dir) > best:
                within, best = rel[len(anchor_dir) + 1:], len(anchor_dir)
        p = PurePosixPath(within)
        stem = p.with_suffix("") if p.suffix.lower() == ".md" else p
        parts = [slugify(part) for part in stem.parts]
        if parts and parts[-1] == "skill" and len(parts) > 1:
            parts = parts[:-1]
        return "/".join(part for part in parts if part) or "index"

    def _route_id(self, base: str, rel: str) -> str:
        return "R-" + hashlib.sha256(f"{base}:{rel}".encode("utf-8")).hexdigest()[:10]

    def build(self, force: bool = False) -> None:
        with self._lock:
            reg_mtime = self.registry_path.stat().st_mtime_ns
            if not force and self.registry and time.monotonic() - self._built_at < self.ttl \
                    and reg_mtime == self._registry_mtime:
                return
            registry = DomainRegistry.load(self.registry_path, self.repo_root, self.policy, self.private_root)
            problems = list(registry.problems)
            max_bytes = self.policy.limits.get("max_file_bytes", 2_000_000)
            claimed: Dict[Tuple[str, str], str] = {}
            pending: List[Tuple[str, Route]] = []
            # More specific domains (more dots, then longer root) claim a file first.
            order = sorted(registry.domains.values(),
                           key=lambda s: (-s.id.count("."), -max(len(r) for r in s.roots), s.id))
            for spec in order:
                for rel in self._iter_files(spec):
                    if PurePosixPath(rel).suffix.lower() not in spec.extensions:
                        continue
                    if any(glob_match(rel, pat) for pat in registry.defaults_exclude + spec.exclude):
                        continue
                    if self.policy.denied_path(rel):
                        continue
                    key = (spec.base, rel)
                    if key in claimed:
                        continue
                    base_dir = self.base_dir(spec.base)
                    try:
                        st = (base_dir / rel).stat()
                    except OSError:
                        continue
                    if st.st_size > max_bytes:
                        problems.append(f"ROUTE_TOO_LARGE:{spec.id}:{rel}")
                        continue
                    claimed[key] = spec.id
                    uri = f"vault://{spec.id}/{self._slug(spec, rel)}"
                    pending.append((uri, Route(self._route_id(spec.base, rel), uri, spec.id, spec.base, rel,
                                               st.st_size, st.st_mtime_ns, spec.classification, spec.trust)))
            # Collisions (two files with the same slug, e.g. README.md under two roots): EVERY file
            # sharing the URI gets its full repository path as slug, so adding a file can never take
            # over a URI another file had — the old URI simply stops resolving.
            counts: Dict[str, int] = {}
            for uri, _ in pending:
                counts[uri] = counts.get(uri, 0) + 1
            routes: Dict[str, Route] = {}
            by_path: Dict[Tuple[str, str], Route] = {}
            notes: List[str] = []
            for uri, route in pending:
                if counts[uri] > 1:
                    full = PurePosixPath(route.path)
                    full = full.with_suffix("") if full.suffix.lower() == ".md" else full
                    slug = "/".join(p for p in (slugify(x) for x in full.parts) if p)
                    route.uri = f"vault://{route.domain}/{slug}"
                    if route.uri in routes:  # still taken: a deterministic hash of the path
                        route.uri += "-h" + hashlib.sha256(f"{route.base}:{route.path}".encode()).hexdigest()[:8]
                    notes.append(f"URI_DISAMBIGUATED:{uri}:{route.path}")
                routes[route.uri] = route
                by_path[(route.base, route.path)] = route
            self.notes = notes
            self.registry = registry
            self.routes = routes
            self.by_id = {r.route_id: r for r in routes.values()}
            self.by_path = by_path
            self.problems = problems
            self._built_at = time.monotonic()
            self._registry_mtime = reg_mtime

    # ── metadata ─────────────────────────────────────────────────────────────────────────
    def _raw_meta(self, route: Route) -> Dict[str, object]:
        """Parsed metadata of one file. The frontmatter is parsed ONCE and its line count and the
        heading scan are reused for title and headings (they used to re-parse it ~3 more times)."""
        base_dir = self.base_dir(route.base)
        try:
            path = contained_path(base_dir, route.path)
            with open(path, "rb") as fh:
                head = fh.read(METADATA_BYTES)
        except (OSError, VaultAccessError):
            head = b""
        is_md = route.path.lower().endswith(".md")
        text = head.decode("utf-8", errors="ignore")
        fm, fm_ok = {}, True
        heads: List = []
        if is_md:
            fm, head_fm_lines, fm_ok = parse_frontmatter(text)
            if not fm_ok and len(head) == METADATA_BYTES:   # long frontmatter: read further, bounded
                try:
                    with open(contained_path(base_dir, route.path), "rb") as fh:
                        more = fh.read(FRONTMATTER_MAX_BYTES).decode("utf-8", errors="ignore")
                    fm, _, fm_ok = parse_frontmatter(more)
                except (OSError, VaultAccessError):
                    fm_ok = False
            # headings are scanned in the first METADATA_BYTES, skipping the frontmatter lines seen there
            heads = heading_scan(text, head_fm_lines)
        lifecycle = fm.get("lifecycle")
        declared = fm.get("classification")
        return {
            "frontmatter_error": not fm_ok,
            "size": route.bytes, "mtime_ns": route.mtime_ns,
            "note_id": str(fm["id"]) if fm.get("id") else None,
            "title": title_of(text, fm, PurePosixPath(route.path).stem, heads) if is_md else PurePosixPath(route.path).name,
            "aliases": list(aliases_of(fm)),
            "headings": [title[:120] for _, _, title in heads[:60]] if is_md else [],
            "lifecycle": str(lifecycle).upper() if isinstance(lifecycle, str) else None,
            "classification": declared.upper() if isinstance(declared, str) else None,
        }

    def _apply_meta(self, route: Route, raw: Dict[str, object]) -> Route:
        spec = self.registry.domains[route.domain]
        route.note_id = raw.get("note_id")
        route.title = str(raw.get("title") or PurePosixPath(route.path).stem)
        route.aliases = tuple(raw.get("aliases") or ())
        route.headings = tuple(raw.get("headings") or ())
        route.lifecycle = raw.get("lifecycle")
        route.classification = spec.classification
        route.trust = spec.trust
        # A note may RAISE its label, never lower it: anything that can edit a note (an agent) could
        # otherwise publish it. Lowering is done by moving the note or changing the domain config.
        declared = raw.get("classification")
        if isinstance(declared, str):
            if declared not in self.policy.classifications:
                route.classification = self.policy.classifications[-1]
            elif self.policy.rank(declared) > self.policy.rank(spec.classification):
                route.classification = declared
        if raw.get("frontmatter_error"):
            # An unreadable label is not an absent label: fail closed.
            route.classification = self.policy.classifications[-1]
            route.trust = "archived"
        elif route.lifecycle in UNVERIFIED_LIFECYCLES:
            if route.trust == "verified":
                route.trust = "unverified"
        elif route.lifecycle not in ACTIVE_LIFECYCLES:
            route.trust = "archived"
        route._meta_loaded = True
        route._tok = None
        return route

    def load_meta(self, route: Route) -> Route:
        if route._meta_loaded:
            return route
        # Per-route lock: a background warm-up and a request thread never parse (or half-apply) the
        # same route twice, yet a single-route call does not wait for the whole warm-up.
        with self._lock:
            if route._meta_loaded:
                return route
            key = f"{route.base}:{route.path}"
            cached = self._meta_cache.get(key)
            if not (cached and cached.get("size") == route.bytes and cached.get("mtime_ns") == route.mtime_ns):
                cached = self._raw_meta(route)
                self._meta_cache[key] = cached
                self._cache_dirty = True
            return self._apply_meta(route, cached)

    def load_all_meta(self) -> None:
        for route in list(self.routes.values()):
            self.load_meta(route)
        with self._lock:
            self._save_cache()

    def warm(self) -> int:
        """Build the route table and the metadata of every route (what the first free-text
        resolve or `vault_list("*")` needs), so that call does not pay for it. Safe to run in a
        background thread while requests are served. Returns the number of routes."""
        self.build()
        self.load_all_meta()
        return len(self.routes)

    # ── metadata cache (per user, outside the repository) ────────────────────────────────
    def _load_cache(self) -> None:
        if self.cache_path is None or not self.cache_path.is_file():
            return
        try:
            data = json.loads(self.cache_path.read_text(encoding="utf-8"))
            if data.get("version") == CACHE_VERSION and data.get("repo") == str(self.repo_root):
                self._meta_cache = dict(data.get("entries") or {})
        except (OSError, ValueError):
            self._meta_cache = {}

    def _save_cache(self) -> None:
        if self.cache_path is None or not self._cache_dirty:
            return
        live = {f"{r.base}:{r.path}" for r in self.routes.values()}
        entries = {k: v for k, v in self._meta_cache.items() if k in live}
        try:
            self.cache_path.parent.mkdir(parents=True, exist_ok=True)
            tmp = self.cache_path.with_suffix(f".{os.getpid()}.tmp")  # one per process: clients start in parallel
            tmp.write_text(json.dumps({"version": CACHE_VERSION, "repo": str(self.repo_root),
                                       "entries": entries}, ensure_ascii=False), encoding="utf-8")
            os.replace(tmp, self.cache_path)
            self._cache_dirty = False
        except OSError:
            pass

    # ── lookup ───────────────────────────────────────────────────────────────────────────
    def get(self, uri_base: str) -> Optional[Route]:
        self.build()
        route = self.routes.get(uri_base)
        return self.load_meta(route) if route else None

    def get_by_path(self, base: str, rel: str) -> Optional[Route]:
        self.build()
        route = self.by_path.get((base, rel))
        return self.load_meta(route) if route else None

    def domain_ids(self) -> List[str]:
        self.build()
        return sorted(self.registry.domains)

    def routes_in(self, domain: str) -> List[Route]:
        self.build()
        return sorted((r for r in self.routes.values() if r.domain == domain), key=lambda r: r.uri)


# ── resolution ───────────────────────────────────────────────────────────────────────────
def tokens(text: str) -> List[str]:
    text = re.sub(r"([a-z])([A-Z])", r"\1 \2", text)
    return [t for t in re.split(r"[^a-z0-9]+", fold(text)) if t and t not in _STOP and len(t) > 1]


def field_index(field_tokens: Iterable[str]) -> Tuple[frozenset, frozenset]:
    ftoks = frozenset(field_tokens)
    return ftoks, frozenset(t[:5] for t in ftoks if len(t) >= 5)


def _field_score(qtoks: Sequence[str], index: Tuple[frozenset, frozenset]) -> float:
    ftoks, prefixes = index
    if not ftoks:
        return 0.0
    score = 0.0
    for q in qtoks:
        if q in ftoks:
            score += 1.0
        elif len(q) >= 5 and q[:5] in prefixes:
            score += 0.6
    return score


def _route_tokens(route: Route):
    if route._tok is None:
        stem = PurePosixPath(route.path).stem
        leaf = route.uri.rsplit("/", 1)[-1]
        exact = tuple([route.route_id.lower(), route.uri, fold(stem), fold(route.title), leaf,
                       leaf.replace("-", " ").replace("_", " ")]
                      + [fold(a) for a in route.aliases])
        name = tuple(tokens(stem) + tokens(leaf) + tokens(route.title) + [t for a in route.aliases for t in tokens(a)])
        head = tuple(t for h in route.headings for t in tokens(h))
        route._tok = (exact, field_index(name), field_index(head))
    return route._tok


def score_route(route: Route, query: str, qtoks: Sequence[str],
                domain_kw: Dict[str, Tuple[frozenset, frozenset]]) -> float:
    qf = fold(query).strip()
    exact, name, head = _route_tokens(route)
    if qf in exact or qf.replace(" ", "_") in exact:
        return 100.0
    if not qtoks:
        return 0.0
    kw = domain_kw.get(route.domain, (frozenset(), frozenset()))
    stem_toks = set(tokens(PurePosixPath(route.path).stem))
    bonus = 3.0 if stem_toks and stem_toks <= set(qtoks) else 0.0
    raw = bonus + 3.0 * _field_score(qtoks, name) + 1.0 * _field_score(qtoks, head) + 1.5 * _field_score(qtoks, kw)
    return round(10.0 * raw / (3.0 * len(qtoks)), 3)
