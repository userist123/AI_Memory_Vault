"""vault_access cold start: one frontmatter parse per file, a faster YAML loader, a background warm-up.

The first `vault_list("*")` / free-text `vault_resolve` used to build metadata for every route
(4220 files measured: 28 s on a cold cache, against a client tool timeout of 60 s). The fix must be
invisible to callers, so most of this file is about EQUIVALENCE: the route set, the metadata, the
policy decisions and the envelopes are the same with and without the caches and the warm-up.
"""
from __future__ import annotations

import importlib.util
import json
import random
import sys
import threading
from pathlib import Path
from types import SimpleNamespace

import pytest
import yaml

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("vault_access_fixture", REPO / "20_TESTS" / "vault_access_fixture.py")
fx = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fx)

from vault_access import markdown  # noqa: E402
from vault_access.audit import AuditLog  # noqa: E402
from vault_access.core import VaultAccess  # noqa: E402
from vault_access.policy import AccessPolicy  # noqa: E402
from vault_access.router import DomainRouter  # noqa: E402

PRINCIPALS = [("cloud_cli.claude_code", "mcp"), ("cloud_cli.unknown", "mcp"), ("telegram.bot", "ollama"),
              ("cloud_web.export", "export"), ("owner", "cli_owner")]

# Frontmatter shapes beyond the shared fixture: bad YAML, an impossible date, a non-mapping, a BOM,
# a list-valued title, a long frontmatter and code fences containing headings.
EXTRA_FILES = {
    "00_GOVERNANCE/rules/Bad_Yaml.md": "---\na: [1\n---\n# Bad yaml\n\nbody\n",
    "00_GOVERNANCE/rules/Bad_Date.md": "---\ndate: 2026-13-45\ntitle: Impossible date\n---\n# Bad date\n",
    "00_GOVERNANCE/rules/List_Frontmatter.md": "---\n- a\n- b\n---\n# List frontmatter\n",
    "00_GOVERNANCE/rules/Bom.md": "﻿---\ntitle: With BOM\naliases: bom note\n---\n# H1\n",
    "00_GOVERNANCE/rules/Fences.md": "# Top\n\n```\n# not a heading\n```\n\n## Real\n\n~~~\n## nor this\n~~~\n\n### Deep\n",
    "00_GOVERNANCE/rules/No_Heading.md": "just text, no heading at all\n",
    "00_GOVERNANCE/rules/Dup.md": "# A\n\n## Same\n\n## Same\n\n# B\n\n## Same\n",
    "00_GOVERNANCE/rules/Long_Frontmatter.md":
        "---\nid: long-1\nlifecycle: REVIEW\nclassification: internal\nnotes: |\n"
        + "".join(f"  line {i} of a long frontmatter block\n" for i in range(700)) + "---\n# Long\n\n## After\n",
}


def build_vault(tmp_path):
    root, cfg, priv = fx.make_vault(tmp_path)
    for rel, text in EXTRA_FILES.items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    return root, cfg, priv


def make_access(vault, principal, interface, *, cache_path=False, router=None):
    root, cfg, priv = vault
    policy = AccessPolicy.load(cfg / "04_CONFIG" / "access_policy.yaml")
    router = router or DomainRouter(root, cfg / "04_CONFIG" / "vault_domains.yaml", policy,
                                    private_root=priv, cache_path=cache_path)
    return VaultAccess(principal, interface, repo_root=root, policy=policy, router=router,
                       audit=AuditLog(None, enabled=False))


def _strip(envelope):
    env = json.loads(json.dumps(envelope, default=str))
    for key in ("request_id", "ts", "elapsed_ms", "latency_ms", "timestamp"):
        env.pop(key, None)
    return env


