"""cognitive_core/benchmarks/multi_hop_evaluator.py — P1.4 Multi-Hop & Graph Retrieval Evaluator.

Provides reproducible evaluation of:
1. Production SynapseStore infrastructure availability (checks 05_DATA/synapses.json & synapse_store.py).
2. Corpus-native structural graph retrieval (1-hop, 2-hop, and entity-mediated traversal)
   using only intrinsic canonical notes, wikilinks [[...]], frontmatter relations, and entities.
3. Deterministic ranking, false expansion accounting, and rescue measurement.

Zero runtime modification. Zero storage mutation.
"""
from __future__ import annotations

import json
import math
import sys
import time
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Set, Tuple

from ..hybrid_retrieval import Hit, HybridRetriever, entities, tokenize
from ..vault_index import Note, VaultIndex


# ---------------------------------------------------------------------------
# 1. Synapse Infrastructure Audit
# ---------------------------------------------------------------------------

@dataclass
class SynapseInfrastructureStatus:
    status: str  # "AVAILABLE" or "BLOCKED"
    synapses_json_path: str
    synapses_json_exists: bool
    synapse_store_module_exists: bool
    missing_dependencies: List[str]
    notes: str


def check_synapse_infrastructure(vault_root: Path | str = ".") -> SynapseInfrastructureStatus:
    """Inspects whether P1.2 production synapse infrastructure is available on this branch."""
    root = Path(vault_root)
    syn_file = root / "05_DATA" / "synapses.json"
    syn_module = root / "cognitive_core" / "synapse_store.py"

    missing = []
    if not syn_file.exists():
        missing.append(str(syn_file))
    if not syn_module.exists():
        missing.append(str(syn_module))

    status = "AVAILABLE" if not missing else "BLOCKED"
    notes = (
        "Production synapse store infrastructure is available."
        if status == "AVAILABLE"
        else "Synapse graph evidence is BLOCKED because 05_DATA/synapses.json and/or "
             "cognitive_core/synapse_store.py (owned by Claude Code) are not materialized "
             "on this branch. This explains edge_pairs_probed=0 in P1.1-B."
    )
    return SynapseInfrastructureStatus(
        status=status,
        synapses_json_path=str(syn_file),
        synapses_json_exists=syn_file.exists(),
        synapse_store_module_exists=syn_module.exists(),
        missing_dependencies=missing,
        notes=notes,
    )


# ---------------------------------------------------------------------------
# 2. Intrinsic Corpus Graph
# ---------------------------------------------------------------------------

class CorpusGraph:
    """Extracts intrinsic, deterministic graph structures from canonical vault notes.

    Edge types:
    - 'wikilink': explicitly declared Obsidian-style internal links [[Target Note]].
    - 'relation': explicitly declared frontmatter relations metadata.
    - 'entity': shared rare technical identifiers (document frequency between 2 and 5).
    """

    def __init__(self, index: VaultIndex):
        self.index = index
        self.adj: Dict[str, Set[str]] = defaultdict(set)
        self.edge_types: Dict[Tuple[str, str], str] = {}
        self.rare_entities: Set[str] = set()
        self.entity_to_notes: Dict[str, Set[str]] = defaultdict(set)
        self._build_graph()

    def _build_graph(self) -> None:
        # 1. Frontmatter relations
        for note in self.index.notes:
            for rel in note.relations():
                if not isinstance(rel, dict):
                    continue
                tid = rel.get("target_id")
                target = self.index.by_id.get(tid) if tid else None
                if not target and rel.get("target"):
                    target = self.index.resolve(str(rel.get("target")))
                if target and target.id != note.id:
                    self.adj[note.id].add(target.id)
                    self.edge_types[(note.id, target.id)] = "relation"

        # 2. Body wikilinks
        for note in self.index.notes:
            for w in note.wikilinks():
                target = self.index.resolve(w)
                if target and target.id != note.id:
                    self.adj[note.id].add(target.id)
                    self.edge_types[(note.id, target.id)] = "wikilink"

        # 3. Rare technical entities (IDF signal)
        ent_counts: Counter[str] = Counter()
        note_ents: Dict[str, Set[str]] = {}
        for note in self.index.notes:
            e_set = entities(note.text) | set(note.tags)
            note_ents[note.id] = e_set
            for e in e_set:
                ent_counts[e] += 1

        self.rare_entities = {e for e, cnt in ent_counts.items() if 2 <= cnt <= 5}
        for note_id, e_set in note_ents.items():
            for e in e_set & self.rare_entities:
                self.entity_to_notes[e].add(note_id)

    def neighbors(self, note_id: str) -> List[str]:
        """Deterministic 1-hop outgoing neighbors sorted by note ID."""
        return sorted(self.adj.get(note_id, set()))

    def two_hop_neighbors(self, note_id: str) -> List[str]:
        """Deterministic 2-hop outgoing neighbors sorted by note ID, excluding 1-hop."""
        direct = self.adj.get(note_id, set())
        two_hop = set()
        for d in direct:
            for n2 in self.adj.get(d, set()):
                if n2 != note_id and n2 not in direct:
                    two_hop.add(n2)
        return sorted(two_hop)

    def entity_neighbors(self, note_id: str, max_count: int = 10) -> List[str]:
        """Deterministic entity-mediated neighbors sharing rare entities."""
        candidates = set()
        note = self.index.by_id.get(note_id)
        if not note:
            return []
        note_e = entities(note.text) | set(note.tags)
        for e in note_e & self.rare_entities:
            candidates.update(self.entity_to_notes.get(e, set()))
        candidates.discard(note_id)
        return sorted(candidates)[:max_count]


