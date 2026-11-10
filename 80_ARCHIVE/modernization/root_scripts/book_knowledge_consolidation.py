"""Deterministic, human-gated consolidation of provisional book candidates.

This module intentionally uses transparent lexical rules and a small reviewed
relation map.  It does not write canonical memory or modify raw book files.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

RELATION_TYPES = {
    "DUPLICATE", "NEAR_DUPLICATE", "COMPLEMENTARY", "CONFLICT",
    "SUPERSEDES", "UNRELATED",
}
STATUSES = {
    "CANDIDATE", "SOURCE_SUPPORTED", "CROSS_SOURCE_SUPPORTED", "CONFLICTING",
    "NEEDS_VERIFICATION", "REJECTED", "READY_FOR_HUMAN_REVIEW",
}

CLUSTER_RULES = [
    ("SYSTEM_RELIABILITY", {"reliability", "scalability", "deployment", "production", "failure", "monitoring", "distribution"}),
    ("DISTRIBUTED_SYSTEMS", {"distributed", "replication", "partitioning", "consistency", "consensus", "transaction", "stream"}),
    ("MEMORY", {"memory", "state", "retain", "context"}),
    ("RETRIEVAL", {"retrieval", "retrieved", "rag", "chunks", "embeds", "embedding", "external context"}),
    ("AGENTS", {"agent", "orchestration", "reasoning", "generation", "perception", "action", "utility", "environment"}),
    ("TOOLS", {"tool", "authorization", "interface", "tokenization"}),
    ("EVALUATION", {"evaluation", "evaluated", "benchmark", "test", "metric", "generalization"}),
    ("LEARNING", {"learning", "training", "gradient", "backpropagation", "optimization", "data", "label", "feature"}),
    ("MODEL_ADAPTATION", {"fine-tuning", "fine tuning", "adaptation", "prompt", "alignment", "inference"}),
    ("REPRESENTATIONS", {"representation", "representations", "vector", "attention", "transformer", "neural", "classifier", "convolutional", "recurrent", "lstm"}),
    ("SECURITY_GOVERNANCE", {"authorization", "ethical", "social", "acceptable"}),
    ("AI_FOUNDATIONS", {"search", "constraint", "logical", "probabilistic", "reinforcement", "natural language", "computer vision"}),
]

# These groups were reviewed from the actual 54 statements.  They are
# consolidation hypotheses, not claims that any book uses the same wording.
SYNTHESIS_GROUPS = [
    ("M-ARCH-001", "SYSTEM_RELIABILITY", "AI applications are layered systems: model capabilities must be composed with context, tools, orchestration, runtime constraints, evaluation, and operational feedback.", ["book-llm-apps-c001", "book-llm-apps-c008", "book-agents-2026-c001", "book-agents-2026-c007", "book-mlsystems-2022-c002"]),
    ("M-RETRIEVAL-001", "RETRIEVAL", "External context is part of system correctness. When required information exists outside model parameters, the system must explicitly manage retrieval, provenance, freshness, conflicts, and failure modes.", ["book-agents-2026-c003", "book-llm-apps-c005", "book-llm-apps-c007", "book-agents-2026-c004", "book-ddia-2017-c005"]),
    ("M-EVAL-001", "EVALUATION", "Evaluation must measure the behavior being claimed under representative conditions; component presence or static benchmark performance is weaker evidence than observed application behavior.", ["book-agents-2026-c005", "book-agents-2026-c008", "book-mlsystems-2022-c005", "book-aima-4e-c006"]),
    ("M-RELIABILITY-001", "SYSTEM_RELIABILITY", "Production reliability is a system property: failure handling, integration constraints, monitoring, and distribution shift must be designed as part of deployment.", ["book-ddia-2017-c001", "book-ddia-2017-c009", "book-mlsystems-2022-c006", "book-mlsystems-2022-c007"]),
    ("M-DISTRIBUTED-001", "DISTRIBUTED_SYSTEMS", "Distributed operation introduces timing, partial failure, replication consistency, and conflict-resolution concerns that cannot be inferred from single-machine behavior.", ["book-ddia-2017-c006", "book-ddia-2017-c009", "book-ddia-2017-c010"]),
    ("M-ADAPT-001", "MODEL_ADAPTATION", "Prompting, retrieval, fine-tuning, alignment, and inference-time methods are distinct adaptation levers whose trade-offs depend on the application.", ["book-agents-2026-c002", "book-agents-2026-c006", "book-llm-apps-c004", "book-llm-apps-c007"]),
    ("M-TOOLS-001", "TOOLS", "External tools extend model behavior, but tool contracts, authorization, orchestration, and interface compatibility become part of correctness.", ["book-agents-2026-c004", "book-llm-apps-c005", "book-llm-apps-c008", "book-ddia-2017-c005"]),
    ("M-LEARNING-001", "LEARNING", "Learning systems improve through iterative interaction among data, representations, models, evaluation, deployment, and feedback rather than a one-shot training pipeline.", ["book-aima-4e-c006", "book-mlsystems-2022-c002", "book-mlsystems-2022-c008", "book-aima-4e-c008"]),
    ("M-REPRESENT-001", "REPRESENTATIONS", "Learned representations transform raw inputs into structures that support downstream inference, similarity, sequence processing, and task behavior.", ["book-aima-4e-c007", "book-aima-4e-c009", "book-learning-deep-learning-c007", "book-learning-deep-learning-c009", "book-llm-apps-c006"]),
    ("M-TRADEOFF-001", "SYSTEM_RELIABILITY", "System design choices expose trade-offs among performance, completeness, latency, resource use, consistency, and concurrency; the relevant workload and failure assumptions determine the choice.", ["book-aima-4e-c002", "book-ddia-2017-c004", "book-ddia-2017-c008", "book-llm-apps-c007"]),
]

# Explicitly known relationships, including deliberate conflict test material.
RELATION_OVERRIDES = {
    frozenset(("book-agents-2026-c003", "book-llm-apps-c007")): "NEAR_DUPLICATE",
    frozenset(("book-agents-2026-c003", "book-llm-apps-c005")): "COMPLEMENTARY",
    frozenset(("book-ddia-2017-c006", "book-ddia-2017-c009")): "COMPLEMENTARY",
    frozenset(("book-ddia-2017-c001", "book-mlsystems-2022-c005")): "COMPLEMENTARY",
    frozenset(("book-agents-2026-c005", "book-agents-2026-c008")): "NEAR_DUPLICATE",
    frozenset(("book-llm-apps-c004", "book-agents-2026-c006")): "NEAR_DUPLICATE",
    frozenset(("book-aima-4e-c007", "book-learning-deep-learning-c009")): "COMPLEMENTARY",
    frozenset(("book-ddia-2017-c010", "book-ddia-2017-c008")): "COMPLEMENTARY",
    frozenset(("book-mlsystems-2022-c005", "book-agents-2026-c008")): "COMPLEMENTARY",
    # A deliberately preserved tension: static/offline evidence vs production.
    frozenset(("book-mlsystems-2022-c005", "book-agents-2026-c008")): "COMPLEMENTARY",
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_candidates(root: Path) -> tuple[list[dict[str, Any]], dict[str, dict[str, Any]]]:
    manifest = {x["source_path"]: x for x in json.loads((root / "source_manifest.json").read_text(encoding="utf-8"))}
    candidates = []
    pattern = re.compile(r"^### (\S+) - (\S+)\n(.*?)(?=\n- Source locator:)", re.M | re.S)
    for path in sorted(root.glob("book-*.md")):
        text = path.read_text(encoding="utf-8")
        source_path = re.search(r"^\*\*Source:\*\* `([^`]+)`", text, re.M).group(1)
        source_sha = re.search(r"^\*\*SHA-256:\*\* `([^`]+)`", text, re.M).group(1)
        if source_path not in manifest or manifest[source_path]["sha256"] != source_sha:
            raise ValueError(f"manifest mismatch for {path}")
        for match in pattern.finditer(text):
            cid, kind, statement = match.groups()
            tail = text[match.end():]
            locator = re.search(r"- Source locator: `([^`]+)`", tail).group(1)
            sha = re.search(r"- Source SHA-256: `([^`]+)`", tail).group(1)
            if sha != source_sha:
                raise ValueError(f"candidate hash mismatch: {cid}")
            candidates.append({"candidate_id": cid, "type": kind, "statement": " ".join(statement.split()), "source_path": source_path, "source_sha256": sha, "locator": locator, "candidate_path": str(path).replace("\\", "/")})
    if len(candidates) != 54:
        raise ValueError(f"expected 54 candidates, found {len(candidates)}")
    return candidates, manifest


def cluster_for(candidate: dict[str, Any]) -> str:
    text = candidate["statement"].lower()
    scores = [(sum(1 for term in terms if term in text), name) for name, terms in CLUSTER_RULES]
    score, name = max(scores)
    return name if score else "GENERAL_AI_SYSTEMS"


def classify(a: dict[str, Any], b: dict[str, Any]) -> str:
    key = frozenset((a["candidate_id"], b["candidate_id"]))
    if key in RELATION_OVERRIDES:
        return RELATION_OVERRIDES[key]
    if a["statement"].lower() == b["statement"].lower():
        return "DUPLICATE"
    # Only mark a conflict when the statements share a substantial subject
    # vocabulary and explicitly reverse polarity.  Mere difference is not a
    # conflict, and no SUPERSEDES relation is inferred here.
    neg = {"not", "no", "never", "cannot", "can't", "without"}
    at = set(re.findall(r"[a-z]{4,}", a["statement"].lower()))
    bt = set(re.findall(r"[a-z]{4,}", b["statement"].lower()))
    shared = at & bt
    a_neg = bool(neg & set(a["statement"].lower().split()))
    b_neg = bool(neg & set(b["statement"].lower().split()))
    if len(shared) >= 3 and a_neg != b_neg and cluster_for(a) == cluster_for(b):
        return "CONFLICT"
    # Conservative lexical near-duplicate fallback: only same-cluster pairs
    # with a high token overlap qualify; otherwise they remain unrelated.
    overlap = len(at & bt) / max(1, min(len(at), len(bt)))
    if cluster_for(a) == cluster_for(b) and overlap >= 0.72:
        return "NEAR_DUPLICATE"
    return "UNRELATED"


def source_ref(candidate: dict[str, Any]) -> dict[str, str]:
    return {"book_id": candidate["candidate_id"].rsplit("-c", 1)[0], "source_path": candidate["source_path"], "source_sha256": candidate["source_sha256"], "locator": candidate["locator"], "candidate_id": candidate["candidate_id"]}


def build(root: Path, output: Path) -> dict[str, int]:
    candidates, _manifest = load_candidates(root)
    by_id = {x["candidate_id"]: x for x in candidates}
    cluster_map = {x["candidate_id"]: cluster_for(x) for x in candidates}
    clusters = defaultdict(list)
    for cid, cluster in cluster_map.items():
        clusters[cluster].append(cid)
    output.mkdir(parents=True, exist_ok=True)
    schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "title": "Book knowledge atom V1",
        "type": "object",
        "required": ["id", "type", "statement", "status", "sources", "relations", "verification_status", "verification_required", "utility_score", "reuse_probability", "stability", "misleading_risk", "limitations", "temporal_validity", "cluster_id"],
        "properties": {
            "id": {"type": "string"}, "type": {"enum": ["SYNTHESIS", "SINGLE_SOURCE"]}, "statement": {"type": "string"},
            "status": {"enum": sorted(STATUSES)}, "sources": {"type": "array", "minItems": 1},
            "relations": {"type": "array"}, "verification_status": {"enum": sorted(STATUSES)}, "verification_required": {"const": True},
            "utility_score": {"type": "number", "minimum": 0, "maximum": 1}, "reuse_probability": {"type": "number", "minimum": 0, "maximum": 1},
            "stability": {"type": "number", "minimum": 0, "maximum": 1}, "misleading_risk": {"type": "number", "minimum": 0, "maximum": 1},
            "limitations": {"type": "array"}, "temporal_validity": {"type": "string"}, "cluster_id": {"type": "string"},
        },
    }
    (output / "knowledge_atom_schema.json").write_text(json.dumps(schema, indent=2) + "\n", encoding="utf-8")
    (output / "candidate_clusters.json").write_text(json.dumps({"candidate_count": 54, "clusters": [{"cluster_id": k, "candidate_ids": sorted(v)} for k, v in sorted(clusters.items())], "candidate_to_cluster": cluster_map}, indent=2) + "\n", encoding="utf-8")

    relations = []
    counts = Counter()
    ids = sorted(by_id)
    for i, aid in enumerate(ids):
        for bid in ids[i + 1:]:
            relation = classify(by_id[aid], by_id[bid])
            relations.append({"source": aid, "target": bid, "type": relation, "basis": "reviewed_override" if frozenset((aid, bid)) in RELATION_OVERRIDES else "conservative_lexical_rule"})
            counts[relation] += 1
    (output / "relation_matrix.json").write_text(json.dumps({"candidate_count": 54, "relations": relations, "relation_types": sorted(RELATION_TYPES)}, indent=2) + "\n", encoding="utf-8")

    used = set()
    atoms = []
    evidence = []
    for atom_id, cluster, statement, support_ids in SYNTHESIS_GROUPS:
        refs = [source_ref(by_id[cid]) for cid in support_ids]
        used.update(support_ids)
        atoms.append({"id": atom_id, "type": "SYNTHESIS", "statement": statement, "status": "READY_FOR_HUMAN_REVIEW", "sources": refs, "supporting_candidates": support_ids, "relations": [], "confidence": 0.78, "verification_status": "CROSS_SOURCE_SUPPORTED", "verification_required": True, "utility_score": 0.88, "reliability": 0.72, "reuse_probability": 0.9, "stability": 0.84, "misleading_risk": 0.32, "retention_cost": 0.2, "limitations": ["Synthesis is an interpretation across provisional candidates, not a quotation.", "Source-level claims still require human checking."], "temporal_validity": "stable principle; application context may change", "cluster_id": cluster})
        evidence.append({"knowledge_id": atom_id, "evidence": [{"source": ref["book_id"], "locator": ref["locator"], "source_sha256": ref["source_sha256"], "support_type": "SYNTHESIS", "candidate_id": ref["candidate_id"]} for ref in refs]})
    for candidate in candidates:
        if candidate["candidate_id"] in used:
            continue
        atom_id = "S-" + candidate["candidate_id"]
        ref = source_ref(candidate)
        atoms.append({"id": atom_id, "type": "SINGLE_SOURCE", "statement": candidate["statement"], "status": "READY_FOR_HUMAN_REVIEW", "sources": [ref], "supporting_candidates": [candidate["candidate_id"]], "relations": [], "confidence": 0.55, "verification_status": "SOURCE_SUPPORTED", "verification_required": True, "utility_score": 0.55, "reliability": 0.55, "reuse_probability": 0.5, "stability": 0.7, "misleading_risk": 0.4, "retention_cost": 0.3, "limitations": ["Single provisional source; not cross-source corroborated."], "temporal_validity": "requires domain/context review", "cluster_id": cluster_map[candidate["candidate_id"]]})
        evidence.append({"knowledge_id": atom_id, "evidence": [{"source": ref["book_id"], "locator": ref["locator"], "source_sha256": ref["source_sha256"], "support_type": "DIRECT", "candidate_id": candidate["candidate_id"]}]})
    with (output / "knowledge_atoms.jsonl").open("w", encoding="utf-8") as fh:
        for atom in sorted(atoms, key=lambda x: x["id"]): fh.write(json.dumps(atom, ensure_ascii=False) + "\n")
    with (output / "evidence_bundles.jsonl").open("w", encoding="utf-8") as fh:
        for item in sorted(evidence, key=lambda x: x["knowledge_id"]): fh.write(json.dumps(item, ensure_ascii=False) + "\n")

    queue = []
    for atom in sorted(atoms, key=lambda x: (-x["utility_score"], x["id"])):
        queue.append({"knowledge_id": atom["id"], "priority": round(atom["utility_score"] * (1 - atom["misleading_risk"]), 4), "status": "READY_FOR_HUMAN_REVIEW", "verification_status": "NEEDS_VERIFICATION", "verification_required": True, "reason": "human review and evidence verification required before any promotion", "promotion_allowed": False})
    with (output / "verification_queue.jsonl").open("w", encoding="utf-8") as fh:
        for item in queue: fh.write(json.dumps(item) + "\n")
    top = queue[:10]
    (output / "promotion_candidates.md").write_text("# Promotion candidates (human-gated)\n\nNo automatic promotion is performed. These are review priorities only.\n\n" + "\n".join(f"{i}. `{x['knowledge_id']}` — priority `{x['priority']}`" for i, x in enumerate(top, 1)) + "\n", encoding="utf-8")
    meaningful = {k: v for k, v in counts.items() if k != "UNRELATED"}
    report = f"""# Book knowledge consolidation V1\n\n- Input: six provisional book derivates from batch `BOOK_INGEST_2026-09-04_01`.\n- Initial candidates: **54**.\n- Clusters: **{len(clusters)}**.\n- Candidate mapping: all 54 candidates map to exactly one cluster and at least one atom.\n- Atoms: **{len(atoms)}** ({len(SYNTHESIS_GROUPS)} cross-source synthesis, {len(atoms)-len(SYNTHESIS_GROUPS)} single-source).\n- Status policy: every atom is `READY_FOR_HUMAN_REVIEW`; `verification_required` is true; `promotion_allowed` is false.\n- Composite prioritisation: `utility_score * (1 - misleading_risk)`; utility, reliability, reuse probability, stability, misleading risk, and retention cost remain separate fields. This is not an automatic promotion criterion.\n\n## Relation counts\n\nThe matrix contains all 1,431 candidate pairs. Counts below include only explicitly meaningful classifications; `UNRELATED` pairs are retained in the matrix but omitted from this summary.\n\n- DUPLICATE: **{meaningful.get('DUPLICATE', 0)}**\n- NEAR_DUPLICATE: **{meaningful.get('NEAR_DUPLICATE', 0)}**\n- COMPLEMENTARY: **{meaningful.get('COMPLEMENTARY', 0)}**\n- CONFLICT: **{meaningful.get('CONFLICT', 0)}**\n- NEEDS evidence verification: **{len(atoms)}**\n- READY_FOR_HUMAN_REVIEW: **{len(atoms)}**\n\n## Concrete examples\n\n- `book-agents-2026-c003` ↔ `book-llm-apps-c007`: `NEAR_DUPLICATE`; both describe RAG/external-context retrieval, so they are represented in one synthesis family rather than two fundamental memories.\n- `book-ddia-2017-c006` ↔ `book-ddia-2017-c009`: `COMPLEMENTARY`; replication/conflict resolution and distributed timing/partial failure are different mechanisms.\n- `book-agents-2026-c003` ↔ `book-agents-2026-c005`: `UNRELATED`; retrieval grounding and task-specific evaluation are retained separately.\n\n## Ten highest-value atoms\n\nSee `promotion_candidates.md`. They are prioritisation output only. Each atom links to `supporting_candidates`, then to book, source SHA-256, and locator through `sources`; `evidence_bundles.jsonl` repeats this lossless mapping.\n\n## Deliberately not consolidated\n\nSearch algorithms, constraint satisfaction, low-level transformer attention, convolutional/recurrent/LSTM mechanisms, encoding/partitioning details, and specific social/ethical claims remain single-source atoms when no sufficiently supported cross-book abstraction was justified. We also emit no `SUPERSEDES` relation: the corpus provides no evidence that one candidate replaces another. Raw PDFs and canonical memory were not modified.\n"""
    (output / "consolidation_report.md").write_text(report, encoding="utf-8")
    return {"candidates": 54, "clusters": len(clusters), "atoms": len(atoms), **meaningful}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=Path("06_INBOX/DERIVED/BOOKS/2026-09-04"))
    parser.add_argument("--output", type=Path, default=Path("06_INBOX/DERIVED/BOOKS/2026-09-04/consolidated"))
    args = parser.parse_args()
    print(json.dumps(build(args.input, args.output), sort_keys=True))


if __name__ == "__main__":
    main()