def snapshot(vault, router):
    """Everything a caller can observe: routes, their metadata, who may read what, and the answers."""
    base = make_access(vault, "cloud_cli.claude_code", "mcp", router=router)
    base.router.build()
    base.router.load_all_meta()
    routes = {}
    for uri, r in sorted(base.router.routes.items()):
        routes[uri] = [r.route_id, r.domain, r.base, r.path, r.bytes, r.classification, r.trust, r.title,
                       list(r.aliases), list(r.headings), r.lifecycle, r.note_id]
    allowed = {}
    for principal, interface in PRINCIPALS:
        view = make_access(vault, principal, interface, router=router)
        allowed[principal] = sorted(u for u, r in router.routes.items() if view._allowed(r))
    hidden = sorted(set(router.routes) - set(allowed["cloud_cli.claude_code"]))
    ops = {
        "list_star": base.list("*"), "list_governance": base.list("governance"),
        "resolve_uri": base.resolve("vault://governance/vault_state"),
        "resolve_name": base.resolve("VAULT_STATE"), "resolve_text": base.resolve("reguli de operare"),
        "read": base.read("vault://governance/vault_state", line_start=1, line_end=5),
        "traversal": base.read("vault://governance/../../etc/passwd"),
        "bad_section": base.read("vault://governance/vault_state#nope"),
        "meta": base.metadata("vault://governance/vault_state"),
        "meta_long": base.metadata("vault://governance/rules/long_frontmatter"),
        "meta_none": base.metadata(None),
    }
    for uri in hidden:
        ops["hidden:" + uri] = base.read(uri)
    return {"routes": routes, "allowed": allowed, "hidden": hidden, "ops": {k: _strip(v) for k, v in ops.items()}}


def legacy_raw_meta(router, route):
    """The pre-optimisation `_raw_meta`: the frontmatter is re-parsed by every helper that needs it.
    Kept here as the reference the optimised version must agree with, field for field."""
    from pathlib import PurePosixPath

    from vault_access.canonical import contained_path
    from vault_access.markdown import aliases_of, parse_frontmatter, sections
    from vault_access.router import FRONTMATTER_MAX_BYTES, METADATA_BYTES

    def legacy_title(text, fm, fallback):
        title = fm.get("title")
        if isinstance(title, str) and title.strip():
            return title.strip()[:200]
        for sec in sections(text):
            if sec.level == 1:
                return sec.title[:200]
        for sec in sections(text):
            return sec.title[:200]
        return fallback

    base_dir = router.base_dir(route.base)
    with open(contained_path(base_dir, route.path), "rb") as fh:
        head = fh.read(METADATA_BYTES)
    is_md = route.path.lower().endswith(".md")
    text = head.decode("utf-8", errors="ignore")
    fm, fm_ok = {}, True
    if is_md:
        fm, _, fm_ok = parse_frontmatter(text)
        if not fm_ok and len(head) == METADATA_BYTES:
            with open(contained_path(base_dir, route.path), "rb") as fh:
                more = fh.read(FRONTMATTER_MAX_BYTES).decode("utf-8", errors="ignore")
            fm, _, fm_ok = parse_frontmatter(more)
    lifecycle = fm.get("lifecycle")
    declared = fm.get("classification")
    return {
        "frontmatter_error": not fm_ok, "size": route.bytes, "mtime_ns": route.mtime_ns,
        "note_id": str(fm["id"]) if fm.get("id") else None,
        "title": legacy_title(text, fm, PurePosixPath(route.path).stem) if is_md else PurePosixPath(route.path).name,
        "aliases": list(aliases_of(fm)),
        "headings": [s.title[:120] for s in sections(text)[:60]] if is_md else [],
        "lifecycle": str(lifecycle).upper() if isinstance(lifecycle, str) else None,
        "classification": declared.upper() if isinstance(declared, str) else None,
    }


class LegacyRouter(DomainRouter):
    def _raw_meta(self, route):
        return legacy_raw_meta(self, route)


@pytest.fixture
def vault(tmp_path):
    return build_vault(tmp_path)


def _router(vault, cls=DomainRouter, cache_path=False):
    root, cfg, priv = vault
    policy = AccessPolicy.load(cfg / "04_CONFIG" / "access_policy.yaml")
    return cls(root, cfg / "04_CONFIG" / "vault_domains.yaml", policy, private_root=priv, cache_path=cache_path)


# ── equivalence ──────────────────────────────────────────────────────────────────────────
def test_route_set_and_policy_output_are_unchanged_by_the_parse_cache(vault, tmp_path):
    reference = snapshot(vault, _router(vault, LegacyRouter))
    cold = snapshot(vault, _router(vault))
    assert cold == reference

    # a persisted cache (what the second process of a machine loads) gives the same answers, and
    # the first process wrote it
    cache = tmp_path / "state" / "route_meta_cache.json"
    first = snapshot(vault, _router(vault, cache_path=cache))
    assert cache.is_file()
    second = snapshot(vault, _router(vault, cache_path=cache))
    assert first == second == reference
    assert reference["hidden"], "the fixture must contain routes hidden from agents"
    assert reference["ops"]["traversal"]["code"] == "INVALID_URI"
    assert all(reference["ops"]["hidden:" + u]["code"] == "NOT_FOUND" for u in reference["hidden"])
    assert reference["ops"]["read"]["cite_as"].startswith("vault://governance/vault_state sha256:")