# ---------------------------------------------------------------------------
# 3. Multi-Hop Evaluation Harness
# ---------------------------------------------------------------------------

@dataclass
class ProbeCase:
    source_id: str
    source_title: str
    target_id: str
    target_title: str
    hop_distance: int
    probe_type: str  # "1-hop", "2-hop", "entity"


@dataclass
class ProbeResult:
    case: ProbeCase
    direct_top10: List[str]
    direct_hit: bool
    multi_hop_top10: List[str]
    multi_hop_hit: bool
    rescued: bool
    false_expansions: int
    latency_direct_ms: float
    latency_multi_hop_ms: float


@dataclass
class MultiHopBenchmarkReport:
    timestamp_utc: str
    infrastructure_status: Dict[str, Any]
    total_probed: int
    direct_hits: int
    direct_recall: float
    multi_hop_hits: int
    multi_hop_recall: float
    rescued_count: int
    rescue_rate: float
    false_expansions_count: int
    net_gain: int
    mean_latency_direct_ms: float
    mean_latency_multi_hop_ms: float
    deterministic: bool
    modality_breakdown: Dict[str, Any]


class MultiHopEvaluator:
    """Evaluates multi-hop retrieval performance against intrinsic corpus graph."""

    def __init__(self, index: VaultIndex, retriever: Optional[HybridRetriever] = None):
        self.index = index
        self.retriever = retriever or HybridRetriever(index)
        self.graph = CorpusGraph(index)

    def collect_probe_cases(self, max_cases_per_type: int = 50) -> List[ProbeCase]:
        """Extracts genuine non-trivial probe pairs from the corpus topology."""
        cases: List[ProbeCase] = []

        # 1. 1-hop wikilink / relation pairs
        seen_1hop = set()
        for src_id, targets in self.graph.adj.items():
            src_note = self.index.by_id.get(src_id)
            if not src_note:
                continue
            for tgt_id in sorted(targets):
                tgt_note = self.index.by_id.get(tgt_id)
                if tgt_note and (src_id, tgt_id) not in seen_1hop:
                    seen_1hop.add((src_id, tgt_id))
                    cases.append(ProbeCase(
                        source_id=src_id,
                        source_title=src_note.title,
                        target_id=tgt_id,
                        target_title=tgt_note.title,
                        hop_distance=1,
                        probe_type="1-hop",
                    ))
                    if len([c for c in cases if c.probe_type == "1-hop"]) >= max_cases_per_type:
                        break

        # 2. 2-hop pairs
        seen_2hop = set()
        for src_id in sorted(self.graph.adj.keys()):
            src_note = self.index.by_id.get(src_id)
            if not src_note:
                continue
            for tgt_id in self.graph.two_hop_neighbors(src_id):
                tgt_note = self.index.by_id.get(tgt_id)
                if tgt_note and (src_id, tgt_id) not in seen_2hop:
                    seen_2hop.add((src_id, tgt_id))
                    cases.append(ProbeCase(
                        source_id=src_id,
                        source_title=src_note.title,
                        target_id=tgt_id,
                        target_title=tgt_note.title,
                        hop_distance=2,
                        probe_type="2-hop",
                    ))
                    if len([c for c in cases if c.probe_type == "2-hop"]) >= max_cases_per_type:
                        break

        # 3. Entity-mediated pairs
        seen_ent = set()
        for e, doc_ids in sorted(self.graph.entity_to_notes.items()):
            sorted_docs = sorted(doc_ids)
            for i in range(len(sorted_docs)):
                for j in range(i + 1, len(sorted_docs)):
                    src_id, tgt_id = sorted_docs[i], sorted_docs[j]
                    if (src_id, tgt_id) not in seen_ent and tgt_id not in self.graph.adj.get(src_id, set()):
                        seen_ent.add((src_id, tgt_id))
                        src_note = self.index.by_id.get(src_id)
                        tgt_note = self.index.by_id.get(tgt_id)
                        if src_note and tgt_note:
                            cases.append(ProbeCase(
                                source_id=src_id,
                                source_title=src_note.title,
                                target_id=tgt_id,
                                target_title=tgt_note.title,
                                hop_distance=1,
                                probe_type="entity",
                            ))
                    if len([c for c in cases if c.probe_type == "entity"]) >= max_cases_per_type:
                        break

        return cases

    def run_probe(self, case: ProbeCase) -> ProbeResult:
        """Runs direct vs multi-hop retrieval for a single probe case."""
        # 1. Direct retrieval (lexical BM25 + entity)
        t0 = time.perf_counter()
        direct_hits = self.retriever.search(case.source_title, top_k=10)
        t_direct = (time.perf_counter() - t0) * 1000
        direct_ids = [h.note.id for h in direct_hits]
        direct_hit = case.target_id in direct_ids

        # 2. Multi-hop expansion
        t1 = time.perf_counter()
        seeds = direct_ids[:3]
        expanded_candidates: List[str] = []
        seen = set(direct_ids)

        if case.probe_type == "1-hop":
            for s in seeds:
                for n_id in self.graph.neighbors(s):
                    if n_id not in seen:
                        seen.add(n_id)
                        expanded_candidates.append(n_id)
        elif case.probe_type == "2-hop":
            for s in seeds:
                for n_id in self.graph.two_hop_neighbors(s):
                    if n_id not in seen:
                        seen.add(n_id)
                        expanded_candidates.append(n_id)
        else:  # entity
            for s in seeds:
                for n_id in self.graph.entity_neighbors(s):
                    if n_id not in seen:
                        seen.add(n_id)
                        expanded_candidates.append(n_id)

        # Deterministic combination: top-5 direct + top-5 graph expansions
        multi_hop_ids = direct_ids[:5] + expanded_candidates[:5]
        t_multi = (time.perf_counter() - t1) * 1000
        multi_hop_hit = case.target_id in multi_hop_ids

        rescued = (not direct_hit) and multi_hop_hit
        false_expansions = sum(1 for cid in expanded_candidates[:5] if cid != case.target_id)

        return ProbeResult(
            case=case,
            direct_top10=direct_ids,
            direct_hit=direct_hit,
            multi_hop_top10=multi_hop_ids,
            multi_hop_hit=multi_hop_hit,
            rescued=rescued,
            false_expansions=false_expansions,
            latency_direct_ms=round(t_direct, 3),
            latency_multi_hop_ms=round(t_multi, 3),
        )

    def evaluate(self, max_cases_per_type: int = 50) -> MultiHopBenchmarkReport:
        """Executes full multi-hop benchmark across all modalities."""
        infra = check_synapse_infrastructure()
        cases = self.collect_probe_cases(max_cases_per_type=max_cases_per_type)

        results: List[ProbeResult] = []
        for case in cases:
            results.append(self.run_probe(case))

        # Check determinism by repeating first 10 probes
        deterministic = True
        for case in cases[:10]:
            r1 = self.run_probe(case)
            r2 = self.run_probe(case)
            if r1.multi_hop_top10 != r2.multi_hop_top10:
                deterministic = False
                break

        total = len(results) or 1
        direct_hits = sum(1 for r in results if r.direct_hit)
        multi_hits = sum(1 for r in results if r.multi_hop_hit)
        rescued = sum(1 for r in results if r.rescued)
        false_expansions = sum(r.false_expansions for r in results)

        # Modality breakdown
        modalities = {}
        for m_type in ("1-hop", "2-hop", "entity"):
            m_res = [r for r in results if r.case.probe_type == m_type]
            if m_res:
                m_total = len(m_res)
                m_direct = sum(1 for r in m_res if r.direct_hit)
                m_multi = sum(1 for r in m_res if r.multi_hop_hit)
                m_rescued = sum(1 for r in m_res if r.rescued)
                modalities[m_type] = {
                    "total": m_total,
                    "direct_hits": m_direct,
                    "direct_recall": round(m_direct / m_total, 4),
                    "multi_hop_hits": m_multi,
                    "multi_hop_recall": round(m_multi / m_total, 4),
                    "rescued": m_rescued,
                    "rescue_rate": round(m_rescued / m_total, 4),
                    "net_gain": m_multi - m_direct,
                }

        mean_lat_direct = sum(r.latency_direct_ms for r in results) / total if results else 0.0
        mean_lat_multi = sum(r.latency_multi_hop_ms for r in results) / total if results else 0.0

        return MultiHopBenchmarkReport(
            timestamp_utc=datetime.now(timezone.utc).isoformat(),
            infrastructure_status=asdict(infra),
            total_probed=len(results),
            direct_hits=direct_hits,
            direct_recall=round(direct_hits / total, 4),
            multi_hop_hits=multi_hits,
            multi_hop_recall=round(multi_hits / total, 4),
            rescued_count=rescued,
            rescue_rate=round(rescued / total, 4),
            false_expansions_count=false_expansions,
            net_gain=multi_hits - direct_hits,
            mean_latency_direct_ms=round(mean_lat_direct, 3),
            mean_latency_multi_hop_ms=round(mean_lat_multi, 3),
            deterministic=deterministic,
            modality_breakdown=modalities,
        )
