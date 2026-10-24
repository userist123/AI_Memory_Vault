"""D-02: graph expansion must not report a plain "ok" when it expanded nothing.

The default budget (GRAPH_EXPANSION_BUDGET=None -> max(0, min(2n, 20) - n)) is 0 for
n >= 20 seeds, and when every neighbour is already a seed nothing can be added either.
Both used to report "ok". The budget and the expansion semantics are unchanged (owner
decision); only the status tells the truth now:

* resolved budget 0                           -> "ok_noop_budget_zero"
* budget > 0, every neighbour already a seed  -> "ok_noop_all_neighbours_seeded"
* at least one node expanded                  -> "ok"

Under `strict_graph_expansion=True` the no-op statuses still raise, exactly as the
plain "ok"-with-nothing-expanded did before.
"""
from __future__ import annotations

from pathlib import Path

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import GraphExpansionDegraded, MemoryController
from memory_controller.storage.file_engine import FileStorageEngine
from retrieval.vault_index import VaultIndex

NBRS = [f"z-nbr-{i}" for i in range(5)]


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_d02_graph_noop_32bytes__")


def _note(nid, body, rels=()):
    rel = "relations: []\n" if not rels else "relations:\n" + "".join(
        f"  - target_id: {t}\n    type: related_to\n" for t in rels)
    return (f"---\nid: {nid}\ntype: knowledge\ncategory: c\ntags: [t]\ncreated: 2026-09-01\n"
            f"updated: 2026-09-01\nprovenance:\n  source_type: user\n  source_ref: f\n"
            f"confidence: high\nverification: verified\n{rel}lifecycle: ACTIVE\n---\n# {nid}\n\n{body}\n")


def _vault(root: Path, n_fill: int) -> Path:
    v = root / "vault"
    k = v / "01_ARCHITECTURE" / "knowledge"
    k.mkdir(parents=True)
    (k / "seed.md").write_text(_note("a-seed", "alpha alpha alpha beacon seed", NBRS), encoding="utf-8")
    for i in range(n_fill):
        (k / f"f{i}.md").write_text(_note(f"b-fill-{i:04d}", f"alpha filler {i}"), encoding="utf-8")
    for n in NBRS:  # no query terms: they become seeds only when the pool is small enough
        (k / f"{n}.md").write_text(_note(n, "unrelated neighbour text"), encoding="utf-8")
    return v


def _search(vault: Path, budget, strict=False):
    st = FileStorageEngine(str(vault))
    c = MemoryController(storage=st, index=VaultIndex.load(vault), enable_graph_expansion=True,
                         strict_graph_expansion=strict)
    return c.search(Principal.AI_AGENT, "alpha beacon", page_size=10,
                    graph_expansion_budget=budget)["candidate_trace"]


@pytest.fixture(scope="module")
def big_vault(tmp_path_factory):
    # 256 notes, candidate limit 200: the 5 neighbours are NOT seeds.
    return _vault(tmp_path_factory.mktemp("big"), 250)


@pytest.fixture(scope="module")
def small_vault(tmp_path_factory):
    # 26 notes: every note, neighbours included, is a seed.
    return _vault(tmp_path_factory.mktemp("small"), 20)


@pytest.mark.parametrize("budget", [None, 0])
def test_budget_zero_is_a_reported_noop(big_vault, budget):
    t = _search(big_vault, budget)
    assert t["graph_expanded_ids"] == []
    assert t["graph_expansion_status"] == "ok_noop_budget_zero"


@pytest.mark.parametrize("budget,expected", [(1, 1), (5, 5), (200, 5)])
def test_real_expansion_reports_ok(big_vault, budget, expected):
    # budget 1 < candidates, 5 == candidates, 200 > candidates (5 eligible neighbours)
    t = _search(big_vault, budget)
    assert len(t["graph_expanded_ids"]) == expected
    assert set(t["graph_expanded_ids"]) <= set(NBRS)
    assert t["graph_expansion_status"] == "ok"


@pytest.mark.parametrize("budget", [1, 5, 200])
def test_all_neighbours_already_seeded_is_a_reported_noop(small_vault, budget):
    t = _search(small_vault, budget)
    assert set(NBRS) <= set(t["graph_seed_ids"])
    assert t["graph_expanded_ids"] == []
    assert t["graph_expansion_status"] == "ok_noop_all_neighbours_seeded"


def test_strict_mode_still_raises_on_noop(big_vault, small_vault):
    with pytest.raises(GraphExpansionDegraded):
        _search(big_vault, None, strict=True)
    with pytest.raises(GraphExpansionDegraded):
        _search(small_vault, 5, strict=True)
    # and does not raise on a real expansion
    assert _search(big_vault, 5, strict=True)["graph_expansion_status"] == "ok"
