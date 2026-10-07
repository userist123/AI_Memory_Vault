#!/usr/bin/env python3
"""corpus_health_gate.py — Automated gate asserting baseline invariants of the vault corpus.

Enforces CI-002 Baseline Invariants (derived from 00_GOVERNANCE/phase_0/CORPUS_HEALTH_BASELINE.md):
1. Graph edges count >= 483 (SynapseStore.from_index).
2. Exact duplicate groups <= 5 (content body normalized SHA-256).
3. Dangling edges == 0 (all synapses point to existing indexed notes).
4. Fixture / test notes inside index == 0 (no test artifacts pollution).
5. Active notes provenance & verification invariants.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, Dict, List, Tuple

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))
packages_path = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
if str(packages_path) not in sys.path:
    sys.path.insert(0, str(packages_path))
impl_path = REPO_ROOT / "03_IMPLEMENTATION"
if str(impl_path) not in sys.path:
    sys.path.insert(0, str(impl_path))

from graph.synapse_store import SynapseStore
from retrieval.vault_index import VaultIndex

MIN_EDGES_BASELINE: int = 483
MAX_DUPLICATE_GROUPS_BASELINE: int = 5


def check_corpus_health(repo_root: Path | str = REPO_ROOT) -> Dict[str, Any]:
    """Evaluates all CI-002 corpus health baseline invariants."""
    root = Path(repo_root).resolve()
    index = VaultIndex.load(root, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(index)

    all_edges = store.all()
    edge_count = len(all_edges)

    # 1. Dangling edges (pointing to nonexistent notes)
    dangling_edges: List[Dict[str, str]] = []
    for synapse in all_edges:
        src_exists = synapse.source_id in index.by_id
        tgt_exists = synapse.target_id in index.by_id
        if not src_exists or not tgt_exists:
            dangling_edges.append({
                "source_id": synapse.source_id,
                "target_id": synapse.target_id,
                "missing": "source" if not src_exists else "target",
            })

    # 2. Fixture notes in index
    fixture_notes: List[str] = []
    for note in index.notes:
        p_str = str(note.path).lower()
        if "fixture" in p_str or "20_tests" in p_str:
            fixture_notes.append(str(note.path))

    # 3. Exact duplicate groups (whitespace-normalised lowercase content)
    hash_map: Dict[str, List[str]] = defaultdict(list)
    for note in index.notes:
        norm_text = re.sub(r"\s+", " ", note.text).strip().lower()
        if len(norm_text) > 20:
            h = hashlib.sha256(norm_text.encode("utf-8")).hexdigest()
            hash_map[h].append(str(note.path))

    duplicate_groups = [
        {"hash": h, "count": len(paths), "paths": paths}
        for h, paths in hash_map.items()
        if len(paths) > 1
    ]

    # 4. Active notes verification and provenance
    active_notes = [
        n for n in index.notes
        if str(n.meta.get("lifecycle", "")).upper() == "ACTIVE"
    ]
    active_missing_provenance: List[str] = []
    active_invalid_verification: List[Dict[str, Any]] = []
    valid_verifications = {
        "verified",
        "verified_source",
        "unverified",
        "partially_verified",
        "not_applicable",
        "inferred",
    }

    for note in active_notes:
        if not note.meta.get("provenance"):
            active_missing_provenance.append(note.id)
        ver = note.meta.get("verification")
        if ver is not None and ver not in valid_verifications:
            active_invalid_verification.append({
                "id": note.id,
                "verification": ver,
            })

    # Assertions
    passed_edges = edge_count >= MIN_EDGES_BASELINE
    passed_dangling = len(dangling_edges) == 0
    passed_fixtures = len(fixture_notes) == 0
    passed_duplicates = len(duplicate_groups) <= MAX_DUPLICATE_GROUPS_BASELINE
    passed_active_provenance = len(active_missing_provenance) == 0
    passed_active_verification = len(active_invalid_verification) == 0

    all_passed = (
        passed_edges
        and passed_dangling
        and passed_fixtures
        and passed_duplicates
        and passed_active_provenance
        and passed_active_verification
    )

    return {
        "passed": all_passed,
        "metrics": {
            "total_notes": len(index),
            "edge_count": edge_count,
            "min_edges_required": MIN_EDGES_BASELINE,
            "dangling_edge_count": len(dangling_edges),
            "fixture_notes_count": len(fixture_notes),
            "exact_duplicate_groups_count": len(duplicate_groups),
            "max_duplicate_groups_allowed": MAX_DUPLICATE_GROUPS_BASELINE,
            "active_notes_count": len(active_notes),
            "active_missing_provenance_count": len(active_missing_provenance),
            "active_invalid_verification_count": len(active_invalid_verification),
        },
        "details": {
            "dangling_edges": dangling_edges,
            "fixture_notes": fixture_notes,
            "duplicate_groups": duplicate_groups,
            "active_missing_provenance": active_missing_provenance,
            "active_invalid_verification": active_invalid_verification,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Corpus Health Gate Validator (CI-002)")
    parser.add_argument("--json", action="store_true", help="Output results in JSON format")
    args = parser.parse_args()

    results = check_corpus_health()

    if args.json:
        print(json.dumps(results, indent=2))
    else:
        status_str = "PASS" if results["passed"] else "FAIL"
        print(f"[{status_str}] Corpus Health Gate (CI-002)")
        metrics = results["metrics"]
        print(f"  - Edges: {metrics['edge_count']} (min: {metrics['min_edges_required']})")
        print(f"  - Dangling Edges: {metrics['dangling_edge_count']} (max: 0)")
        print(f"  - Fixture Notes: {metrics['fixture_notes_count']} (max: 0)")
        print(f"  - Duplicate Groups: {metrics['exact_duplicate_groups_count']} (max: {metrics['max_duplicate_groups_allowed']})")
        print(f"  - Active Notes: {metrics['active_notes_count']}")
        print(f"  - Active Missing Provenance: {metrics['active_missing_provenance_count']} (max: 0)")
        print(f"  - Active Invalid Verification: {metrics['active_invalid_verification_count']} (max: 0)")

    return 0 if results["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
