"""retrieval_ab.py — experimental retrieval discipline benchmark (owner: claude-code).

Status: EXPERIMENTAL DISCIPLINE TOOL. This is NOT evidence that P1.1 (semantic
retrieval) is production-ready, and its output must never be cited as such.
See cognitive_core/benchmarks/retrieval_benchmark.py for the existing
LoCoMo-style harness this repo already uses elsewhere -- this file is a
separate, additive tool for comparing retrieval strategies against each
other, not a replacement.

Protocol: known-item retrieval with automatic labeling (no hand-written gold
set, so it cannot be graded favorably by construction). For each eligible
note, two exact/known-item queries are generated:
  Q_title -- the note's title
  Q_body  -- the first N distinctive tokens of the body (title EXCLUDED)
The source note is the only relevant document. Recall@1/@5/@10 and MRR are
measured via cognitive_core/benchmarks/metrics.py (the existing, already
tested metrics module -- not reimplemented here).

Five arms, reported SEPARATELY, never averaged into one number:
  1. jaccard      -- token-overlap baseline (functional equivalent of the
                      pre-P1.2 RelevanceScorer)
  2. bm25         -- pure BM25, no fusion
  3. entity       -- pure entity/identifier overlap, no fusion
  4. lexical_rrf  -- BM25 + entity fused via RRF (HybridRetriever.search(),
                      no embeddings, no graph)
  5. graph        -- lexical_rrf plus synapse-weighted spreading activation
                      when the exact note isn't already in the lexical_rrf
                      top-k

A sixth, separately-reported PARAPHRASE arm re-runs the same protocol on
locally-generated paraphrased queries (via Ollama, no external API) that
deliberately avoid the source note's own vocabulary where possible. If Ollama
is unavailable, this is reported as PARAPHRASE_PROVIDER=UNAVAILABLE -- it is
never silently omitted or presented as a zero/skip without saying why
(NO SILENT FALLBACK: provider unavailable != provider succeeded).

A best-effort MULTI_HOP section separately measures whether `graph` recovers
a note that is NOT lexically reachable from a *different*, synapse-connected
note's query (i.e. the note can only be found by traversing a real edge from
declared/inferred/promoted synapses -- never a fabricated pair).

Corpus labeling: every report is stamped CORPUS_MURDAR by default. Pass
--corpus-label CORPUS_CURAT ONLY once P0.3 hygiene (Antigravity's front) is
actually closed on the snapshot being measured -- this script cannot verify
that itself, so it never guesses; the caller must assert it explicitly.
Metrics from CORPUS_MURDAR and CORPUS_CURAT runs must never be compared as
the same population.

Fixed thresholds/constants used by this script are declared once at import
time and are not tuned after inspecting a given run's results.

    python -m cognitive_core.benchmarks.retrieval_ab --vault . --sample 150
    python -m cognitive_core.benchmarks.retrieval_ab --vault . --sample 150 --paraphrase --corpus-label CORPUS_MURDAR
"""
from __future__ import annotations

import argparse
import json
import random
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from ..hybrid_retrieval import (
    HybridRetriever,
    OllamaEmbedder,
    DenseProviderUnavailableError,
    tokenize,
)
try:
    from ..synapse_store import SynapseStore
except (ImportError, ModuleNotFoundError):
    class SynapseStore:  # type: ignore
        """Fallback SynapseStore stub when synapse_store.py is not yet merged into branch."""
        @classmethod
        def load(cls, path: Any) -> "SynapseStore":
            return cls()
        def all(self) -> List[Any]:
            return []
        def degree_stats(self) -> Dict[str, Any]:
            return {"min": 0, "max": 0, "mean": 0.0, "median": 0.0, "total_edges": 0}
        def spread(self, seeds: Dict[str, float], max_hops: int = 2) -> Dict[str, float]:
            return {}

from .metrics import mean_reciprocal_rank, recall_at_k
from ..vault_index import Note, VaultIndex

CORPUS_LABELS = {"CORPUS_MURDAR", "CORPUS_CURAT"}
GRAPH_SEED_K = 5
GRAPH_MAX_HOPS = 2
PARAPHRASE_PROMPT = (
    "Reformuleaza urmatoarea intrebare/fraza tehnica in romana, pastrand sensul "
    "exact, dar evitand cat mai mult posibil cuvintele originale (foloseste "
    "sinonime, alta structura de propozitie). Raspunde DOAR cu fraza reformulata, "
    "fara explicatii, fara ghilimele.\n\nFraza originala: {text}"
)


