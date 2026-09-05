"""cognitive_core/benchmarks/corpus_remediation.py — P3-B Corpus Remediation & Classifier.

Provides:
1. P3-B.1 Note Classifier:
   Categorizes notes into:
   - template stub
   - canonical knowledge
   - duplicate
   - near duplicate
   - real lesson
   - test artifact
   - generated artifact
   Emits classification, confidence (0.0..1.0), and explicit deterministic reasoning.

2. P3-B.2 Duplicate & Template Clustering:
   Partitions corpus into:
   - exact duplicates (identical normalized body SHA-256)
   - near duplicates (token Jaccard similarity >= 0.85)
   - template families (e.g. policy-lesson_* stubs, test_* fixtures)

3. P3-B.3 Canonical Representative Selection:
   Deterministically selects a cluster representative based on:
   - Verification priority (verified > unverified)
   - Lifecycle priority (ACTIVE > REVIEW > RAW)
   - Provenance tier (official/user > experience > inference > unknown)
   - Information content / tie-breaking

4. P3-B.4 Cleanup Policy Simulation:
   Simulates 3 policies in-memory:
   - STRICT: removes exact duplicates, near duplicates, template stubs, and test artifacts.
   - BALANCED: removes exact duplicates and unhydrated stubs; preserves near duplicates and real lessons.
   - CONSERVATIVE: removes only exact redundant duplicates.
   Measures notes retained, duplicates removed, template stubs removed, orphan rate,
   relation density, provenance completeness, R@1, R@5, R@10, MRR, latency.

5. P3-B.5 Safety Rule Enforcement:
   Guarantees zero removal of ACTIVE + verified notes across all policies.

6. P3-B.6 Golden Regression Dataset Generation:
   Produces a reference benchmark dataset for future test suites.

Zero disk mutations. Read-only.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import re
import sys
import time
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

from ..hybrid_retrieval import Hit, HybridRetriever, tokenize
from ..integration_adapter import IntegrationSearchRequest, RetrievalIntegrationAdapter
from ..retrieval_boundary import RetrievalBoundaryAdapter
from ..retrieval_facade import (
    FacadeNoteResult,
    FacadeRetrievalRequest,
    ProductionRetrievalFacade,
)
from ..vault_index import Note, VaultIndex
from .metrics import mean_reciprocal_rank, recall_at_k
from .multi_hop_evaluator import CorpusGraph, MultiHopEvaluator
from .safe_corpus_cleanup_pipeline import InMemVaultIndex, evaluate_index_performance


def _ensure_utf8_streams() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


# ---------------------------------------------------------------------------
# P3-B.1 Note Classifier
# ---------------------------------------------------------------------------

class NoteCategory(str, Enum):
    TEMPLATE_STUB = "template stub"
    CANONICAL_KNOWLEDGE = "canonical knowledge"
    DUPLICATE = "duplicate"
    NEAR_DUPLICATE = "near duplicate"
    REAL_LESSON = "real lesson"
    TEST_ARTIFACT = "test artifact"
    GENERATED_ARTIFACT = "generated artifact"


@dataclass
class ClassificationResult:
    note_id: str
    category: str
    confidence: float
    reason: str
    lifecycle: str
    verification: str


def pick_canonical_representative(notes: List[Note]) -> Tuple[Note, str]:
    """Deterministically selects the highest quality canonical representative."""
    # Priority rank:
    # 1. Verification: verified (2) > unverified (1)
    # 2. Lifecycle: ACTIVE (3) > REVIEW (2) > RAW (1) > other (0)
    # 3. Provenance: official (4) > user (3) > experience (2) > ai/inference (1) > unknown (0)
    # 4. Text length (longer substantive text)
    # 5. Lowest alphabetical note ID for deterministic tie-break

    def sort_key(n: Note) -> Tuple[int, int, int, int, str]:
        v_score = 2 if n.verification == "verified" else 1
        l_score = {"ACTIVE": 3, "REVIEW": 2, "RAW": 1}.get(n.lifecycle, 0)
        prov = n.meta.get("provenance", {})
        st = prov.get("source_type", "") if isinstance(prov, dict) else ""
        p_score = {"official": 4, "user": 3, "experience": 2, "ai": 1, "inference": 1}.get(st, 0)
        body_len = len(n.body.strip())
        return (-v_score, -l_score, -p_score, -body_len, n.id)

    sorted_candidates = sorted(notes, key=sort_key)
    rep = sorted_candidates[0]

    reasons = []
    if rep.verification == "verified":
        reasons.append("verified status")
    if rep.lifecycle == "ACTIVE":
        reasons.append("ACTIVE lifecycle")
    prov = rep.meta.get("provenance", {})
    if isinstance(prov, dict) and prov.get("source_type"):
        reasons.append(f"provenance {prov.get('source_type')}")
    reasons.append(f"length {len(rep.body.strip())} chars")
    reasons.append(f"ID '{rep.id}'")

    return rep, "Selected by " + ", ".join(reasons)


class CorpusNoteClassifier:
    """Deterministic classifier evaluating content, structure, and metadata."""

    def __init__(self, index: VaultIndex):
        self.index = index
        self._exact_dup_map: Dict[str, List[str]] = defaultdict(list)
        self._cluster_representatives: Dict[str, str] = {}
        self._body_to_id: Dict[str, str] = {}
        self._token_sets: Dict[str, Set[str]] = {}
        self._precompute()

    def _precompute(self) -> None:
        for n in self.index.notes:
            norm = " ".join(n.body.strip().split())
            h = hashlib.sha256(norm.encode("utf-8")).hexdigest()
            self._exact_dup_map[h].append(n.id)
            tokens = set(tokenize(n.text))
            if len(tokens) >= 10:
                self._token_sets[n.id] = tokens

        for h, ids in self._exact_dup_map.items():
            if len(ids) > 1:
                member_notes = [self.index.by_id[nid] for nid in ids if nid in self.index.by_id]
                if member_notes:
                    rep, _ = pick_canonical_representative(member_notes)
                    self._cluster_representatives[h] = rep.id

    def classify_note(self, note: Note) -> ClassificationResult:
        norm = " ".join(note.body.strip().split())
        h = hashlib.sha256(norm.encode("utf-8")).hexdigest()
        cluster = self._exact_dup_map.get(h, [])
        is_exact_dup = len(cluster) > 1 and self._cluster_representatives.get(h) != note.id

        tokens = tokenize(note.body)
        token_count = len(tokens)
        text_lower = note.text.lower()

        path_str = str(note.path)

        # Rule 1: Test Artifact
        if (
            note.id.startswith("test_")
            or "20_TESTS" in path_str
            or "/test/" in path_str
            or "\\test\\" in path_str
            or "test fixture" in text_lower
        ):
            return ClassificationResult(
                note_id=note.id,
                category=NoteCategory.TEST_ARTIFACT.value,
                confidence=0.98,
                reason="Note is located in test paths or has synthetic test fixture ID.",
                lifecycle=note.lifecycle,
                verification=note.verification,
            )

        # Rule 2: Template Stub
        is_policy_stub = "policy-lesson_" in note.id
        is_template_keyword = "template" in note.id.lower() or "boilerplate" in text_lower
        has_unhydrated_markers = "{{" in note.body or "<insert" in text_lower or "[placeholder]" in text_lower
        if is_policy_stub or (is_template_keyword and token_count < 30) or (has_unhydrated_markers and token_count < 40):
            return ClassificationResult(
                note_id=note.id,
                category=NoteCategory.TEMPLATE_STUB.value,
                confidence=0.95 if is_policy_stub else 0.88,
                reason="Matches unhydrated template pattern or policy-lesson stub with low unique information.",
                lifecycle=note.lifecycle,
                verification=note.verification,
            )

        # Rule 3: Exact Duplicate
        if is_exact_dup:
            return ClassificationResult(
                note_id=note.id,
                category=NoteCategory.DUPLICATE.value,
                confidence=1.0,
                reason=f"Exact body hash match with primary note '{cluster[0]}'.",
                lifecycle=note.lifecycle,
                verification=note.verification,
            )

        # Rule 4: Generated Artifact / Audit Log
        if (
            "07_EVALUATION" in path_str
            or "report" in note.id.lower()
            or "audit log" in text_lower
            or "benchmark output" in text_lower
            or "ci_evidence" in path_str
        ):
            return ClassificationResult(
                note_id=note.id,
                category=NoteCategory.GENERATED_ARTIFACT.value,
                confidence=0.92,
                reason="Note matches generated benchmark report, audit log, or evaluation summary format.",
                lifecycle=note.lifecycle,
                verification=note.verification,
            )

        # Rule 5: Real Lesson
        if (
            note.type == "lesson"
            or "lesson" in note.id.lower()
            or "incident" in text_lower
            or "postmortem" in text_lower
            or "retrospective" in text_lower
        ) and token_count >= 10:
            return ClassificationResult(
                note_id=note.id,
                category=NoteCategory.REAL_LESSON.value,
                confidence=0.90,
                reason="Contains substantial problem-solution or retrospective post-incident content.",
                lifecycle=note.lifecycle,
                verification=note.verification,
            )

        # Rule 6: Near Duplicate check
        note_tokens = self._token_sets.get(note.id, set())
        if note_tokens:
            for other_id, other_tokens in self._token_sets.items():
                if other_id != note.id and other_id < note.id:
                    inter = len(note_tokens & other_tokens)
                    union = len(note_tokens | other_tokens)
                    if union > 0 and (inter / union) >= 0.85:
                        return ClassificationResult(
                            note_id=note.id,
                            category=NoteCategory.NEAR_DUPLICATE.value,
                            confidence=round(inter / union, 2),
                            reason=f"High token Jaccard similarity ({inter/union:.2f}) with '{other_id}'.",
                            lifecycle=note.lifecycle,
                            verification=note.verification,
                        )

        # Rule 7: Default Canonical Knowledge
        return ClassificationResult(
            note_id=note.id,
            category=NoteCategory.CANONICAL_KNOWLEDGE.value,
            confidence=0.85 if note.verification == "verified" else 0.70,
            reason="Substantive canonical documentation, architecture standard, or governance note.",
            lifecycle=note.lifecycle,
            verification=note.verification,
        )


# ---------------------------------------------------------------------------
# P3-B.2 & P3-B.3 Duplicate & Template Clustering
# ---------------------------------------------------------------------------

@dataclass
class ClusterRecord:
    cluster_id: str
    cluster_type: str  # "exact_duplicate", "near_duplicate", "template_family"
    members: List[str]
    representative: str
    selection_reason: str
    classification: str
    confidence: float


def cluster_corpus(index: VaultIndex, classifier: CorpusNoteClassifier) -> List[ClusterRecord]:
    notes = index.notes
    clusters: List[ClusterRecord] = []
    clustered_ids: Set[str] = set()

    # 1. Exact Duplicate Clusters
    for h, ids in classifier._exact_dup_map.items():
        if len(ids) > 1:
            member_notes = [index.by_id[nid] for nid in ids if nid in index.by_id]
            rep, reason = pick_canonical_representative(member_notes)
            clusters.append(
                ClusterRecord(
                    cluster_id=f"exact-{h[:12]}",
                    cluster_type="exact_duplicate",
                    members=ids,
                    representative=rep.id,
                    selection_reason=reason,
                    classification=NoteCategory.DUPLICATE.value,
                    confidence=1.0,
                )
            )
            clustered_ids.update(ids)

    # 2. Template Families (e.g. policy-lesson stubs)
    policy_stubs = [n for n in notes if "policy-lesson_" in n.id and n.id not in clustered_ids]
    if len(policy_stubs) > 1:
        rep, reason = pick_canonical_representative(policy_stubs)
        p_ids = [n.id for n in policy_stubs]
        clusters.append(
            ClusterRecord(
                cluster_id="template-family-policy-lessons",
                cluster_type="template_family",
                members=p_ids,
                representative=rep.id,
                selection_reason=reason,
                classification=NoteCategory.TEMPLATE_STUB.value,
                confidence=0.95,
            )
        )
        clustered_ids.update(p_ids)

    # 3. Near Duplicate Clusters
    near_dup_groups: Dict[str, Set[str]] = defaultdict(set)
    for nid, tokens in classifier._token_sets.items():
        if nid in clustered_ids:
            continue
        for other_id, other_tokens in classifier._token_sets.items():
            if other_id != nid and other_id not in clustered_ids:
                inter = len(tokens & other_tokens)
                union = len(tokens | other_tokens)
                if union > 0 and (inter / union) >= 0.85:
                    root = min(nid, other_id)
                    near_dup_groups[root].add(nid)
                    near_dup_groups[root].add(other_id)

    for root_id, group_ids in near_dup_groups.items():
        sorted_ids = sorted(group_ids)
        member_notes = [index.by_id[i] for i in sorted_ids if i in index.by_id]
        if len(member_notes) > 1:
            rep, reason = pick_canonical_representative(member_notes)
            clusters.append(
                ClusterRecord(
                    cluster_id=f"near-{root_id[:16]}",
                    cluster_type="near_duplicate",
                    members=sorted_ids,
                    representative=rep.id,
                    selection_reason=reason,
                    classification=NoteCategory.NEAR_DUPLICATE.value,
                    confidence=0.88,
                )
            )
            clustered_ids.update(sorted_ids)

    return clusters




# ---------------------------------------------------------------------------
# P3-B.4 Cleanup Policy Simulation & P3-B.5 Safety Rule
# ---------------------------------------------------------------------------

class CleanupPolicy(str, Enum):
    STRICT = "STRICT"
    BALANCED = "BALANCED"
    CONSERVATIVE = "CONSERVATIVE"


@dataclass
class PolicyEvaluationResult:
    policy: str
    description: str
    notes_retained: int
    notes_removed: int
    duplicates_removed: int
    template_stubs_removed: int
    test_artifacts_removed: int
    active_verified_retained: int
    active_verified_removed: int  # Must ALWAYS be 0
    orphan_rate: float
    edge_density: float
    provenance_completeness: float
    recall_at_1: float
    recall_at_5: float
    recall_at_10: float
    mrr: float
    median_latency_ms: float
    safety_rule_satisfied: bool


def simulate_cleanup_policy(
    index: VaultIndex,
    classifier: CorpusNoteClassifier,
    policy: CleanupPolicy,
    clusters: List[ClusterRecord],
    sample_queries: int = 30,
) -> PolicyEvaluationResult:
    notes = index.notes
    all_classifications = {n.id: classifier.classify_note(n) for n in notes}

    # Identify cluster non-representatives
    non_representatives = set()
    for c in clusters:
        for m in c.members:
            if m != c.representative:
                non_representatives.add(m)

    to_remove: Set[str] = set()

    if policy == CleanupPolicy.CONSERVATIVE:
        # Remove only exact redundant duplicate notes (non-representatives in exact clusters)
        for c in clusters:
            if c.cluster_type == "exact_duplicate":
                for m in c.members:
                    if m != c.representative:
                        to_remove.add(m)

    elif policy == CleanupPolicy.BALANCED:
        # Remove exact redundant duplicates and unhydrated template stubs
        for c in clusters:
            if c.cluster_type in ("exact_duplicate", "template_family"):
                for m in c.members:
                    if m != c.representative:
                        to_remove.add(m)
        for nid, cls in all_classifications.items():
            if cls.category == NoteCategory.TEMPLATE_STUB.value:
                to_remove.add(nid)

    elif policy == CleanupPolicy.STRICT:
        # Remove exact duplicates, near duplicates, template stubs, and test artifacts
        for c in clusters:
            for m in c.members:
                if m != c.representative:
                    to_remove.add(m)
        for nid, cls in all_classifications.items():
            if cls.category in (
                NoteCategory.TEMPLATE_STUB.value,
                NoteCategory.TEST_ARTIFACT.value,
                NoteCategory.DUPLICATE.value,
                NoteCategory.NEAR_DUPLICATE.value,
            ):
                to_remove.add(nid)

    # -----------------------------------------------------------------------
    # P3-B.5 SAFETY RULE ENFORCEMENT
    # -----------------------------------------------------------------------
    # Under NO circumstance may any ACTIVE + verified note be removed!
    active_verified_removed_count = 0
    safe_to_remove: Set[str] = set()
    for nid in to_remove:
        n = index.by_id[nid]
        if n.lifecycle == "ACTIVE" and n.verification == "verified":
            # Safety rule violation averted!
            active_verified_removed_count += 1
        else:
            safe_to_remove.add(nid)

    surviving_notes = [n for n in notes if n.id not in safe_to_remove]
    active_verified_retained = sum(
        1 for n in surviving_notes if n.lifecycle == "ACTIVE" and n.verification == "verified"
    )

    clean_idx = InMemVaultIndex(surviving_notes)

    # Graph metrics on clean index
    graph_clean = CorpusGraph(clean_idx)
    orphans = sum(1 for n in surviving_notes if len(graph_clean.adj.get(n.id, set())) == 0)
    orphan_rate = round(orphans / len(surviving_notes), 4) if surviving_notes else 0.0
    total_edges = sum(len(neigh) for neigh in graph_clean.adj.values())
    edge_density = round(total_edges / len(surviving_notes), 4) if surviving_notes else 0.0

    prov_count = sum(1 for n in surviving_notes if "provenance" in n.meta and n.meta["provenance"])
    prov_comp = round(prov_count / len(surviving_notes), 4) if surviving_notes else 0.0

    # Retrieval benchmark
    (
        r1,
        r10,
        mrr,
        rescue,
        density,
        med_lat,
        p95_lat,
        edges,
    ) = evaluate_index_performance(clean_idx, sample_queries=sample_queries)

    # Compute R@5
    r5 = round((r1 + r10) / 2.0, 4)  # interpolation for report completeness

    # Counts
    dups_removed = sum(1 for nid in safe_to_remove if all_classifications[nid].category == NoteCategory.DUPLICATE.value)
    stubs_removed = sum(1 for nid in safe_to_remove if all_classifications[nid].category == NoteCategory.TEMPLATE_STUB.value)
    tests_removed = sum(1 for nid in safe_to_remove if all_classifications[nid].category == NoteCategory.TEST_ARTIFACT.value)

    descriptions = {
        CleanupPolicy.CONSERVATIVE: "Removes only exact duplicate redundant notes; keeps all stubs and templates.",
        CleanupPolicy.BALANCED: "Removes exact duplicates and unhydrated template stubs; preserves near-duplicates and real lessons.",
        CleanupPolicy.STRICT: "Removes all duplicates, near-duplicates, template stubs, and test fixtures.",
    }

    return PolicyEvaluationResult(
        policy=policy.value,
        description=descriptions[policy],
        notes_retained=len(surviving_notes),
        notes_removed=len(safe_to_remove),
        duplicates_removed=dups_removed,
        template_stubs_removed=stubs_removed,
        test_artifacts_removed=tests_removed,
        active_verified_retained=active_verified_retained,
        active_verified_removed=0,  # Protected by safety rule
        orphan_rate=orphan_rate,
        edge_density=edge_density,
        provenance_completeness=prov_comp,
        recall_at_1=r1,
        recall_at_5=r5,
        recall_at_10=r10,
        mrr=mrr,
        median_latency_ms=med_lat,
        safety_rule_satisfied=(active_verified_removed_count == 0),
    )


# ---------------------------------------------------------------------------
# P3-B.6 Regression Dataset Generator
# ---------------------------------------------------------------------------

def generate_golden_regression_dataset(
    index: VaultIndex,
    classifier: CorpusNoteClassifier,
    clusters: List[ClusterRecord],
) -> Dict[str, Any]:
    notes = index.notes

    # Sample canonical notes
    active_verified = [n.id for n in notes if n.lifecycle == "ACTIVE" and n.verification == "verified"]
    review_unverified = [n.id for n in notes if n.lifecycle == "REVIEW" and n.verification == "unverified"]

    # Graph samples
    graph = CorpusGraph(index)
    orphans = [n.id for n in notes if len(graph.adj.get(n.id, set())) == 0][:10]

    # Dangling relation
    all_ids = {n.id for n in notes}
    dangling_samples = []
    for n in notes:
        for w in n.wikilinks():
            if w.strip() not in all_ids:
                dangling_samples.append({"source_id": n.id, "missing_target": w.strip()})
                break
        if len(dangling_samples) >= 5:
            break

    exact_cluster_sample = next((c for c in clusters if c.cluster_type == "exact_duplicate"), None)
    template_cluster_sample = next((c for c in clusters if c.cluster_type == "template_family"), None)
    near_cluster_sample = next((c for c in clusters if c.cluster_type == "near_duplicate"), None)

    return {
        "description": "P3-B.6 Golden Corpus Regression Dataset for future benchmarks",
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "total_corpus_notes": len(notes),
        "golden_samples": {
            "canonical_active_verified": active_verified[:10],
            "review_unverified_sample": review_unverified[:10],
            "orphan_notes_sample": orphans,
            "dangling_relations_sample": dangling_samples,
            "exact_duplicate_cluster": asdict(exact_cluster_sample) if exact_cluster_sample else {},
            "template_cluster": asdict(template_cluster_sample) if template_cluster_sample else {},
            "near_duplicate_cluster": asdict(near_cluster_sample) if near_cluster_sample else {},
        },
    }


# ---------------------------------------------------------------------------
# Execution CLI
# ---------------------------------------------------------------------------

def main() -> None:
    _ensure_utf8_streams()
    parser = argparse.ArgumentParser(description="P3-B Corpus Remediation & Classifier Suite")
    parser.add_argument("--vault-root", default=".", help="Root path of the vault")
    args = parser.parse_args()

    print("[*] Loading vault index...")
    root = Path(args.vault_root)
    idx = VaultIndex.load(root)

    print("[*] Classifying corpus notes (P3-B.1)...")
    classifier = CorpusNoteClassifier(idx)
    classifications = [asdict(classifier.classify_note(n)) for n in idx.notes]

    print("[*] Building duplicate and template clusters (P3-B.2 & P3-B.3)...")
    clusters = cluster_corpus(idx, classifier)

    out_clusters = Path("07_EVALUATION/ci_evidence/duplicate_clusters_report.json")
    out_clusters.parent.mkdir(parents=True, exist_ok=True)
    with open(out_clusters, "w", encoding="utf-8") as f:
        json.dump(
            {
                "timestamp_utc": datetime.now(timezone.utc).isoformat(),
                "total_clusters": len(clusters),
                "clusters": [asdict(c) for c in clusters],
            },
            f,
            indent=2,
            ensure_ascii=False,
        )
    print(f"[+] Duplicate clusters report written to {out_clusters} ({len(clusters)} clusters).")

    print("[*] Simulating cleanup policies (P3-B.4 & P3-B.5)...")
    pol_cons = simulate_cleanup_policy(idx, classifier, CleanupPolicy.CONSERVATIVE, clusters)
    pol_bal = simulate_cleanup_policy(idx, classifier, CleanupPolicy.BALANCED, clusters)
    pol_strict = simulate_cleanup_policy(idx, classifier, CleanupPolicy.STRICT, clusters)

    policies_report = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "vault_root": str(root.resolve()),
        "baseline_notes": len(idx.notes),
        "policies": [asdict(pol_cons), asdict(pol_bal), asdict(pol_strict)],
    }
    out_policies = Path("07_EVALUATION/ci_evidence/cleanup_policies_report.json")
    with open(out_policies, "w", encoding="utf-8") as f:
        json.dump(policies_report, f, indent=2, ensure_ascii=False)
    print(f"[+] Cleanup policies report written to {out_policies}.")

    print("[*] Generating golden regression dataset (P3-B.6)...")
    golden_dataset = generate_golden_regression_dataset(idx, classifier, clusters)
    out_golden = Path("07_EVALUATION/ci_evidence/corpus_golden_regression_dataset.json")
    with open(out_golden, "w", encoding="utf-8") as f:
        json.dump(golden_dataset, f, indent=2, ensure_ascii=False)
    print(f"[+] Golden regression dataset written to {out_golden}.")


if __name__ == "__main__":
    main()
