"""VAULT_STATE.md must stay true, or the suite fails.

A state document nobody verifies becomes the next stale docstring. This vault
already produced one: `synapse_store.py` claimed to be "NOT wired into
MemoryController.search()" while the controller imported it in its
constructor, and an external audit believed the docstring over the code.

So the onboarding document is not prose on trust. Every numeric claim in it is
re-derived here from the real vault and compared. When the vault changes, this
fails until someone updates the state card in the same commit.

Tolerance is deliberate: exact equality would make the file fail on every note
added, and a file that fails constantly gets deleted. The bands are wide enough
to absorb ordinary drift and narrow enough to catch a structural change.
"""
import re
from pathlib import Path

import pytest

from graph.synapse_store import SynapseStore
from retrieval.vault_index import VaultIndex

REPO = Path(__file__).resolve().parents[1]
STATE = REPO / "00_GOVERNANCE" / "VAULT_STATE.md"


@pytest.fixture(scope="module")
def state_text():
    return STATE.read_text(encoding="utf-8")


@pytest.fixture(scope="module")
def index():
    return VaultIndex.load(REPO, include_raw=True, include_archived=True)


@pytest.fixture(scope="module")
def store(index):
    return SynapseStore.from_index(index)


def _claimed(text: str, label: str) -> int:
    """Pull the number from the table row whose first cell contains `label`."""
    for line in text.splitlines():
        if line.startswith("|") and label in line:
            for cell in reversed([c.strip() for c in line.split("|")]):
                m = re.fullmatch(r"\*?\*?([\d]+)\*?\*?", cell)
                if m:
                    return int(m.group(1))
    raise AssertionError(f"no numeric claim found for {label!r} in VAULT_STATE.md")


def _within(actual: int, claimed: int, tolerance: float) -> bool:
    return abs(actual - claimed) <= max(3, claimed * tolerance)


def test_the_state_card_exists_and_is_read_first(state_text):
    assert state_text.startswith("# VAULT STATE"), "the entry point must be unmistakable"


def test_index_note_count_is_current(state_text, index):
    claimed = _claimed(state_text, "Notes in the index")
    assert _within(len(index), claimed, 0.05), (
        f"VAULT_STATE.md claims {claimed} indexed notes, the vault has {len(index)}. "
        "Update the state card in the same commit as the change that moved it."
    )


def test_edge_count_is_current(state_text, store):
    claimed = _claimed(state_text, "Graph edges")
    actual = len(store.all())
    assert _within(actual, claimed, 0.10), (
        f"VAULT_STATE.md claims {claimed} edges, the graph has {actual}."
    )


def test_seed_and_gold_populations_are_current(state_text, index, store):
    usable = lambda n: n is not None and not n.id.startswith("path:") and len(n.text) >= 400
    pairs = [
        (index.by_id.get(s.source_id), index.by_id.get(s.target_id))
        for s in store.all()
    ]
    pairs = [(a, b) for a, b in pairs if usable(a) and usable(b) and a.id != b.id]
    seeds = len({a.id for a, _ in pairs})
    golds = len({b.id for _, b in pairs})

    assert _within(seeds, _claimed(state_text, "graph **seed**"), 0.15)
    assert _within(golds, _claimed(state_text, "graph **gold**"), 0.15)


def test_graph_expansion_is_still_off_by_default(state_text):
    """The state card tells newcomers the flag is off. If someone flips it,
    that is a real behavioural change and the card must say so."""
    controller = (REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py").read_text(
        encoding="utf-8", errors="ignore"
    )
    assert "enable_graph_expansion: bool = False" in controller, (
        "graph expansion default changed; update VAULT_STATE.md section 3"
    )
    assert "OFF by default" in state_text


def test_traversal_is_still_single_hop(state_text):
    controller = (REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py").read_text(
        encoding="utf-8", errors="ignore"
    )
    assert "Traverse 1 hop" in controller
    assert "one hop" in state_text, "the card must not claim multi-hop while the runtime does one"


@pytest.mark.parametrize("module,wired", [
    ("graph.plasticity", True),
    ("memory.global_workspace", True),
    ("memory.executive", True),
])
def test_modules_the_card_calls_unwired_really_are(module, wired):
    """The card's central promise is that "present" and "used" are different.
    If one of these gains a production consumer, the card is lying."""
    name = module.split(".")[-1]
    hits = []
    for path in (REPO / "03_IMPLEMENTATION" / "packages").rglob("*.py"):
        s = str(path)
        # A package __init__.py re-exporting a symbol is namespace plumbing,
        # not consumption: `from .plasticity import X` in graph/__init__.py
        # says nothing about whether any call path uses it.
        if "test" in s or "benchmark" in s or path.stem == name or path.name == "__init__.py":
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        if re.search(rf"^\s*(from|import)[^#\n]*\b{name}\b", text, re.M):
            hits.append(path.name)
    assert bool(hits) == wired, (
        f"{module} production consumers changed to {hits}; update VAULT_STATE.md section 3"
    )


