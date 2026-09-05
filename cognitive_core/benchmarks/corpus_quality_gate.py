"""cognitive_core/benchmarks/corpus_quality_gate.py — P2.1 Corpus Quality Gate.

Transforms P1.7 corpus measurements into a formal, reproducible quality gate.
Evaluates both:
1. FULL CORPUS (entire vault index, including in-flight and unverified notes).
2. RETRIEVAL TRUST BOUNDARY (notes satisfying ACTIVE + verified).

Each gate metric specifies:
- metric name and description
- rationale / justification
- impact on retrieval (ranking degradation, false positives, noise)
- measured baseline (dynamic SHA recalculation)
- target recommended threshold
- gate status: PASS, WARN, or FAIL.

Zero storage mutation. Read-only.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

from ..hybrid_retrieval import tokenize
from ..vault_index import Note, VaultIndex


@dataclass
class QualityGateMetric:
    metric: str
    dimension: str
    justification: str
    retrieval_impact: str
    measured_value: Any
    target_threshold: str
    status: str  # "PASS", "WARN", "FAIL"
    context: str  # "FULL_CORPUS" or "RETRIEVAL_BOUNDARY"


@dataclass
class CorpusQualityGateReport:
    timestamp_utc: str
    vault_root: str
    total_notes: int
    active_verified_notes: int
    gate_summary: Dict[str, int]  # {"PASS": n, "WARN": n, "FAIL": n}
    overall_verdict: str  # "PASS", "CONDITIONAL_PASS", "BLOCKED"
    metrics: List[QualityGateMetric]


def evaluate_corpus_quality_gate(vault_root: Path | str = ".") -> CorpusQualityGateReport:
    """Calculates all quality metrics and evaluates them against formal thresholds."""
    root = Path(vault_root)
    idx = VaultIndex.load(root)
    notes = idx.notes
    total = len(notes)

    active_verified = [n for n in notes if n.lifecycle == "ACTIVE" and n.verification == "verified"]
    total_av = len(active_verified)

    # -----------------------------------------------------------------------
    # Metric Calculations
    # -----------------------------------------------------------------------
    # 1. Lifecycle distribution
    lc_counts = Counter(n.lifecycle for n in notes)
    active_ratio = lc_counts.get("ACTIVE", 0) / total if total else 0.0

    # 2. Verification distribution
    verif_counts = Counter(n.verification for n in notes)
    verified_ratio = verif_counts.get("verified", 0) / total if total else 0.0
    unverified_ratio = (total - verif_counts.get("verified", 0)) / total if total else 0.0

    # 3. Provenance completeness
    prov_full = sum(1 for n in notes if "provenance" in n.meta and n.meta["provenance"])
    prov_full_ratio = prov_full / total if total else 0.0

    prov_av = sum(1 for n in active_verified if "provenance" in n.meta and n.meta["provenance"])
    prov_av_ratio = prov_av / total_av if total_av else 0.0

    # 4. Exact Duplicates
    def compute_duplicates(note_subset: List[Note]) -> Tuple[int, int, float]:
        hashes: Dict[str, List[str]] = defaultdict(list)
        for n in note_subset:
            h = hashlib.sha256(n.body.strip().encode("utf-8")).hexdigest()
            hashes[h].append(n.id)
        clusters = {h: ids for h, ids in hashes.items() if len(ids) > 1}
        redundant = sum(len(ids) - 1 for ids in clusters.values())
        rate = redundant / len(note_subset) if note_subset else 0.0
        return len(clusters), redundant, round(rate, 4)

    dup_clusters_full, dup_redundant_full, dup_rate_full = compute_duplicates(notes)
    dup_clusters_av, dup_redundant_av, dup_rate_av = compute_duplicates(active_verified)

    # 5. Near Duplicates (Jaccard >= 0.85) in active_verified
    token_sets_av = {n.id: set(tokenize(n.text)) for n in active_verified if len(tokenize(n.text)) >= 10}
    av_ids = list(token_sets_av.keys())
    near_dup_av = 0
    for i in range(len(av_ids)):
        for j in range(i + 1, len(av_ids)):
            sa, sb = token_sets_av[av_ids[i]], token_sets_av[av_ids[j]]
            if len(sa | sb) > 0 and (len(sa & sb) / len(sa | sb)) >= 0.85:
                near_dup_av += 1
    near_dup_rate_av = round(near_dup_av / total_av, 4) if total_av else 0.0

    # 6. Graph Connectivity & Edges
    def compute_graph_metrics(note_subset: List[Note], allowed_id_set: Optional[Set[str]] = None):
        valid_ids = allowed_id_set or {n.id for n in note_subset}
        out_e = defaultdict(set)
        in_e = defaultdict(set)
        resolvable = set()
        dangling = 0

        for n in note_subset:
            for w in n.wikilinks():
                tgt = idx.resolve(w)
                if tgt and tgt.id in valid_ids and tgt.id != n.id:
                    resolvable.add((n.id, tgt.id))
                    out_e[n.id].add(tgt.id)
                    in_e[tgt.id].add(n.id)
                elif not tgt or tgt.id not in valid_ids:
                    dangling += 1

            for rel in n.relations():
                if isinstance(rel, dict):
                    tid = rel.get("target_id")
                    tgt = idx.by_id.get(tid) if tid else None
                    if not tgt and rel.get("target"):
                        tgt = idx.resolve(str(rel.get("target")))
                    if tgt and tgt.id in valid_ids and tgt.id != n.id:
                        resolvable.add((n.id, tgt.id))
                        out_e[n.id].add(tgt.id)
                        in_e[tgt.id].add(n.id)
                    elif not tgt or tgt.id not in valid_ids:
                        dangling += 1

        orphans = [n.id for n in note_subset if len(out_e[n.id]) == 0 and len(in_e[n.id]) == 0]
        density = len(resolvable) / len(note_subset) if note_subset else 0.0
        orphan_ratio = len(orphans) / len(note_subset) if note_subset else 0.0
        return len(resolvable), dangling, round(density, 4), len(orphans), round(orphan_ratio, 4)

    res_e_full, dang_e_full, dens_full, orph_full, orph_ratio_full = compute_graph_metrics(notes)
    res_e_av, dang_e_av, dens_av, orph_av, orph_ratio_av = compute_graph_metrics(
        active_verified, allowed_id_set={n.id for n in active_verified}
    )

    # -----------------------------------------------------------------------
    # Quality Gate Rules & Evaluations
    # -----------------------------------------------------------------------
    metrics: List[QualityGateMetric] = []

    # --- RETRIEVAL BOUNDARY METRICS (Production-Critical) ---
    metrics.append(QualityGateMetric(
        metric="Duplicate Rate (Verified Boundary)",
        dimension="Data Hygiene",
        justification="Duplicate notes consume top-k slots, skew RRF rank fusion, and degrade candidate diversity.",
        retrieval_impact="Causes redundant results in top-5 and displaces distinct relevant notes.",
        measured_value=dup_rate_av,
        target_threshold="<= 0.0500 (<= 5%)",
        status="PASS" if dup_rate_av <= 0.05 else "FAIL",
        context="RETRIEVAL_BOUNDARY",
    ))

    metrics.append(QualityGateMetric(
        metric="Provenance Completeness (Verified Boundary)",
        dimension="Trust & Lineage",
        justification="Every note returned to an agent must trace its origin (author, session, source).",
        retrieval_impact="Unprovenanced notes allow unverified assertions to masquerade as ground truth.",
        measured_value=round(prov_av_ratio, 4),
        target_threshold=">= 0.8500 (>= 85%)",
        status="PASS" if prov_av_ratio >= 0.85 else "FAIL",
        context="RETRIEVAL_BOUNDARY",
    ))

    metrics.append(QualityGateMetric(
        metric="Relation Density (Verified Boundary)",
        dimension="Graph Connectivity",
        justification="Graph-assisted retrieval requires minimum edge density to execute spreading activation.",
        retrieval_impact="Low edge density reduces multi-hop rescue rate from 60% to near 0%.",
        measured_value=dens_av,
        target_threshold=">= 0.4000 edges/note",
        status="PASS" if dens_av >= 0.40 else "FAIL",
        context="RETRIEVAL_BOUNDARY",
    ))

    metrics.append(QualityGateMetric(
        metric="Dangling Edges (Verified Boundary)",
        dimension="Graph Integrity",
        justification="Links pointing outside the verified boundary fail resolution and waste expansion budget.",
        retrieval_impact="Generates false graph expansions and increases multi-hop latency without recall gain.",
        measured_value=dang_e_av,
        target_threshold="<= 50 dangling links",
        status="PASS" if dang_e_av <= 50 else "WARN",
        context="RETRIEVAL_BOUNDARY",
    ))

    metrics.append(QualityGateMetric(
        metric="Orphan Rate (Verified Boundary)",
        dimension="Graph Connectivity",
        justification="Notes with zero incoming or outgoing structural links cannot participate in multi-hop rescue.",
        retrieval_impact="Orphan notes rely 100% on direct lexical match and receive 0 graph assistance.",
        measured_value=orph_ratio_av,
        target_threshold="<= 0.8000 (<= 80%)",
        status="PASS" if orph_ratio_av <= 0.80 else "WARN",
        context="RETRIEVAL_BOUNDARY",
    ))

    # --- FULL CORPUS METRICS (Backlog & Hygiene Audit) ---
    metrics.append(QualityGateMetric(
        metric="Duplicate Rate (Full Corpus)",
        dimension="Storage Hygiene",
        justification="Mass-generated template stubs inflate BM25 document frequencies and distort IDF scores.",
        retrieval_impact="High duplicate count degrades term discrimination across the lexical index.",
        measured_value=dup_rate_full,
        target_threshold="<= 0.2000 (<= 20%)",
        status="FAIL" if dup_rate_full > 0.20 else "PASS",
        context="FULL_CORPUS",
    ))

    metrics.append(QualityGateMetric(
        metric="Review Backlog Ratio",
        dimension="Lifecycle Governance",
        justification="Notes in REVIEW cannot be served under security invariants I-001..I-012 until attested.",
        retrieval_impact="633 notes are inaccessible to retrieval until human review is completed.",
        measured_value=round(lc_counts.get("REVIEW", 0) / total, 4) if total else 0.0,
        target_threshold="<= 0.5000 (<= 50%)",
        status="WARN" if lc_counts.get("REVIEW", 0) / total > 0.50 else "PASS",
        context="FULL_CORPUS",
    ))

    metrics.append(QualityGateMetric(
        metric="Unverified Ratio (Full Corpus)",
        dimension="Security & Attestation",
        justification="Unverified notes must not leak into production context packs.",
        retrieval_impact="Protected boundary correctly drops these, but high ratio limits knowledge availability.",
        measured_value=round(unverified_ratio, 4),
        target_threshold="<= 0.7000 (<= 70%)",
        status="WARN" if unverified_ratio > 0.70 else "PASS",
        context="FULL_CORPUS",
    ))

    # Summary
    summary = Counter(m.status for m in metrics)
    # Overall verdict: If any RETRIEVAL_BOUNDARY metric FAILs -> BLOCKED.
    # If only FULL_CORPUS fails or warnings exist -> CONDITIONAL_PASS.
    boundary_fails = [m for m in metrics if m.context == "RETRIEVAL_BOUNDARY" and m.status == "FAIL"]
    if boundary_fails:
        overall = "BLOCKED"
    elif summary.get("FAIL", 0) > 0 or summary.get("WARN", 0) > 0:
        overall = "CONDITIONAL_PASS"
    else:
        overall = "PASS"

    return CorpusQualityGateReport(
        timestamp_utc=datetime.now(timezone.utc).isoformat(),
        vault_root=str(root.resolve()),
        total_notes=total,
        active_verified_notes=total_av,
        gate_summary=dict(summary),
        overall_verdict=overall,
        metrics=metrics,
    )


def main() -> None:
    parser = argparse.ArgumentParser(description="P2.1 Corpus Quality Gate Evaluator")
    parser.add_argument("--vault", default=".", help="Path to vault root")
    parser.add_argument("--out", default="07_EVALUATION/ci_evidence/corpus_quality_gate_report.json",
                        help="Output JSON path")
    args = parser.parse_args()

    report = evaluate_corpus_quality_gate(args.vault)
    out_p = Path(args.out)
    out_p.parent.mkdir(parents=True, exist_ok=True)
    out_p.write_text(json.dumps(asdict(report), indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"[P2.1] Corpus Quality Gate Verdict: {report.overall_verdict}")
    print(f"[P2.1] Summary: {report.gate_summary}")
    for m in report.metrics:
        print(f"  [{m.status:4s}] ({m.context}) {m.metric}: {m.measured_value} (Target: {m.target_threshold})")
    print(f"[P2.1] Detailed report saved to {out_p}")


if __name__ == "__main__":
    main()