def test_every_frontmatter_shape_gets_the_same_metadata_as_before(vault):
    new, old = _router(vault), _router(vault, LegacyRouter)
    for r in (new, old):
        r.build()
        r.load_all_meta()
    assert set(new.routes) == set(old.routes)
    for uri, route in new.routes.items():
        o = old.routes[uri]
        assert (route.title, route.aliases, route.headings, route.lifecycle, route.note_id,
                route.classification, route.trust) == \
               (o.title, o.aliases, o.headings, o.lifecycle, o.note_id, o.classification, o.trust), uri


def test_an_impossible_date_makes_that_note_unreadable_not_the_whole_table(vault):
    """`date: 2026-13-45` is well-formed YAML with an impossible scalar: yaml raised ValueError, which
    escaped the route build and broke vault_resolve / vault_list("*") for every note."""
    router = _router(vault)
    router.build()
    router.load_all_meta()  # must not raise
    bad = router.routes["vault://governance/rules/bad_date"]
    assert bad.trust == "archived" and bad.classification == router.policy.classifications[-1]
    assert router.routes["vault://governance/rules/rules"].trust == "verified"
    view = make_access(vault, "cloud_cli.claude_code", "mcp", router=router)
    assert view.read("vault://governance/rules/bad_date")["code"] == "NOT_FOUND"
    assert view.resolve("reguli de operare")["code"] in ("OK", "AMBIGUOUS")


def test_sections_match_a_naive_reference_on_random_documents():
    def naive(text):
        lines = text.split("\n")
        fm_lines = markdown.parse_frontmatter(text)[1]
        found, in_fence = [], False
        for no, line in enumerate(lines, start=1):
            if no <= fm_lines:
                continue
            if markdown._FENCE_RE.match(line):
                in_fence = not in_fence
                continue
            if in_fence:
                continue
            m = markdown._HEADING_RE.match(line)
            if m:
                found.append((no, len(m.group(1)), m.group(2).strip()))
        out, seen = [], {}
        for i, (no, level, title) in enumerate(found):
            end = len(lines)
            for later_no, later_level, _ in found[i + 1:]:
                if later_level <= level:
                    end = later_no - 1
                    break
            anchor = markdown.anchor_for(title)
            if anchor in seen:
                seen[anchor] += 1
                anchor = f"{anchor}-{seen[anchor]}"
            else:
                seen[anchor] = 1
            out.append(markdown.Section(anchor, title, level, no, max(no, end)))
        return out

    rng = random.Random(214)
    pieces = ["# One", "## Two", "### Three", "#### Four", "###### Six", "####### Seven", "#NoSpace", " # indented",
              "```", "~~~", "  ```python", "plain text", "", "# Same", "## Same", "Éclair # x", "#\ttab heading #"]
    for _ in range(300):
        body = "\n".join(rng.choice(pieces) for _ in range(rng.randint(0, 40)))
        text = ("---\ntitle: x\n---\n" if rng.random() < 0.3 else "") + body + ("\n" if rng.random() < 0.5 else "")
        assert markdown.sections(text) == naive(text), repr(text)
        # passing the parsed line count must not change anything
        assert markdown.sections(text, markdown.parse_frontmatter(text)[1]) == naive(text)


# ── one parse per file ───────────────────────────────────────────────────────────────────
def _count_yaml_loads(monkeypatch):
    """Count the YAML documents the FRONTMATTER parser loads (not the policy / domain config files,
    which go through the global `yaml` module)."""
    calls = []

    def counting(stream, Loader=None):  # noqa: N803 - PyYAML's own name
        calls.append(stream)
        return yaml.load(stream, Loader=Loader)

    proxy = SimpleNamespace(load=counting, YAMLError=yaml.YAMLError, SafeLoader=yaml.SafeLoader,
                            CSafeLoader=getattr(yaml, "CSafeLoader", None))
    monkeypatch.setattr(markdown, "yaml", proxy)
    return calls


