"""Unit tests for the per-note route coverage gate in 30_SCRIPTS/routing/build_route_manifest.py.

The real registry is exercised end to end by test_vault_access_clients.py::
test_the_real_registry_passes_its_ci_check; these tests pin the rules on small synthetic inputs so a change
that weakens the gate (a note silently left without a route) fails here first.
"""
import importlib.util
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("build_route_manifest",
                                               REPO / "30_SCRIPTS" / "routing" / "build_route_manifest.py")
brm = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(brm)

NEVER_DENIED = lambda path: False  # noqa: E731
REASON = "a reason long enough to pass"


def _cov(tracked, routed=(), unrouted=(), specs=(), defaults=(), denied=NEVER_DENIED):
    return brm.coverage_problems(tracked, set(routed), list(unrouted), list(specs), tuple(defaults), denied)


def test_a_note_without_route_or_reason_fails():
    assert _cov(["02_PRODUCT/projects/New/spec.md"]) == ["NOTE_WITHOUT_ROUTE:02_PRODUCT/projects/New/spec.md"]


def test_routed_unrouted_excluded_and_denied_notes_pass():
    tracked = ["a/routed.md", ".agents/worker_x/handoff.md", "kb/test_0123abcd.md", "kb/secrets/x.md"]
    problems = _cov(tracked, routed=["a/routed.md"],
                    unrouted=[{"glob": ".agents/worker*/**", "reason": REASON}],
                    specs=[(("kb",), ())], defaults=("**/test_[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f].md",),
                    denied=lambda p: "/secrets/" in p)
    assert problems == []


def test_an_exclude_pattern_only_counts_under_the_root_of_its_domain():
    # the domain covering `kb` excludes test notes; the same file name elsewhere is still a gap
    problems = _cov(["other/test_0123abcd.md"], specs=[(("kb",), ("**/test_*.md",))])
    assert problems == ["NOTE_WITHOUT_ROUTE:other/test_0123abcd.md"]


def test_an_unrouted_glob_that_matches_nothing_is_reported():
    problems = _cov(["a/routed.md"], routed=["a/routed.md"],
                    unrouted=[{"glob": ".agents/gone*/**", "reason": REASON}])
    assert problems == ["UNROUTED_GLOB_UNUSED:.agents/gone*/**"]


def test_unrouted_entries_need_a_valid_glob_and_a_reason():
    entries, problems = brm.validate_unrouted({"version": 1, "domains": {}, "unrouted": [
        {"glob": "ok/**", "reason": REASON},
        {"glob": "no/reason/**"},
        {"glob": "short/**", "reason": "too short"},
        {"glob": "../escape/**", "reason": REASON},
        {"glob": "/abs/**", "reason": REASON},
        {"glob": "x/**", "reason": REASON, "extra": 1},
        "not-a-dict",
    ]})
    assert [e["glob"] for e in entries] == ["ok/**", "x/**"]
    assert "UNROUTED_NO_REASON:no/reason/**" in problems
    assert "UNROUTED_NO_REASON:short/**" in problems
    assert any(p.startswith("UNROUTED_BAD_GLOB:3:") for p in problems)
    assert any(p.startswith("UNROUTED_BAD_GLOB:4:") for p in problems)
    assert "UNROUTED_UNKNOWN_KEYS:5:['extra']" in problems
    assert "UNROUTED_BAD_ENTRY:6" in problems


def test_unknown_top_level_keys_are_reported_not_ignored():
    _, problems = brm.validate_unrouted({"version": 1, "domains": {}, "unroutd": []})
    assert problems == ["TOP_LEVEL_UNKNOWN_KEYS:['unroutd']"]


def test_root_matching_is_case_insensitive_like_glob_matching():
    # canonical.glob_regex is IGNORECASE; a literal root must attribute exclusions the same way
    problems = _cov(["KB/test_0123abcd.md"], specs=[(("kb",), ("**/test_*.md",))])
    assert problems == []


def test_tracked_symlinks_are_reported_not_counted_as_notes(tmp_path):
    import subprocess
    git = lambda *a: subprocess.run(["git", *a], cwd=tmp_path, check=True, capture_output=True)  # noqa: E731
    git("init", "-q")
    (tmp_path / "real.md").write_text("x", encoding="utf-8")
    git("add", "real.md")
    blob = subprocess.run(["git", "hash-object", "-w", "--stdin"], cwd=tmp_path, input=b"real.md",
                          capture_output=True, check=True).stdout.decode().strip()
    git("update-index", "--add", "--cacheinfo", f"120000,{blob},link.md")
    entries = brm.tracked_markdown(tmp_path)
    assert sorted(entries) == [("100644", "real.md"), ("120000", "link.md")]
    problems, paths = brm.split_symlinks(entries)
    assert problems == ["TRACKED_SYMLINK:link.md"]
    assert paths == ["real.md"]


def test_the_public_export_cannot_read_the_domains_added_for_coverage():
    """The coverage work added routes; none of them may widen what the PUBLIC web export can read."""
    import sys
    sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
    from vault_access.policy import AccessPolicy
    from vault_access.router import DomainRouter
    policy = AccessPolicy.load(REPO / "04_CONFIG" / "access_policy.yaml")
    router = DomainRouter(REPO, REPO / "04_CONFIG" / "vault_domains.yaml", policy, cache_path=False)
    router.build(force=True)
    export = policy.principal("cloud_web.export", "export")
    added = {"skills_nested", "claude_config", "entry", "tests_docs", "projects.casa3d"}
    by_domain = {}
    for route in router.routes.values():
        by_domain.setdefault(route.domain, []).append(route)
    assert added <= set(by_domain), f"missing domains: {added - set(by_domain)}"
    for name in added:
        for route in by_domain[name]:
            decision = policy.decide(export, domain=route.domain, rel_path=route.path,
                                     classification=route.classification, trust=route.trust)
            assert not decision.allowed, route.uri
    # nested SKILL.md files are not reviewed: never verified, never PUBLIC, and templates are not skills
    assert {r.trust for r in by_domain["skills_nested"]} == {"unverified"}
    assert {r.classification for r in by_domain["skills_nested"]} != {"PUBLIC"}
    assert not any("/templates/" in r.path for r in by_domain["skills_nested"])
    assert all(r.path.count("/") == 3 for r in by_domain["skills"]), "skills is top-level SKILL.md only"


def test_the_telegram_bot_cannot_read_the_claude_code_configuration():
    import sys
    sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
    from vault_access.policy import AccessPolicy
    policy = AccessPolicy.load(REPO / "04_CONFIG" / "access_policy.yaml")
    bot = policy.principal("telegram.bot", "ollama")
    assert not policy.domain_allowed(bot.domains, "claude_config")
    assert policy.domain_allowed(bot.domains, "procedures")  # the exclusion is narrow


def test_the_real_registry_lists_a_reason_for_every_unrouted_glob():
    import yaml
    raw = yaml.safe_load((REPO / "04_CONFIG" / "vault_domains.yaml").read_text(encoding="utf-8"))
    entries, problems = brm.validate_unrouted(raw)
    assert problems == []
    assert entries, "the registry should declare its deliberate exclusions"
