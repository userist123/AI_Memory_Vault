"""SynapseStore — P1.2 derived synaptic substrate (owner: claude-code).

Status: EXPERIMENTAL, NOT wired into MemoryController.search(),
cognitive_core/tool_router.py or cognitive_core/activation.py. It is a
deliberately separate offline layer; see the "Relationship to existing
runtime code" note below before touching this file.

A synapse = (source_id, target_id, relation, weight, evidence[]).

Design rules:
- This is NOT a source of truth. It is reconstructible from `relations:` in
  Markdown frontmatter plus promoted proposals. It can be deleted and
  regenerated at any time.
- Weight starts at the relation type's base value and is changed ONLY by
  externally-verified outcomes (pytest/CI/outcome ledger/human) — never by an
  agent's self-report. This is the system's actual STDP. See
  30_SCRIPTS/knowledge/plasticity_update.py.
- Edges that are never activated atrophy and get pruned at consolidation.

Weight contract (single, coherent — do not reintroduce a [0,1] assumption
anywhere downstream): 0 <= weight <= MAX_WEIGHT, where MAX_WEIGHT is the
explicit constant below (1.5, i.e. weight is a multiplicative gain that can
exceed 1.0 for a strongly-reinforced edge, not a probability). Anything that
needs a [0,1]-normalized value for propagation should normalize at the point
of use (see `normalize_for_propagation`) — never rescale or clamp values in
storage/persistence to fit [0,1].

Relation vocabulary (fixed, closed set — reject everything else, no fuzzy
matching, no auto-correct; enforced in `add()` and on `load()`):
  STRONG_RELATIONS = depends_on, contradicts, supersedes, caused, verified_by,
                      applies_to
  WEAK_RELATIONS    = related_to, part_of
Weak relations are allowed in the proposal layer (see
30_SCRIPTS/knowledge/edge_proposer.py) but never auto-promote and always carry
a reduced initial weight and `origin="proposed_weak"` — strictness lives at
the PROMOTION boundary, not at the PROPOSAL boundary.

Relationship to existing runtime code: `cognitive_core/synapse.py`
(SynapticGraph) is a separate, ephemeral, zero-persistence extractor of
*declared* relations only, already wired into
`cognitive_core/activation.py` -> `MemoryController`. This module is a
superset in scope (adds weights, plasticity, proposed/inferred edges,
persistence, spreading activation) but intentionally does not touch, import,
or replace that runtime path. Do not merge the two without an explicit
architecture decision from the runtime/security owner.
"""
from __future__ import annotations

import json
import math
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Tuple

STRONG_RELATIONS = frozenset({
    "depends_on", "contradicts", "supersedes", "caused", "verified_by", "applies_to",
})
WEAK_RELATIONS = frozenset({"related_to", "part_of"})
ALLOWED_RELATIONS = STRONG_RELATIONS | WEAK_RELATIONS

# Signed indication of the direction of influence in recall. Only relations in
# ALLOWED_RELATIONS are accepted; this table simply supplies a base weight.
RELATION_BASE_WEIGHT = {
    "depends_on": 0.9,
    "supersedes": 0.9,
    "contradicts": 0.8,       # important to surface as an alert, not to hide
    "verified_by": 1.0,
    "caused": 0.8,
    "applies_to": 0.7,
    "part_of": 0.7,
    "related_to": 0.4,
}
DEFAULT_WEIGHT = 0.4
MIN_WEIGHT = 0.0
MAX_WEIGHT = 1.5
PRUNE_THRESHOLD = 0.12
# Weak relations (related_to, part_of) always start at a reduced fraction of
# whatever their computed/base weight would otherwise be.
WEAK_WEIGHT_FACTOR = 0.5

# A wikilink is human-authored but untyped, so it is weaker evidence than a
# declared typed relation and starts below DEFAULT_WEIGHT.
WIKILINK_WEIGHT = 0.2
# In-degree at which a wikilink target is treated as a navigation hub and
# dropped. Measured against this vault: 8 targets absorbed 64% of all links.
HUB_IN_DEGREE_THRESHOLD = 50