def test_each_file_frontmatter_is_parsed_once_per_index_build(vault, monkeypatch, tmp_path):
    calls = _count_yaml_loads(monkeypatch)
    cache = tmp_path / "state" / "route_meta_cache.json"
    router = _router(vault, cache_path=cache)
    router.build()
    router.load_all_meta()
    with_frontmatter = [r for r in router.routes.values()
                        if r.path.endswith(".md") and (Path(vault[0] if r.base == "repo" else vault[2]) / r.path)
                        .read_text(encoding="utf-8", errors="ignore").lstrip("﻿").startswith("---")]
    # exactly one parse per file (the long-frontmatter note is read twice, but its first, truncated
    # read never reaches the YAML loader)
    assert len(calls) == len(with_frontmatter), (len(calls), len(with_frontmatter))

    before = len(calls)
    router.load_all_meta()          # in-memory: nothing to do
    router.build(force=True)        # rebuilt table, same files: metadata comes from the cache
    router.load_all_meta()
    assert len(calls) == before

    fresh = _router(vault, cache_path=cache)   # another process: persisted cache
    fresh.build()
    fresh.load_all_meta()
    assert len(calls) == before


def test_title_and_headings_do_not_reparse_the_frontmatter(monkeypatch):
    calls = _count_yaml_loads(monkeypatch)
    text = "---\nid: a\n---\n# Title\n\n## Sub\n"
    fm, lines, ok = markdown.parse_frontmatter(text)
    assert len(calls) == 1 and ok
    heads = markdown.heading_scan(text, lines)
    assert markdown.title_of(text, fm, "fallback", heads) == "Title"
    assert [h[2] for h in heads] == ["Title", "Sub"]
    assert len(calls) == 1


# ── loader selection ─────────────────────────────────────────────────────────────────────
def test_the_c_loader_is_used_when_available_and_the_python_loader_is_the_fallback():
    assert markdown._YAML_LOADER is getattr(yaml, "CSafeLoader", yaml.SafeLoader)
    assert issubclass(markdown._YAML_LOADER, yaml.SafeLoader) or markdown._YAML_LOADER.__name__ == "CSafeLoader"
    # both loaders read the same safe data model, and neither executes tags
    text = "---\ntitle: T\naliases: [a, b]\nn: 3\nd: 2026-10-07\n---\n"
    data_default = markdown.parse_frontmatter(text)
    data_python = yaml.load("title: T\naliases: [a, b]\nn: 3\nd: 2026-10-07", Loader=yaml.SafeLoader)
    assert data_default[0] == data_python and data_default[2]
    evil = "---\na: !!python/object/apply:os.system ['echo x']\n---\n"
    assert markdown.parse_frontmatter(evil)[2] is False


# ── warm-up ──────────────────────────────────────────────────────────────────────────────
def test_warm_builds_everything_the_first_listing_needs_and_is_idempotent(vault, monkeypatch):
    calls = _count_yaml_loads(monkeypatch)
    router = _router(vault)
    count = router.warm()
    assert count == len(router.routes) > 10
    assert all(r._meta_loaded for r in router.routes.values())
    after_warm = len(calls)
    router.warm()
    view = make_access(vault, "cloud_cli.claude_code", "mcp", router=router)
    view.list("*")
    view.resolve("reguli de operare")
    assert len(calls) == after_warm, "the first real call must not pay for the metadata again"


def test_a_request_during_the_warm_up_gets_the_same_answer(vault):
    reference = snapshot(vault, _router(vault, LegacyRouter))
    router = _router(vault)
    results = {}
    started = threading.Event()

    def background():
        started.set()
        router.warm()

    thread = threading.Thread(target=background)
    thread.start()
    started.wait(5)
    view = make_access(vault, "cloud_cli.claude_code", "mcp", router=router)
    results["listing"] = _strip(view.list("*"))
    results["resolve"] = _strip(view.resolve("VAULT_STATE"))
    thread.join(30)
    assert not thread.is_alive()
    assert results["listing"] == reference["ops"]["list_star"]
    assert results["resolve"] == reference["ops"]["resolve_name"]
    assert snapshot(vault, router) == reference


def test_concurrent_route_loading_parses_each_file_once(vault, monkeypatch):
    def parses(workers):
        calls = _count_yaml_loads(monkeypatch)
        router = _router(vault)
        router.build()
        threads = [threading.Thread(target=router.load_all_meta) for _ in range(workers)]
        for t in threads:
            t.start()
        for t in threads:
            t.join(30)
        assert all(r._meta_loaded for r in router.routes.values())
        return len(calls)

    one = parses(1)
    assert one >= 1
    assert parses(6) == one, "six racing threads must do exactly the work one thread does"


