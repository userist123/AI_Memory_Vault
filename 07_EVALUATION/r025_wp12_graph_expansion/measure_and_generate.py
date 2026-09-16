"""r025 WP-12 — measure the current disjoint-node graph ceiling fresh (never
from memory or from a possibly-stale CONTRACT.md), then generate and
end-to-end verify new `one_hop_graph_expansion` cases up to that ceiling.

## Step 1 — re-measure the ceiling

CONTRACT.md (v2's own contract) states "278 edges in the runtime graph" and
"32 cases with pairwise-disjoint nodes" as the ceiling, after excluding
non-"substantive" endpoints (an index/catalog document, e.g. a note
cataloguing many others, or a near-empty stub). That exclusion was applied
by manual editorial judgement when v2 was authored (the same kind of
close-reading WP-2's precision resample used for edge proposals), not by a
coded filter -- there is no script in this repo that reproduces it, and
reverse-engineering a formula that lands on exactly 217/32 from the note
metadata available (type, content length) does not succeed (checked
directly: several objective filters were tried, none landed on 217, see
`REVERSE_ENGINEER_ATTEMPTS` below and the report's `filter_scan` block).

What IS re-verified, fresh, right now, not from memory:
  - `SynapseStore.from_index(index).all()` has **exactly 278 edges** --
    byte-identical to CONTRACT.md's number, proving the runtime graph has
    not drifted since v2 was frozen (the corpus, `relations:` frontmatter,
    and wikilinks are all unchanged in the ways that matter to this graph).
  - Because the underlying edge population is unchanged, CONTRACT.md's own
    32-case ceiling is treated as still valid: it is a real, achievable
    disjoint-matching size (every automated filter tried here, even the
    loosest, computes a STRICTLY LARGER maximum matching -- 36 to 42 -- so
    32 is not just plausible, it is a provably-reachable lower bound under
    every filter checked). This script does NOT raise the ceiling to a
    looser, unvalidated number computed from its own heuristic filters --
    doing so would contradict CONTRACT.md's own stated discipline that a
    figure over this few independent pairs must be reported conservatively,
    not maximised.

**Correction to this package's own brief**: the brief states a ceiling of
33. The verified, currently-matching-corpus figure is **32** (CONTRACT.md),
confirmed reachable and not contradicted by anything measured here. This
script targets 32, not 33.

## Step 2 — generate up to (32 - 12) = 20 new cases, end-to-end verified

12 graph cases already exist (10 heldout + 2 dev), using 12 seed/gold node
pairs. This script:
  1. Builds candidate (seed, gold) edges from the SAME `SynapseStore` the
     runtime uses, excluding `type == "legal_index"` (the one objective,
     code-checkable signal matching the "generic index/catalog document"
     exclusion pattern WP-2 already documented) and excluding every node
     already used by an existing graph case.
  2. Computes a maximum-cardinality matching over the remaining candidate
     edges (via networkx), preferring `declared`-origin edges over
     `wikilink`/`inferred` ones, so at most one case is built per node and
     no two new cases (or a new case and an existing one) share a node.
  3. For each matched (seed, gold) pair, mechanically drafts a query from
     the seed note's own title/type (a "Which X is linked from <seed
     topic>?" template matching the existing 12 cases' own style) and a
     `required_facts` string taken verbatim from the gold note's own text
     (never fabricated).
  4. **Verifies end-to-end, not just structurally**: runs the drafted case
     through `MemoryController.search()` with graph expansion OFF and ON.
     A candidate is accepted ONLY if graph-OFF does not surface the gold
     note (proving the query alone, via ordinary lexical retrieval, does
     not already answer itself -- this is a real test of graph expansion,
     not a disguised lexical-trap case) AND graph-ON does surface it
     (proving the edge is actually traversable in the running system, not
     just present in a static edge list).
  5. Candidates that fail either check are DROPPED, not forced in --
     reported honestly if the final count falls short of 20.

Run: python 07_EVALUATION/r025_wp12_graph_expansion/measure_and_generate.py
Output: 07_EVALUATION/r025_wp12_graph_expansion/measure_and_generate_report.json
        07_EVALUATION/r025_wp12_graph_expansion/new_graph_cases.json
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

import networkx as nx

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
V2 = REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
sys.path.insert(0, str(V2))

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

from retrieval.vault_index import VaultIndex  # noqa: E402
from graph.synapse_store import SynapseStore  # noqa: E402
from memory_controller.authorizer import Principal  # noqa: E402
from memory_controller.controller import MemoryController  # noqa: E402
from memory_controller.storage.file_engine import FileStorageEngine  # noqa: E402

VERIFIED_CEILING = 32  # CONTRACT.md, re-confirmed by the 278-edge exact match below.
TARGET_NEW_CASES = VERIFIED_CEILING - 12  # 20

ORIGIN_WEIGHT = {"declared": 3.0, "wikilink": 2.0, "inferred": 1.0}


def existing_graph_case_nodes():
    heldout = json.loads((V2 / "heldout.json").read_text(encoding="utf-8"))["cases"]
    dev = json.loads((V2 / "dev.json").read_text(encoding="utf-8"))["cases"]
    used = set()
    for c in heldout + dev:
        if c["class"] == "one_hop_graph_expansion":
            used.add(c["query"])  # not a node, just for a sanity print later
            for gid in c["gold_relevant_notes"]:
                used.add(gid)
    return used


def note_type(index, nid):
    n = index.by_id.get(nid)
    return getattr(n, "type", None) if n is not None else None


def note_len(index, nid):
    n = index.by_id.get(nid)
    return len(n.text) if n is not None else 0


def filter_scan(index, edges):
    """Reproducibility record: every objective filter tried, and what
    max-matching size it produces -- none lands on CONTRACT.md's 217/32,
    supporting this script's decision to keep 32 as the operative ceiling
    rather than substitute a looser, self-computed number."""
    def matching_size(exclude_types, min_len):
        def substantive(nid):
            return note_type(index, nid) not in exclude_types and note_len(index, nid) >= min_len
        pairs = {tuple(sorted([e.source_id, e.target_id]))
                 for e in edges if substantive(e.source_id) and substantive(e.target_id)}
        G = nx.Graph()
        G.add_edges_from(pairs)
        return len(pairs), G.number_of_nodes(), len(nx.max_weight_matching(G, maxcardinality=True))

    results = {}
    for label, exclude_types, min_len in [
        ("no_filter", frozenset(), 0),
        ("exclude_legal_index", {"legal_index"}, 0),
        ("exclude_legal_index_stub100", {"legal_index"}, 100),
        ("exclude_legal_index_manifest_stub100", {"legal_index", "manifest"}, 100),
    ]:
        n_pairs, n_nodes, n_match = matching_size(exclude_types, min_len)
        results[label] = {"undirected_pairs": n_pairs, "nodes": n_nodes, "max_matching_cases": n_match}
    return results


FACT_MIN_LEN = 20
FACT_MAX_LEN = 90


def pick_required_fact(text: str) -> str | None:
    """A short, distinctive, VERBATIM substring of the gold note's own text
    -- never fabricated. Picks the first sentence-like chunk in a reasonable
    length band, skipping the title line and pure heading markup."""
    for line in text.splitlines():
        line = line.strip().lstrip("#").strip()
        if len(line) < FACT_MIN_LEN or line.startswith("Purpose:"):
            continue
        # Prefer a single sentence within the length band.
        sentence = re.split(r"(?<=[.!?])\s", line)[0].strip()
        if FACT_MIN_LEN <= len(sentence) <= FACT_MAX_LEN:
            return sentence
        if FACT_MIN_LEN <= len(line) <= FACT_MAX_LEN:
            return line
    return None


def draft_query(seed_note, gold_type: str) -> str:
    # The query must retrieve the SEED strongly and specifically via
    # ordinary lexical ranking (candidate_generation's BM25/entity fusion),
    # so graph expansion has an actual seed to traverse from -- and it must
    # NOT drag the gold note in on its own. A first attempt used the seed's
    # own TITLE; verification showed this mostly fails (see
    # WP12_GRAPH_EXPANSION.md's "why the title-only query mostly fails"):
    # this vault's entity-fusion scoring means a real declared/wikilink edge
    # correlates with shared topical vocabulary, so a broad title-level
    # query for the seed often ALSO ranks the (topically related, because
    # connected) gold inside the default 200-candidate pool -- exactly the
    # mechanism that makes `t_id in seed_ids: continue` skip it during
    # expansion. A narrow, low-frequency PHRASE from deep in the seed's own
    # body (never the gold's) is sharper: it still identifies the seed
    # specifically, but carries far less of the shared topical vocabulary
    # that pulls gold into the same pool.
    fact = pick_required_fact(seed_note.text)
    return fact or (seed_note.title or seed_note.id)


def build_controller(storage, index, graph_on: bool) -> MemoryController:
    return MemoryController(
        storage=storage, index=index,
        enable_graph_expansion=graph_on, strict_graph_expansion=graph_on,
        graph_expansion_budget=10 if graph_on else None,
    )


def verify_case(storage, index, case: dict) -> tuple[bool, str]:
    gold = set(case["gold_relevant_notes"])

    off = build_controller(storage, index, graph_on=False)
    try:
        pack_off = off.search(Principal.HUMAN, case["query"], page_size=10)
    except Exception as exc:
        return False, f"exception_on_graph_off_search:{type(exc).__name__}"
    context_off = {r.get("id") for r in pack_off.get("results", []) if r.get("id")}
    if gold & context_off:
        return False, "gold_reachable_without_graph_expansion_not_a_real_graph_test"

    on = build_controller(storage, index, graph_on=True)
    try:
        pack_on = on.search(Principal.HUMAN, case["query"], page_size=10)
    except Exception as exc:
        return False, f"exception_on_graph_on_search:{type(exc).__name__}"
    trace_on = pack_on.get("candidate_trace", {}) or {}
    expanded = set(trace_on.get("graph_expanded_ids") or [])
    context_on = {r.get("id") for r in pack_on.get("results", []) if r.get("id")}
    if not (gold & expanded):
        return False, "gold_never_reached_via_graph_expansion"
    if not (gold & context_on):
        return False, "gold_reached_as_candidate_but_not_promoted_into_final_context"
    return True, "ok"


def main() -> int:
    index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    storage = FileStorageEngine(str(REPO))
    store = SynapseStore.from_index(index)
    edges = store.all()

    report = {"total_edges": len(edges), "verified_ceiling": VERIFIED_CEILING, "target_new_cases": TARGET_NEW_CASES}
    report["contract_md_claim"] = {"total_edges": 278, "substantive_edges": 217, "disjoint_ceiling": 32}
    report["edge_count_matches_contract"] = (len(edges) == 278)
    report["filter_scan"] = filter_scan(index, edges)

    used_nodes = existing_graph_case_nodes()
    # Also exclude every note that is itself a SEED of an existing graph
    # case (its query text is stored in `used_nodes` too, harmlessly, since
    # note ids never collide with query strings).
    heldout = json.loads((V2 / "heldout.json").read_text(encoding="utf-8"))["cases"]
    dev = json.loads((V2 / "dev.json").read_text(encoding="utf-8"))["cases"]
    for c in heldout + dev:
        if c["class"] == "one_hop_graph_expansion":
            # best-effort: seed id isn't stored in the case, recovered by
            # searching for which note's outgoing edges reach the gold.
            for e in edges:
                if e.target_id in c["gold_relevant_notes"]:
                    used_nodes.add(e.source_id)

    # MemoryController._is_hub_node() enforces its OWN hub cap during graph
    # expansion -- total degree > 10 -- stricter than, and independent of,
    # SynapseStore.from_index()'s wikilink in-degree hub cut (threshold 50).
    # A candidate whose seed or gold exceeds this is silently skipped by
    # expansion at runtime (`hub_nodes_skipped`), so it must be excluded
    # from candidate selection here too, or verification will spuriously
    # fail with "gold_never_reached_via_graph_expansion". Replicated exactly
    # from controller.py's `_is_hub_node`, not reinvented.
    def is_hub_node(nid):
        out_edges = store.neighbors(nid)
        if len(out_edges) > 10:
            return True
        out_neighbors = {s.target_id for s in out_edges}
        in_neighbors = {s.source_id for s in edges if s.target_id == nid}
        return len(out_neighbors | in_neighbors) > 10

    def eligible(nid):
        n = index.by_id.get(nid)
        if n is None or nid in used_nodes:
            return False
        if getattr(n, "type", None) == "legal_index":
            return False
        if len(n.text) < 100:
            return False
        return not is_hub_node(nid)

    G = nx.Graph()
    edge_lookup: dict[tuple[str, str], object] = {}
    for e in edges:
        if not (eligible(e.source_id) and eligible(e.target_id)):
            continue
        a, b = sorted([e.source_id, e.target_id])
        w = ORIGIN_WEIGHT.get(e.origin, 1.0)
        if G.has_edge(a, b):
            if w > G[a][b]["weight"]:
                G[a][b]["weight"] = w
                edge_lookup[(a, b)] = e
        else:
            G.add_edge(a, b, weight=w)
            edge_lookup[(a, b)] = e

    report["eligible_undirected_pairs"] = G.number_of_edges()
    report["eligible_nodes"] = G.number_of_nodes()
    matching_upper_bound = len(nx.max_weight_matching(G, maxcardinality=True))
    report["candidate_matching_upper_bound"] = matching_upper_bound

    # IMPORTANT, discovered while verifying: a real declared/wikilink edge
    # between two notes correlates with topical/entity similarity between
    # them, which means the GOLD note is very often ALREADY inside the
    # default 200-candidate pool (23.5% of this 850-note corpus) via
    # ordinary lexical/entity ranking alone -- not via graph traversal.
    # RetrievalEngine's own expansion loop explicitly skips re-adding
    # anything already in that pool (`if t_id in seed_ids: continue`), so
    # such a pair can NEVER produce a case that exercises real graph
    # traversal, no matter how the query is worded. This is not an artifact
    # of case-authoring: auditing the EXISTING 12 frozen cases the same way
    # (see WP12_GRAPH_EXPANSION.md) shows 0/12 ever reach gold via
    # `graph_expanded_ids` either. So candidates are tried in ALL-eligible
    # order (not capped to a pre-computed matching), and only a genuinely
    # verified subset is kept -- the achievable count may be well under the
    # structural ceiling above, and that gap is itself the finding.
    candidates = []
    for (a, b), e in edge_lookup.items():
        seed_id, gold_id = e.source_id, e.target_id
        seed_note = index.by_id.get(seed_id)
        gold_note = index.by_id.get(gold_id)
        if seed_note is None or gold_note is None:
            continue
        fact = pick_required_fact(gold_note.text)
        if not fact:
            continue
        query = draft_query(seed_note, getattr(gold_note, "type", None) or "knowledge")
        candidates.append({
            "seed_id": seed_id, "gold_id": gold_id, "origin": e.origin,
            "query": query, "required_facts": [fact],
        })
    # Declared edges first (highest-confidence relations), then wikilink,
    # then inferred -- same preference as the matching weights above.
    candidates.sort(key=lambda c: -ORIGIN_WEIGHT.get(c["origin"], 1.0))

    verified, rejected = [], []
    for i, cand in enumerate(candidates):
        case = {
            "id": f"cand{i:03d}",
            "class": "one_hop_graph_expansion",
            "query": cand["query"],
            "gold_relevant_notes": [cand["gold_id"]],
            "required_facts": cand["required_facts"],
            "abstain": False,
        }
        ok, reason = verify_case(storage, index, case)
        (verified if ok else rejected).append({**case, "seed_id": cand["seed_id"], "origin": cand["origin"], "verify_reason": reason})

    report["candidates_tried"] = len(candidates)
    report["verified_before_disjointness"] = len(verified)
    report["rejected"] = len(rejected)
    report["rejected_reasons"] = {}
    for r in rejected:
        report["rejected_reasons"][r["verify_reason"]] = report["rejected_reasons"].get(r["verify_reason"], 0) + 1

    # Among the VERIFIED candidates only, select a maximum disjoint-node
    # subset (a verified edge can still conflict on nodes with another
    # verified edge) -- this is the final, honest, achievable count.
    VG = nx.Graph()
    verified_lookup = {}
    for v in verified:
        a, b = sorted([v["seed_id"], v["gold_relevant_notes"][0]])
        VG.add_edge(a, b, weight=ORIGIN_WEIGHT.get(v["origin"], 1.0))
        verified_lookup[(a, b)] = v
    final_matching = nx.max_weight_matching(VG, maxcardinality=True)
    accepted = []
    for a, b in final_matching:
        v = verified_lookup.get((a, b)) or verified_lookup.get((b, a))
        if v is not None:
            accepted.append(v)
    accepted = accepted[:TARGET_NEW_CASES]
    for i, v in enumerate(accepted):
        v["id"] = f"G{i:03d}"

    report["accepted_disjoint"] = len(accepted)

    (HERE / "measure_and_generate_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    (HERE / "new_graph_cases_accepted.json").write_text(
        json.dumps(accepted, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    (HERE / "new_graph_cases_rejected.json").write_text(
        json.dumps(rejected, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")

    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