# Origins produced by an algorithm's guess rather than a human action: a
# candidate proposal (edge_proposer.py TIER 1) or an LLM reclassification of
# one (TIER 2), sitting in PROPOSED_PENDING_REVIEW until a human promotes it.
# "Never activated" IS meaningful evidence of low value for these -- nothing
# has verified they connect anything real, so they are the only origins
# `decay_unused()`/`prune()` treat as ephemeral by default.
#
# Everything else -- `declared` (a typed relation an author wrote),
# `inferred` (its automatically-generated structural mirror), and
# `wikilink` (an Obsidian [[link]] an author actually wrote, just untyped)
# -- is durable: a human is responsible for that edge existing, so its
# absence from spreading activation so far says nothing about its value.
# Declared/inferred/wikilink edges currently make up the entire real graph
# (see 07_EVALUATION/r005_graph_edge_reality_gate_report.md); a consolidation
# loop that cannot tell "unactivated because untyped" apart from "unactivated
# because it's a bad guess" would, after ~26 decay_unused() cycles, prune
# every wikilink and mirror edge -- 77% of the graph -- for the sole reason
# that nothing has wired activation into the query path yet (r005: NO-GO).
MACHINE_PROPOSED_ORIGINS = frozenset({"proposed", "proposed_weak", "proposed_llm"})


def is_durable(origin: str) -> bool:
    """True unless `origin` is a machine-generated proposal.

    A closed blocklist (MACHINE_PROPOSED_ORIGINS), not an allowlist: an
    origin value this module doesn't yet recognize defaults to durable, so a
    future new origin can't be silently made prunable just by omission --
    only the enumerated proposal origins ever decay/prune by default.
    """
    return origin not in MACHINE_PROPOSED_ORIGINS


def normalize_for_propagation(weight: float, max_weight: float = MAX_WEIGHT) -> float:
    """Map a stored [0, MAX_WEIGHT] weight into [0, 1] for callers that need a
    probability-like scale. Storage itself is NEVER rescaled — call this only
    at the point of use."""
    if max_weight <= 0:
        return 0.0
    return max(0.0, min(1.0, weight / max_weight))


class InvalidSynapseError(ValueError):
    """Raised when a synapse violates the relation/weight/self-loop contract."""


@dataclass
class Synapse:
    source_id: str
    target_id: str
    relation: str = "related_to"
    weight: float = DEFAULT_WEIGHT
    origin: str = "declared"          # declared | inferred | wikilink | proposed | proposed_weak | proposed_llm
    activations: int = 0
    reinforcements: int = 0
    depressions: int = 0
    evidence: List[str] = field(default_factory=list)   # verified run_ids
    updated: str = ""

    @property
    def key(self) -> Tuple[str, str, str]:
        return (self.source_id, self.target_id, self.relation)

    def validate(self) -> None:
        if not self.source_id or not self.target_id:
            raise InvalidSynapseError("synapse requires both source_id and target_id")
        if self.source_id == self.target_id:
            raise InvalidSynapseError(f"self-loop rejected: {self.source_id}")
        if self.relation not in ALLOWED_RELATIONS:
            raise InvalidSynapseError(f"relation not in allowed enum: {self.relation!r}")
        if not isinstance(self.weight, (int, float)) or isinstance(self.weight, bool):
            raise InvalidSynapseError(f"weight must be numeric, got {self.weight!r}")
        if not (MIN_WEIGHT <= float(self.weight) <= MAX_WEIGHT):
            raise InvalidSynapseError(
                f"weight {self.weight} out of bounds [{MIN_WEIGHT}, {MAX_WEIGHT}]"
            )


