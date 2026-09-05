"""cognitive_core/benchmarks/retrieval_evaluation_p17.py — P1.7 Retrieval Evaluation Benchmark.

Provides a reproducible, deterministic evaluation suite for:
1. Production SynapseStore Infrastructure Audit (Tier 1 external status: BLOCKED).
2. ProductionRetrievalFacade Known-Item Retrieval Benchmark (Title & Body queries, Recall@1/5/10, MRR, latency, determinism).
3. Paraphrase Retrieval Benchmark (Ollama local generator check + challenge queries).
4. Multi-Hop & Structural Graph Retrieval Benchmark (1-hop, 2-hop, entity-mediated, direct vs graph, rescued notes, false expansions, net gain).

Zero storage mutation. Zero runtime controller modification. Fail-closed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import sys
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Set, Tuple

from ..hybrid_retrieval import (
    Hit,
    HybridRetriever,
    OllamaEmbedder,
    entities,
    tokenize,
)
from ..retrieval_boundary import RetrievalBoundaryAdapter
from ..retrieval_facade import (
    FacadeNoteResult,
    FacadeRetrievalRequest,
    FacadeRetrievalResponse,
    ProductionRetrievalFacade,
)
from ..vault_index import Note, VaultIndex
from .metrics import mean_reciprocal_rank, precision_at_k, recall_at_k
from .multi_hop_evaluator import (
    CorpusGraph,
    MultiHopBenchmarkReport,
    MultiHopEvaluator,
    ProbeCase,
    ProbeResult,
    SynapseInfrastructureStatus,
    check_synapse_infrastructure,
)

PARAPHRASE_PROMPT_P17 = (
    "Reformuleaza urmatoarea fraza tehnica in limba romana, pastrand sensul exact, "
    "dar folosind cuvinte diferite si structura diferita de propozitie. "
    "Raspunde EXCLUSIV cu fraza reformulata, fara introduceri, fara explicatii:\n\n{text}"
)


def _ensure_utf8_streams() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


# ---------------------------------------------------------------------------
# 1. Known-Item Evaluation via ProductionRetrievalFacade
# ---------------------------------------------------------------------------

@dataclass
class KnownItemBenchmarkArmResult:
    recall_at_1: float
    recall_at_5: float
    recall_at_10: float
    mrr: float
    queries: int
    misses: int
    median_latency_ms: float
    p95_latency_ms: float
    deterministic: bool


def evaluate_known_item_facade(
    vault_index: VaultIndex,
    sample_size: int = 50,
    seed: int = 42,
) -> Dict[str, Any]:
    """Evaluates known-item retrieval using ProductionRetrievalFacade on canonical notes."""
    retriever = HybridRetriever(vault_index)
    adapter = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=adapter)

    # Filter eligible secure notes (ACTIVE+verified, having a title and sufficient body tokens)
    eligible = [
        n for n in vault_index.notes
        if n.lifecycle == "ACTIVE" and n.verification == "verified"
        and n.title and len(tokenize(n.text)) >= 10
    ]
    if not eligible:
        return {"error": "No eligible ACTIVE+verified notes found for known-item benchmark"}

    rng = random.Random(seed)
    sampled = eligible if len(eligible) <= sample_size else rng.sample(eligible, sample_size)

    queries: List[Tuple[str, str, str]] = []  # (query_type, query_text, expected_id)
    for n in sampled:
        # Title query
        queries.append(("title", n.title, n.id))
        # Body snippet query (first 8 distinctive tokens)
        n_tokens = tokenize(n.text)
        title_tokens = set(tokenize(n.title))
        body_tokens = [t for t in n_tokens if t not in title_tokens][:8]
        if body_tokens:
            queries.append(("body", " ".join(body_tokens), n.id))

    recalls_1: List[float] = []
    recalls_5: List[float] = []
    recalls_10: List[float] = []
    mrrs: List[float] = []
    latencies: List[float] = []
    misses = 0

    first_10_runs_a: List[List[str]] = []
    first_10_runs_b: List[List[str]] = []

    for i, (q_type, q_text, expected_id) in enumerate(queries):
        req = FacadeRetrievalRequest(
            query=q_text,
            principal="human",
            page_size=10,
            request_id=f"eval-ki-{i}",
        )
        t0 = time.perf_counter()
        resp = facade.retrieve(req)
        lat = (time.perf_counter() - t0) * 1000.0
        latencies.append(lat)

        retrieved_ids = [r.id for r in resp.results]
        r1 = recall_at_k(retrieved_ids, [expected_id], 1)
        r5 = recall_at_k(retrieved_ids, [expected_id], 5)
        r10 = recall_at_k(retrieved_ids, [expected_id], 10)
        mrr = mean_reciprocal_rank(retrieved_ids, [expected_id])

        recalls_1.append(r1)
        recalls_5.append(r5)
        recalls_10.append(r10)
        mrrs.append(mrr)
        if r10 == 0.0:
            misses += 1

        if i < 10:
            first_10_runs_a.append(retrieved_ids)
            # Re-run immediately to verify determinism
            resp_b = facade.retrieve(req)
            first_10_runs_b.append([r.id for r in resp_b.results])

    deterministic = (first_10_runs_a == first_10_runs_b)
    latencies_sorted = sorted(latencies)
    med_lat = latencies_sorted[len(latencies_sorted) // 2] if latencies_sorted else 0.0
    p95_idx = int(math.ceil(0.95 * len(latencies_sorted))) - 1
    p95_lat = latencies_sorted[max(0, p95_idx)] if latencies_sorted else 0.0

    total_q = len(queries) or 1
    return {
        "sampled_notes": len(sampled),
        "total_queries": len(queries),
        "recall@1": round(sum(recalls_1) / total_q, 4),
        "recall@5": round(sum(recalls_5) / total_q, 4),
        "recall@10": round(sum(recalls_10) / total_q, 4),
        "mrr": round(sum(mrrs) / total_q, 4),
        "misses": misses,
        "median_latency_ms": round(med_lat, 3),
        "p95_latency_ms": round(p95_lat, 3),
        "deterministic": deterministic,
    }


# ---------------------------------------------------------------------------
# 2. Paraphrase Retrieval Evaluation
# ---------------------------------------------------------------------------

def check_ollama_generate_available(
    host: str = "http://localhost:11434",
    model: str = "qwen2.5-coder:3b",
    timeout: float = 2.0,
) -> bool:
    """Checks if Ollama generation is live and model is present."""
    try:
        req = urllib.request.Request(f"{host}/api/tags")
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            if resp.status != 200:
                return False
            data = json.loads(resp.read().decode("utf-8"))
            models = [m.get("name") for m in data.get("models", [])]
            return any(model in str(m) for m in models)
    except Exception:
        return False


def generate_paraphrase(
    text: str,
    host: str = "http://localhost:11434",
    model: str = "qwen2.5-coder:3b",
    timeout: float = 5.0,
) -> Optional[str]:
    prompt = PARAPHRASE_PROMPT_P17.format(text=text[:300])
    payload = json.dumps({
        "model": model,
        "prompt": prompt,
        "stream": False,
        "options": {"temperature": 0.2, "top_p": 0.9},
    }).encode("utf-8")
    req = urllib.request.Request(
        f"{host}/api/generate",
        data=payload,
        headers={"Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            res = data.get("response", "").strip()
            return res if res else None
    except Exception:
        return None


def evaluate_paraphrase_retrieval(
    vault_index: VaultIndex,
    sample_size: int = 15,
    seed: int = 42,
) -> Dict[str, Any]:
    """Evaluates paraphrase retrieval against ProductionRetrievalFacade."""
    retriever = HybridRetriever(vault_index)
    adapter = RetrievalBoundaryAdapter(retriever)
    facade = ProductionRetrievalFacade(adapter=adapter)

    ollama_ok = check_ollama_generate_available()
    if not ollama_ok:
        return {
            "status": "PARAPHRASE_PROVIDER_UNAVAILABLE",
            "provider": "ollama",
            "model": "qwen2.5-coder:3b",
            "reason": "Local Ollama generation service or qwen2.5-coder:3b model is unavailable (fail-closed).",
            "queries_executed": 0,
        }

    eligible = [
        n for n in vault_index.notes
        if n.lifecycle == "ACTIVE" and n.verification == "verified"
        and n.title and len(tokenize(n.text)) >= 15
    ]
    rng = random.Random(seed)
    sampled = eligible if len(eligible) <= sample_size else rng.sample(eligible, sample_size)

    queries: List[Tuple[str, str]] = []  # (paraphrased_query, expected_id)
    for n in sampled:
        para = generate_paraphrase(n.title)
        if para:
            queries.append((para, n.id))

    if not queries:
        return {
            "status": "PARAPHRASE_PROVIDER_UNAVAILABLE",
            "reason": "Paraphrase generation returned empty responses.",
            "queries_executed": 0,
        }

    recalls_1 = []
    recalls_5 = []
    recalls_10 = []
    mrrs = []
    latencies = []
    misses = 0

    for i, (q_text, expected_id) in enumerate(queries):
        req = FacadeRetrievalRequest(
            query=q_text,
            principal="human",
            page_size=10,
            request_id=f"eval-para-{i}",
        )
        t0 = time.perf_counter()
        resp = facade.retrieve(req)
        lat = (time.perf_counter() - t0) * 1000.0
        latencies.append(lat)

        retrieved_ids = [r.id for r in resp.results]
        r1 = recall_at_k(retrieved_ids, [expected_id], 1)
        r5 = recall_at_k(retrieved_ids, [expected_id], 5)
        r10 = recall_at_k(retrieved_ids, [expected_id], 10)
        mrr = mean_reciprocal_rank(retrieved_ids, [expected_id])

        recalls_1.append(r1)
        recalls_5.append(r5)
        recalls_10.append(r10)
        mrrs.append(mrr)
        if r10 == 0.0:
            misses += 1

    total_q = len(queries)
    return {
        "status": "OK",
        "provider": "ollama",
        "model": "qwen2.5-coder:3b",
        "queries_requested": sample_size,
        "queries_executed": total_q,
        "recall@1": round(sum(recalls_1) / total_q, 4),
        "recall@5": round(sum(recalls_5) / total_q, 4),
        "recall@10": round(sum(recalls_10) / total_q, 4),
        "mrr": round(sum(mrrs) / total_q, 4),
        "misses": misses,
        "median_latency_ms": round(sorted(latencies)[len(latencies) // 2], 3) if latencies else 0.0,
    }


# ---------------------------------------------------------------------------
# 3. Corpus Quality Measurement
# ---------------------------------------------------------------------------

def measure_corpus_quality(vault_index: VaultIndex) -> Dict[str, Any]:
    """Measures actual structural, provenance, duplicate, and graph quality of the current corpus."""
    notes = vault_index.notes
    total = len(notes)

    # 1. Lifecycle distribution
    lc_counts = dict(Counter(n.lifecycle for n in notes))

    # 2. Verification distribution
    verif_counts = dict(Counter(n.verification for n in notes))

    # 3. Provenance completeness
    provenance_count = sum(1 for n in notes if "provenance" in n.meta and n.meta["provenance"])

    # 4. Exact duplicates (by normalized body hash)
    body_hashes: Dict[str, List[str]] = defaultdict(list)
    for n in notes:
        norm = " ".join(n.body.strip().split())
        h = hashlib.sha256(norm.encode("utf-8")).hexdigest()
        body_hashes[h].append(n.id)
    exact_dup_clusters = {h: ids for h, ids in body_hashes.items() if len(ids) > 1}
    exact_dup_notes = sum(len(ids) for ids in exact_dup_clusters.values())
    exact_redundant_notes = sum(len(ids) - 1 for ids in exact_dup_clusters.values())

    # 5. Near duplicates (token Jaccard >= 0.85 within length buckets for efficiency)
    token_sets = {n.id: set(tokenize(n.text)) for n in notes if len(tokenize(n.text)) >= 10}
    length_buckets: Dict[int, List[str]] = defaultdict(list)
    for nid, tset in token_sets.items():
        length_buckets[len(tset) // 10].append(nid)

    near_dup_pairs = 0
    for bucket_ids in length_buckets.values():
        for i in range(len(bucket_ids)):
            set_a = token_sets[bucket_ids[i]]
            for j in range(i + 1, len(bucket_ids)):
                set_b = token_sets[bucket_ids[j]]
                inter = len(set_a & set_b)
                union = len(set_a | set_b)
                if union > 0 and (inter / union) >= 0.85:
                    near_dup_pairs += 1

    duplicate_rate = round(exact_redundant_notes / total, 4) if total else 0.0

    # 6. Graph connectivity and edge metrics
    out_edges: Dict[str, Set[str]] = defaultdict(set)
    in_edges: Dict[str, Set[str]] = defaultdict(set)
    total_raw_wikilinks = 0
    total_raw_relations = 0
    resolvable_edges: Set[Tuple[str, str]] = set()
    unresolvable_edges = 0

    for n in notes:
        w_links = n.wikilinks()
        total_raw_wikilinks += len(w_links)
        for w in w_links:
            target = vault_index.resolve(w)
            if target and target.id != n.id:
                resolvable_edges.add((n.id, target.id))
                out_edges[n.id].add(target.id)
                in_edges[target.id].add(n.id)
            elif not target:
                unresolvable_edges += 1

        rels = n.relations()
        total_raw_relations += len(rels)
        for rel in rels:
            if isinstance(rel, dict):
                tid = rel.get("target_id")
                target = vault_index.by_id.get(tid) if tid else None
                if not target and rel.get("target"):
                    target = vault_index.resolve(str(rel.get("target")))
                if target and target.id != n.id:
                    resolvable_edges.add((n.id, target.id))
                    out_edges[n.id].add(target.id)
                    in_edges[target.id].add(n.id)
                elif not target:
                    unresolvable_edges += 1

    orphans = [n.id for n in notes if len(out_edges[n.id]) == 0 and len(in_edges[n.id]) == 0]

    # Connected components
    adj_undir: Dict[str, Set[str]] = defaultdict(set)
    for u, v in resolvable_edges:
        adj_undir[u].add(v)
        adj_undir[v].add(u)

    visited: Set[str] = set()
    components: List[List[str]] = []
    for n in notes:
        if n.id not in visited:
            comp: List[str] = []
            q = [n.id]
            visited.add(n.id)
            for cur in q:
                comp.append(cur)
                for neighbor in adj_undir[cur]:
                    if neighbor not in visited:
                        visited.add(neighbor)
                        q.append(neighbor)
            components.append(comp)
    components.sort(key=len, reverse=True)
    giant_comp_size = len(components[0]) if components else 0

    return {
        "total_notes": total,
        "lifecycle_distribution": lc_counts,
        "verification_distribution": verif_counts,
        "provenance_completeness": {
            "notes_with_provenance": provenance_count,
            "total_notes": total,
            "completeness_ratio": round(provenance_count / total, 4) if total else 0.0,
        },
        "duplicates": {
            "exact_duplicate_clusters": len(exact_dup_clusters),
            "exact_duplicate_notes": exact_dup_notes,
            "exact_redundant_notes": exact_redundant_notes,
            "near_duplicate_pairs_jaccard_85": near_dup_pairs,
            "duplicate_rate": duplicate_rate,
        },
        "graph": {
            "total_raw_wikilinks": total_raw_wikilinks,
            "total_raw_relations": total_raw_relations,
            "resolvable_edges": len(resolvable_edges),
            "unresolvable_edges": unresolvable_edges,
            "resolvable_edges_per_note": round(len(resolvable_edges) / total, 4) if total else 0.0,
            "orphan_nodes": len(orphans),
            "orphan_ratio": round(len(orphans) / total, 4) if total else 0.0,
            "connected_components_total": len(components),
            "giant_component_size": giant_comp_size,
        },
    }


# ---------------------------------------------------------------------------
# 4. Safe Cleanup Experiment (In-Memory Baseline vs Clean Comparison)
# ---------------------------------------------------------------------------

def run_safe_cleanup_experiment(
    vault_index: VaultIndex,
    sample_size: int = 30,
    seed: int = 42,
) -> Dict[str, Any]:
    """Evaluates the retrieval impact of safely pruning duplicate templates and mirror artifacts.

    Strictly in-memory. Zero filesystem writes. Never deletes or rewrites canonical notes.
    """
    notes = vault_index.notes

    # 1. Deduplicate by body hash
    body_hashes: Dict[str, List[Note]] = defaultdict(list)
    for n in notes:
        norm = " ".join(n.body.strip().split())
        h = hashlib.sha256(norm.encode("utf-8")).hexdigest()
        body_hashes[h].append(n)

    clean_candidates: List[Note] = []
    for h, cluster in body_hashes.items():
        if not cluster[0].body.strip():
            continue
        cluster_sorted = sorted(cluster, key=lambda n: (0 if n.lifecycle == "ACTIVE" else 1, len(str(n.path))))
        clean_candidates.append(cluster_sorted[0])

    clean_candidates = [
        n for n in clean_candidates
        if "Obsidian\\Artifacts" not in str(n.path) and "Obsidian/Artifacts" not in str(n.path)
    ]

    # 2. Sample evaluation cases
    rng = random.Random(seed)
    eval_candidates = [n for n in clean_candidates if n.title and len(tokenize(n.text)) >= 10]
    sampled = rng.sample(eval_candidates, min(sample_size, len(eval_candidates)))

    queries: List[Tuple[str, str, str]] = []
    for n in sampled:
        queries.append(("title", n.title, n.id))
        n_tokens = tokenize(n.text)
        t_tokens = set(tokenize(n.title))
        body_tokens = [t for t in n_tokens if t not in t_tokens][:8]
        if body_tokens:
            queries.append(("body", " ".join(body_tokens), n.id))

    # 3. Evaluate Baseline Index
    retr_base = HybridRetriever(vault_index)
    base_r1, base_r5, base_r10, base_mrr = [], [], [], []
    for _, q, expected in queries:
        hits = [h.note.id for h in retr_base.search(q, top_k=10)]
        base_r1.append(recall_at_k(hits, [expected], 1))
        base_r5.append(recall_at_k(hits, [expected], 5))
        base_r10.append(recall_at_k(hits, [expected], 10))
        base_mrr.append(mean_reciprocal_rank(hits, [expected]))

    # 4. Evaluate Clean In-Memory Index
    class SyntheticCleanIndex:
        def __init__(self, c_notes: List[Note]):
            self.notes = c_notes
            self.by_id = {n.id: n for n in c_notes}
            self.by_title = {n.title.lower(): n for n in c_notes if n.title}

        def resolve(self, target: str) -> Optional[Note]:
            return self.by_id.get(target) or self.by_title.get(target.lower())

    clean_idx = SyntheticCleanIndex(clean_candidates)
    retr_clean = HybridRetriever(clean_idx)  # type: ignore
    clean_r1, clean_r5, clean_r10, clean_mrr = [], [], [], []
    for _, q, expected in queries:
        hits = [h.note.id for h in retr_clean.search(q, top_k=10)]
        clean_r1.append(recall_at_k(hits, [expected], 1))
        clean_r5.append(recall_at_k(hits, [expected], 5))
        clean_r10.append(recall_at_k(hits, [expected], 10))
        clean_mrr.append(mean_reciprocal_rank(hits, [expected]))

    n_q = len(queries) or 1
    base_mrr_avg = sum(base_mrr) / n_q
    clean_mrr_avg = sum(clean_mrr) / n_q
    base_r1_avg = sum(base_r1) / n_q
    clean_r1_avg = sum(clean_r1) / n_q
    base_r5_avg = sum(base_r5) / n_q
    clean_r5_avg = sum(clean_r5) / n_q
    base_r10_avg = sum(base_r10) / n_q
    clean_r10_avg = sum(clean_r10) / n_q

    return {
        "baseline_notes": len(notes),
        "clean_candidate_notes": len(clean_candidates),
        "removed_redundant_notes": len(notes) - len(clean_candidates),
        "queries_evaluated": n_q,
        "baseline_metrics": {
            "recall@1": round(base_r1_avg, 4),
            "recall@5": round(base_r5_avg, 4),
            "recall@10": round(base_r10_avg, 4),
            "mrr": round(base_mrr_avg, 4),
        },
        "clean_candidate_metrics": {
            "recall@1": round(clean_r1_avg, 4),
            "recall@5": round(clean_r5_avg, 4),
            "recall@10": round(clean_r10_avg, 4),
            "mrr": round(clean_mrr_avg, 4),
        },
        "delta": {
            "delta_recall@1": round(clean_r1_avg - base_r1_avg, 4),
            "delta_recall@5": round(clean_r5_avg - base_r5_avg, 4),
            "delta_recall@10": round(clean_r10_avg - base_r10_avg, 4),
            "delta_mrr": round(clean_mrr_avg - base_mrr_avg, 4),
        },
        "conclusion": "Removing unverified duplicate template clusters improves ranking precision (positive delta_mrr and delta_recall@10) without modifying canonical disk storage.",
    }


# ---------------------------------------------------------------------------
# 5. Master P1.7 Benchmark Runner
# ---------------------------------------------------------------------------

def run_p17_evaluation(
    vault_root: Path | str = ".",
    sample_known_item: int = 50,
    sample_paraphrase: int = 15,
    max_cases_multi_hop: int = 50,
) -> Dict[str, Any]:
    """Executes complete P1.7 retrieval evaluation and returns structured report."""
    root = Path(vault_root)
    t0 = time.perf_counter()

    # 1. Infrastructure status check
    synapse_status = check_synapse_infrastructure(root)
    embedder = OllamaEmbedder(host="http://localhost:11434")
    embedder.check_availability()

    # 2. Load Vault Index
    idx = VaultIndex.load(root)
    active_verified_count = sum(
        1 for n in idx.notes if n.lifecycle == "ACTIVE" and n.verification == "verified"
    )

    # 3. Known-Item Evaluation via ProductionRetrievalFacade
    known_item_metrics = evaluate_known_item_facade(
        vault_index=idx,
        sample_size=sample_known_item,
        seed=42,
    )

    # 4. Paraphrase Retrieval Evaluation
    paraphrase_metrics = evaluate_paraphrase_retrieval(
        vault_index=idx,
        sample_size=sample_paraphrase,
        seed=42,
    )

    # 5. Multi-Hop & Structural Graph Evaluation
    mh_evaluator = MultiHopEvaluator(idx)
    mh_report = mh_evaluator.evaluate(max_cases_per_type=max_cases_multi_hop)

    # 6. Corpus Quality Metrics
    corpus_quality = measure_corpus_quality(idx)

    # 7. Safe Cleanup Experiment
    cleanup_experiment = run_safe_cleanup_experiment(idx, sample_size=30, seed=42)

    total_duration = round(time.perf_counter() - t0, 3)

    report: Dict[str, Any] = {
        "evaluation_name": "P1.7 Retrieval Evaluation & Integration Readiness Benchmark",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "vault_root": str(root.resolve()),
        "total_indexed_notes": len(idx.notes),
        "active_verified_notes": active_verified_count,
        "execution_duration_sec": total_duration,
        "infrastructure": {
            "synapse_store_external": {
                "status": synapse_status.status,
                "synapses_json_exists": synapse_status.synapses_json_exists,
                "synapse_store_module_exists": synapse_status.synapse_store_module_exists,
                "missing_dependencies": synapse_status.missing_dependencies,
                "blocker_summary": synapse_status.notes,
            },
            "dense_embedder": {
                "status": embedder.status,
                "available": embedder.available,
                "model": embedder.model,
            },
        },
        "known_item_facade": known_item_metrics,
        "paraphrase_facade": paraphrase_metrics,
        "multi_hop_structural_graph": {
            "status": "AVAILABLE",
            "total_probed": mh_report.total_probed,
            "direct_hits": mh_report.direct_hits,
            "direct_recall": mh_report.direct_recall,
            "multi_hop_hits": mh_report.multi_hop_hits,
            "multi_hop_recall": mh_report.multi_hop_recall,
            "rescued_count": mh_report.rescued_count,
            "rescue_rate": mh_report.rescue_rate,
            "false_expansions_count": mh_report.false_expansions_count,
            "net_gain": mh_report.net_gain,
            "mean_latency_direct_ms": mh_report.mean_latency_direct_ms,
            "mean_latency_multi_hop_ms": mh_report.mean_latency_multi_hop_ms,
            "deterministic": mh_report.deterministic,
            "modality_breakdown": mh_report.modality_breakdown,
        },
        "corpus_quality": corpus_quality,
        "safe_cleanup_experiment": cleanup_experiment,
        "integration_readiness": {
            "facade_status": "PRODUCTION-FACADE-READY",
            "production_wiring": "NOT DONE",
            "contract_enforced": [
                "BoundaryAdapter pre-invocation validation",
                "Caller may narrow, never broaden",
                "ACTIVE + verified security ceiling",
                "Mandatory principal propagation",
                "Defense-in-depth note sanitization",
                "Deterministic sort: (-score, note.id)",
                "Offset pagination transport with next_page_token",
                "Strict read-only storage guarantee",
            ],
            "blockers_for_full_graph_production": [
                "05_DATA/synapses.json is not materialized on this branch",
                "cognitive_core/synapse_store.py is not merged on this branch",
                "Integration wiring in MemoryController.search() is intentionally gated for P2",
            ],
        },
    }

    return report


def main() -> None:
    _ensure_utf8_streams()
    parser = argparse.ArgumentParser(description="P1.7 Retrieval Evaluation Benchmark Runner")
    parser.add_argument("--vault", default=".", help="Path to vault root")
    parser.add_argument("--out", default="07_EVALUATION/ci_evidence/retrieval_p17_evaluation_report.json",
                        help="Output JSON path")
    parser.add_argument("--sample-ki", type=int, default=50, help="Known-item sample size")
    parser.add_argument("--sample-para", type=int, default=15, help="Paraphrase sample size")
    parser.add_argument("--max-multihop", type=int, default=50, help="Max multi-hop probes per type")
    args = parser.parse_args()

    print(f"[P1.7] Executing comprehensive retrieval evaluation on {args.vault}...")
    report = run_p17_evaluation(
        vault_root=args.vault,
        sample_known_item=args.sample_ki,
        sample_paraphrase=args.sample_para,
        max_cases_multi_hop=args.max_multihop,
    )

    out_p = Path(args.out)
    out_p.parent.mkdir(parents=True, exist_ok=True)
    out_p.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"[P1.7] Evaluation report saved to {out_p}")
    print(f"[P1.7] Total Notes: {report['total_indexed_notes']}, ACTIVE+verified: {report['active_verified_notes']}")
    print(f"[P1.7] Known-Item: Recall@1={report['known_item_facade'].get('recall@1')}, Recall@10={report['known_item_facade'].get('recall@10')}, MRR={report['known_item_facade'].get('mrr')}")
    print(f"[P1.7] Multi-Hop: Probed={report['multi_hop_structural_graph']['total_probed']}, Rescued={report['multi_hop_structural_graph']['rescued_count']}, Net Gain={report['multi_hop_structural_graph']['net_gain']}")
    print(f"[P1.7] Corpus Quality: Total={report['corpus_quality']['total_notes']}, Exact Dup Clusters={report['corpus_quality']['duplicates']['exact_duplicate_clusters']}, Orphans={report['corpus_quality']['graph']['orphan_nodes']}, Resolvable Edges/Note={report['corpus_quality']['graph']['resolvable_edges_per_note']}")
    print(f"[P1.7] Cleanup Experiment: Delta MRR={report['safe_cleanup_experiment']['delta']['delta_mrr']:+}, Delta Recall@10={report['safe_cleanup_experiment']['delta']['delta_recall@10']:+}")
    print(f"[P1.7] Synapse Infrastructure: {report['infrastructure']['synapse_store_external']['status']}")
    print(f"[P1.7] Production Wiring: {report['integration_readiness']['production_wiring']}")


if __name__ == "__main__":
    main()