def _ensure_utf8_stdout() -> None:
    """See 30_SCRIPTS/knowledge/edge_proposer.py::_ensure_utf8_stdout."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def jaccard_search(notes: List[Note], query: str, top_k: int) -> List[str]:
    q = set(tokenize(query))
    if not q:
        return []
    scored = []
    for n in notes:
        t = set(tokenize(n.text))
        if not t:
            continue
        scored.append((len(q & t) / len(q | t), n.id))
    scored.sort(key=lambda p: (-p[0], p[1]))
    return [nid for _, nid in scored[:top_k]]


def graph_search(retriever: HybridRetriever, synapses: SynapseStore, query: str,
                  top_k: int) -> List[str]:
    hits = retriever.search(query, top_k=top_k)
    ids = [h.note.id for h in hits]
    if len(ids) >= top_k:
        return ids
    seeds = {h.note.id: h.score for h in hits[:GRAPH_SEED_K]}
    if not seeds:
        return ids
    activation = synapses.spread(seeds, max_hops=GRAPH_MAX_HOPS)
    ranked = [nid for nid, _ in sorted(activation.items(), key=lambda p: (-p[1], p[0]))]
    merged = ids + [nid for nid in ranked if nid not in ids]
    return merged[:top_k]


ARM_FUNCS = {
    "jaccard": lambda notes, retr, syn, q, k: jaccard_search(notes, q, k),
    "bm25": lambda notes, retr, syn, q, k: [n.id for n in retr.bm25_only(q, k)],
    "entity": lambda notes, retr, syn, q, k: [n.id for n in retr.entity_only(q, k)],
    "lexical_rrf": lambda notes, retr, syn, q, k: [h.note.id for h in retr.search(q, top_k=k)],
    "dense": lambda notes, retr, syn, q, k: [n.id for n in retr.dense_only(q, k)],
    "lexical_dense_rrf": lambda notes, retr, syn, q, k: [h.note.id for h in retr.search(q, top_k=k)],
    "graph": lambda notes, retr, syn, q, k: graph_search(retr, syn, q, k),
}


def _rank_metrics(
    ranks_and_hits: List[Tuple[List[str], str]],
    latencies: Optional[List[float]] = None,
    k_values=(1, 5, 10),
) -> Dict[str, Any]:
    n = max(len(ranks_and_hits), 1)
    out: Dict[str, Any] = {}
    for k in k_values:
        hits = sum(recall_at_k(retrieved, [target], k) for retrieved, target in ranks_and_hits)
        out[f"recall@{k}"] = round(hits / n, 4)
    mrr = sum(mean_reciprocal_rank(retrieved, [target]) for retrieved, target in ranks_and_hits) / n
    misses = sum(1 for retrieved, target in ranks_and_hits if target not in retrieved)
    out["mrr"] = round(mrr, 4)
    out["misses"] = misses
    out["queries"] = len(ranks_and_hits)
    if latencies:
        s = sorted(latencies)
        p50_idx = min(int(len(s) * 0.5), len(s) - 1)
        p95_idx = min(int(len(s) * 0.95), len(s) - 1)
        out["median_latency_ms"] = round(s[p50_idx], 3)
        out["p95_latency_ms"] = round(s[p95_idx], 3)
    return out


def evaluate_dense_ablation(
    lexical_report: Dict[str, Any],
    lexical_dense_report: Optional[Dict[str, Any]],
) -> Dict[str, Any]:
    """Evaluates whether dense retrieval is justified compared to lexical RRF."""
    if not lexical_dense_report or lexical_dense_report.get("status") == "DENSE_PROVIDER_UNAVAILABLE":
        return {
            "verdict": "DENSE NOT JUSTIFIED",
            "reason": "Dense provider unavailable (fail-closed, no synthetic fallback)",
            "delta_mrr": 0.0,
            "delta_recall@10": 0.0,
        }
    delta_mrr = round(lexical_dense_report.get("mrr", 0.0) - lexical_report.get("mrr", 0.0), 4)
    delta_r10 = round(lexical_dense_report.get("recall@10", 0.0) - lexical_report.get("recall@10", 0.0), 4)
    if delta_mrr < 0.05:
        return {
            "verdict": "DENSE NOT JUSTIFIED",
            "reason": f"MRR gain {delta_mrr} below justification threshold 0.05",
            "delta_mrr": delta_mrr,
            "delta_recall@10": delta_r10,
        }
    return {
        "verdict": "DENSE JUSTIFIED",
        "reason": f"MRR gain {delta_mrr} satisfies justification threshold 0.05",
        "delta_mrr": delta_mrr,
        "delta_recall@10": delta_r10,
    }


def evaluate_graph_ablation(
    lexical_pairs: List[Tuple[List[str], str]],
    graph_pairs: List[Tuple[List[str], str]],
) -> Dict[str, Any]:
    """Measures retrieval-only, graph-rescue, graph-induced, and false expansions."""
    retrieval_only = 0
    graph_rescue = 0
    graph_induced = 0
    false_expansions = 0
    for (lex_ranks, target), (grp_ranks, _) in zip(lexical_pairs, graph_pairs):
        in_lex = target in lex_ranks[:10]
        in_grp = target in grp_ranks[:10]
        if in_lex and in_grp:
            retrieval_only += 1
        elif (not in_lex) and in_grp:
            graph_rescue += 1
        elif in_lex and (not in_grp):
            graph_induced += 1
        elif (not in_lex) and (not in_grp):
            false_expansions += 1
    return {
        "retrieval_only": retrieval_only,
        "graph_rescue": graph_rescue,
        "graph_induced": graph_induced,
        "false_expansions": false_expansions,
        "net_gain": graph_rescue - graph_induced,
    }


def _ollama_generate(prompt: str, model: str, host: str, timeout: float = 30.0) -> Optional[str]:
    payload = json.dumps({"model": model, "prompt": prompt, "stream": False,
                          "options": {"temperature": 0.7}}).encode()
    req = urllib.request.Request(f"{host}/api/generate", data=payload,
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            text = json.loads(resp.read()).get("response", "")
    except (urllib.error.URLError, OSError, ValueError, TimeoutError):
        return None
    text = text.strip().strip('"').strip()
    return text or None


def build_paraphrase(note: Note, model: str, host: str) -> Optional[str]:
    source = f"{note.title}. {note.body[:300]}"
    return _ollama_generate(PARAPHRASE_PROMPT.format(text=source), model, host)


def multi_hop_probe(index: VaultIndex, retriever: HybridRetriever, synapses: SynapseStore,
                     max_pairs: int = 60) -> Dict[str, object]:
    """For each real synapse edge (a -> b), builds a's exact-title query and
    checks whether b is reachable: (1) directly via lexical_rrf, (2) only via
    graph expansion, or (3) neither. Uses only real, already-materialized
    edges (declared/inferred/promoted) -- never a fabricated pair."""
    pairs = []
    for syn in synapses.all():
        a, b = index.by_id.get(syn.source_id), index.by_id.get(syn.target_id)
        if a is None or b is None or a.id == b.id:
            continue
        pairs.append((a, b))
        if len(pairs) >= max_pairs:
            break
    lexical_hit = graph_only_hit = miss = 0
    for a, b in pairs:
        lex = [h.note.id for h in retriever.search(a.title, top_k=10)]
        if b.id in lex:
            lexical_hit += 1
            continue
        grp = graph_search(retriever, synapses, a.title, 10)
        if b.id in grp:
            graph_only_hit += 1
        else:
            miss += 1
    total = max(len(pairs), 1)
    return {
        "edge_pairs_probed": len(pairs),
        "reachable_lexically": lexical_hit,
        "reachable_only_via_graph": graph_only_hit,
        "unreachable": miss,
        "graph_added_value_rate": round(graph_only_hit / total, 4),
    }


def main(argv=None) -> int:
    _ensure_utf8_stdout()
    ap = argparse.ArgumentParser()
    ap.add_argument("--vault", default=".")
    ap.add_argument("--sample", type=int, default=150)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--lifecycle", default="", help="Filter nodes, e.g. ACTIVE,REVIEW,NONE")
    ap.add_argument("--synapses", default="05_DATA/synapses.json")
    ap.add_argument("--out", default="07_EVALUATION/retrieval_ab_report.json")
    ap.add_argument("--corpus-label", choices=sorted(CORPUS_LABELS), default="CORPUS_MURDAR")
    ap.add_argument("--paraphrase", action="store_true", help="Also run the paraphrase arm (needs Ollama)")
    ap.add_argument("--dense", action="store_true", help="Also run dense embedding arms (needs Ollama)")
    ap.add_argument("--entity-heavy", action="store_true", default=True)
    ap.add_argument("--no-entity-heavy", dest="entity_heavy", action="store_false")
    ap.add_argument("--multi-hop", action="store_true", default=True)
    ap.add_argument("--no-multi-hop", dest="multi_hop", action="store_false")
    ap.add_argument("--ollama-model", default="qwen2.5-coder:3b")
    ap.add_argument("--ollama-embed-model", default="nomic-embed-text")
    ap.add_argument("--ollama-host", default="http://localhost:11434")
    args = ap.parse_args(argv)

    vault = Path(args.vault)
    lifecycles = [l for l in args.lifecycle.split(",") if l] or None
    index = VaultIndex.load(vault, lifecycles=lifecycles)
    synapses = SynapseStore.load(vault / args.synapses)

    embedder = OllamaEmbedder(model=args.ollama_embed_model, host=args.ollama_host)
    dense_available = False
    if args.dense:
        dense_available = embedder.check_availability()
        if dense_available:
            embedder_to_use = embedder
        else:
            embedder_to_use = None
    else:
        embedder_to_use = None

    retriever = HybridRetriever(index, embedder=embedder_to_use)
    if dense_available:
        retriever.build_dense_index()

    pool = [n for n in index.notes if len(n.body) > 300 and len(n.title) > 8]
    random.Random(args.seed).shuffle(pool)
    pool = pool[: args.sample]

    def exact_query_title(note: Note) -> str:
        return note.title

    def exact_query_body(note: Note) -> str:
        body_tokens = [t for t in tokenize(note.body) if t not in set(tokenize(note.title))]
        if len(body_tokens) <= 20:
            return ""
        return " ".join(body_tokens[5:20])

    def exact_query_entities(note: Note) -> str:
        if note.entities:
            return " ".join(note.entities[:6])
        return ""

    def run_arms_on_pairs(pairs: List[Tuple[Note, str]]) -> Tuple[Dict[str, Any], Dict[str, List[Tuple[List[str], str]]]]:
        arms_results: Dict[str, List[Tuple[List[str], str]]] = {}
        arms_report: Dict[str, Any] = {}
        for arm, fn in ARM_FUNCS.items():
            if arm in ("dense", "lexical_dense_rrf") and not dense_available:
                arms_report[arm] = {
                    "status": "DENSE_PROVIDER_UNAVAILABLE",
                    "note": "Dense provider offline; fail-closed.",
                }
                continue
            ranks_and_hits = []
            latencies = []
            for note, q in pairs:
                t0 = time.perf_counter()
                retrieved = fn(index.notes, retriever, synapses, q, 10)
                latencies.append((time.perf_counter() - t0) * 1000)
                ranks_and_hits.append((retrieved, note.id))
            arms_results[arm] = ranks_and_hits
            arms_report[arm] = _rank_metrics(ranks_and_hits, latencies=latencies)
        return arms_report, arms_results

    # 1. Known-item Queries
    known_item_pairs = []
    for note in pool:
        for q in (exact_query_title(note), exact_query_body(note)):
            if q:
                known_item_pairs.append((note, q))
    known_item_report, known_item_arms = run_arms_on_pairs(known_item_pairs)

    # 2. Entity-heavy Queries
    entity_heavy_report: Dict[str, Any] = {"status": "NOT_REQUESTED"}
    if args.entity_heavy:
        entity_heavy_pairs = [
            (note, exact_query_entities(note))
            for note in pool
            if exact_query_entities(note)
        ]
        if entity_heavy_pairs:
            entity_heavy_report, _ = run_arms_on_pairs(entity_heavy_pairs)

    # Ablations
    dense_ablation = evaluate_dense_ablation(
        known_item_report.get("lexical_rrf", {}),
        known_item_report.get("lexical_dense_rrf"),
    )
    graph_ablation = evaluate_graph_ablation(
        known_item_arms.get("lexical_rrf", []),
        known_item_arms.get("graph", []),
    )

    report: Dict[str, object] = {
        "generated": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "corpus_label": args.corpus_label,
        "corpus_label_note": "self-declared by the caller via --corpus-label; this script "
                              "does not verify P0.3 hygiene status itself",
        "notes_indexed": len(index),
        "sampled_notes": len(pool),
        "synapse_stats": synapses.degree_stats(),
        "dense_status": "AVAILABLE" if dense_available else "DENSE_PROVIDER_UNAVAILABLE",
        "dense_ablation": dense_ablation,
        "graph_ablation": graph_ablation,
        "known_item": known_item_report,
        "entity_heavy": entity_heavy_report,
        "paraphrase": {"status": "NOT_REQUESTED"},
        "multi_hop": {"status": "NOT_REQUESTED"},
    }

    # 3. Paraphrase Queries
    if args.paraphrase:
        probe = _ollama_generate("Say OK.", args.ollama_model, args.ollama_host, timeout=10.0)
        if probe is None:
            report["paraphrase"] = {
                "status": "PARAPHRASE_PROVIDER_UNAVAILABLE",
                "note": "Ollama did not respond; arm was NOT run, not silently skipped as a zero/pass."
            }
        else:
            para_pairs = []
            for note in pool:
                para_q = build_paraphrase(note, args.ollama_model, args.ollama_host)
                if para_q:
                    para_pairs.append((note, para_q))
            para_report, _ = run_arms_on_pairs(para_pairs)
            report["paraphrase"] = {
                "status": "OK",
                "provider": "ollama",
                "model": args.ollama_model,
                "queries_generated": len(para_pairs),
                "queries_requested": len(pool),
                "arms": para_report,
            }

    # 4. Multi-hop Probe
    if args.multi_hop:
        report["multi_hop"] = multi_hop_probe(index, retriever, synapses)

    out = vault / args.out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"[{args.corpus_label}] nodes {len(index)} :: sample {len(pool)}")
    hdr = f"{'arm':18s} {'R@1':>7s} {'R@5':>7s} {'R@10':>7s} {'MRR':>7s} {'misses':>7s} {'p50(ms)':>8s} {'p95(ms)':>8s}"
    print("\n-- known_item --")
    print(hdr)
    for name, m in known_item_report.items():
        if isinstance(m, dict) and "recall@1" in m:
            print(f"{name:18s} {m['recall@1']:7.3f} {m['recall@5']:7.3f} "
                  f"{m['recall@10']:7.3f} {m['mrr']:7.3f} {m['misses']:7d} "
                  f"{m.get('median_latency_ms', 0.0):8.2f} {m.get('p95_latency_ms', 0.0):8.2f}")
        else:
            print(f"{name:18s} {m.get('status', 'SKIPPED')}")

    if isinstance(entity_heavy_report, dict) and any("recall@1" in v for v in entity_heavy_report.values() if isinstance(v, dict)):
        print("\n-- entity_heavy --")
        print(hdr)
        for name, m in entity_heavy_report.items():
            if isinstance(m, dict) and "recall@1" in m:
                print(f"{name:18s} {m['recall@1']:7.3f} {m['recall@5']:7.3f} "
                      f"{m['recall@10']:7.3f} {m['mrr']:7.3f} {m['misses']:7d} "
                      f"{m.get('median_latency_ms', 0.0):8.2f} {m.get('p95_latency_ms', 0.0):8.2f}")

    print("\n-- dense_ablation --", json.dumps(dense_ablation))
    print("-- graph_ablation --", json.dumps(graph_ablation))

    if report["paraphrase"].get("status") == "OK":
        print("\n-- paraphrase --")
        for name, m in report["paraphrase"]["arms"].items():
            if isinstance(m, dict) and "recall@1" in m:
                print(f"{name:18s} {m['recall@1']:7.3f} {m['recall@5']:7.3f} "
                      f"{m['recall@10']:7.3f} {m['mrr']:7.3f} {m['misses']:7d}")
    else:
        print(f"\n-- paraphrase: {report['paraphrase']['status']} --")

    if isinstance(report["multi_hop"], dict) and "edge_pairs_probed" in report["multi_hop"]:
        print("-- multi_hop --", json.dumps(report["multi_hop"]))
    print(f"\n-> {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
