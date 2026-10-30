"""Imported skills are not published as verified.

`.agents/skills` was filled mostly by bulk imports from third-party collections. The public web
export (`cloud_web.export`, trust [verified]) may publish only skills written in this vault and
reviewed, listed one by one in the `skills.reviewed` domain of 04_CONFIG/vault_domains.yaml.
Everything else under `skills` is unverified, and a new skill folder lands there by default.
"""
import re
import sys
from pathlib import Path, PurePosixPath

import pytest
import yaml

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

from vault_access.canonical import has_glob  # noqa: E402
from vault_access.policy import AccessPolicy  # noqa: E402
from vault_access.router import DomainRouter  # noqa: E402

DOMAINS = REPO / "04_CONFIG" / "vault_domains.yaml"
# Frontmatter fields that say a skill came from somewhere else.
UPSTREAM_FIELDS = ("source_repo", "upstream", "source_type", "license_source", "repository")


@pytest.fixture(scope="module")
def built():
    policy = AccessPolicy.load(REPO / "04_CONFIG" / "access_policy.yaml")
    router = DomainRouter(REPO, DOMAINS, policy, cache_path=False)
    router.build(force=True)
    by_domain = {}
    for route in router.routes.values():
        by_domain.setdefault(route.domain, []).append(route)
    return policy, router, by_domain


def _reviewed_roots():
    raw = yaml.safe_load(DOMAINS.read_text(encoding="utf-8"))
    return list(raw["domains"]["skills.reviewed"]["roots"])


def _frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8", errors="replace")
    m = re.match(r"---\r?\n(.*?)\r?\n---", text, re.S)
    try:
        data = yaml.safe_load(m.group(1)) if m else {}
    except yaml.YAMLError:
        data = {}
    return data if isinstance(data, dict) else {}


def test_the_public_export_publishes_no_unreviewed_skill(built):
    policy, _, by_domain = built
    export = policy.principal("cloud_web.export", "export")
    assert len(by_domain["skills"]) > 1000, "the bulk of .agents/skills should still be routed"
    assert {r.trust for r in by_domain["skills"]} == {"unverified"}
    for route in by_domain["skills"]:
        decision = policy.decide(export, domain=route.domain, rel_path=route.path,
                                 classification=route.classification, trust=route.trust)
        assert not decision.allowed, route.uri


def test_reviewed_skills_are_listed_one_by_one_and_are_not_imports():
    roots = _reviewed_roots()
    assert roots, "skills.reviewed lists no skill"
    for root in roots:
        assert not has_glob(root), f"{root}: reviewed skills are named one by one, never by a glob"
        p = PurePosixPath(root)
        assert p.name == "SKILL.md" and p.parent.parent.as_posix() == ".agents/skills", root
        folder = REPO / p.parent
        assert (REPO / p).is_file(), f"{root} does not exist"
        assert not (folder / "PROVENANCE.json").exists(), f"{root} is an import (PROVENANCE.json)"
        fm = _frontmatter(REPO / p)
        found = [k for k in UPSTREAM_FIELDS if fm.get(k)]
        assert not found, f"{root} declares an upstream source: {found}"


def test_reviewed_skills_are_the_only_ones_the_export_reads(built):
    policy, router, by_domain = built
    export = policy.principal("cloud_web.export", "export")
    reviewed = by_domain["skills.reviewed"]
    assert sorted(r.path for r in reviewed) == sorted(_reviewed_roots())
    for route in reviewed:
        assert route.trust == "verified" and route.classification == "PUBLIC", route.uri
        assert policy.decide(export, domain=route.domain, rel_path=route.path,
                             classification=route.classification, trust=route.trust).allowed, route.uri
        # the URI names the skill, as under `skills`
        assert route.uri == f"vault://skills.reviewed/{PurePosixPath(route.path).parent.name}", route.uri
    readable = {r.path for r in router.routes.values()
                if r.path.startswith(".agents/skills/") and policy.decide(
                    export, domain=r.domain, rel_path=r.path,
                    classification=r.classification, trust=r.trust).allowed}
    assert readable == {r.path for r in reviewed}


def test_a_literal_skill_root_gets_the_folder_as_slug(tmp_path):
    from vault_access.router import DomainRegistry  # noqa: F401  (import check only)
    policy = AccessPolicy.load(REPO / "04_CONFIG" / "access_policy.yaml")
    for name in ("alpha", "beta"):
        (tmp_path / ".agents" / "skills" / name).mkdir(parents=True)
        (tmp_path / ".agents" / "skills" / name / "SKILL.md").write_text(
            f"---\nname: {name}\ndescription: x\n---\n# {name}\n", encoding="utf-8")
    reg = tmp_path / "domains.yaml"
    reg.write_text(
        "version: 1\n"
        "domains:\n"
        "  skills:\n"
        "    roots: [\".agents/skills/*/SKILL.md\"]\n"
        "    trust: unverified\n"
        "  skills.reviewed:\n"
        "    roots: [\".agents/skills/alpha/SKILL.md\"]\n", encoding="utf-8")
    router = DomainRouter(tmp_path, reg, policy, cache_path=False)
    router.build(force=True)
    uris = {r.uri: (r.domain, r.trust) for r in router.routes.values()}
    assert uris == {"vault://skills.reviewed/alpha": ("skills.reviewed", "verified"),
                    "vault://skills/beta": ("skills", "unverified")}