@pytest.fixture(scope="module")
def production_python_sources():
    """(relative path, text) of every .py file that could be a production consumer: not a test,
    not evaluation or imported material, not the root `security/` layer itself. One walk, shared."""
    skipped_parts = {"20_TESTS", "07_EVALUATION", "06_INBOX", "imported", "benchmarks", "tests", "node_modules", "__pycache__"}
    sources = []
    for path in REPO.rglob("*.py"):
        parts = path.relative_to(REPO).parts
        if parts[0] in {"security", ".git", ".claude"} or skipped_parts & set(parts) or path.stem.startswith("test_"):
            continue
        sources.append((path.relative_to(REPO).as_posix(), path.stem, path.read_text(encoding="utf-8", errors="ignore")))
    return sources


@pytest.mark.parametrize("module", [
    "runtime_enforcer", "runtime_adapter", "memory_adapter", "memory_boundary",
    "memory_integrity", "security_update_manager",
])
def test_runtime_authority_layer_has_no_production_consumer(module, state_text, production_python_sources):
    """The card says the root `security/` runtime-authority layer is implemented and tested but not
    wired into production (the rule from CLAUDE.md: grep for importers outside tests). Importers
    inside `security/` are the layer talking to itself. The day a tool-execution path imports one
    of these modules, this fails, and the card's row (and docs/security) must say it is wired."""
    importer = re.compile(rf"^\s*(from|import)[^#\n]*\b{module}\b", re.M)
    hits = [rel for rel, stem, text in production_python_sources if stem != module and importer.search(text)]
    assert not hits, (
        f"{module} gained production consumers {hits}: update the runtime-authority row of "
        "VAULT_STATE.md section 3 and docs/security/AUDIT_REMEDIATION.md"
    )
    assert "NOT wired into production" in state_text


def test_the_shim_warning_still_applies(state_text):
    """If memory_controller ever becomes a real package, the warning is wrong."""
    shim = REPO / "03_IMPLEMENTATION" / "packages" / "memory_controller" / "__init__.py"
    assert "__path__" in shim.read_text(encoding="utf-8"), "shim replaced; rewrite section 2"
    assert "shim" in state_text.lower()


def test_every_referenced_path_exists(state_text):
    """A state card pointing at files that moved is worse than none."""
    referenced = set(re.findall(r"`([0-9A-Za-z_./]+\.(?:py|toml|md))`", state_text))
    missing = [
        r for r in referenced
        if "/" in r and not (REPO / r).exists() and not list(REPO.rglob(Path(r).name))
    ]
    assert not missing, f"VAULT_STATE.md references paths that do not exist: {missing}"


# ── Direct routes (`vault://`): the route count the card states ──────────────────────────
@pytest.fixture(scope="module")
def route_table():
    """The route table exactly as the MCP server builds it (real registry, real policy), without
    the per-user metadata cache and without a private overlay: what a clean checkout has."""
    import os

    from vault_access.policy import AccessPolicy
    from vault_access.router import PRIVATE_ROOT_ENV, DomainRouter

    saved = os.environ.pop(PRIVATE_ROOT_ENV, None)
    try:
        policy = AccessPolicy.load(REPO / "04_CONFIG" / "access_policy.yaml")
        router = DomainRouter(REPO, REPO / "04_CONFIG" / "vault_domains.yaml", policy, cache_path=False)
        router.build()
    finally:
        if saved is not None:
            os.environ[PRIVATE_ROOT_ENV] = saved
    return router


def _claimed_routes(text: str):
    """(routes, domains) from the sentence "<N> routes in <M> domains" of the direct-routes row."""
    for line in text.splitlines():
        if line.startswith("|") and "Direct routes" in line:
            m = re.search(r"(\d[\d,]*) routes in (\d+) domains", line)
            if m:
                return int(m.group(1).replace(",", "")), int(m.group(2)), line
    raise AssertionError('no "<N> routes in <M> domains" claim in the direct-routes row of VAULT_STATE.md')


def test_route_and_domain_counts_are_current(state_text, route_table):
    routes, domains, _ = _claimed_routes(state_text)
    assert not route_table.problems, f"the route registry reports problems: {route_table.problems[:5]}"
    assert _within(len(route_table.routes), routes, 0.05), (
        f"VAULT_STATE.md claims {routes} direct routes, the route table has {len(route_table.routes)}. "
        "Update the state card (and 07_EVALUATION/vault_routing/README.md) in the same commit."
    )
    assert _within(len(route_table.registry.domains), domains, 0.05), (
        f"VAULT_STATE.md claims {domains} domains, the registry has {len(route_table.registry.domains)}."
    )


def test_the_resolution_claim_is_about_every_route_the_card_counts(state_text):
    """"by URI N/N" must be the same N as the route count: a measurement of a different population
    would not support the sentence it sits in."""
    routes, _, line = _claimed_routes(state_text)
    m = re.search(r"by URI (\d+)/(\d+)", line)
    assert m, "the direct-routes row no longer says how many routes resolve by URI"
    assert int(m.group(1)) == int(m.group(2)) == routes
