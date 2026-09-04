import json
from pathlib import Path

from cognitive_core.recall import RecallEngine
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.working_memory import WorkingMemory
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, StorageEngine


ROOT = Path(__file__).resolve().parents[1]
ATOM_PATH = ROOT / "06_INBOX" / "DERIVED" / "BOOKS" / "2026-09-04" / "consolidated" / "knowledge_atoms.jsonl"
CASE_PATH = ROOT / "07_EVALUATION" / "model_memory_benchmark_v1" / "retrieval_challenge_cases.jsonl"
EXPECTED_IDS = {
    "M-ADAPT-001", "M-ARCH-001", "M-DISTRIBUTED-001", "M-EVAL-001",
    "M-LEARNING-001", "M-RELIABILITY-001", "M-REPRESENT-001",
    "M-TRADEOFF-001", "M-RETRIEVAL-001", "M-TOOLS-001",
}


def _read_jsonl(path):
    with path.open("r", encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def _build_review_controller():
    atoms = {
        atom["id"]: atom
        for atom in _read_jsonl(ATOM_PATH)
        if atom.get("id") in EXPECTED_IDS
    }
    assert set(atoms) == EXPECTED_IDS

    storage = StorageEngine()
    for atom_id, atom in atoms.items():
        storage.set(
            atom_id,
            {
                "id": atom_id,
                "type": atom["type"],
                "content": atom["statement"],
                "lifecycle": "REVIEW",
                "confidence": "unknown",
                "verification_status": atom.get("verification_status"),
                "verification_required": atom.get("verification_required"),
                "provenance": {"source_type": "import", "source_ref": "book-consolidation"},
            },
        )
    return MemoryController(storage), atoms


def test_review_safe_retrieval_challenge_30_cases():
    controller, atoms = _build_review_controller()
    engine = RecallEngine(controller, DeterministicSemanticProvider())
    principal = Principal.AI_AGENT
    working_memory = WorkingMemory()
    cases = _read_jsonl(CASE_PATH)

    assert len(cases) == 30
    positive = [case for case in cases if case["relevant_ids"]]
    negative = [case for case in cases if not case["relevant_ids"]]
    assert len(positive) == 20
    assert len(negative) == 10

    before = {key: value.copy() for key, value in controller.storage.store.items()}
    positive_hits = 0
    negative_false_positives = 0

    for case in cases:
        results = engine.recall(principal, case["query"], [], working_memory)
        ids = [node["id"] for node, _ in results[:3]]

        if case["relevant_ids"]:
            expected = case["relevant_ids"][0]
            if expected in ids:
                positive_hits += 1
            assert expected in atoms
            assert all(node.get("_cognitive_unverified") is True for node, _ in results)
        else:
            if ids:
                negative_false_positives += 1

    # Deterministic provider is intentionally lexical/Jaccard, so this gate
    # measures the observed floor without pretending it provides embedding-level
    # paraphrase understanding. A future real semantic provider benchmark should
    # carry the stronger recall target.
    assert positive_hits >= 14
    assert negative_false_positives == 0

    # Retrieval is observational only: REVIEW remains REVIEW and storage is unchanged.
    after = {key: value.copy() for key, value in controller.storage.store.items()}
    assert after == before
    assert all(note["lifecycle"] == "REVIEW" for note in controller.storage.store.values())


def test_review_nodes_are_detached_and_unverified():
    controller, _ = _build_review_controller()
    engine = RecallEngine(controller, DeterministicSemanticProvider())

    review_nodes = engine._review_nodes()
    assert len(review_nodes) == len(EXPECTED_IDS)
    assert all(node["lifecycle"] == "REVIEW" for node in review_nodes)
    assert all(node["_cognitive_unverified"] is True for node in review_nodes)

    review_nodes[0]["content"] = "MUTATED IN RESULT"
    stored = controller.storage.get(review_nodes[0]["id"])
    assert stored["content"] != "MUTATED IN RESULT"
    assert stored["lifecycle"] == "REVIEW"