def test_the_cache_file_is_written_atomically_per_process(vault, tmp_path):
    cache = tmp_path / "state" / "route_meta_cache.json"
    router = _router(vault, cache_path=cache)
    router.warm()
    data = json.loads(cache.read_text(encoding="utf-8"))
    assert data["version"] == 2 and data["entries"]
    assert not list(cache.parent.glob("*.tmp")), "temporary files must not be left behind"


# ── the MCP server starts the warm-up without blocking ───────────────────────────────────
def _server_module():
    sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
    from interfaces import memory_mcp_server
    return memory_mcp_server


def test_the_server_warms_the_router_in_a_background_thread(vault, monkeypatch):
    server = _server_module()
    router = _router(vault)
    access = make_access(vault, "cloud_cli.claude_code", "mcp", router=router)
    monkeypatch.setattr(server, "_access", access)
    monkeypatch.setattr(server, "_warm_thread", None)
    monkeypatch.delenv(server.WARM_ENV, raising=False)
    thread = server.start_warmup()
    assert isinstance(thread, threading.Thread) and thread.daemon
    assert server.start_warmup() is thread, "starting twice must not start a second warm-up"
    thread.join(30)
    assert not thread.is_alive()
    assert router.routes and all(r._meta_loaded for r in router.routes.values())


def test_the_warm_up_can_be_disabled_and_its_failure_is_not_fatal(vault, monkeypatch, capsys):
    server = _server_module()
    monkeypatch.setattr(server, "_warm_thread", None)
    monkeypatch.setenv(server.WARM_ENV, "0")
    assert server.start_warmup() is None

    def boom():
        raise RuntimeError("no such principal")

    monkeypatch.setattr(server, "_get_access", boom)
    server._warm()   # must swallow the error
    captured = capsys.readouterr()
    assert captured.out == "", "stdout belongs to the MCP protocol"
    assert "warm-up failed" in captured.err

    from vault_access.errors import ErrorCode, VaultAccessError

    def refused():
        raise VaultAccessError(ErrorCode.DENIED_POLICY, "principal owner cannot be asserted through mcp")

    monkeypatch.setattr(server, "_get_access", refused)
    server._warm()
    captured = capsys.readouterr()
    assert captured.out == ""
    assert "refused" in captured.err and "DENIED_POLICY" in captured.err


def test_get_access_creates_one_instance_under_concurrency(vault, monkeypatch):
    server = _server_module()
    created = []

    class Fake:
        def __init__(self, *a, **k):
            created.append(1)

    import vault_access.core as core
    monkeypatch.setattr(core, "VaultAccess", Fake)
    monkeypatch.setattr(server, "_access", None)
    got = []
    threads = [threading.Thread(target=lambda: got.append(server._get_access())) for _ in range(8)]
    for t in threads:
        t.start()
    for t in threads:
        t.join(10)
    assert len(created) == 1 and len({id(g) for g in got}) == 1
    monkeypatch.setattr(server, "_access", None)  # leave no fake behind


def test_mcp_cold_start_over_stdio_serves_a_listing_and_a_resolve(vault, tmp_path):
    """A real subprocess with the warm-up on: the handshake, then the first listing (which waits for the
    warm-up it shares work with) and a resolve, from a cold per-user directory."""
    import asyncio
    import os

    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    root, cfg, priv = vault
    env = {**os.environ, "AI_MEMORY_VAULT_HOME": str(tmp_path / "state"), "MEMORY_VAULT_ROOT": str(root),
           "MEMORY_CONTROLLER_HMAC_SECRET": "test-secret-for-the-perf-test-" + "x" * 20,
           "PYTHONIOENCODING": "utf-8"}
    env.pop("VAULT_ACCESS_WARM", None)
    server = REPO / "03_IMPLEMENTATION" / "packages" / "interfaces" / "memory_mcp_server.py"

    async def go():
        params = StdioServerParameters(command=sys.executable, args=[str(server), "--principal", "cloud_cli.claude_code"],
                                       env=env, cwd=str(REPO))
        async with stdio_client(params) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                out = {}
                for name, args in (("vault_list", {"domain": "*"}), ("vault_resolve", {"query": "VAULT_STATE"})):
                    res = await session.call_tool(name, args)
                    assert not res.isError, res.content
                    sc = res.structuredContent or {}
                    out[name] = sc.get("result", sc)
                return out

    out = asyncio.run(asyncio.wait_for(go(), 120))
    assert out["vault_list"]["ok"] is True and out["vault_list"]["count"] >= 1
    assert out["vault_resolve"]["code"] == "OK"
