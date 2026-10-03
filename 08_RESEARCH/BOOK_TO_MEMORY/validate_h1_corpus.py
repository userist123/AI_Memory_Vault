"""Deterministic structural validator for the H1 retrieval corpus.

Research-only. It never changes notes, retrieval configuration, or benchmark
cases. Runtime lexical/entity reachability is intentionally reported as an
external prerequisite rather than inferred from truncated labeling excerpts.
"""
from __future__ import annotations

import hashlib
import json
from collections import Counter
from pathlib import Path
from typing import Any, Dict, List, Tuple

FAMILIES = {
    "direct_lexical",
    "paraphrase",
    "indirect_cue",
    "entity_context",
    "multi_hop_associative",
    "conflict",
    "distractor",
}
BOUNDARIES = {"candidate_generation", "ranking", "graph", "context_pack", "end_to_end"}
PRINCIPALS = {"HUMAN", "AI_AGENT", "ADMIN"}
BLOCKING_LIFECYCLES = {"RAW", "ARCHIVED"}
DEFAULT_SEARCH_LIFECYCLES = {
    "HUMAN": {"CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "RECONSOLIDATING", "SUPERSEDED"},
    "ADMIN": {"CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "RECONSOLIDATING", "SUPERSEDED"},
    "AI_AGENT": {"REVIEW", "ACTIVE"},
}


def canonical_hash(payload: Dict[str, Any]) -> str:
    """Hash canonical JSON bytes, excluding the corpus_hash field itself."""
    data = dict(payload)
    data.pop("corpus_hash", None)
    raw = json.dumps(data, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def _note_map(corpus: Dict[str, Any]) -> Dict[str, Dict[str, Any]]:
    return {str(n.get("id")): n for n in corpus.get("notes", []) if n.get("id")}


def _edge_set(corpus: Dict[str, Any]) -> set[Tuple[str, str, str]]:
    edges = set()
    for edge in corpus.get("links", []):
        edges.add((
            str(edge.get("source")),
            str(edge.get("target")),
            str(edge.get("relation", "")),
        ))
    return edges


def validate_case(case: Dict[str, Any], corpus: Dict[str, Any], expected_commit: str, expected_hash: str) -> Dict[str, Any]:
    errors: List[str] = []
    warnings: List[str] = []
    notes = _note_map(corpus)
    edges = _edge_set(corpus)

    cid = str(case.get("id", ""))
    if not cid:
        errors.append("missing_case_id")
    query = str(case.get("query", "")).strip()
    if not query:
        errors.append("missing_query")
    principal = str(case.get("principal", "")).upper()
    if principal not in PRINCIPALS:
        errors.append("invalid_principal")

    family = case.get("family")
    if family not in FAMILIES:
        errors.append("invalid_family")
    boundary = case.get("intended_boundary")
    if boundary not in BOUNDARIES:
        errors.append("invalid_intended_boundary")

    if case.get("corpus_commit") != expected_commit:
        errors.append("case_corpus_commit_mismatch")
    if case.get("corpus_hash") != expected_hash:
        errors.append("case_corpus_hash_mismatch")

    gold = [str(x) for x in (case.get("gold_relevant_notes") or [])]
    required = [str(x) for x in (case.get("required_facts") or [])]
    abstain = bool(case.get("abstain", False))
    split = str(case.get("split", "")).lower()
    if split not in {"development", "calibration", "held_out"}:
        errors.append("invalid_split")
    if split == "held_out" and case.get("expected_baseline") is not None:
        errors.append("held_out_case_has_expected_baseline")

    if abstain and gold:
        errors.append("abstain_case_has_gold")
    if abstain and required:
        errors.append("abstain_case_has_required_facts")
    if len(gold) != len(set(gold)):
        errors.append("duplicate_gold_id")
    if not abstain and not gold:
        errors.append("answerable_case_has_no_gold")
    if not abstain and not required:
        errors.append("answerable_case_has_no_required_facts")
    if len(gold) > 1 and not case.get("multi_gold_reason"):
        errors.append("multiple_gold_requires_reason")

    for gid in gold:
        note = notes.get(gid)
        if note is None:
            errors.append(f"missing_gold:{gid}")
            continue
        lifecycle = str(note.get("lifecycle", "")).upper()
        if lifecycle in BLOCKING_LIFECYCLES:
            errors.append(f"ineligible_gold_lifecycle:{gid}:{lifecycle}")
        elif lifecycle not in DEFAULT_SEARCH_LIFECYCLES.get(principal, set()):
            errors.append(f"ineligible_gold_for_principal:{gid}:{principal}:{lifecycle}")
        evidence = " ".join(str(note.get(k, "")) for k in ("title", "excerpt", "content")).lower()
        for fact in required:
            if fact.lower() not in evidence:
                errors.append(f"missing_required_fact:{gid}:{fact}")

    if family == "multi_hop_associative":
        path = case.get("graph_path") or []
        if len(path) < 2:
            errors.append("multi_hop_requires_graph_path")
        for index, step in enumerate(path):
            source = str(step.get("source", ""))
            target = str(step.get("target", ""))
            relation = str(step.get("relation", ""))
            if (source, target, relation) not in edges:
                errors.append(f"missing_or_reversed_edge:{source}:{target}:{relation}")
            if index > 0:
                previous_target = str(path[index - 1].get("target", ""))
                if source != previous_target:
                    errors.append(f"disconnected_graph_path:{index}:{previous_target}->{source}")
        if gold and path and str(path[-1].get("target")) not in gold:
            errors.append("graph_path_target_not_gold")

    if case.get("distractor_ids"):
        for did in case["distractor_ids"]:
            if str(did) not in notes:
                errors.append(f"missing_distractor:{did}")
            if str(did) in gold:
                errors.append(f"distractor_is_gold:{did}")

    if case.get("expected_baseline") is not None:
        warnings.append("expected_baseline_is_unmeasured_until_runtime_run")

    warnings.append("lexical_entity_reachability_not_measured_by_structural_validator")
    if split == "held_out" and case.get("contamination_notes"):
        warnings.append("held_out_case_has_contamination_note")
    return {
        "id": cid,
        "valid": not errors,
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
    }


def validate_corpus(cases_payload: Dict[str, Any], corpus: Dict[str, Any], final: bool = False) -> Dict[str, Any]:
    cases = cases_payload.get("cases") or []
    expected_commit = str(cases_payload.get("corpus_commit", ""))
    expected_hash = str(cases_payload.get("corpus_hash", ""))

    errors: List[str] = []
    warnings: List[str] = []
    ids = [str(c.get("id", "")) for c in cases]
    for cid, count in Counter(ids).items():
        if not cid:
            errors.append("missing_case_id")
        elif count > 1:
            errors.append(f"duplicate_case_id:{cid}")

    if not expected_commit or expected_commit.startswith("REPLACE_"):
        errors.append("corpus_commit_not_frozen")
    if not expected_hash or expected_hash.startswith("REPLACE_"):
        errors.append("corpus_hash_not_frozen")

    actual_hash = canonical_hash(corpus)
    if expected_hash and not expected_hash.startswith("REPLACE_") and expected_hash != actual_hash:
        errors.append("corpus_hash_mismatch")

    results = [
        validate_case(c, corpus, expected_commit, expected_hash)
        for c in sorted(cases, key=lambda x: str(x.get("id", "")))
    ]
    for result in results:
        errors.extend(f"{result['id']}:{e}" for e in result["errors"])
        warnings.extend(f"{result['id']}:{w}" for w in result["warnings"])

    query_counts = Counter(str(case.get("query", "")).strip() for case in cases if str(case.get("query", "")).strip())
    for query, count in sorted(query_counts.items()):
        if count > 1 and final:
            errors.append(f"duplicate_final_query:{query}")

    if final:
        for case in cases:
            if str(case.get("split", "")).lower() != "held_out":
                errors.append(f"final_case_not_held_out:{case.get("id", "")}")

    target_counts = Counter(
        str(g)
        for case in cases
        for g in (case.get("gold_relevant_notes") or [])
    )
    for gid, count in sorted(target_counts.items()):
        if count > 1:
            warnings.append(f"gold_target_reused:{gid}:{count}")
            if final:
                reused_cases = [
                    case for case in cases
                    if gid in [str(x) for x in (case.get("gold_relevant_notes") or [])]
                ]
                allowed = all(
                    case.get("family") in {"conflict", "distractor"}
                    and str(case.get("gold_reuse_reason", "")).strip()
                    for case in reused_cases
                )
                if not allowed:
                    errors.append(f"gold_target_reused_in_final:{gid}:{count}")

    return {
        "valid": not errors,
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
        "case_results": results,
        "actual_corpus_hash": actual_hash,
        "case_count": len(cases),
    }


def main(argv: List[str] | None = None) -> int:
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("cases", type=Path)
    parser.add_argument("corpus", type=Path)
    parser.add_argument("--final", action="store_true")
    args = parser.parse_args(argv)

    cases = json.loads(args.cases.read_text(encoding="utf-8"))
    corpus = json.loads(args.corpus.read_text(encoding="utf-8"))
    result = validate_corpus(cases, corpus, final=args.final)
    print(json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True))
    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