class SynapseStore:
    def __init__(self, synapses: Optional[List[Synapse]] = None):
        self._by_key: Dict[Tuple[str, str, str], Synapse] = {}
        self._rejected_on_load: List[Dict[str, object]] = []
        for s in synapses or []:
            self.add(s)
        self._rebuild_adjacency()

    def _rebuild_adjacency(self) -> None:
        self._out: Dict[str, List[Synapse]] = {}
        for s in self._by_key.values():
            self._out.setdefault(s.source_id, []).append(s)

    # ---------- construction ----------

    def add(self, syn: Synapse) -> None:
        """Validates and inserts/merges a synapse. Raises InvalidSynapseError
        for a self-loop, an unknown relation, or an out-of-bounds weight —
        this store never silently stores an invalid edge.

        Unlike the original implementation, this does NOT treat weight==0.0
        as "unset and replace with the relation's base weight" — 0.0 is now a
        legal in-bounds value (MIN_WEIGHT==0.0) and callers must pass the
        weight they actually mean. Use RELATION_BASE_WEIGHT.get(relation,
        DEFAULT_WEIGHT) explicitly at the call site if you want the default."""
        syn.validate()
        existing = self._by_key.get(syn.key)
        if existing:
            existing.weight = max(existing.weight, syn.weight)
            existing.evidence = sorted(set(existing.evidence) | set(syn.evidence))
        else:
            self._by_key[syn.key] = syn
        self._rebuild_adjacency()

    @classmethod
    def from_index(
        cls,
        index,
        symmetric_weak: bool = True,
        include_wikilinks: bool = True,
        hub_in_degree: int = HUB_IN_DEGREE_THRESHOLD,
    ) -> "SynapseStore":
        """Builds from `relations:` declared in notes. Ignores unresolvable
        targets and self-loops (defensively — a note listing itself as its own
        relation target is dropped, not stored).

        With `include_wikilinks=True`, Obsidian `[[wikilinks]]` in note bodies
        are ingested as a second, weaker edge source (`origin="wikilink"`).
        This matters because the two representations had diverged: the vault
        carries thousands of resolvable wikilinks while `relations:` yields
        single-digit edge counts, so the runtime graph was effectively empty
        while the Obsidian graph looked dense.

        Navigation hubs are excluded. A note that everything links to (a map
        of content, an index) carries almost no retrieval signal: activation
        reaches the hub from any seed and from the hub reaches everything, so
        such edges connect all-to-all without distinguishing anything. Any
        target whose wikilink in-degree reaches `hub_in_degree` is dropped as
        both source and target. Set `hub_in_degree=0` to disable the cut.

        Wikilink edges are directional and are NOT mirrored, unlike declared
        relations: mirroring them would double an already large edge set and
        assert a reciprocity the author never wrote.
        """
        store = cls()
        for note in index.notes:
            for rel in note.relations():
                target = rel.get("target_id")
                if not target:
                    continue
                target = str(target)
                if target not in index.by_id or target == note.id:
                    continue
                relation = str(rel.get("type") or "related_to").lower()
                if relation not in ALLOWED_RELATIONS:
                    relation = "related_to"
                try:
                    store.add(Synapse(
                        source_id=note.id, target_id=target, relation=relation,
                        weight=RELATION_BASE_WEIGHT.get(relation, DEFAULT_WEIGHT),
                        origin="declared",
                        updated=datetime.now(timezone.utc).isoformat(timespec="seconds"),
                    ))
                except InvalidSynapseError:
                    continue
                if symmetric_weak and target != note.id:
                    try:
                        store.add(Synapse(
                            source_id=target, target_id=note.id, relation="related_to",
                            weight=0.25, origin="inferred",
                        ))
                    except InvalidSynapseError:
                        continue

        if include_wikilinks:
            resolve = getattr(index, "resolve", None)
            if callable(resolve):
                # Pass 1: in-degree, to identify navigation hubs.
                in_degree: Dict[str, int] = {}
                resolved: List[Tuple[str, str]] = []
                for note in index.notes:
                    for raw in note.wikilinks():
                        target_note = resolve(raw)
                        if target_note is None or target_note.id == note.id:
                            continue
                        in_degree[target_note.id] = in_degree.get(target_note.id, 0) + 1
                        resolved.append((note.id, target_note.id))
                hubs = (
                    {nid for nid, deg in in_degree.items() if deg >= hub_in_degree}
                    if hub_in_degree > 0
                    else set()
                )
                # Pass 2: emit, skipping hubs on either end. Sorted for determinism.
                for source_id, target_id in sorted(set(resolved)):
                    if source_id in hubs or target_id in hubs:
                        continue
                    try:
                        store.add(Synapse(
                            source_id=source_id, target_id=target_id,
                            relation="related_to", weight=WIKILINK_WEIGHT,
                            origin="wikilink",
                            updated=datetime.now(timezone.utc).isoformat(timespec="seconds"),
                        ))
                    except InvalidSynapseError:
                        continue

        store._rebuild_adjacency()
        return store

    # ---------- persistence ----------

    def save(self, path: Path | str) -> Path:
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "version": 1,
            "generated": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "synapses": [asdict(s) for s in self._by_key.values()],
        }
        path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        return path

    @classmethod
    def load(cls, path: Path | str) -> "SynapseStore":
        """Loads a persisted store. Malformed or invalid records (bad
        relation, out-of-bounds weight, self-loop, missing fields) are
        skipped, not fatal — the store must survive corrupted/hand-edited
        persistence. Skipped records are recorded in `.rejected_on_load()`."""
        path = Path(path)
        store = cls()
        if not path.exists():
            return store
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as exc:
            store._rejected_on_load.append({"reason": "unreadable_file", "detail": str(exc)})
            return store
        for raw in data.get("synapses", []):
            try:
                known_fields = {f.name for f in Synapse.__dataclass_fields__.values()}
                filtered = {k: v for k, v in raw.items() if k in known_fields}
                syn = Synapse(**filtered)
                store.add(syn)
            except (TypeError, InvalidSynapseError) as exc:
                store._rejected_on_load.append({"record": raw, "reason": str(exc)})
        store._rebuild_adjacency()
        return store

    def rejected_on_load(self) -> List[Dict[str, object]]:
        return list(self._rejected_on_load)

    # ---------- query ----------

    def neighbors(self, node_id: str) -> List[Synapse]:
        return self._out.get(node_id, [])

    def all(self) -> List[Synapse]:
        return list(self._by_key.values())

    def degree_stats(self) -> Dict[str, float]:
        degrees = [len(v) for v in self._out.values()]
        strong = sum(1 for s in self._by_key.values() if s.relation in STRONG_RELATIONS)
        weak = sum(1 for s in self._by_key.values() if s.relation in WEAK_RELATIONS)
        return {
            "edges": len(self._by_key),
            "edges_strong": strong,
            "edges_weak": weak,
            "nodes_with_edges": len(self._out),
            "mean_out_degree": round(sum(degrees) / max(len(degrees), 1), 2),
        }

    # ---------- activation ----------

    def spread(self, seeds: Dict[str, float], decay: float = 0.6,
               max_hops: int = 2, record: bool = False) -> Dict[str, float]:
        """Weighted spreading activation with per-hop exponential decay.
        Deterministic: frontier is processed as a stack (LIFO) in insertion
        order, and a target's activation is only updated (and re-queued) when
        strictly improved, so repeated runs on the same seeds/graph always
        converge to the same activation map."""
        activation: Dict[str, float] = dict(seeds)
        frontier: List[Tuple[str, float, int]] = [(n, s, 0) for n, s in seeds.items()]
        while frontier:
            node, score, hop = frontier.pop()
            if hop >= max_hops:
                continue
            for syn in self.neighbors(node):
                w = max(min(syn.weight, MAX_WEIGHT), 0.0)
                propagated = score * (decay ** (hop + 1)) * w
                if propagated <= 1e-6:
                    continue
                if propagated > activation.get(syn.target_id, 0.0):
                    activation[syn.target_id] = propagated
                    if record:
                        syn.activations += 1
                    frontier.append((syn.target_id, propagated, hop + 1))
        return activation

    # ---------- plasticity ----------

    def reinforce(self, edges: Iterable[Tuple[str, str]], run_id: str,
                  success: bool, rate: float = 0.15) -> int:
        """Updates weights from an EXTERNALLY VERIFIED outcome.

        `edges` are the pairs actually present in the OBSERVED trace of the
        run (what entered context), never what the agent claims it used.
        Success moves weight asymptotically toward MAX_WEIGHT (so a 2nd
        confirmation and a 20th confirmation remain numerically distinct
        until saturation); failure moves it proportionally toward MIN_WEIGHT.
        """
        touched = 0
        pairs = {(str(a), str(b)) for a, b in edges}
        for syn in self._by_key.values():
            if (syn.source_id, syn.target_id) not in pairs:
                continue
            touched += 1
            if success:
                syn.weight = min(MAX_WEIGHT, syn.weight + rate * (MAX_WEIGHT - syn.weight))
                syn.reinforcements += 1
                if run_id not in syn.evidence:
                    syn.evidence.append(run_id)
            else:
                syn.weight = max(MIN_WEIGHT, syn.weight - rate * syn.weight)
                syn.depressions += 1
            syn.updated = datetime.now(timezone.utc).isoformat(timespec="seconds")
        return touched

    def decay_unused(self, factor: float = 0.98) -> None:
        """Atrophy: edges with no recorded activation slowly lose weight.

        Applies only to non-durable (machine-proposed) edges -- see
        is_durable(). A declared relation, its structural mirror, or a
        human-authored wikilink is not evidence-poor merely because
        spreading activation hasn't reached it yet; only an algorithm's
        unverified guess is."""
        for syn in self._by_key.values():
            if syn.activations == 0 and not is_durable(syn.origin):
                syn.weight = max(MIN_WEIGHT, syn.weight * factor)

    def prune(self, threshold: float = PRUNE_THRESHOLD, keep_durable: bool = True) -> int:
        """Cuts atrophied edges.

        By default (keep_durable=True), only non-durable, machine-proposed
        edges (see MACHINE_PROPOSED_ORIGINS / is_durable()) are eligible for
        automatic removal: a declared relation, its structural mirror, and a
        human-authored wikilink are never auto-pruned, regardless of
        activation history or current weight -- see is_durable() for why
        "never activated" isn't evidence against them the way it is for a
        proposal. Pass keep_durable=False to lift that protection entirely
        and prune by weight/reinforcement alone (e.g. an explicit,
        human-invoked cleanup): prune() can still remove anything when
        asked, only the DEFAULT changed.
        """
        removed = 0
        for key, syn in list(self._by_key.items()):
            if keep_durable and is_durable(syn.origin):
                continue
            if syn.weight < threshold and syn.reinforcements == 0:
                del self._by_key[key]
                removed += 1
        self._rebuild_adjacency()
        return removed
